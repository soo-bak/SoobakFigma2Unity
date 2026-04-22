// URP UGUI shader implementing Figma's "Appearance = Color" (HSL chroma blend):
//   result.rgb = HslToRgb(src.hue, src.saturation, dst.luminance)
//
// Source comes from the regular UGUI sprite × tint. Destination comes from the
// global texture _UISceneColor that UISceneColorCopyFeature blits each frame
// (URP RendererFeature, AfterRenderingTransparents). Material queue is
// bumped (Transparent + 50) so the sample is meaningful.
//
// URP-only. GrabPass / BiRP path was removed by design.
Shader "SoobakFigma2Unity/URP/BlendColor"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent+50"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
            "RenderPipeline"="UniversalPipeline"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        ColorMask [_ColorMask]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "Default"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
                float4 worldPos   : TEXCOORD1;
                float4 screenPos  : TEXCOORD2;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_UISceneColor);
            SAMPLER(sampler_UISceneColor);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _Color;
                half4  _TextureSampleAdd;
                float4 _ClipRect;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                OUT.worldPos   = IN.positionOS;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv         = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color      = IN.color * _Color;
                OUT.screenPos  = ComputeScreenPos(OUT.positionCS);
                return OUT;
            }

            // ---- HSL helpers (Figma-matching color picker space) ----

            float3 RgbToHsl(float3 c)
            {
                float maxC = max(c.r, max(c.g, c.b));
                float minC = min(c.r, min(c.g, c.b));
                float l = 0.5 * (maxC + minC);
                float d = maxC - minC;
                float s = 0.0;
                float h = 0.0;
                if (d > 1e-5)
                {
                    s = (l > 0.5) ? d / (2.0 - maxC - minC) : d / (maxC + minC);
                    if (maxC == c.r)      h = (c.g - c.b) / d + (c.g < c.b ? 6.0 : 0.0);
                    else if (maxC == c.g) h = (c.b - c.r) / d + 2.0;
                    else                  h = (c.r - c.g) / d + 4.0;
                    h /= 6.0;
                }
                return float3(h, s, l);
            }

            float HueChannel(float p, float q, float t)
            {
                if (t < 0.0) t += 1.0;
                if (t > 1.0) t -= 1.0;
                if (t < 1.0/6.0) return p + (q - p) * 6.0 * t;
                if (t < 1.0/2.0) return q;
                if (t < 2.0/3.0) return p + (q - p) * (2.0/3.0 - t) * 6.0;
                return p;
            }

            float3 HslToRgb(float3 hsl)
            {
                float h = hsl.x, s = hsl.y, l = hsl.z;
                if (s <= 1e-5) return float3(l, l, l);
                float q = (l < 0.5) ? l * (1.0 + s) : l + s - l * s;
                float p = 2.0 * l - q;
                return float3(
                    HueChannel(p, q, h + 1.0/3.0),
                    HueChannel(p, q, h),
                    HueChannel(p, q, h - 1.0/3.0)
                );
            }

            // 2D UI rect clip (UGUI's _ClipRect is xyzw = minX, minY, maxX, maxY in
            // worldPos.xy space, set by Mask / RectMask2D / Canvas).
            half ClipUI(float2 worldXY)
            {
                float2 ge = step(_ClipRect.xy, worldXY);
                float2 le = step(worldXY, _ClipRect.zw);
                return ge.x * ge.y * le.x * le.y;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 src = (SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) + _TextureSampleAdd) * IN.color;

                float2 screenUV = IN.screenPos.xy / max(IN.screenPos.w, 1e-5);
                half4 dst = SAMPLE_TEXTURE2D(_UISceneColor, sampler_UISceneColor, screenUV);

                float3 srcHsl = RgbToHsl(saturate(src.rgb));
                float3 dstHsl = RgbToHsl(saturate(dst.rgb));

                // Figma COLOR: keep destination luminance, apply source hue & saturation.
                float3 blended = HslToRgb(float3(srcHsl.x, srcHsl.y, dstHsl.z));

                half4 outCol = half4(blended, src.a);
                outCol.a *= ClipUI(IN.worldPos.xy);
                return outCol;
            }
            ENDHLSL
        }
    }

    Fallback "UI/Default"
}
