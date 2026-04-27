using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Mapping
{
    /// <summary>
    /// Before every re-import writes a prefab, <see cref="CreateSnapshot"/> copies the
    /// existing .prefab (and its .meta) into <c>Library/Soobak/backups/{hash}/{timestamp}.prefab</c>.
    /// <see cref="Restore"/> reverses the copy. <c>Library/</c> is gitignored by Unity and
    /// not indexed by AssetDatabase, so backups don't pollute the project.
    ///
    /// The backup index keeps the newest N (default 5) snapshots per original prefab.
    /// </summary>
    internal static class MergeBackup
    {
        private static readonly string BackupRoot =
            Path.Combine(Directory.GetCurrentDirectory(), "Library", "Soobak", "backups");

        public readonly struct Snapshot
        {
            public readonly string OriginalPrefabPath; // Assets/... path
            public readonly string BackupPrefabPath;   // absolute path under Library/
            public readonly DateTime CreatedAt;
            public Snapshot(string original, string backup, DateTime at)
            {
                OriginalPrefabPath = original;
                BackupPrefabPath = backup;
                CreatedAt = at;
            }
        }

        /// <summary>
        /// Copy the existing prefab (if any) into the backup store. No-op if the prefab
        /// doesn't exist yet. Returns the snapshot or null on failure / no source.
        /// </summary>
        public static Snapshot? CreateSnapshot(string prefabAssetPath, int retentionCount)
        {
            if (string.IsNullOrEmpty(prefabAssetPath)) return null;
            var fullSrc = Path.GetFullPath(prefabAssetPath);
            if (!File.Exists(fullSrc)) return null;

            try
            {
                var bucket = GetBucketDir(prefabAssetPath);
                Directory.CreateDirectory(bucket);

                var now = DateTime.UtcNow;
                var stamp = now.ToString("yyyy-MM-dd_HHmmss_fff");
                var backupFile = Path.Combine(bucket, $"{stamp}.prefab");

                File.Copy(fullSrc, backupFile, overwrite: true);

                // Also copy .meta so GUIDs survive a restore.
                var srcMeta = fullSrc + ".meta";
                if (File.Exists(srcMeta))
                    File.Copy(srcMeta, backupFile + ".meta", overwrite: true);

                // Record the source path next to the backup so we can restore by snapshot only.
                File.WriteAllText(backupFile + ".src", prefabAssetPath);

                PruneOld(bucket, retentionCount);

                return new Snapshot(prefabAssetPath, backupFile, now);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SoobakFigma2Unity] MergeBackup.CreateSnapshot failed for {prefabAssetPath}: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// List the snapshots available for a given prefab, newest first.
        /// </summary>
        public static IReadOnlyList<Snapshot> ListSnapshots(string prefabAssetPath)
        {
            var bucket = GetBucketDir(prefabAssetPath);
            if (!Directory.Exists(bucket)) return Array.Empty<Snapshot>();
            var list = new List<Snapshot>();
            foreach (var f in Directory.EnumerateFiles(bucket, "*.prefab"))
            {
                var ts = File.GetLastWriteTimeUtc(f);
                list.Add(new Snapshot(prefabAssetPath, f, ts));
            }
            list.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
            return list;
        }

        /// <summary>
        /// Restore the prefab from a given snapshot. Triggers AssetDatabase import.
        /// </summary>
        public static bool Restore(Snapshot snapshot)
        {
            if (string.IsNullOrEmpty(snapshot.OriginalPrefabPath)) return false;
            if (string.IsNullOrEmpty(snapshot.BackupPrefabPath) || !File.Exists(snapshot.BackupPrefabPath))
                return false;

            try
            {
                var dstFull = Path.GetFullPath(snapshot.OriginalPrefabPath);
                var dstDir = Path.GetDirectoryName(dstFull);
                if (!string.IsNullOrEmpty(dstDir)) Directory.CreateDirectory(dstDir);

                File.Copy(snapshot.BackupPrefabPath, dstFull, overwrite: true);
                var backupMeta = snapshot.BackupPrefabPath + ".meta";
                if (File.Exists(backupMeta))
                    File.Copy(backupMeta, dstFull + ".meta", overwrite: true);

                AssetDatabase.ImportAsset(snapshot.OriginalPrefabPath, ImportAssetOptions.ForceSynchronousImport);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SoobakFigma2Unity] MergeBackup.Restore failed for {snapshot.OriginalPrefabPath}: {e.Message}");
                return false;
            }
        }

        private static string GetBucketDir(string prefabAssetPath)
        {
            var hash = ShortHash(prefabAssetPath);
            return Path.Combine(BackupRoot, hash);
        }

        private static void PruneOld(string bucketDir, int retention)
        {
            if (retention <= 0) return;
            var files = Directory.EnumerateFiles(bucketDir, "*.prefab")
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                .ToList();
            for (int i = retention; i < files.Count; i++)
            {
                SafeDelete(files[i]);
                SafeDelete(files[i] + ".meta");
                SafeDelete(files[i] + ".src");
            }
        }

        private static void SafeDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* swallow */ }
        }

        private static string ShortHash(string s)
        {
            using var sha = SHA1.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
            var sb = new StringBuilder(16);
            for (int i = 0; i < 8; i++) sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }
    }
}
