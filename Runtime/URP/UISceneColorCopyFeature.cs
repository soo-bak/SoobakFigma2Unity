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
    /// </summary>
    public sealed class UISceneColorCopyFeature : ScriptableRendererFeature
    {
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
            if (renderingData.cameraData.cameraType == CameraType.Preview) return;
            renderer.EnqueuePass(_copyPass);
            renderer.EnqueuePass(_drawPass);
        }

        private sealed class CopyPass : ScriptableRenderPass
        {
            private static readonly int GlobalTexId = Shader.PropertyToID("_UISceneColor");
            private static bool _firstFireLogged;

            private class PassData
            {
                public TextureHandle source;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resourceData = frameData.Get<UniversalResourceData>();
                if (!resourceData.activeColorTexture.IsValid()) return;

                if (!_firstFireLogged)
                {
                    _firstFireLogged = true;
                    Debug.Log("[SoobakFigma2Unity] CopyPass fired — _UISceneColor will be bound.");
                }

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
            // Must match the LightMode tag declared in URPBlendColor.shader.
            // Renderers whose shader pass carries this tag are excluded from
            // URP's default transparent pass and drawn here instead.
            private static readonly ShaderTagId TagId = new ShaderTagId("SoobakColorBlend");
            private static readonly List<ShaderTagId> TagList = new List<ShaderTagId> { TagId };
            private static bool _firstFireLogged;

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

                if (!_firstFireLogged)
                {
                    _firstFireLogged = true;
                    Debug.Log("[SoobakFigma2Unity] DrawPass fired — color-blend renderers will be drawn.");
                }

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
                    builder.AllowPassCulling(false);

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
