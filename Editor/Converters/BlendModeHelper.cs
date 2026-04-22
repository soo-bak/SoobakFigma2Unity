using System.Collections.Generic;
using System.IO;
using SoobakFigma2Unity.Editor.Util;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SoobakFigma2Unity.Editor.Converters
{
    /// <summary>
    /// Applies Figma blend modes to Unity UI Image components
    /// by assigning the appropriate custom shader material.
    /// </summary>
    internal static class BlendModeHelper
    {
        private static readonly Dictionary<string, string> ShaderMap = new Dictionary<string, string>
        {
            { "MULTIPLY", "SoobakFigma2Unity/UI/Multiply" },
            { "SCREEN", "SoobakFigma2Unity/UI/Screen" },
            { "OVERLAY", "SoobakFigma2Unity/UI/Overlay" },
            // COLOR routes to the URP shader (SoobakFigma2Unity/URP/BlendColor) which
            // samples the global _UISceneColor texture filled by UISceneColorCopyFeature.
            // GrabPass is unsupported in URP, so destination access goes through the
            // RendererFeature instead. Falls back to ChromaBlendModes (hide / 35% alpha)
            // if the shader isn't available (URP not installed).
            { "COLOR", "SoobakFigma2Unity/URP/BlendColor" },
        };

        // Blend modes whose result depends on the destination's HSL (chroma/luma
        // redistribution). UGUI's default material can't reach the destination pixel,
        // so when the source is a neutral gray — the common "desaturate overlay" case
        // — the least-wrong rendering is to skip drawing the source at all. That lets
        // the underlying graphic show through instead of being covered by an opaque
        // gray block. Colored sources still get a semi-transparent fallback.
        private static readonly HashSet<string> ChromaBlendModes =
            new HashSet<string> { "COLOR", "HUE", "SATURATION", "DARKEN", "LIGHTEN" };

        /// <summary>
        /// Apply blend mode material to an Image component if needed.
        /// LUMINOSITY is approximated by desaturating image.color (Rec.601 luma);
        /// when a sprite is present, the gray tint multiplies it to a desaturated look.
        /// Returns true if a non-normal blend mode was applied.
        /// </summary>
        public static bool TryApply(Image image, string figmaBlendMode, ImportLogger logger)
        {
            if (image == null || string.IsNullOrEmpty(figmaBlendMode))
                return false;

            if (figmaBlendMode == "NORMAL" || figmaBlendMode == "PASS_THROUGH")
                return false;

            if (figmaBlendMode == "LUMINOSITY")
            {
                image.color = ApproximateLuminosity(image.color);
                return true;
            }

            if (ShaderMap.TryGetValue(figmaBlendMode, out var shaderName))
            {
                var mat = GetOrCreateSharedMaterial(shaderName);
                if (mat != null)
                {
                    image.material = mat;
                    logger?.Info($"{image.gameObject.name}: blend mode '{figmaBlendMode}' applied via shader");
                    return true;
                }
                logger?.Warn($"{image.gameObject.name}: shader '{shaderName}' not found for blend mode '{figmaBlendMode}'");
                // Intentional fall-through to the ChromaBlendModes fallback below so a
                // missing shader still produces a sensible result (hide / semi-transparent)
                // instead of an opaque solid cover.
            }

            if (ChromaBlendModes.Contains(figmaBlendMode))
            {
                if (IsApproximatelyGrayscale(image.color))
                {
                    // Gray source + COLOR-family blend ≈ desaturate destination. We can't
                    // desaturate without a shader, but covering the destination with solid
                    // gray is worse than showing the untouched destination — so we hide
                    // this graphic entirely and let the layer below render unchanged.
                    image.color = new UnityEngine.Color(image.color.r, image.color.g, image.color.b, 0f);
                    logger?.Info($"{image.gameObject.name}: '{figmaBlendMode}' with gray source — hidden (closer to Figma than an opaque cover)");
                    return true;
                }
                // Colored source: fall back to semi-transparent overlay so the destination
                // at least partially reads through.
                var c = image.color;
                image.color = new UnityEngine.Color(c.r, c.g, c.b, c.a * 0.35f);
                logger?.Warn($"{image.gameObject.name}: '{figmaBlendMode}' with colored source — rendered as 35% alpha overlay (approximation)");
                return true;
            }

            logger?.Warn($"{image.gameObject.name}: blend mode '{figmaBlendMode}' not supported — using Normal");
            return false;
        }

        private static bool IsApproximatelyGrayscale(UnityEngine.Color c)
        {
            const float tol = 0.04f; // ≈ 10 / 255
            return Mathf.Abs(c.r - c.g) < tol
                && Mathf.Abs(c.g - c.b) < tol
                && Mathf.Abs(c.r - c.b) < tol;
        }

        /// <summary>
        /// Apply blend mode to a TMP text. Currently only LUMINOSITY is approximated
        /// (text color desaturated to its Rec.601 luma); shader-based modes are not
        /// supported on TMP and warn instead.
        /// </summary>
        public static bool TryApplyToText(TMP_Text text, string figmaBlendMode, ImportLogger logger)
        {
            if (text == null || string.IsNullOrEmpty(figmaBlendMode))
                return false;

            if (figmaBlendMode == "NORMAL" || figmaBlendMode == "PASS_THROUGH")
                return false;

            if (figmaBlendMode == "LUMINOSITY")
            {
                text.color = ApproximateLuminosity(text.color);
                return true;
            }

            if (ChromaBlendModes.Contains(figmaBlendMode))
            {
                if (IsApproximatelyGrayscale(text.color))
                {
                    text.color = new UnityEngine.Color(text.color.r, text.color.g, text.color.b, 0f);
                    logger?.Info($"{text.gameObject.name}: '{figmaBlendMode}' with gray source on text — hidden");
                    return true;
                }
                var c = text.color;
                text.color = new UnityEngine.Color(c.r, c.g, c.b, c.a * 0.35f);
                logger?.Warn($"{text.gameObject.name}: '{figmaBlendMode}' with colored source on text — rendered as 35% alpha");
                return true;
            }

            logger?.Warn($"{text.gameObject.name}: blend mode '{figmaBlendMode}' not supported on text — using Normal");
            return false;
        }

        /// <summary>
        /// For a wrapper FRAME/INSTANCE that has a blend mode but no own Graphic,
        /// approximate the blend by walking descendants. Currently only LUMINOSITY
        /// cascades (desaturate every Image and TMP_Text underneath).
        /// </summary>
        public static void PropagateApproximationToDescendants(GameObject root, string figmaBlendMode, ImportLogger logger)
        {
            if (root == null || string.IsNullOrEmpty(figmaBlendMode))
                return;
            if (figmaBlendMode == "NORMAL" || figmaBlendMode == "PASS_THROUGH")
                return;

            if (figmaBlendMode == "LUMINOSITY")
            {
                foreach (var img in root.GetComponentsInChildren<Image>(true))
                    img.color = ApproximateLuminosity(img.color);
                foreach (var tmp in root.GetComponentsInChildren<TMP_Text>(true))
                    tmp.color = ApproximateLuminosity(tmp.color);
                return;
            }

            logger?.Warn($"{root.name}: wrapper blend mode '{figmaBlendMode}' cannot cascade to descendants — ignored");
        }

        private static UnityEngine.Color ApproximateLuminosity(UnityEngine.Color c)
        {
            // Rec.601 luma — same weighting Figma uses for color → grayscale.
            float l = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
            return new UnityEngine.Color(l, l, l, c.a);
        }

        // A freshly-created Material (new Material(shader)) is an in-memory object with no
        // project GUID. When a prefab referencing it is saved, Unity can't round-trip that
        // reference — the serialised field resets to {fileID: 0} and the Image renders as
        // plain Normal again. So every blend shader is persisted as a single shared
        // Material asset under Assets/Soobak/Materials/ and referenced by all Images that
        // need it.
        private const string MaterialFolder = "Assets/Soobak/Materials";
        private static readonly Dictionary<string, Material> _materialCache = new Dictionary<string, Material>();

        private static Material GetOrCreateSharedMaterial(string shaderName)
        {
            if (string.IsNullOrEmpty(shaderName)) return null;

            if (_materialCache.TryGetValue(shaderName, out var cached) && cached != null)
                return cached;

            var assetPath = $"{MaterialFolder}/{SanitizeShaderName(shaderName)}.mat";

            var shader = Shader.Find(shaderName);

            var mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (mat != null)
            {
                // A .mat saved while its shader was missing / broken serializes with
                // shader = {fileID: 0}, which Unity surfaces as Hidden/InternalErrorShader.
                // Once the shader is fixed we have to re-bind explicitly — Unity never
                // revisits a null reference on its own. Do it every load so the material
                // self-heals without the user having to delete and recreate it.
                if (shader != null && mat.shader != shader)
                {
                    mat.shader = shader;
                    EditorUtility.SetDirty(mat);
                    AssetDatabase.SaveAssets();
                }
                _materialCache[shaderName] = mat;
                return mat;
            }

            if (shader == null) return null;

            AssetFolderUtil.EnsureFolder(MaterialFolder);
            mat = new Material(shader) { name = Path.GetFileNameWithoutExtension(assetPath) };
            AssetDatabase.CreateAsset(mat, assetPath);
            AssetDatabase.SaveAssets();
            _materialCache[shaderName] = mat;
            return mat;
        }

        private static string SanitizeShaderName(string name)
        {
            // "SoobakFigma2Unity/UI/ColorBlend" -> "SoobakFigma2Unity_UI_ColorBlend"
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (var c in name) sb.Append(c == '/' || c == '\\' ? '_' : c);
            return sb.ToString();
        }
    }
}
