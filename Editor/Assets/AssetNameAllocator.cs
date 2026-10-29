using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using SoobakFigma2Unity.Editor.Util;
using UnityEditor;

namespace SoobakFigma2Unity.Editor.Assets
{
    /// <summary>
    /// Decides the final asset path for a downloaded PNG.
    ///
    /// Two responsibilities folded into one allocator so the same hash/name maps stay
    /// consistent across every call:
    ///   1. Content-hash dedup. Identical bytes → identical asset path. Multiple Figma
    ///      nodes that rasterize to the same pixels share a single Sprite asset (and a
    ///      single GUID) instead of polluting the project with byte-identical copies.
    ///   2. Human-readable filenames with collision counters. The first PNG to claim a
    ///      sanitized base name lands as <c>{name}.png</c>; later PNGs with the same
    ///      desired name but DIFFERENT bytes get bumped to <c>{name}__2.png</c>,
    ///      <c>{name}__3.png</c>, …
    ///
    /// Re-import stability: <see cref="PreloadDirectory"/> hashes every existing PNG
    /// in the output folder before allocation begins, so an unchanged image lands at
    /// the exact same asset path on subsequent imports — no GUID churn, no spurious
    /// git diff.
    /// </summary>
    internal sealed class AssetNameAllocator
    {
        private readonly string _assetDir;
        private readonly Dictionary<string, string> _hashToPath = new();
        private readonly Dictionary<string, string> _nameToPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _reusableNameToPath = new(StringComparer.OrdinalIgnoreCase);

        public int DeduplicatedCount { get; private set; }
        public int ReusedProjectAssetCount { get; private set; }
        public int AllocatedCount { get; private set; }

        public AssetNameAllocator(string assetDir)
        {
            _assetDir = assetDir.Replace("\\", "/");
        }

        /// <summary>
        /// Hash every *.png already in the output folder so unchanged content keeps the
        /// same asset path across re-imports. Skips quietly when the folder is missing.
        /// </summary>
        public void PreloadDirectory()
        {
            if (string.IsNullOrEmpty(_assetDir) || !Directory.Exists(_assetDir))
                return;

            foreach (var fullPath in Directory.GetFiles(_assetDir, "*.png", SearchOption.TopDirectoryOnly))
            {
                string hash;
                try { hash = HashFile(fullPath); }
                catch { continue; }

                var fileName = Path.GetFileName(fullPath);
                var nameNoExt = Path.GetFileNameWithoutExtension(fileName);
                var assetPath = JoinAssetPath(_assetDir, fileName);

                // Many existing files share the same hash only if a previous run already
                // produced duplicates — keep the first one as canonical so re-imports
                // don't shuffle which path is "primary".
                _hashToPath.TryAdd(hash, assetPath);
                _nameToPath.TryAdd(nameNoExt, assetPath);
            }
        }

