#ifndef ASHEN_FOREST_LIGHTING
#define ASHEN_FOREST_LIGHTING
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "../Rendering/MaterialOptics.hlsl"
TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
half4 ForestNormalAO(float2 uv,float2 yaw,float width,half3 fallback)
{
    if(_AtlasLightingVersion<1)return half4(fallback,1);
    half4 encoded=SAMPLE_TEXTURE2D(_NormalMap,sampler_NormalMap,uv);
    half3 n=encoded.xyz*2-1;n.xz/=max(.01,width);
    n=half3(n.x*yaw.x+n.z*yaw.y,n.y,-n.x*yaw.y+n.z*yaw.x);
    return half4(SafeNormalize(n),_AtlasLightingVersion>=3?VertexOptics(SAMPLE_TEXTURE2D_LOD(_NormalMap,sampler_PointClamp,uv,0).a).z:encoded.a);
}
half2 ForestOptics(float2 uv)
{
    return _AtlasLightingVersion>=3?VertexOptics(SAMPLE_TEXTURE2D_LOD(_NormalMap,sampler_PointClamp,uv,0).a).xy:half2(1,0);
}

// Existing atlas: eight horizontal views and one true overhead view. Project
// each capture onto the same camera-facing quad, then merge near the pole only.
// A single coverage test is shared by forward, depth and normals; no extra draw.
float ForestOverheadBlend(float elevation)
{
    return smoothstep(0.970295726,0.999390827,elevation); // sin(76), sin(88)
}
float ForestProjectedHeight(float elevation,float height,float width,float blend)
{
    return max(height*sqrt(saturate(1-elevation*elevation)),width*abs(elevation));
}
float4 ForestProjectedUV(float2 uv,float elevation,float height,float width,float projected,float2 localAzimuth)
{
    float sideHeight=max(.001,height*sqrt(saturate(1-elevation*elevation)));
    float topHeight=max(.001,width*abs(elevation));
    float2 side=float2(uv.x,(uv.y-.5)*projected/sideHeight+.5);
    // The overhead camera was baked with world +Z as image-up. Undo camera
    // azimuth/instance yaw so its canopy remains attached to the same tree.
    float2 right=float2(localAzimuth.y,-localAzimuth.x);
    float2 top=.5+right*(uv.x-.5)-localAzimuth*((uv.y-.5)*projected/topHeight);
    return float4(side,top);
}
float2 ForestAtlasUV(float tile,float2 localUV)
{
    return (float2(fmod(tile,12),floor(tile/12))+lerp(.002,.998,saturate(localUV)))/12;
}
half ForestInsideView(float2 uv)
{
    return step(0,uv.x)*step(uv.x,1)*step(0,uv.y)*step(uv.y,1);
}
struct ForestViewSample {half3 albedo;half4 normalAO;half2 optics;};
ForestViewSample ForestSampleViews(float4 localUV,float3 views,float2 yaw,float width,half3 fallback)
{
    float2 sideUV=ForestAtlasUV(views.x,localUV.xy),topUV=ForestAtlasUV(views.y,localUV.zw);
    half blend=views.z;half4 side=0,top=0;
    // Uniform per instance: ordinary oblique and overhead cases retain one
    // albedo sample; only the narrow transition reads both atlas views.
    [branch] if(blend<1)side=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,sideUV)*ForestInsideView(localUV.xy);
    [branch] if(blend>0)top=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,topUV)*ForestInsideView(localUV.zw);
    half2 visible=step(.38h,half2(side.a,top.a));
    // Blending binary-alpha images with a fixed cutoff merely moves the pop to
    // blend=.38. Preserve all overlapping coverage and distribute changing
    // edge coverage over stable atlas cells instead. No frame/screen noise.
    [branch] if(blend>0&&blend<1)ClipMaterialStable(dot(half2(1-blend,blend),visible),float3(topUV*120,31));
    else clip(lerp(side.a,top.a,blend)-.38);
    half2 weights=half2((1-blend)*side.a,blend*top.a)*visible;half coverage=weights.x+weights.y;
    weights/=max(.001h,coverage);
    ForestViewSample result;result.albedo=side.rgb*weights.x+top.rgb*weights.y;
    half4 sideNormal=half4(fallback,1),topNormal=sideNormal;half2 sideOptics=half2(1,0),topOptics=sideOptics;
    [branch] if(weights.x>0){sideNormal=ForestNormalAO(sideUV,yaw,width,fallback);sideOptics=ForestOptics(sideUV);}
    [branch] if(weights.y>0){topNormal=ForestNormalAO(topUV,yaw,width,fallback);topOptics=ForestOptics(topUV);}
    result.normalAO=sideNormal*weights.x+topNormal*weights.y;result.normalAO.xyz=SafeNormalize(result.normalAO.xyz);
    // Decode each point-sampled material byte before blending physical optics.
    result.optics=sideOptics*weights.x+topOptics*weights.y;
    ClipMaterialStable(result.optics.x,float3(lerp(sideUV,topUV,blend)*120,0));
    return result;
}

half3 ForestLighting(half3 albedo,half4 normalAO,float3 positionWS,float4 positionCS,half2 optics)
{
    if(_AtlasLightingVersion<1)return albedo;
    BRDFData brdf;half alpha=1;
    InitializeBRDFData(albedo,0,half3(.04,.04,.04),.08,alpha,brdf);
    AmbientOcclusionFactor ao=GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(positionCS));
    #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
    float4 shadowCoord=ComputeScreenPos(TransformWorldToHClip(positionWS));
    #else
    float4 shadowCoord=TransformWorldToShadowCoord(positionWS);
    #endif
    Light sun=GetMainLight(shadowCoord);
    half3 color=brdf.diffuse*max(MaterialAmbient(normalAO.xyz),half3(.02,.02,.02))*min(lerp(.45h,1.0h,normalAO.a),lerp(.35h,1.0h,ao.indirectAmbientOcclusion));
    color+=LightingPhysicallyBased(brdf,sun,normalAO.xyz,SafeNormalize(GetWorldSpaceViewDir(positionWS)))*ao.directAmbientOcclusion;
    color+=MaterialTransmission(albedo,normalAO.xyz,sun,optics.y);
    return color;
}
#endif
