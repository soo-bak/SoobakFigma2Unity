// URP UGUI shader implementing Figma's "Appearance = Color" (HSL chroma blend):
//   result.rgb = HslToRgb(src.hue, src.saturation, dst.luminance)
//
// Source comes from the regular UGUI sprite × tint pipeline. Destination comes
// from the global texture _UISceneColor that UISceneColorCopyFeature blits into
// once per frame (URP RendererFeature, AfterRenderingTransparents). Material
// queue is bumped slightly so this shader renders late enough that the copy can
// be relied on for the prior frame's content.
//
// Built-in RP is intentionally not supported here — that path used GrabPass and
// the user opted in to URP-only tooling.
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

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
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

        // Output the HSL-blended color modulated by the source alpha.
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "Default"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct Attributes
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                float4 screenPos     : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4    _Color;
            fixed4    _TextureSampleAdd;
            float4    _ClipRect;
            float4    _MainTex_ST;

            // Filled in by UISceneColorCopyFeature once per frame (URP RendererFeature).
            // Holds the camera color buffer captured AfterRenderingTransparents.
            sampler2D _UISceneColor;

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color * _Color;
                o.screenPos = ComputeScreenPos(o.vertex);
                return o;
            }

            // ---- HSL helpers (working space matches Figma's color picker) ----

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

            fixed4 frag(Varyings i) : SV_Target
            {
                // Source: sprite × tint × per-vertex color
                fixed4 src = (tex2D(_MainTex, i.texcoord) + _TextureSampleAdd) * i.color;

                // Destination: camera color captured by UISceneColorCopyFeature.
                // Project the screen-space position into [0,1] sample coords.
                float2 screenUV = i.screenPos.xy / max(i.screenPos.w, 1e-5);
                fixed4 dst = tex2D(_UISceneColor, screenUV);

                float3 srcHsl = RgbToHsl(saturate(src.rgb));
                float3 dstHsl = RgbToHsl(saturate(dst.rgb));

                // Figma COLOR: keep destination luminance, apply source hue & saturation.
                float3 blended = HslToRgb(float3(srcHsl.x, srcHsl.y, dstHsl.z));

                fixed4 outCol = fixed4(blended, src.a);

                #ifdef UNITY_UI_CLIP_RECT
                outCol.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(outCol.a - 0.001);
                #endif

                return outCol;
            }
            ENDHLSL
        }
    }

    Fallback "UI/Default"
}
