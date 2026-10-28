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
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
        #include "../Rendering/MaterialOptics.hlsl"
        #include "../Rendering/AshenAtmosphere.hlsl"
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
        // ---- Merged voxel steps (VoxelStepSurface) -------------------------------------
        // Per-renderer (MaterialPropertyBlock) tile data; zero for every other ground mesh.
        TEXTURE2D(_StepColors);
        TEXTURE2D(_StepHeights);
        float4 _StepTile;        // x block width m, y columns per side, z block height m (>0 enables), w packed UNorm16 positions
        float4 _AshenStepMorph;  // global: x pixel-target spacing per metre, y morph enabled
        float4 _AshenStepCamera; // global: render-space camera position (identical in every pass)
        float4 _AshenVirtualSteps; // global: x enabled, y full-step pixel size m, z steps gone pixel size m, w cell size m
        #define STEP_SLICES 4
        bool StepEnabled(){return _StepTile.z>0.0;}
        float StepParentBlockHeight(){return min(2.0*_StepTile.z,1.6);}
        void StepDecode(float4 packed,out float3 level,out float morphY)
        {
            if(_StepTile.w>.5)
            {
                float4 v=round(packed*65535.0);
                level=float3(v.x*_StepTile.x,(v.y-32768.0)*0.1,v.z*_StepTile.x);
                morphY=(v.w-32768.0)*0.1;
            }
            else{level=packed.xyz;morphY=packed.w;}
        }
        // 0 while blocks are at least one pixel-target wide, 1 when the parent's twice-larger
        // blocks would be. The planner selects tile detail with the same spacing rule, so a
        // fully morphed level equals the next coarser tile and transitions stay continuous.
        float StepMorphFactor(float3 levelWS)
        {
            if(_AshenStepMorph.y<.5)return 0;
            float spacing=distance(levelWS,_AshenStepCamera.xyz)*_AshenStepMorph.x;
            return saturate(log2(max(spacing,1e-6)/max(_StepTile.x,1e-4)));
        }
        float3 StepPositionOS(float4 packed,out float morph)
        {
            float3 level;float morphY;StepDecode(packed,level,morphY);
            morph=StepMorphFactor(TransformObjectToWorld(level));
            return float3(level.x,lerp(level.y,morphY,morph),level.z);
        }
        float3 GroundPositionOS(float4 positionOS,out float morph)
        {
            morph=0;
            if(StepEnabled())return StepPositionOS(positionOS,morph);
            return positionOS.xyz;
        }
        float StepTexelHeight(int2 texel)
        {
            float v=LOAD_TEXTURE2D(_StepHeights,texel).r;
            return (round(v*65535.0)-32768.0)*0.1;
        }
        // column in [-1,n]; parent in [-1,n/2] (one-texel halo on every side).
        float StepHeight(int2 column){return StepTexelHeight(column+1);}
        float StepParentHeight(int2 parent){int n=(int)_StepTile.y;return StepTexelHeight(int2(parent.x+1,n+2+parent.y+1));}
        float StepLevelHeight(int2 c,bool parent){return parent?StepParentHeight(c):StepHeight(c);}
        int StepParentIndex(int v,int n){return v<0?-1:(v>=n?n/2:(v>>1));}
        float StepCornerAO(bool a,bool b,bool c)
        {
            float light=(a&&b)?0.0:3.0-(a?1.0:0.0)-(b?1.0:0.0)-(c?1.0:0.0);
            return (64.0+light*(191.0/3.0))/255.0;
        }
        // Per-block corner occlusion reconstructed from neighbouring column tops,
        // the same rule as the exposed-cube mesher, interpolated across the block.
        float StepTopAO(int2 c,float2 f,bool parent)
        {
            float h=StepLevelHeight(c,parent)+.001;
            bool xm=StepLevelHeight(c+int2(-1,0),parent)>h,xp=StepLevelHeight(c+int2(1,0),parent)>h;
            bool zm=StepLevelHeight(c+int2(0,-1),parent)>h,zp=StepLevelHeight(c+int2(0,1),parent)>h;
            bool mm=StepLevelHeight(c+int2(-1,-1),parent)>h,pm=StepLevelHeight(c+int2(1,-1),parent)>h;
            bool mp=StepLevelHeight(c+int2(-1,1),parent)>h,pp=StepLevelHeight(c+int2(1,1),parent)>h;
            float a00=StepCornerAO(xm,zm,mm),a10=StepCornerAO(xp,zm,pm),a01=StepCornerAO(xm,zp,mp),a11=StepCornerAO(xp,zp,pp);
            return lerp(lerp(a00,a10,f.x),lerp(a01,a11,f.x),f.y);
        }
        half StepRoughness(uint m,half wet)
        {
            if(m==19u)return .18h;if(m==18u)return .92h;
            half r=(m==2u||m==16u)?.82h:.95h;
            return max(.45h,r-wet*.24h);
        }
        struct StepSurfaceData{half3 albedo;half ao;uint material;half wet;};
        // Paint the authoritative per-block pigment back onto merged rectangles.
        StepSurfaceData StepShade(float3 posOS,half3 normal,float morph)
        {
            StepSurfaceData o;
            int n=(int)_StepTile.y;float c=_StepTile.x;int hn=n/2;
            float2 inward=posOS.xz-normal.xz*(0.5*c);
            int2 col=clamp((int2)floor(inward/c),0,n-1);
            int2 pcol=col>>1;
            float top=lerp(StepHeight(col),StepParentHeight(pcol),morph);
            float block=lerp(_StepTile.z,StepParentBlockHeight(),morph);
            int slice=(int)clamp(floor((top-posOS.y)/block+.0005),0,STEP_SLICES-1);
            half4 f=LOAD_TEXTURE2D(_StepColors,int2(col.x,col.y+slice*n));
            half4 p=LOAD_TEXTURE2D(_StepColors,int2((slice&1)*hn+pcol.x,n*STEP_SLICES+(slice>>1)*hn+pcol.y));
            half3 fl=f.rgb,pl=p.rgb;
            #if !defined(UNITY_COLORSPACE_GAMMA)
            fl=SRGBToLinear(fl);pl=SRGBToLinear(pl);
            #endif
            o.albedo=lerp(fl,pl,(half)morph);
            uint fa=(uint)round(f.a*255.0),pa=(uint)round(p.a*255.0);
            o.material=morph<.5?(fa>>3):(pa>>3);
            o.wet=(half)lerp((fa&7u)/7.0,(pa&7u)/7.0,morph);
            if(normal.y>.5)
            {
                float2 ff=frac(posOS.xz/c),pf=frac(posOS.xz/(2.0*c));
                o.ao=(half)lerp(StepTopAO(col,ff,false),StepTopAO(pcol,pf,true),morph);
            }
            else
            {
                int2 nb=col+(int2)round(normal.xz);
                int2 pnb=int2(StepParentIndex(nb.x,n),StepParentIndex(nb.y,n));
                float bottom=lerp(StepHeight(clamp(nb,-1,n)),StepParentHeight(pnb),morph);
                o.ao=(half)lerp(.55,1.0,saturate((posOS.y-bottom)/block));
            }
            return o;
        }
        // ---- Virtual 10 cm steps on the smooth connected mesh ---------------------------
        // The smooth surface stays the geometry. Per pixel, the view ray is marched through
        // the 10 cm column grid of the local tangent plane, each column's top rounded to a
        // 10 cm level, and the first top or riser it meets is shaded instead.
        struct VirtualStepHit{float3 position;half3 normal;half ao;float3 cell;};
        float VirtualTop(float2 cellIndex,float3 q,float2 g,float c)
        {
            float2 center=(cellIndex+.5)*c;
            return round((q.y+g.x*(center.x-q.x)+g.y*(center.y-q.z))/c)*c;
        }
        half VirtualTopAO(float2 cellIndex,float3 q,float2 g,float c,float top,float2 f)
        {
            float h=top+.001;
            bool xm=VirtualTop(cellIndex+float2(-1,0),q,g,c)>h,xp=VirtualTop(cellIndex+float2(1,0),q,g,c)>h;
            bool zm=VirtualTop(cellIndex+float2(0,-1),q,g,c)>h,zp=VirtualTop(cellIndex+float2(0,1),q,g,c)>h;
            bool mm=VirtualTop(cellIndex+float2(-1,-1),q,g,c)>h,pm=VirtualTop(cellIndex+float2(1,-1),q,g,c)>h;
            bool mp=VirtualTop(cellIndex+float2(-1,1),q,g,c)>h,pp=VirtualTop(cellIndex+float2(1,1),q,g,c)>h;
            float a00=StepCornerAO(xm,zm,mm),a10=StepCornerAO(xp,zm,pm),a01=StepCornerAO(xm,zp,mp),a11=StepCornerAO(xp,zp,pp);
            return (half)lerp(lerp(a00,a10,f.x),lerp(a01,a11,f.x),f.y);
        }
        bool VirtualStep(float3 positionWS,half3 n,out VirtualStepHit hit)
        {
            hit=(VirtualStepHit)0;hit.ao=1;hit.normal=n;hit.position=positionWS;
            float c=max(_AshenVirtualSteps.w,.01);
            if(n.y<.3)return false;
            float3 phase=_SurfaceOriginPhase.xyz;
            float3 q=positionWS+phase;
            float3 V=normalize(positionWS-_WorldSpaceCameraPos.xyz);
            float2 g=-n.xz/n.y;
            float d=V.y-g.x*V.x-g.y*V.z; // rate at which the ray sinks below the tangent plane
            if(d>-1e-3)return false;
            float margin=c*(1.0+.5*(abs(g.x)+abs(g.y)))+.01;
            float tb=min(margin/(-d),3.0);
            float3 r0=q-V*tb;
            float2 cellIndex=floor(r0.xz/c);
            float2 dir=float2(V.x>=0?1.0:-1.0,V.z>=0?1.0:-1.0);
            float2 inv=1.0/max(abs(V.xz),1e-6);
            float2 next=((cellIndex+max(dir,0))*c-r0.xz)*dir*inv;
            float2 delta=c*inv;
            float tEnter=0;int axis=-1;float previousTop=0;
            [loop] for(int i=0;i<24;i++)
            {
                float top=VirtualTop(cellIndex,q,g,c);
                float yEnter=r0.y+V.y*tEnter;
                float tExit=min(next.x,next.y);
                if(yEnter<=top&&axis>=0)
                {
                    hit.position=r0+V*tEnter;
                    hit.normal=axis==0?half3(-dir.x,0,0):half3(0,0,-dir.y);
                    hit.ao=(half)lerp(.55,1.0,saturate((hit.position.y-previousTop)/c));
                    hit.cell=float3(cellIndex.x,round(hit.position.y/c-.5),cellIndex.y);
                    hit.position-=phase;return true;
                }
                if(V.y<0)
                {
                    float tTop=tEnter+(top-yEnter)/V.y;
                    if(tTop<=tExit)
                    {
                        hit.position=r0+V*max(tTop,tEnter);hit.normal=half3(0,1,0);
                        hit.ao=VirtualTopAO(cellIndex,q,g,c,top,frac(hit.position.xz/c));
                        hit.cell=float3(cellIndex.x,round(top/c),cellIndex.y);
                        hit.position-=phase;return true;
                    }
                }
                previousTop=top;
                if(next.x<next.y){cellIndex.x+=dir.x;tEnter=next.x;next.x+=delta.x;axis=0;}
                else{cellIndex.y+=dir.y;tEnter=next.y;next.y+=delta.y;axis=1;}
            }
            return false;
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
                float4 stepData : TEXCOORD6;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float morph;float3 positionOS=GroundPositionOS(input.positionOS,morph);
                output.stepData=float4(positionOS,morph);
                VertexPositionInputs pos = GetVertexPositionInputs(positionOS);
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
                half3 normal = normalize(input.normalWS);
                half3 baseColor=input.color.rgb;half vertexOcclusion=input.color.a;half2 optics=input.optics;uint material=input.material;
                half roughness = input.surface.w > 0.5 && input.surface.x >= 0.1 ? saturate(input.surface.x) : _Roughness;
                [branch] if(StepEnabled())
                {
                    StepSurfaceData s=StepShade(input.stepData.xyz,normal,input.stepData.w);
                    baseColor=s.albedo;vertexOcclusion=s.ao;material=s.material;optics=MaterialOptics(material);roughness=StepRoughness(material,s.wet);
                }
                ClipVoxelTerrain(input.positionWS);ClipMaterialStable(MaterialViewOpacity(optics.x,material,input.positionWS),GroundCoveragePosition(input.positionWS));
                // A tiny inward bias makes cell grain continuous on negative-facing surfaces.
                float3 cell = floor((input.positionWS + _SurfaceOriginPhase.xyz - normal * 0.0001) / max(0.001, _VoxelSize));
                float3 footprint = fwidth(input.positionWS);
                float pixelSize = max(footprint.x, max(footprint.y, footprint.z));
                float cellFade = saturate(1.0 - pixelSize / max(0.001, _VoxelSize));
                float3 shadePosition=input.positionWS;half grainStrength=_GrainStrength;
                [branch] if(_AshenVirtualSteps.x>.5&&!StepEnabled()&&pixelSize<_AshenVirtualSteps.z)
                {
                    half fade=(half)(saturate((_AshenVirtualSteps.z-pixelSize)/max(_AshenVirtualSteps.z-_AshenVirtualSteps.y,1e-4))*saturate((normal.y-.3)/.15));
                    VirtualStepHit hit;
                    if(fade>0&&VirtualStep(input.positionWS,normal,hit))
                    {
                        normal=normalize(lerp(normal,hit.normal,fade));
                        vertexOcclusion*=lerp(1.0h,hit.ao,fade);
                        shadePosition=lerp(input.positionWS,hit.position,fade);
                        cell=hit.cell;cellFade=1;grainStrength=lerp(_GrainStrength,max(_GrainStrength,.07h),fade);
                    }
                }
                half grain = (Hash(cell) - 0.5) * grainStrength * cellFade;
                [branch] if (_FineGrainStrength > 0.00001)
                {
                    float fineFade = saturate(1.0 - pixelSize * 100.0);
                    grain += (Hash(floor((input.positionWS + _SurfaceOriginPhase.xyz) * 65.0)) - 0.5) * _FineGrainStrength * fineFade;
                }
                half3 albedo = saturate(baseColor * (1.0h + grain));
                half3 view = SafeNormalize(GetWorldSpaceViewDir(shadePosition));
                // UV0 carries continuous roughness/wetness/snow and a valid flag on
                // height proxies. Voxel chunks omit UV0 and retain the generic fallback;
                // voxel steps read material and wetness per block from their colour texels.
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                float4 shadowCoord = TransformWorldToShadowCoord(shadePosition);
                #endif
                Light sun = GetMainLight(shadowCoord);
                AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(input.positionCS));
                half indirectOcclusion = min(lerp(.45h,1.0h,saturate(vertexOcclusion)),lerp(.35h,1.0h,ao.indirectAmbientOcclusion));
                half alpha=1;BRDFData brdf;InitializeBRDFData(albedo,0.0h,half3(.04,.04,.04),1.0h-roughness,alpha,brdf);
                half3 color = brdf.diffuse * max(MaterialAmbient(normal), half3(.02,.02,.02)) * indirectOcclusion;
                color += LightingPhysicallyBased(brdf,sun,normal,view) * ao.directAmbientOcclusion;
                color += MaterialTransmission(albedo,normal,sun,optics.y);
                #ifdef _ADDITIONAL_LIGHTS
                uint count = GetAdditionalLightsCount();
                for (uint index = 0u; index < count; index++)
                {
                    Light light = GetAdditionalLight(index, shadePosition, half4(1,1,1,1));
                    color += LightingPhysicallyBased(brdf,light,normal,view) * ao.directAmbientOcclusion;
                }
                #endif
                return half4(AshenApplyFog(color, input.fog, input.positionWS), 1);
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
                float morph;float3 positionWS = TransformObjectToWorld(GroundPositionOS(input.positionOS,morph));
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
                ShadowVaryings output; output.positionCS = positionCS; output.positionWS = positionWS;half2 optics=StepEnabled()?half2(1,0):input.surface.w>.5?GroundOptics(input.surface.w):VertexOptics(input.color.a).xy;output.opacity=MaterialShadowCoverage(optics.x,optics.y); return output;
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
            { DepthVaryings output; float morph; output.positionWS = TransformObjectToWorld(GroundPositionOS(positionOS,morph)); output.positionCS = TransformWorldToHClip(output.positionWS);output.opacity=StepEnabled()?1:surface.w>.5?GroundOptics(surface.w).x:VertexOptics(color.a).x;output.material=StepEnabled()?0u:GroundMaterialId(surface.w,color.a);return output; }
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
                float morph;float3 positionOS=GroundPositionOS(input.positionOS,morph);
                output.positionCS = TransformObjectToHClip(positionOS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionWS = TransformObjectToWorld(positionOS);
                output.opacity=StepEnabled()?1:input.surface.w>.5?GroundOptics(input.surface.w).x:VertexOptics(input.color.a).x;
                output.material=StepEnabled()?0u:GroundMaterialId(input.surface.w,input.color.a);
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
