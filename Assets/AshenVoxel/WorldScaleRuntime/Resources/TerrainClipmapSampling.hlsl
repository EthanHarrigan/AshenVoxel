#ifndef ASHEN_TERRAIN_CLIPMAP_SAMPLING
#define ASHEN_TERRAIN_CLIPMAP_SAMPLING
// World lattice origins remain 64-bit integers on the CPU. Only origin-relative
// metres, small local coordinates, and integer torus phases reach this shader.
Texture2DArray<float> _CmHeights;
Texture2DArray<float4> _CmTint, _CmAppearance;
SamplerState sampler_linear_repeat;
float4 _CmRect;                 // local x,z and quad width,height (actual ring rectangle)
float4 _CmLevel;                // spacing, T, physical slice, parent physical slice
float4 _CmOrigin;               // render-relative x,z; positive torus x,z
float4 _CmParentOrigin;
float4 _CmParent;               // parent spacing, has parent
float4 _CmParentOffset;         // exact integer (a/2-parentA,b/2-parentB)
float4 _CmMorph;                // transition width, fully-morphed edge inset
float _CmOriginY;
int _CmVisibleCount;
float4 _CmVisibleLevel[16];     // spacing, physical slice
float4 _CmVisibleOrigin[16];

int2 CmTexel(int2 local, float2 torus)
{ return (local + (int2)torus) & ((int)_CmLevel.y - 1); }
float CmLoad(int slice, int2 local, float2 torus)
{ return _CmHeights.Load(int4(CmTexel(local, torus), slice, 0)); }
float2 CmUV(float2 local, float2 torus)
{ return (local + torus + .5) / _CmLevel.y; }
float CmLinearHeight(int slice, float2 local, float2 torus)
{ return _CmHeights.SampleLevel(sampler_linear_repeat, float3(CmUV(local, torus), slice), 0); }

