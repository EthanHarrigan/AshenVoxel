Shader "AshenVoxel/GroundSurface"
{
    Properties
    {
        [HideInInspector] _SurfaceOriginPhase("Surface origin phase", Vector) = (0,0,0,0)
        _GrainStrength("Optional cell grain", Range(0,0.35)) = 0.025
        _FineGrainStrength("Optional fine grain", Range(0,0.1)) = 0
        _VoxelSize("World cell size", Float) = 0.1
        _Roughness("Roughness", Range(0,1)) = 0.92
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 200
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "../Rendering/MaterialOptics.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float _GrainStrength;
            float _FineGrainStrength;
            float _VoxelSize;
            float _Roughness;
            float4 _SurfaceOriginPhase;
            float4 _VoxelTerrainGrid;
            float4 _VoxelTerrainMask[64];
        CBUFFER_END
        uint GroundMaterialId(float encoded,half vertexAlpha)
        {
            if(encoded<.5)return VertexMaterialId(vertexAlpha);
            uint bits=(uint)round(encoded*255.0);
            return bits>=128u&&bits<153u?bits-128u:31u;
        }
        float3 GroundCoveragePosition(float3 positionWS)
        {
            // WorldExplorer publishes origin modulo4096. Wrap the reconstructed
            // coordinate to that same period so a phase rollover/rebase cannot
            // select a different coverage hash. Shared by all four passes;
            // shadow bias affects clip position only, never this surface anchor.
            return frac((positionWS+_SurfaceOriginPhase.xyz)/4096.0)*4096.0;
        }
        void ClipVoxelTerrain(float3 positionWS)
        {
            if (_VoxelTerrainGrid.w < 0.5) return;
            int2 cell = (int2)floor((positionWS.xz - _VoxelTerrainGrid.xy) / _VoxelTerrainGrid.z);
            int width = _VoxelTerrainGrid.w > 8 ? 16 : 8;
            if (all(cell >= 0) && all(cell < width))
            {
                int index = cell.x + cell.y * width;
                clip(0.5 - _VoxelTerrainMask[index / 4][index % 4]);
            }
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; float4 surface : TEXCOORD0; };
            struct Varyings {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : COLOR;
                half fog : TEXCOORD2;
                float4 surface : TEXCOORD3;
                half2 optics : TEXCOORD4;
                nointerpolation uint material : TEXCOORD5;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = pos.positionCS; output.positionWS = pos.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                output.optics=GroundOptics(input.surface.w);
                output.material=GroundMaterialId(input.surface.w,input.color.a);
                if(input.surface.w<.5){half3 optics=VertexOptics(input.color.a);output.optics=optics.xy;output.color.a=optics.z;}
                output.surface = input.surface;
                // Vertex colors store artist sRGB; unlike textures the GPU does
                // not convert this attribute automatically. Each quad has one RGB.
                #if !defined(UNITY_COLORSPACE_GAMMA)
                output.color.rgb = SRGBToLinear(output.color.rgb);
                #endif
                output.fog = ComputeFogFactor(pos.positionCS.z);
                return output;
            }
            float Hash(float3 p)
            {
                p = frac(p * 0.1031); p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }
            half3 DirectLight(Light light, half3 n, half3 view, half3 albedo, half roughness)
            {
                half ndl = saturate(dot(n, light.direction));
                half3 h = SafeNormalize(light.direction + view);
                half exponent = lerp(128.0h, 6.0h, roughness);
                half spec = pow(saturate(dot(n, h)), exponent) * (1.0h - roughness) * 0.18h;
                return (albedo * ndl + spec * ndl) * light.color * light.distanceAttenuation * light.shadowAttenuation;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                ClipVoxelTerrain(input.positionWS);ClipMaterialStable(MaterialViewOpacity(input.optics.x,input.material,input.positionWS),GroundCoveragePosition(input.positionWS));
                half3 normal = normalize(input.normalWS);
                // A tiny inward bias makes cell grain continuous on negative-facing surfaces.
                float3 cell = floor((input.positionWS + _SurfaceOriginPhase.xyz - normal * 0.0001) / max(0.001, _VoxelSize));
                float3 footprint = fwidth(input.positionWS);
                float pixelSize = max(footprint.x, max(footprint.y, footprint.z));
                float cellFade = saturate(1.0 - pixelSize / max(0.001, _VoxelSize));
                half grain = (Hash(cell) - 0.5) * _GrainStrength * cellFade;
                [branch] if (_FineGrainStrength > 0.00001)
                {
                    float fineFade = saturate(1.0 - pixelSize * 100.0);
                    grain += (Hash(floor((input.positionWS + _SurfaceOriginPhase.xyz) * 65.0)) - 0.5) * _FineGrainStrength * fineFade;
                }
                half3 albedo = saturate(input.color.rgb * (1.0h + grain));
                half3 view = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                // UV0 carries continuous roughness/wetness/snow and a valid flag on
                // height proxies. Voxel chunks omit UV0 and retain the generic fallback.
                half roughness = input.surface.w > 0.5 && input.surface.x >= 0.1 ? saturate(input.surface.x) : _Roughness;
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                Light sun = GetMainLight(shadowCoord);
                AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(input.positionCS));
                half indirectOcclusion = min(lerp(.45h,1.0h,saturate(input.color.a)),lerp(.35h,1.0h,ao.indirectAmbientOcclusion));
                half alpha=1;BRDFData brdf;InitializeBRDFData(albedo,0.0h,half3(.04,.04,.04),1.0h-roughness,alpha,brdf);
                half3 color = brdf.diffuse * max(MaterialAmbient(normal), half3(.02,.02,.02)) * indirectOcclusion;
                color += LightingPhysicallyBased(brdf,sun,normal,view) * ao.directAmbientOcclusion;
                color += MaterialTransmission(albedo,normal,sun,input.optics.y);
                #ifdef _ADDITIONAL_LIGHTS
                uint count = GetAdditionalLightsCount();
                for (uint index = 0u; index < count; index++)
                {
                    Light light = GetAdditionalLight(index, input.positionWS, half4(1,1,1,1));
                    color += LightingPhysicallyBased(brdf,light,normal,view) * ao.directAmbientOcclusion;
                }
                #endif
                return half4(MixFog(color, input.fog), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color:COLOR; float4 surface:TEXCOORD0; };
            struct ShadowVaryings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half opacity:TEXCOORD1; };
            ShadowVaryings ShadowVert(Attributes input)
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirection = normalize(_LightPosition - positionWS);
                #else
                float3 lightDirection = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirection));
                #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE * positionCS.w);
                #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE * positionCS.w);
                #endif
                ShadowVaryings output; output.positionCS = positionCS; output.positionWS = positionWS;half2 optics=input.surface.w>.5?GroundOptics(input.surface.w):VertexOptics(input.color.a).xy;output.opacity=MaterialShadowCoverage(optics.x,optics.y); return output;
            }
            half4 ShadowFrag(ShadowVaryings input) : SV_Target { ClipVoxelTerrain(input.positionWS); ClipMaterialStable(input.opacity,GroundCoveragePosition(input.positionWS));return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma target 3.5
            #pragma fragment DepthFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct DepthVaryings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half opacity:TEXCOORD1; nointerpolation uint material:TEXCOORD2; };
            DepthVaryings DepthVert(float4 positionOS : POSITION,half4 color:COLOR,float4 surface:TEXCOORD0)
            { DepthVaryings output; output.positionWS = TransformObjectToWorld(positionOS.xyz); output.positionCS = TransformWorldToHClip(output.positionWS);output.opacity=surface.w>.5?GroundOptics(surface.w).x:VertexOptics(color.a).x;output.material=GroundMaterialId(surface.w,color.a);return output; }
            half4 DepthFrag(DepthVaryings input) : SV_Target { ClipVoxelTerrain(input.positionWS); ClipMaterialStable(MaterialViewOpacity(input.opacity,input.material,input.positionWS),GroundCoveragePosition(input.positionWS));return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex NormalVert
            #pragma fragment NormalFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color:COLOR; float4 surface:TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; half opacity:TEXCOORD2; nointerpolation uint material:TEXCOORD3; };
            Varyings NormalVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.opacity=input.surface.w>.5?GroundOptics(input.surface.w).x:VertexOptics(input.color.a).x;
                output.material=GroundMaterialId(input.surface.w,input.color.a);
                return output;
            }
            half4 NormalFrag(Varyings input) : SV_Target
            {
                ClipVoxelTerrain(input.positionWS);ClipMaterialStable(MaterialViewOpacity(input.opacity,input.material,input.positionWS),GroundCoveragePosition(input.positionWS));
                float3 normalWS = normalize(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                half3 packed = PackFloat2To888(saturate(octNormalWS * 0.5 + 0.5));
                return half4(packed, 0);
                #else
                return half4(normalWS, 0);
                #endif
            }
            ENDHLSL
        }
    }
    Fallback Off
}