        /// <summary>
        /// Index existing Sprite assets outside the generated-image output folder so a
        /// Figma layer named <c>ic_close</c>, <c>img_portrait</c>, or
        /// <c>slice_panel</c> can use the project's existing artwork instead of creating
        /// another PNG. Assets inside a UITexture folder win when duplicate names exist;
        /// all other ties are resolved by the shortest, then ordinal, asset path so the
        /// result stays deterministic across machines.
        /// </summary>
        public void PreloadReusableProjectSprites(IEnumerable<string> desiredBaseNames)
        {
            var desiredNames = new HashSet<string>(
                desiredBaseNames.Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => FileNameSanitizer.Sanitize(name)),
                StringComparer.OrdinalIgnoreCase);
            if (desiredNames.Count == 0)
                return;

            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var guid in AssetDatabase.FindAssets("t:Sprite", new[] { "Assets" }))
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid)?.Replace("\\", "/");
                if (string.IsNullOrEmpty(assetPath) || IsInOutputDirectory(assetPath))
                    continue;

                var name = FileNameSanitizer.Sanitize(Path.GetFileNameWithoutExtension(assetPath));
                if (!desiredNames.Contains(name))
                    continue;

                // FindAssets can return texture assets that do not expose a Sprite as
                // their main representation. Only index paths the converter can assign
                // directly to a UnityEngine.UI.Image.
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>(assetPath) == null)
                    continue;

                candidates.Add(assetPath);
            }

            foreach (var assetPath in candidates.OrderBy(GetReusableAssetPriority)
                         .ThenBy(p => p.Length)
                         .ThenBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var name = FileNameSanitizer.Sanitize(Path.GetFileNameWithoutExtension(assetPath));
                if (!string.IsNullOrEmpty(name))
                    _reusableNameToPath.TryAdd(name, assetPath);
            }
        }

        /// <summary>
        /// Resolve the asset path for a downloaded PNG.
        /// Returns either a reusable project Sprite path or a path under
        /// <c>_assetDir</c>, plus flags telling the caller whether copying and importer
        /// configuration can be skipped.
        /// </summary>
        public AllocationResult Allocate(string desiredBaseName, byte[] bytes, string ext = ".png")
        {
            if (bytes == null || bytes.Length == 0)
                throw new ArgumentException("bytes must be non-empty", nameof(bytes));

            var sanitized = FileNameSanitizer.Sanitize(desiredBaseName);
            var hash = HashBytes(bytes);

            // A matching project Sprite is an intentional asset reference, not an
            // export collision. Prefer it even when a previous converter run left a
            // byte-identical copy in the output folder.
            if (_reusableNameToPath.TryGetValue(sanitized, out var reusable))
            {
                ReusedProjectAssetCount++;
                return new AllocationResult(reusable, hash, alreadyOnDisk: true,
                    preserveImporter: true);
            }

            // Same content already placed (either preloaded or from this run) — reuse.
            if (_hashToPath.TryGetValue(hash, out var existing))
            {
                DeduplicatedCount++;
                return new AllocationResult(existing, hash, alreadyOnDisk: true,
                    preserveImporter: false);
            }

            // Hash didn't match anything → this is genuinely new content. Find an
            // unused name slot. The early hash check above means any existing name
            // collision is guaranteed to be different content, so we always bump.
            var candidate = sanitized;
            int counter = 2;
            while (_nameToPath.ContainsKey(candidate))
            {
                candidate = sanitized + "__" + counter;
                counter++;
            }

            var fileName = candidate + ext;
            var assetPath = JoinAssetPath(_assetDir, fileName);

            _hashToPath[hash] = assetPath;
            _nameToPath[candidate] = assetPath;
            AllocatedCount++;
            return new AllocationResult(assetPath, hash, alreadyOnDisk: false,
                preserveImporter: false);
        }

        private bool IsInOutputDirectory(string assetPath)
        {
            var prefix = _assetDir.TrimEnd('/') + "/";
            return assetPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static int GetReusableAssetPriority(string assetPath)
        {
            var normalized = "/" + assetPath.Replace("\\", "/").Trim('/') + "/";
            return normalized.IndexOf("/UITexture/", StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1;
        }

        private static string HashBytes(byte[] bytes)
        {
            using var sha = SHA1.Create();
            var hashBytes = sha.ComputeHash(bytes);
            // 8 hex chars → 32-bit content key. Collisions on uniform-random inputs are
            // ~1 in 4 billion; for a project's worth of UI sprites that's effectively
            // zero, and the shorter form keeps any debug filename humane.
            var sb = new System.Text.StringBuilder(16);
            for (int i = 0; i < 4; i++) sb.Append(hashBytes[i].ToString("x2"));
            return sb.ToString();
        }

        private static string HashFile(string fullPath)
        {
            using var stream = File.OpenRead(fullPath);
            using var sha = SHA1.Create();
            var hashBytes = sha.ComputeHash(stream);
            var sb = new System.Text.StringBuilder(16);
            for (int i = 0; i < 4; i++) sb.Append(hashBytes[i].ToString("x2"));
            return sb.ToString();
        }

        private static string JoinAssetPath(string dir, string fileName)
        {
            if (string.IsNullOrEmpty(dir)) return fileName;
            return (dir.TrimEnd('/') + "/" + fileName).Replace("\\", "/");
        }

        public readonly struct AllocationResult
        {
            public readonly string AssetPath;
            public readonly string ContentHash;
            /// <summary>True when the resolved path already holds a usable asset and the
            /// caller can skip the copy step.</summary>
            public readonly bool AlreadyOnDisk;
            /// <summary>True when this path belongs to an existing project Sprite whose
            /// importer settings and 9-slice border must be left untouched.</summary>
            public readonly bool PreserveImporter;

            public AllocationResult(
                string assetPath,
                string contentHash,
                bool alreadyOnDisk,
                bool preserveImporter)
            {
                AssetPath = assetPath;
                ContentHash = contentHash;
                AlreadyOnDisk = alreadyOnDisk;
                PreserveImporter = preserveImporter;
            }
        }
    }
}
