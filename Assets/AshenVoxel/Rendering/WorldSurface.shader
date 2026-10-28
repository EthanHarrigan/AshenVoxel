Shader "AshenVoxel/WorldSurface"
{
    Properties
    {
        _GrainStrength("Optional cell grain", Range(0,0.35)) = 0
        _FineGrainStrength("Optional fine grain", Range(0,0.1)) = 0
        _VoxelSize("World cell size", Float) = 0.1
        _Roughness("Roughness", Range(0,1)) = 0.92
        _AmbientFloor("Minimum indirect light", Range(0,0.1)) = 0.02
        [HideInInspector] _MatterRigid("Rigid matter without foliage wind",Float)=0
        [HideInInspector] _MatterInstances("Weighted debris instances",Float)=0
        [HideInInspector] _MatterChunkMaskEnabled("Session chunk ownership clipping",Float)=0
        [HideInInspector] _MatterChunkMask("Session chunk owners",3D)="white"{}
        [HideInInspector] _MatterChunkMaskOrigin("Canonical source anchor metres",Vector)=(0,0,0,0)
        [HideInInspector] _MatterChunkMaskSize("Source chunk texture extent metres",Vector)=(1,1,1,0)
        [HideInInspector] _MatterChunkOwner("Session owner byte",Float)=1
        [HideInInspector] _MatterBreakSphere("Pending break in source local metres",Vector)=(0,0,0,0)
        [HideInInspector] _MatterBreakAmount("Transient break acknowledgement",Float)=0
        [HideInInspector] _VoxelWindStaticInstances("Static exploration instances",Float)=0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 200
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
        #include "MaterialOptics.hlsl"
        #include "AshenAtmosphere.hlsl"
        #include "VoxelWind.hlsl"
        // One material layout shared by forward, shadow, depth, normals, motion.
        CBUFFER_START(UnityPerMaterial)
            float _GrainStrength,_FineGrainStrength,_VoxelSize,_Roughness,_AmbientFloor;
            float _VoxelWindStaticInstances,_MatterInstances,_MatterRigid;
            float _MatterChunkMaskEnabled,_MatterChunkOwner;
            float4 _MatterChunkMaskOrigin,_MatterChunkMaskSize;
            float4 _MatterBreakSphere;
            float _MatterBreakAmount;
        CBUFFER_END
        TEXTURE3D(_MatterChunkMask);SAMPLER(sampler_MatterChunkMask);
        void ClipMatterChunk(float3 originalPositionOS,float3 originalNormalOS)
        {
            [branch] if(_MatterChunkMaskEnabled>.5)
            {
                // The prototype vertex remains canonical even after wind/body
                // transforms. Move inward to choose the solid face owner's cell.
                float3 uv=(originalPositionOS+_MatterChunkMaskOrigin.xyz-originalNormalOS*.0001)/max(_MatterChunkMaskSize.xyz,.0001);
                clip(uv);clip(1-uv);clip(_MatterChunkOwner-.5);
                float owner=round(SAMPLE_TEXTURE3D_LOD(_MatterChunkMask,sampler_MatterChunkMask,uv,0).r*255);
                clip(.25-abs(owner-_MatterChunkOwner));
            }
        }
        UNITY_INSTANCING_BUFFER_START(MatterAppearance)
            UNITY_DEFINE_INSTANCED_PROP(float4,_MatterTint)
        UNITY_INSTANCING_BUFFER_END(MatterAppearance)
        half4 MatterColor(half4 vertexColor)
        {
            return _MatterInstances>.5 ? UNITY_ACCESS_INSTANCED_PROP(MatterAppearance,_MatterTint) : vertexColor;
        }
        struct Attributes {
            float4 positionOS:POSITION;
            float3 normalOS:NORMAL;
            half4 color:COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings {
            float4 positionCS:SV_POSITION;
            float3 positionWS:TEXCOORD0;
            half3 normalWS:TEXCOORD1;
            half4 color:COLOR;
            half fog:TEXCOORD2;
            float3 patternPositionOS:TEXCOORD3;
            half3 normalOS:TEXCOORD4;
            half2 optics:TEXCOORD5;
            nointerpolation uint material:TEXCOORD6;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings Vert(Attributes input)
        {
            Varyings output=(Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            input.color=MatterColor(input.color);
            UNITY_TRANSFER_INSTANCE_ID(input,output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.material=VertexMaterialId(input.color.a);
            float3 positionWS,normalWS;
            VoxelWindVertex(input.positionOS.xyz,input.normalOS,output.material,positionWS,normalWS);
            if(_MatterRigid>.5){positionWS=TransformObjectToWorld(input.positionOS.xyz);normalWS=TransformObjectToWorldNormal(input.normalOS);}
            output.positionWS=positionWS;output.normalWS=normalWS;
            output.positionCS=TransformWorldToHClip(positionWS);
            output.patternPositionOS=input.positionOS.xyz;output.normalOS=input.normalOS;
            output.color=input.color;
            half3 optics=VertexOptics(input.color.a);output.optics=optics.xy;output.color.a=optics.z;
            #if !defined(UNITY_COLORSPACE_GAMMA)
            output.color.rgb=SRGBToLinear(output.color.rgb);
            #endif
            output.fog=ComputeFogFactor(output.positionCS.z);
            return output;
        }
        void ClipView(Varyings input)
        {
            ClipMatterChunk(input.patternPositionOS,input.normalOS);
            ClipMaterialStable(MaterialViewOpacity(input.optics.x,input.material,input.positionWS),input.patternPositionOS);
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit" Tags { "LightMode"="UniversalForward" }
            Cull Back ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling
            float Hash(float3 p)
            {
                p = frac(p * 0.1031); p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                ClipMatterChunk(input.patternPositionOS,input.normalOS);
                ClipMaterialStable(MaterialViewOpacity(input.optics.x,input.material,input.positionWS),input.patternPositionOS);
                half3 normal = normalize(input.normalWS);
                // Optional detail is attached to the prototype, so camera-origin
                // rebasing cannot replace it with an unrelated world-space hash.
                float3 cell = floor((input.patternPositionOS - input.normalOS * 0.0001) / max(0.001, _VoxelSize));
                float3 footprint = fwidth(input.patternPositionOS);
                float pixelSize = max(footprint.x, max(footprint.y, footprint.z));
                float cellFade = saturate(1.0 - pixelSize / max(0.001, _VoxelSize));
                half grain = (Hash(cell) - 0.5) * _GrainStrength * cellFade;
                [branch] if (_FineGrainStrength > 0.00001)
                {
                    float fineFade = saturate(1.0 - pixelSize * 100.0);
                    grain += (Hash(floor(input.patternPositionOS * 65.0)) - 0.5) * _FineGrainStrength * fineFade;
                }
                half3 albedo = saturate(input.color.rgb * (1.0h + grain));
                // Render-only acknowledgement on the active source. Keep the
                // 10cm cell pattern attached to the prop through yaw/rebasing;
                // no geometry, intrinsic pigment, depth or shadow modification.
                half breaking = 0;
                [branch] if (_MatterBreakAmount > .0001)
                {
                    float3 faceCell = (cell + .5) * max(.001, _VoxelSize);
                    float distanceToCut = distance(faceCell, _MatterBreakSphere.xyz);
                    breaking = saturate((_MatterBreakSphere.w - distanceToCut) / .08) * _MatterBreakAmount;
                    albedo = lerp(albedo, half3(1.0h, .32h, .055h), breaking * .7h);
                }
                half3 view = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half alpha = 1;
                BRDFData brdf;
                InitializeBRDFData(albedo, 0.0h, half3(0.04h,0.04h,0.04h), 1.0h - saturate(_Roughness), alpha, brdf);
                AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(input.positionCS));
                // Both terms estimate overlapping occlusion. Taking their minimum
                // avoids multiplying two unrelated dark rims into baked pigment.
                half indirectOcclusion = min(lerp(.45h,1.0h,saturate(input.color.a)),lerp(.35h,1.0h,ao.indirectAmbientOcclusion));
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                Light sun = GetMainLight(shadowCoord);
                half3 color = brdf.diffuse * max(MaterialAmbient(normal), _AmbientFloor.xxx) * indirectOcclusion;
                color += LightingPhysicallyBased(brdf, sun, normal, view) * ao.directAmbientOcclusion;
                color += MaterialTransmission(albedo,normal,sun,input.optics.y);
                #ifdef _ADDITIONAL_LIGHTS
                uint count = GetAdditionalLightsCount();
                for (uint index = 0u; index < count; index++)
                {
                    Light light = GetAdditionalLight(index, input.positionWS, half4(1,1,1,1));
                    color += LightingPhysicallyBased(brdf, light, normal, view) * ao.directAmbientOcclusion;
                    color += MaterialTransmission(albedo,normal,light,input.optics.y);
                }
                #endif
                color += half3(.24h, .065h, .008h) * breaking;
                return half4(AshenApplyFog(color, input.fog, input.positionWS), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster" Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection,_LightPosition;
            Varyings ShadowVert(Attributes input)
            {
                Varyings output=Vert(input);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirection=normalize(_LightPosition-output.positionWS);
                #else
                float3 lightDirection=_LightDirection;
                #endif
                output.positionCS=TransformWorldToHClip(ApplyShadowBias(output.positionWS,output.normalWS,lightDirection));
                #if UNITY_REVERSED_Z
                output.positionCS.z=min(output.positionCS.z,UNITY_NEAR_CLIP_VALUE*output.positionCS.w);
                #else
                output.positionCS.z=max(output.positionCS.z,UNITY_NEAR_CLIP_VALUE*output.positionCS.w);
                #endif
                return output;
            }
            half4 ShadowFrag(Varyings input):SV_Target
            {
                ClipMatterChunk(input.patternPositionOS,input.normalOS);
                ClipMaterialStable(MaterialShadowCoverage(input.optics.x,input.optics.y),input.patternPositionOS);return 0;
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly" Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling
            half4 DepthFrag(Varyings input):SV_Target{ClipView(input);return 0;}
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals" Tags { "LightMode"="DepthNormals" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment NormalFrag
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
            half4 NormalFrag(Varyings input):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                ClipView(input);
                float3 normalWS=normalize(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormalWS=PackNormalOctQuadEncode(normalWS);
                return half4(PackFloat2To888(saturate(octNormalWS*.5+.5)),0);
                #else
                return half4(normalWS,0);
                #endif
            }
            ENDHLSL
        }
        Pass
        {
            Name "MotionVectors" Tags { "LightMode"="MotionVectors" }
            Cull Back ZWrite On ColorMask RG
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex MotionVert
            #pragma fragment MotionFrag
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MotionVectorsCommon.hlsl"
            struct MotionVaryings {
                float4 positionCS:SV_POSITION;
                float4 currentCS:TEXCOORD0;
                float4 previousCS:TEXCOORD1;
                float3 patternPositionOS:TEXCOORD2;
                float3 positionWS:TEXCOORD3;
                half opacity:TEXCOORD4;
                nointerpolation uint material:TEXCOORD5;
                half3 originalNormalOS:TEXCOORD6;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };
            MotionVaryings MotionVert(Attributes input)
            {
                MotionVaryings o=(MotionVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);UNITY_TRANSFER_INSTANCE_ID(input,o);
                input.color=MatterColor(input.color);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                uint material=VertexMaterialId(input.color.a);
                float3 currentRoot=TransformObjectToWorld(float3(0,0,0));
                float3 unbent=TransformObjectToWorld(input.positionOS.xyz);
                float3 current=VoxelWindPosition(unbent,currentRoot,material,_VoxelWindTime,_VoxelWindState);
                float3 previousUnbent=mul(UNITY_PREV_MATRIX_M,input.positionOS).xyz;
                float3 previousRoot=mul(UNITY_PREV_MATRIX_M,float4(0,0,0,1)).xyz;
                if(_VoxelWindStaticInstances>.5)
                {
                    // RenderMeshInstanced matrix arrays do not retain per-instance
                    // previous matrices. Placements are immutable except rebasing.
                    previousUnbent=unbent+_VoxelWindOriginDelta.xyz;
                    previousRoot=currentRoot+_VoxelWindOriginDelta.xyz;
                }
                float3 previous=VoxelWindPosition(previousUnbent,previousRoot,material,_VoxelWindPreviousTime,_VoxelWindPreviousState);
                if(_MatterRigid>.5){current=unbent;previous=previousUnbent;}
                o.positionCS=TransformWorldToHClip(current);
                o.currentCS=mul(_NonJitteredViewProjMatrix,float4(current,1));
                o.previousCS=mul(_PrevViewProjMatrix,float4(previous,1));
                o.positionWS=current;o.patternPositionOS=input.positionOS.xyz;
                o.originalNormalOS=input.normalOS;
                o.opacity=VertexOptics(input.color.a).x;o.material=material;
                return o;
            }
            float4 MotionFrag(MotionVaryings input):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                ClipMatterChunk(input.patternPositionOS,input.originalNormalOS);
                ClipMaterialStable(MaterialViewOpacity(input.opacity,input.material,input.positionWS),input.patternPositionOS);
                return float4(CalcNdcMotionVectorFromCsPositions(input.currentCS,input.previousCS),0,0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
