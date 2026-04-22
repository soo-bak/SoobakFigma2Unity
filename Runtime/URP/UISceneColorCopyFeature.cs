#if SOOBAK_FIGMA2UNITY_URP
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace SoobakFigma2Unity.Runtime.URP
{
    /// <summary>
    /// URP RendererFeature that copies the active camera color buffer to a global shader
    /// texture (<c>_UISceneColor</c>) right after the regular UI transparents finish drawing.
    /// Figma "Appearance = Color" (HSL chroma blend) Image materials sit at queue 3050+ so
    /// they render <i>after</i> this copy and can sample the texture as their destination.
    ///
    /// Without this feature, URP UI shaders have no equivalent to Built-in RP's GrabPass,
    /// so HSL-space blends like COLOR / HUE / SATURATION can't read what's underneath.
    /// </summary>
    public sealed class UISceneColorCopyFeature : ScriptableRendererFeature
    {
        private CopyPass _pass;

        public override void Create()
        {
            _pass = new CopyPass
            {
                renderPassEvent = RenderPassEvent.AfterRenderingTransparents
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType == CameraType.Preview) return;
            renderer.EnqueuePass(_pass);
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

                // Allocate a destination texture matching the active color descriptor.
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
    }
}
#endif
