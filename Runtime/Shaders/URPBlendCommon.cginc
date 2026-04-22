// Shared boilerplate for SoobakFigma2Unity/URP/Blend* shaders.
//
// Each shader picks a chroma blend mode by #define-ing one of:
//   SOOBAK_BLEND_VARIANT_COLOR, _HUE, _SATURATION, _DARKEN, _LIGHTEN
// before including this file. The variant gates which arm of
// SoobakChromaBlend() is compiled. Everything else (vertex,
// _UISceneColor sampling, alpha composite, UI clip/alphaclip) is
// identical across variants and lives here to avoid duplication.
//
// Required CGPROGRAM context (declare in the shader's Pass):
//   #pragma vertex vert
//   #pragma fragment frag
//   #pragma target 2.0
//   #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
//   #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

#ifndef SOOBAK_URP_BLEND_COMMON_INCLUDED
#define SOOBAK_URP_BLEND_COMMON_INCLUDED

#include "UnityCG.cginc"
#include "UnityUI.cginc"

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
// Defaults to black if the feature didn't run; the shader still
// produces a deterministic (if visually wrong) result in that case.
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

// --- HSL helpers (Figma chroma-blend spec) -----------------------
// Figma defines its chroma blends in non-linear (sRGB) HSL, so we
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
// -----------------------------------------------------------------

// Per-mode chroma blend. The variant macro is set by the .shader.
half3 SoobakChromaBlend(half3 src, half3 dst)
{
    #if defined(SOOBAK_BLEND_VARIANT_COLOR)
        // COLOR: src hue + src saturation + dst luminance
        float3 s = RgbToHsl(src);
        float3 d = RgbToHsl(dst);
        return HslToRgb(float3(s.x, s.y, d.z));
    #elif defined(SOOBAK_BLEND_VARIANT_HUE)
        // HUE: src hue + dst saturation + dst luminance
        float3 s = RgbToHsl(src);
        float3 d = RgbToHsl(dst);
        return HslToRgb(float3(s.x, d.y, d.z));
    #elif defined(SOOBAK_BLEND_VARIANT_SATURATION)
        // SATURATION: dst hue + src saturation + dst luminance
        float3 s = RgbToHsl(src);
        float3 d = RgbToHsl(dst);
        return HslToRgb(float3(d.x, s.y, d.z));
    #elif defined(SOOBAK_BLEND_VARIANT_DARKEN)
        // DARKEN: per-channel min, no HSL roundtrip needed.
        return min(src, dst);
    #elif defined(SOOBAK_BLEND_VARIANT_LIGHTEN)
        // LIGHTEN: per-channel max.
        return max(src, dst);
    #else
        // No variant defined — fall back to passthrough so a missing
        // #define produces a visible (and obviously-wrong) result.
        return src;
    #endif
}

fixed4 frag(v2f IN) : SV_Target
{
    half4 src = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;

    // Sample the previously-rendered UI as the blend destination.
    // screenPos is clip-space scaled; divide by w to get 0..1 UV.
    float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
    half3 dst = tex2D(_UISceneColor, screenUV).rgb;

    half3 blended = SoobakChromaBlend(saturate(src.rgb), saturate(dst));

    // Alpha-blend the chroma-replaced pixel onto destination by src.a so
    // partially-transparent rectangles still let the original UI show
    // through proportionally.
    half3 outRgb = lerp(dst, blended, src.a);
    half4 color = half4(outRgb, src.a);

    #ifdef UNITY_UI_CLIP_RECT
    color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
    #endif

    #ifdef UNITY_UI_ALPHACLIP
    clip(color.a - 0.001);
    #endif

    return color;
}

#endif // SOOBAK_URP_BLEND_COMMON_INCLUDED