// Matches TerrainClipmapMath.TriangleHeight and the emitted (00,01,11),(00,11,10)
// triangles. Bilinear interpolation is deliberately NOT used for geometry.
float CmTriangleHeight(int slice, float2 local, float2 torus)
{
    int2 q = (int2)floor(local); float2 f = local - q;
    float h00 = CmLoad(slice, q, torus), h11 = CmLoad(slice, q + int2(1, 1), torus);
    if (f.x >= f.y)
    {
        float h10 = CmLoad(slice, q + int2(1, 0), torus);
        return h00 + f.x * (h10 - h00) + f.y * (h11 - h10);
    }
    float h01 = CmLoad(slice, q + int2(0, 1), torus);
    return h00 + f.y * (h01 - h00) + f.x * (h11 - h01);
}
float CmMorphAt(float2 local, bool hasParent)
{
    if (!hasParent) return 0;
    float2 d = min(local - 2, _CmLevel.y - 2 - local);
    return saturate(1 - (min(d.x, d.y) - _CmMorph.y) / _CmMorph.x);
}
float2 CmParentLocal(float2 local) { return local * .5 + _CmParentOffset.xy; }
float2 CmVertexLocal(uint vertexID)
{
    uint quad = vertexID / 6u, corner = vertexID % 6u;
    // Clockwise from above (Unity's front face), with one stable 00-to-11 diagonal.
    uint2 offset = corner == 0u || corner == 3u ? uint2(0, 0) :
        corner == 1u ? uint2(0, 1) : corner == 5u ? uint2(1, 0) : uint2(1, 1);
    uint width = (uint)_CmRect.z;
    return _CmRect.xy + float2(quad % width, quad / width) + offset;
}
float3 CmPosition(float2 local)
{
    float alpha = CmMorphAt(local, _CmParent.y > .5);
    float height = CmLoad((int)_CmLevel.z, (int2)local, _CmOrigin.zw);
    float2 xz = _CmOrigin.xy + local * _CmLevel.x;
    if (_CmParent.y > .5 && alpha > 0)
    {
        float2 parent = CmParentLocal(local);
        height = lerp(height, CmTriangleHeight((int)_CmLevel.w, parent, _CmParentOrigin.zw), alpha);
        // Use the same arithmetic as the coarse vertices at shared edges. This
        // avoids introducing two distinct float positions for the same boundary.
        xz = lerp(xz, _CmParentOrigin.xy + parent * _CmParent.x, alpha);
    }
    return float3(xz.x, height - _CmOriginY, xz.y);
}
float3 CmLevelNormal(int slice, float2 local, float2 torus, float spacing)
{
    float dx = CmLinearHeight(slice, local + float2(1, 0), torus) - CmLinearHeight(slice, local - float2(1, 0), torus);
    float dz = CmLinearHeight(slice, local + float2(0, 1), torus) - CmLinearHeight(slice, local - float2(0, 1), torus);
    return normalize(float3(-dx, 2 * spacing, -dz));
}
half3 CmNormal(float2 local)
{
    float3 normal = CmLevelNormal((int)_CmLevel.z, local, _CmOrigin.zw, _CmLevel.x);
    float alpha = CmMorphAt(local, _CmParent.y > .5);
    if (alpha > 0)
        normal = normalize(lerp(normal, CmLevelNormal((int)_CmLevel.w, CmParentLocal(local), _CmParentOrigin.zw, _CmParent.x), alpha));
    return (half3)normal;
}
half3 CmTint(int slice, float2 local, float2 torus)
{
    half3 color = (half3)_CmTint.SampleLevel(sampler_linear_repeat, float3(CmUV(local, torus), slice), 0).rgb;
    #if !defined(UNITY_COLORSPACE_GAMMA)
    color = SRGBToLinear(color);
    #endif
    return color;
}
struct CmSurfaceData { half3 color; half roughness; half wetness; half snow; uint material; };
CmSurfaceData CmSurface(float2 local)
{
    CmSurfaceData value;
    int slice = (int)_CmLevel.z;
    float4 a = _CmAppearance.SampleLevel(sampler_linear_repeat, float3(CmUV(local, _CmOrigin.zw), slice), 0);
    // Material IDs are categorical. Nearest-neighbour fetch before decoding;
    // filtering them would invent materials absent from every contributing texel.
    float category = _CmAppearance.Load(int4(CmTexel((int2)floor(local + .5), _CmOrigin.zw), slice, 0)).a;
    value.color = CmTint(slice, local, _CmOrigin.zw);
    float alpha = CmMorphAt(local, _CmParent.y > .5);
    if (alpha > 0)
    {
        float2 parent = CmParentLocal(local); int parentSlice = (int)_CmLevel.w;
        float4 coarse = _CmAppearance.SampleLevel(sampler_linear_repeat, float3(CmUV(parent, _CmParentOrigin.zw), parentSlice), 0);
        a.rgb = lerp(a.rgb, coarse.rgb, alpha);
        value.color = lerp(value.color, CmTint(parentSlice, parent, _CmParentOrigin.zw), (half)alpha);
        if (alpha >= .5)
            category = _CmAppearance.Load(int4(CmTexel((int2)floor(parent + .5), _CmParentOrigin.zw), parentSlice, 0)).a;
    }
    uint id = (uint)round(category * 255);
    value.material = id >= 128u && id < 153u ? id - 128u : 31u;
    value.roughness = (half)a.r; value.wetness = (half)a.g; value.snow = (half)a.b;
    return value;
}
// Pigment uses the exact world-grid block centre, with the finest admitted
// published level and the same edge morph. No screen-derivative extrapolation.
half3 CmPigment(float2 renderXZ, half3 fallback)
{
    [loop] for (int i = 0; i < _CmVisibleCount; i++)
    {
        float4 origin = _CmVisibleOrigin[i], level = _CmVisibleLevel[i];
        float2 local = (renderXZ - origin.xy) / level.x;
        if (any(local < 2) || any(local > _CmLevel.y - 2)) continue;
        half3 color = CmTint((int)level.y, local, origin.zw);
        float alpha = CmMorphAt(local, i + 1 < _CmVisibleCount);
        if (alpha > 0)
        {
            float4 parentOrigin = _CmVisibleOrigin[i + 1], parent = _CmVisibleLevel[i + 1];
            color = lerp(color, CmTint((int)parent.y, (renderXZ - parentOrigin.xy) / parent.x, parentOrigin.zw), (half)alpha);
        }
        return color;
    }
    return fallback;
}
#endif
