Shader "AshenVoxel/ForestImpostor"
{
    Properties { [MainTexture] _BaseMap("Prototype views",2D)="white" {} _NormalMap("Prototype normals and AO",2D)="bump" {} _AtlasLightingVersion("Atlas lighting version",Float)=0 _HorizonNear("Horizon start",Float)=0 _HorizonFar("Horizon end",Float)=100000 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off ZWrite On
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
        float _HorizonNear,_HorizonFar,_AtlasLightingVersion;
        CBUFFER_END
        #include "ForestImpostorLighting.hlsl"
        #include "../Rendering/AshenAtmosphere.hlsl"
        struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; float4 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct V { float4 positionCS:SV_POSITION; float4 uv:TEXCOORD0; half fog:TEXCOORD1; half3 normalWS:TEXCOORD2; float distance:TEXCOORD3; float3 positionWS:TEXCOORD4; float2 yaw:TEXCOORD5; float3 views:TEXCOORD6; UNITY_VERTEX_OUTPUT_STEREO };
        V Vert(A a)
        {
            V o=(V)0;UNITY_SETUP_INSTANCE_ID(a);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            float3 center=TransformObjectToWorld(float3(0,a.uv.w,0));
            float3 direction=SafeNormalize(_WorldSpaceCameraPos-center);
            o.distance=distance(_WorldSpaceCameraPos,center);
            float3 right=SafeNormalize(cross(float3(0,1,0),direction)+float3(0.00001,0,0));
            float3 up=SafeNormalize(cross(direction,right));
            float3x3 transform=(float3x3)GetObjectToWorldMatrix();
            float sx=length(float3(transform[0][0],transform[1][0],transform[2][0]));
            float sy=length(float3(transform[0][1],transform[1][1],transform[2][1]));
            float sz=length(float3(transform[0][2],transform[1][2],transform[2][2]));
            float blend=ForestOverheadBlend(direction.y);float projected=ForestProjectedHeight(direction.y,sy,sz,blend);
            float3 position=center+right*a.positionOS.x*sx+up*(a.positionOS.y-a.uv.w)*projected;
            o.positionWS=position;float3 axis=TransformObjectToWorldDir(float3(0,0,1));o.yaw=normalize(float2(axis.z,axis.x));o.positionCS=TransformWorldToHClip(position);o.fog=ComputeFogFactor(o.positionCS.z);o.normalWS=direction;
            float3 local=TransformWorldToObjectDir(direction);
            float view=fmod(floor(atan2(local.x,local.z)*1.273239545+8.5),8);
            float2 azimuth=local.xz;azimuth=dot(azimuth,azimuth)>.00000001?normalize(azimuth):float2(-o.yaw.y,o.yaw.x);
            o.views=float3(a.uv.z*9+view,a.uv.z*9+8,blend);
            o.uv=ForestProjectedUV(a.uv.xy,direction.y,sy,sz,projected,azimuth);
            return o;
        }
        // Distant atlas coverage is attached to atlas coordinates; no screen/time noise.
        ForestViewSample Sample(V i){clip(i.distance-_HorizonNear);clip(_HorizonFar-i.distance);return ForestSampleViews(i.uv,i.views,i.yaw,1,i.normalWS);}
        ENDHLSL
        Pass
        {
            Name "ForwardLit" Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            half4 Frag(V i):SV_Target { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);ForestViewSample c=Sample(i);return half4(AshenApplyFog(ForestLighting(c.albedo,c.normalAO,i.positionWS,i.positionCS,c.optics),i.fog,i.positionWS),1); }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly" Tags { "LightMode"="DepthOnly" } ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling
            half4 Frag(V i):SV_Target {Sample(i);return 0;}
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals" Tags { "LightMode"="DepthNormals" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
            half4 Frag(V i):SV_Target
            {
                ForestViewSample c=Sample(i);float3 n=c.normalAO.xyz;
                #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct=PackNormalOctQuadEncode(n);return half4(PackFloat2To888(saturate(oct*.5+.5)),0);
                #else
                return half4(n,0);
                #endif
            }
            ENDHLSL
        }
    }
}
