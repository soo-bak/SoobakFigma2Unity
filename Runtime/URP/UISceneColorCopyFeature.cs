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
    /// This feature is opt-in. Do not probe Shader.Find / FindObjectsOfTypeAll from
    /// AddRenderPasses — that runs inside editor GUI (ProcessEvent) and can re-enter
    /// UniversalRenderPipeline construction while Blitter is already initialized.
    /// </summary>
    public sealed class UISceneColorCopyFeature : ScriptableRendererFeature
    {
        internal static readonly ShaderTagId ColorBlendTagId = new ShaderTagId("SoobakColorBlend");

        private CopyPass _copyPass;
        private DrawPass _drawPass;

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
            var cameraType = renderingData.cameraData.cameraType;
            if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection)
                return;

            renderer.EnqueuePass(_copyPass);
            renderer.EnqueuePass(_drawPass);
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
