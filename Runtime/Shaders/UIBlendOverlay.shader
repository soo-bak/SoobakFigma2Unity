Shader "SoobakFigma2Unity/UI/Overlay"
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
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        GrabPass { "_GrabTexture" }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
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
                float4 worldPosition : TEXCOORD1;
                float4 grabPos : TEXCOORD2;
            };

            sampler2D _MainTex;
            sampler2D _GrabTexture;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;

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

            // Overlay blend: if base < 0.5: 2*base*blend, else 1-2*(1-base)*(1-blend)
            fixed overlay(fixed base, fixed blend)
            {
                return base < 0.5
                    ? 2.0 * base * blend
                    : 1.0 - 2.0 * (1.0 - base) * (1.0 - blend);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 src = (tex2D(_MainTex, i.texcoord) + _TextureSampleAdd) * i.color;
                fixed4 dst = tex2Dproj(_GrabTexture, i.grabPos);

                fixed4 result;
                result.r = overlay(dst.r, src.r);
                result.g = overlay(dst.g, src.g);
                result.b = overlay(dst.b, src.b);
                result.a = src.a;

                result.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);

                // Lerp between dst and blended result by source alpha
                result.rgb = lerp(dst.rgb, result.rgb, src.a);
                return result;
            }
            ENDCG
        }
    }
}
