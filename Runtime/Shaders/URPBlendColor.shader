// Figma "Appearance = Color" (HSL chroma blend) for Unity UGUI under URP.
//
// result.rgb = HslToRgb(src.hue, src.saturation, dst.luminance)
// where `dst` is sampled from _UISceneColor — a global texture that
// UISceneColorCopyFeature blits from the active camera color buffer at
// RenderPassEvent.AfterRenderingTransparents.
//
// Structure intentionally mirrors Unity's shipped UI/Default shader
// 1:1 (properties, tags, stencil block, pass body) so we inherit its
// URP-compatibility guarantees. The only deviation is the fragment
// body, which replaces the trivial tint pipeline with HSL blend.
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
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
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
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
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

            // Filled each frame by UISceneColorCopyFeature as a URP global.
            // Defaults to black if the feature didn't run (e.g. scene view
            // preview); the shader still renders a visible output in that
            // case — see frag() for details.
            sampler2D _UISceneColor;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                OUT.screenPos = ComputeScreenPos(OUT.vertex);
                return OUT;
            }

            // --- HSL helpers (Figma COLOR-blend spec) -------------------
            // Figma specifies chroma blends in non-linear (sRGB) HSL, so we
            // convert sRGB <-> HSL directly without going through linear.
            float3 RgbToHsl(float3 c)
            {
                float maxC = max(c.r, max(c.g, c.b));
                float minC = min(c.r, min(c.g, c.b));
                float d = maxC - minC;
                float l = 0.5 * (maxC + minC);
                float s = 0.0;
                float h = 0.0;
                if (d > 1e-5)
                {
                    s = (l < 0.5) ? d / (maxC + minC) : d / (2.0 - maxC - minC);
                    if (maxC == c.r)      h = (c.g - c.b) / d + (c.g < c.b ? 6.0 : 0.0);
                    else if (maxC == c.g) h = (c.b - c.r) / d + 2.0;
                    else                  h = (c.r - c.g) / d + 4.0;
                    h /= 6.0;
                }
                return float3(h, s, l);
            }

            float HueToRgb(float p, float q, float t)
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
                return float3(HueToRgb(p, q, h + 1.0/3.0),
                              HueToRgb(p, q, h),
                              HueToRgb(p, q, h - 1.0/3.0));
            }
            // -----------------------------------------------------------

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 src = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;

                // Sample the previously-rendered UI as the blend destination.
                // screenPos is clip-space scaled; divide by w to get 0..1 UV.
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
                half3 dst = tex2D(_UISceneColor, screenUV).rgb;

                // --- DIAGNOSTIC ---
                // Return the _UISceneColor sample directly. Tells us exactly
                // what the RendererFeature is (or isn't) capturing:
                //   - opaque gray matching the Figma source → feature isn't
                //     running or Canvas is ScreenSpace-Overlay (URP can't
                //     capture Overlay UI from a camera pass)
                //   - the character image visible inside the rectangle → UI
                //     is captured correctly; HSL math is the next suspect
                //   - black → _UISceneColor global is unbound entirely
                // Once verified we switch back to the HSL blend path below.
                return half4(dst, src.a);

                // Figma COLOR blend: replace destination's hue+saturation with
                // source's, keep destination's luminance.
                // float3 srcHsl = RgbToHsl(saturate(src.rgb));
                // float3 dstHsl = RgbToHsl(saturate(dst));
                // float3 blended = HslToRgb(float3(srcHsl.x, srcHsl.y, dstHsl.z));
                // half3 outRgb = lerp(dst, blended, src.a);
                // half4 color = half4(outRgb, src.a);
                // #ifdef UNITY_UI_CLIP_RECT
                // color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                // #endif
                // #ifdef UNITY_UI_ALPHACLIP
                // clip(color.a - 0.001);
                // #endif
                // return color;
            }
            ENDCG
        }
    }
}
