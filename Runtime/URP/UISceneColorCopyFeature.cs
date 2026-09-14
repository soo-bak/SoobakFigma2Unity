#if SOOBAK_FIGMA2UNITY_URP
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace SoobakFigma2Unity.Runtime.URP
{
    /// <summary>
    /// Two-pass URP RendererFeature for Figma "Appearance = Color" (HSL chroma blend).
    ///
    /// The Figma COLOR shader needs to sample what's been drawn behind it. Putting it
    /// in the regular transparent queue creates a feedback loop — the shader reads
    /// _UISceneColor that doesn't yet contain the rest of this frame's UI, then the
    /// copy captures the shader's own output, which it samples again next frame, and
    /// the result converges to a flat colour.
    ///
    /// To break the loop, the COLOR shader's Pass declares a custom LightMode
    /// ("SoobakColorBlend") that URP's default transparent pass ignores. This feature
    /// then schedules two passes inside the camera pipeline:
    ///
    ///   1. CopyPass  — RenderPassEvent.AfterRenderingTransparents
    ///                  Blits the active camera colour (which contains every UI
    ///                  element except the COLOR-blend ones) into _UISceneColor.
    ///
    ///   2. DrawPass  — RenderPassEvent.AfterRenderingTransparents + 1
    ///                  Walks every renderer whose shader carries the
    ///                  "SoobakColorBlend" tag and draws it with _UISceneColor
    ///                  bound, producing a correct one-frame, single-pass blend.
    ///
    /// Both passes are skipped unless a chroma-blend material is actually loaded.
    /// DrawPass only sees MeshRenderer / SkinnedMeshRenderer entries in cullResults;
    /// UGUI CanvasRenderer never appears there, so an unused install must not blit
    /// every camera.
    /// </summary>
    public sealed class UISceneColorCopyFeature : ScriptableRendererFeature
    {
        internal static readonly ShaderTagId ColorBlendTagId = new ShaderTagId("SoobakColorBlend");

        private static readonly string[] ColorBlendShaderNames =
        {
            "SoobakFigma2Unity/URP/BlendColor",
            "SoobakFigma2Unity/URP/BlendHue",
            "SoobakFigma2Unity/URP/BlendSaturation",
            "SoobakFigma2Unity/URP/BlendDarken",
            "SoobakFigma2Unity/URP/BlendLighten",
        };

        private const int OccupancyRefreshInterval = 30;

        private static Shader[] _colorBlendShaders;
        private static int _occupancyFrame = int.MinValue;
        private static bool _hasLoadedBlendMaterial;

        private CopyPass _copyPass;
        private DrawPass _drawPass;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _colorBlendShaders = null;
            _occupancyFrame = int.MinValue;
            _hasLoadedBlendMaterial = false;
        }

        public override void Create()
        {
            _copyPass = new CopyPass
            {
                renderPassEvent = RenderPassEvent.AfterRenderingTransparents
            };
            _drawPass = new DrawPass
            {
                renderPassEvent = RenderPassEvent.AfterRenderingTransparents + 1
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!ShouldEnqueue(renderingData.cameraData.cameraType))
                return;

            renderer.EnqueuePass(_copyPass);
            renderer.EnqueuePass(_drawPass);
        }

        private static bool ShouldEnqueue(CameraType cameraType)
        {
            if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection)
                return false;

            return HasLoadedColorBlendMaterial();
        }

        private static bool HasLoadedColorBlendMaterial()
        {
            int frame = Time.renderedFrameCount;
            if (_occupancyFrame != int.MinValue && frame - _occupancyFrame < OccupancyRefreshInterval)
                return _hasLoadedBlendMaterial;

            _occupancyFrame = frame;
            _hasLoadedBlendMaterial = ScanLoadedColorBlendMaterials();
            return _hasLoadedBlendMaterial;
        }

        private static bool ScanLoadedColorBlendMaterials()
        {
            var shaders = GetColorBlendShaders();
            if (shaders.Length == 0)
                return false;

            var materials = Resources.FindObjectsOfTypeAll<Material>();
            for (int i = 0; i < materials.Length; i++)
            {
                var shader = materials[i] != null ? materials[i].shader : null;
                if (shader == null)
                    continue;

                for (int s = 0; s < shaders.Length; s++)
                {
                    if (shader == shaders[s])
                        return true;
                }
            }

            return false;
        }

        private static Shader[] GetColorBlendShaders()
        {
            if (_colorBlendShaders != null)
                return _colorBlendShaders;

            var found = new List<Shader>(ColorBlendShaderNames.Length);
            for (int i = 0; i < ColorBlendShaderNames.Length; i++)
            {
                var shader = Shader.Find(ColorBlendShaderNames[i]);
                if (shader != null)
                    found.Add(shader);
            }

            _colorBlendShaders = found.ToArray();
            return _colorBlendShaders;
        }

        private sealed class CopyPass : ScriptableRenderPass
        {
            private static readonly int GlobalTexId = Shader.PropertyToID("_UISceneColor");

            private class PassData
            {
                public TextureHandle source;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (!HasLoadedColorBlendMaterial())
                    return;

                var resourceData = frameData.Get<UniversalResourceData>();
                if (!resourceData.activeColorTexture.IsValid()) return;

                var desc = renderGraph.GetTextureDesc(resourceData.activeColorTexture);
                desc.name = "_UISceneColor";
                desc.clearBuffer = false;
                desc.depthBufferBits = 0;
                var dst = renderGraph.CreateTexture(desc);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                           "SoobakFigma2Unity: Copy UI Scene Color", out var passData))
                {
                    passData.source = resourceData.activeColorTexture;

                    builder.UseTexture(passData.source, AccessFlags.Read);
                    builder.SetRenderAttachment(dst, 0, AccessFlags.Write);
                    builder.SetGlobalTextureAfterPass(dst, GlobalTexId);
                    builder.AllowPassCulling(false);

                    builder.SetRenderFunc<PassData>((data, context) =>
                    {
                        Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), 0, false);
                    });
                }
            }
        }

        private sealed class DrawPass : ScriptableRenderPass
        {
            private static readonly List<ShaderTagId> TagList = new List<ShaderTagId> { ColorBlendTagId };

            private class PassData
            {
                public RendererListHandle rendererList;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (!HasLoadedColorBlendMaterial())
                    return;

                var resourceData = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                var renderingData = frameData.Get<UniversalRenderingData>();
                var lightData = frameData.Get<UniversalLightData>();

                if (!resourceData.activeColorTexture.IsValid()) return;

                var sortingCriteria = SortingCriteria.CommonTransparent;
                var drawingSettings = RenderingUtils.CreateDrawingSettings(
                    TagList, renderingData, cameraData, lightData, sortingCriteria);
                var filterSettings = new FilteringSettings(RenderQueueRange.transparent);

                var rendererListParams = new RendererListParams(
                    renderingData.cullResults, drawingSettings, filterSettings);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                           "SoobakFigma2Unity: Draw Color-blend UI", out var passData))
                {
                    passData.rendererList = renderGraph.CreateRendererList(rendererListParams);
                    builder.UseRendererList(passData.rendererList);
                    builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
                    builder.AllowPassCulling(true);

                    builder.SetRenderFunc<PassData>((data, context) =>
                    {
                        context.cmd.DrawRendererList(data.rendererList);
                    });
                }
            }
        }
    }
}
#endif
