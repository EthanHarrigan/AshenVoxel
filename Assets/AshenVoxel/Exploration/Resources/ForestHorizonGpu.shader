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
        #include "../../Rendering/AshenAtmosphere.hlsl"
        struct V { float4 positionCS:SV_POSITION; float4 uv:TEXCOORD0; half fog:TEXCOORD1; half3 normalWS:TEXCOORD2; float range:TEXCOORD3; float3 positionWS:TEXCOORD4; float3 orientation:TEXCOORD5; float3 views:TEXCOORD6; nointerpolation float4 canopy:TEXCOORD7; };
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
            // V1 canopy payload fits exactly in a float's24 integer bits:
            // crown crop, growth, correlated tint and occupancy, six bits each.
            // Zero retains the legacy single-tree path and existing GPU fixtures.
            uint packed=(uint)round(stand.style.w);bool cluster=packed>0;
            float height=cluster ? .86+.28*((packed>>6)&63)/63.0 : 1;
            float blend=ForestOverheadBlend(direction.y);float projected=ForestProjectedHeight(direction.y,height,width,blend);
            float3 position=center+right*(uv.x-.5)*span*width+up*(uv.y-.5)*span*projected;
            if(cluster)
            {
                uint slopes=(uint)round(frac(stand.style.y)*1048576);
                float2 gradient=(float2(slopes&1023,(slopes>>10)&1023)-511)/511;
                position.y+=dot(gradient,position.xz-center.xz);
            }
            float a=stand.style.z,co=cos(a),si=sin(a);
            float3 local=float3(direction.x*co-direction.z*si,direction.y,direction.x*si+direction.z*co);
            float view=fmod(floor(atan2(local.x,local.z)*1.273239545+8.5),8);
            float2 azimuth=local.xz;azimuth=dot(azimuth,azimuth)>.00000001?normalize(azimuth):float2(-si,co);
            float prototype=cluster?floor(stand.style.y):stand.style.y;
            V o;o.positionWS=position;o.orientation=float3(co,si,cluster?1:width);o.positionCS=TransformWorldToHClip(position);o.uv=ForestProjectedUV(uv,direction.y,height,width,projected,azimuth);o.views=float3(prototype*9+view,prototype*9+8,blend);
            float crop=cluster?max(.12,(packed&63)/63.0):0;
            o.canopy=float4(crop,cluster?((packed>>18)&63)/63.0:1,cluster?((packed>>12)&63)/63.0:.5,cluster?max(1,width/crop):0);
            o.fog=ComputeFogFactor(o.positionCS.z);o.normalWS=direction;o.range=distance(_WorldSpaceCameraPos,center);return o;
        }
        float CanopyHash(float2 cell,float phase)
        {return frac(sin(dot(cell,float2(127.1,311.7))+phase*73.7)*43758.5453);}
        // A far quad is a stand, not a tree stretched to terrain-tile size.
        // Reuse actual crown silhouettes at their physical width. No time/frame,
        // screen-space seed, extra geometry, atlas rebake or per-frame confetti.
        ForestViewSample Tree(V i)
        {
            clip(i.range-_HorizonNear);clip(_HorizonFar-i.range);
            float4 uv=i.uv;float cellShade=1;
            if(i.canopy.x>0)
            {
                float repeat=i.canopy.w,phase=i.canopy.z;
                float sideX=uv.x*repeat+phase;
                float2 topGrid=uv.zw*repeat+phase;
                float2 sideCell=float2(floor(sideX),0),topCell=floor(topGrid);
                float sideNoise=CanopyHash(sideCell,phase),topNoise=CanopyHash(topCell,phase);
                // Keep deliberate biome-density gaps among cropped crowns.
                // Stable view blend uses the same coverage path in every pass.
                float visible=lerp(step(sideNoise,i.canopy.y),step(topNoise,i.canopy.y),i.views.z);
                ClipMaterialStable(visible,float3(frac(topGrid)*120,phase*17));
                uv.x=(frac(sideX)-.5)*i.canopy.x+.5;
                uv.y/=lerp(.88,1,sideNoise);
                uv.zw=(frac(topGrid)-.5)*i.canopy.x+.5;
                cellShade=lerp(.97,1.03,lerp(sideNoise,topNoise,i.views.z));
            }
            ForestViewSample c=ForestSampleViews(uv,i.views,i.orientation.xy,i.orientation.z,i.normalWS);
            if(i.canopy.x>0)c.albedo*=lerp(half3(.87,.91,.83),half3(1.08,1.07,1.10),i.canopy.z)*cellShade;
            return c;
        }
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
            half4 Frag(V i):SV_Target{ForestViewSample c=Tree(i);return half4(AshenApplyFog(ForestLighting(c.albedo,c.normalAO,i.positionWS,i.positionCS,c.optics),i.fog,i.positionWS),1);}
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
