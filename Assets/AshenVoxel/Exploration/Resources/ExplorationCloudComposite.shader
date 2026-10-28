Shader "AshenVoxel/ExplorationCloudComposite"
{
    Properties
    {
        _SkyTint("Zenith tint",Color)=(.42,.53,.65,1)
        _GroundColor("Ground",Color)=(.12,.13,.12,1)
        _SunDirection("Direction toward sun",Vector)=(.4,.6,.3,0)
        _Exposure("Sky exposure multiplier",Range(0,8))=1
        _HorizonColor("Horizon",Color)=(.68,.77,.84,1)
        [HDR] _SunColor("Sun tint",Color)=(1,.92,.81,1)
        _SunAngularDiameter("Sun diameter (degrees)",Range(.1,5))=.53
        _SunRadiance("Sun disc radiance",Range(0,100))=12
        _Haze("Haze",Range(0,1))=.35
        _CloudCoverage("Cloud coverage",Range(0,1))=.42
        _CloudDensity("Cloud density",Range(0,2))=.8
        _CloudScale("Cloud frequency",Range(.25,4))=1
        _CloudWind("Wind direction (xy)",Vector)=(1,.25,0,0)
        _CloudSpeed("Wind (noise units/second)",Range(0,.1))=.012
        [Enum(Off,0,Layered,1,Volumetric,2)] _CloudQuality("Cloud quality",Float)=2
        [HDR] _CloudLightColor("Cloud lit",Color)=(1.05,1.03,.98,1)
        [HDR] _CloudShadowColor("Cloud shade",Color)=(.30,.36,.44,1)
        [NoScaleOffset] _CloudNoiseTex("AVCN64-v1 linear noise",3D)="white" {}
        [HideInInspector] _CloudNoiseReady("Noise ready",Float)=0
        [HideInInspector] _CloudVolumeExternal("External volume owns clouds",Float)=0
        [HideInInspector] _CloudDepthReady("Opaque depth available before transparents",Float)=0
        _CloudDepthBias("Opaque surface ray bias (m)",Range(0,10))=.2
        _CloudCameraData("Phase X, height above base (m), phase Z, unused",Vector)=(0,-1200,0,0)
        _CloudNoiseWorldSize("Noise repeat length at scale 1 (m)",Range(1000,20000))=6400
        _CloudThickness("Layer thickness (m)",Range(100,3000))=700
        _CloudVerticalScale("Vertical shape frequency",Range(.5,8))=3
        _CloudExtinction("Extinction per metre",Range(.001,.08))=.018
        _CloudErosion("Fine erosion",Range(0,.6))=.22
        _CloudMaxDistance("Cloud ray distance (m)",Range(3000,80000))=30000
        _CloudViewSteps("Primary steps",Range(24,64))=64
        _CloudLightSteps("Sun probes per occupied step",Range(3,5))=4
        _CloudAnisotropy("Forward scattering",Range(0,.75))=.45
        _CloudAmbientStrength("Diffuse ambient strength",Range(0,2))=1
        _CloudSunStrength("Sun lighting strength",Range(0,4))=1
        _CloudAerialDensity("Cloud distance haze per metre",Range(0,.0002))=.000035
    }
    SubShader
    {
        Tags { "Queue"="Transparent+100" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Front
            ZWrite Off
            ZTest Always
            Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "ExplorationCloudVolume.hlsl"
            struct A { float4 positionOS:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V
            {
                float4 positionCS:SV_POSITION;
                float3 positionWS:TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            V Vert(A a)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(a);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS=TransformObjectToWorld(a.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS);
                return o;
            }
            float4 Frag(V i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                // Camera owner must verify current opaque depth is available at
                // this queue. A merely allocated or stale depth texture is unsafe.
                // Perspective only: disable external mode for orthographic cameras.
                if(_CloudVolumeExternal<.5 || _CloudDepthReady<.5 ||
                   _CloudNoiseReady<.5 || _CloudQuality<1.5 || unity_OrthoParams.w>.5)
                    return float4(0,0,0,0);
                float3 ray=AVCloudSafeDirection(i.positionWS-_WorldSpaceCameraPos);
                float width=max(length(ddx(ray)),length(ddy(ray)));
                float2 uv=GetNormalizedScreenSpaceUV(i.positionCS);
                float rawDepth=SampleSceneDepth(uv);
                float limit=clamp(_CloudMaxDistance,3000,80000);
                #if UNITY_REVERSED_Z
                    bool hasOpaque=rawDepth>1e-7;
                    float ndcDepth=rawDepth;
                #else
                    bool hasOpaque=rawDepth<1-1e-7;
                    float ndcDepth=lerp(UNITY_NEAR_CLIP_VALUE,1,rawDepth);
                #endif
                if(hasOpaque)
                {
                    float3 opaqueWS=ComputeWorldSpacePosition(uv,ndcDepth,UNITY_MATRIX_I_VP);
                    float distanceToOpaque=dot(opaqueWS-_WorldSpaceCameraPos,ray);
                    limit=min(limit,max(0,distanceToOpaque-clamp(_CloudDepthBias,0,10)));
                }
                if(limit<=0) return float4(0,0,0,0);
                float3 sun=AVCloudSafeDirection(_SunDirection.xyz);
                float day=smoothstep(-.10,.08,sun.y);
                float3 radiance;
                float transmission;
                AVCloudIntegrateClouds(ray,sun,day,width,AVCloudWindPhase(),
                    float3(0,0,0),limit,radiance,transmission);
                // No horizon fade here: an observer inside/above a cloud sea must
                // see its horizontal opacity. The base sky horizon is unchanged.
                // Radiance is already premultiplied by integrated sample opacity.
                return float4(clamp(radiance*clamp(_Exposure,0,8),0,60000),
                              saturate(1-transmission));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
