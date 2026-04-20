// UGUI shader implementing Figma's "COLOR" blend mode in HSL space:
//   result.rgb = HSL(source.hue, source.saturation, destination.luminance)
//
// Figma's COLOR blend keeps the destination's luma and applies the source's
// chroma on top. For a neutral-gray source (saturation=0) it collapses to
// full desaturation of the destination — which is how the "게임결과_모험실패"
// screen turns its character illustration black-and-white.
//
// Implemented with a GrabPass so the fragment shader can read the destination
// pixel (UGUI's default material can't do this, which is why the non-shader
// approximations were wrong). Built-in render pipeline only; URP/HDRP
// projects need a different implementation path.
Shader "SoobakFigma2Unity/UI/ColorBlend"
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
        ColorMask [_ColorMask]

        // Snapshot the destination so the fragment shader can read it.
        GrabPass { "_UIColorBlendGrab" }

        // Standard alpha blending — the fragment already computes the final RGB,
        // source alpha only controls how strongly it replaces the destination.
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 grabPos : TEXCOORD1;
                float4 worldPosition : TEXCOORD2;
            };

            sampler2D _MainTex;
            sampler2D _UIColorBlendGrab;
            fixed4 _TextureSampleAdd;
            fixed4 _Color;
            float4 _ClipRect;

            // RGB → HSL. Components normalized to [0,1].
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

            v2f vert(appdata v)
            {
                v2f o;
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                o.color = v.color * _Color;
                o.grabPos = ComputeGrabScreenPos(o.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 src = (tex2D(_MainTex, i.texcoord) + _TextureSampleAdd) * i.color;
                fixed4 dst = tex2Dproj(_UIColorBlendGrab, i.grabPos);

                float3 srcHsl = RgbToHsl(src.rgb);
                float3 dstHsl = RgbToHsl(dst.rgb);

                // Figma COLOR: destination luminance, source hue & saturation.
                float3 blended = HslToRgb(float3(srcHsl.x, srcHsl.y, dstHsl.z));

                float clip = UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                return fixed4(blended, src.a * clip);
            }
            ENDCG
        }
    }
}
