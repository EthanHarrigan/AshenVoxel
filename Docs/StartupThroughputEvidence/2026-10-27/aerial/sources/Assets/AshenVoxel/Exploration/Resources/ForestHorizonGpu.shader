Shader "AshenVoxel/ForestHorizonGpu"
{
    Properties { [MainTexture] _BaseMap("Actual tree views",2D)="white" {} _NormalMap("Prototype normals and AO",2D)="bump" {} _AtlasLightingVersion("Atlas lighting version",Float)=0 _HorizonNear("Near",Float)=0 _HorizonFar("Far",Float)=100000 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off ZWrite On
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawArgs
        #include "UnityIndirect.cginc"
        struct ForestInstance { float4 positionSpan; float4 style; };
        StructuredBuffer<ForestInstance> _ForestInstances;
        StructuredBuffer<uint> _VisibleForest;
        TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
        float4 _SnapshotOffset;
        CBUFFER_START(UnityPerMaterial)
        float _HorizonNear,_HorizonFar,_AtlasLightingVersion;
        CBUFFER_END
        #include "../ForestImpostorLighting.hlsl"
        struct V { float4 positionCS:SV_POSITION; float4 uv:TEXCOORD0; half fog:TEXCOORD1; half3 normalWS:TEXCOORD2; float range:TEXCOORD3; float3 positionWS:TEXCOORD4; float3 orientation:TEXCOORD5; float3 views:TEXCOORD6; };
        V Vert(uint vertexID:SV_VertexID,uint instanceID:SV_InstanceID)
        {
            InitIndirectDrawArgs(0);
            ForestInstance stand=_ForestInstances[_VisibleForest[GetIndirectInstanceID(instanceID)]];
            const float2 corners[6]={float2(0,0),float2(1,0),float2(1,1),float2(0,0),float2(1,1),float2(0,1)};
            float2 uv=corners[GetIndirectVertexID(vertexID)];
            float3 center=stand.positionSpan.xyz+_SnapshotOffset.xyz;
            float3 direction=SafeNormalize(_WorldSpaceCameraPos-center);
            float3 right=SafeNormalize(cross(float3(0,1,0),direction)+float3(.00001,0,0));
            float3 up=SafeNormalize(cross(direction,right));
            float width=stand.style.x,span=stand.positionSpan.w;
            float blend=ForestOverheadBlend(direction.y);float projected=ForestProjectedHeight(direction.y,1,width,blend);
            float3 position=center+right*(uv.x-.5)*span*width+up*(uv.y-.5)*span*projected;
            float a=stand.style.z,co=cos(a),si=sin(a);
            float3 local=float3(direction.x*co-direction.z*si,direction.y,direction.x*si+direction.z*co);
            float view=fmod(floor(atan2(local.x,local.z)*1.273239545+8.5),8);
            float2 azimuth=local.xz;azimuth=dot(azimuth,azimuth)>.00000001?normalize(azimuth):float2(-si,co);
            V o;o.positionWS=position;o.orientation=float3(co,si,width);o.positionCS=TransformWorldToHClip(position);o.uv=ForestProjectedUV(uv,direction.y,1,width,projected,azimuth);o.views=float3(stand.style.y*9+view,stand.style.y*9+8,blend);
            o.fog=ComputeFogFactor(o.positionCS.z);o.normalWS=direction;o.range=distance(_WorldSpaceCameraPos,center);return o;
        }
        // Far stand wind is subpixel; keep geometry static and coverage atlas-anchored.
        ForestViewSample Tree(V i){clip(i.range-_HorizonNear);clip(_HorizonFar-i.range);return ForestSampleViews(i.uv,i.views,i.orientation.xy,i.orientation.z,i.normalWS);}
        ENDHLSL
        Pass
        {
            Name "ForwardLit" Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            half4 Frag(V i):SV_Target{ForestViewSample c=Tree(i);return half4(MixFog(ForestLighting(c.albedo,c.normalAO,i.positionWS,i.positionCS,c.optics),i.fog),1);}
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly" Tags { "LightMode"="DepthOnly" } ColorMask R
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(V i):SV_Target{Tree(i);return 0;}
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals" Tags { "LightMode"="DepthNormals" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
            half4 Frag(V i):SV_Target
            {
                ForestViewSample c=Tree(i);float3 normal=c.normalAO.xyz;
                #if defined(_GBUFFER_NORMALS_OCT)
                return half4(PackFloat2To888(saturate(PackNormalOctQuadEncode(normal)*.5+.5)),0);
                #else
                return half4(normal,0);
                #endif
            }
            ENDHLSL
        }
    }
}
