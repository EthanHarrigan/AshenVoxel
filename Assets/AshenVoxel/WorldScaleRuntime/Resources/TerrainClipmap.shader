Shader "AshenVoxel/TerrainClipmap"
{
    Properties
    {
        [HideInInspector][NoScaleOffset] _CmHeights("Clipmap heights", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset] _CmTint("Clipmap tint", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset] _CmAppearance("Clipmap appearance", 2DArray) = "" {}
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
        #include "../../Rendering/MaterialOptics.hlsl"
        #include "../../Rendering/AshenAtmosphere.hlsl"
        #include "TerrainClipmapSampling.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float _GrainStrength, _FineGrainStrength, _VoxelSize, _Roughness;
            float4 _SurfaceOriginPhase;
            float4 _VoxelTerrainGrid;
            float4 _VoxelTerrainMask[64];
        CBUFFER_END
        float3 GroundCoveragePosition(float3 positionWS)
        { return frac((positionWS+_SurfaceOriginPhase.xyz)/4096.0)*4096.0; }
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
        float2 CmLocalAt(float3 positionWS) { return (positionWS.xz-_CmOrigin.xy)/_CmLevel.x; }
        void CmClipView(float3 positionWS, CmSurfaceData surface)
        {
            ClipVoxelTerrain(positionWS);
            ClipMaterialStable(MaterialViewOpacity(MaterialOptics(surface.material).x,surface.material,positionWS),GroundCoveragePosition(positionWS));
        }
        float4 _AshenVirtualSteps;
        // Keep the established world-anchored voxel shading. Geometry stays the
        // watertight height surface; stepped silhouettes await native acceptance.
        float StepCornerAO(bool a,bool b,bool c)
        {
            float light=(a&&b)?0.0:3.0-(a?1.0:0.0)-(b?1.0:0.0)-(c?1.0:0.0);
            return (64.0+light*(191.0/3.0))/255.0;
        }
        float4 _AshenVoxelLook;   // global: x on, y target pixels per block, z strength at the largest blocks, w largest block m
        float4 _AshenVoxelLook2;  // global: x stepped silhouettes on, y largest silhouette step m, z grazing band, w world-grid pigment strength
        float4 _AshenVoxelPixel;  // global: x radians per screen pixel
        struct VirtualStepHit{float3 position;half3 normal;half ao;float3 cell;float3 center;half bevel;half3 bevelNormal;};
        // Local surface: tangent plane plus (on lifted silhouette shells only) its curvature H.
        float SurfaceAt(float2 xz,float3 q,float2 g,float2x2 H)
        {
            float2 D=xz-q.xz;return q.y+dot(g,D)+.5*dot(D,mul(H,D));
        }
        float VirtualTop(float2 cellIndex,float3 q,float2 g,float c,float2x2 H)
        {
            return round(SurfaceAt((cellIndex+.5)*c,q,g,H)/c)*c;
        }
        // Curvature of the interpolated surface (Hessian of height over world xz) from screen
        // derivatives. Must be evaluated in uniform control flow.
        float2x2 SurfaceHessian(float3 positionWS,float3 n)
        {
            float2 g=-n.xz/max(n.y,.05);
            float2 gx=ddx(g),gy=ddy(g),px=ddx(positionWS.xz),py=ddy(positionWS.xz);
            float det=px.x*py.y-py.x*px.y;
            if(abs(det)<1e-12)return (float2x2)0;
            float2x2 Pi=float2x2(py.y,-py.x,-px.y,px.x)/det;// inverse of [px py] (columns)
            float2x2 G=float2x2(gx.x,gy.x,gx.y,gy.y);
            float2x2 H=mul(G,Pi);H=.5*(H+transpose(H));
            H[0]=clamp(H[0],-2,2);H[1]=clamp(H[1],-2,2);return H;
        }
        half VirtualTopAO(float2 cellIndex,float3 q,float2 g,float c,float2x2 H,float top,float2 f,out half4 lower)
        {
            float h=top+.001;
            float txm=VirtualTop(cellIndex+float2(-1,0),q,g,c,H),txp=VirtualTop(cellIndex+float2(1,0),q,g,c,H);
            float tzm=VirtualTop(cellIndex+float2(0,-1),q,g,c,H),tzp=VirtualTop(cellIndex+float2(0,1),q,g,c,H);
            bool xm=txm>h,xp=txp>h,zm=tzm>h,zp=tzp>h;
            bool mm=VirtualTop(cellIndex+float2(-1,-1),q,g,c,H)>h,pm=VirtualTop(cellIndex+float2(1,-1),q,g,c,H)>h;
            bool mp=VirtualTop(cellIndex+float2(-1,1),q,g,c,H)>h,pp=VirtualTop(cellIndex+float2(1,1),q,g,c,H)>h;
            // Exposed (convex) edges: the neighbour column is a full step lower.
            float l=top-.5*c;lower=half4(txm<l?1:0,txp<l?1:0,tzm<l?1:0,tzp<l?1:0);
            float a00=StepCornerAO(xm,zm,mm),a10=StepCornerAO(xp,zm,pm),a01=StepCornerAO(xm,zp,mp),a11=StepCornerAO(xp,zp,pp);
            // Smoothstep across the face: soft, rounded occlusion gradients instead of a linear ramp.
            float2 s=f*f*(3-2*f);
            return (half)lerp(lerp(a00,a10,s.x),lerp(a01,a11,s.x),s.y);
        }
        // Returns 1 on a hit, 0 when undecided (iteration budget), -1 when a lifted shell pixel
        // runs past its reach without meeting a step (sky or farther terrain shows through).
        int VirtualStepAt(float3 positionWS,half3 n,float c,float lift,float2x2 H,out VirtualStepHit hit)
        {
            hit=(VirtualStepHit)0;hit.ao=1;hit.normal=n;hit.position=positionWS;hit.center=positionWS;hit.bevelNormal=n;
            if(n.y<.3)return 0;
            float3 phase=_SurfaceOriginPhase.xyz;
            float3 p=positionWS+phase;float3 q=p-float3(0,lift,0);
            float3 V=normalize(positionWS-_WorldSpaceCameraPos.xyz);
            float2 g=-n.xz/n.y;
            float d=V.y-g.x*V.x-g.y*V.z; // rate at which the ray sinks below the tangent plane
            if(d>-1e-4)return lift>0?-1:0;
            float margin=c*(1.0+.5*(abs(g.x)+abs(g.y)))+.01;
            float tb=clamp((margin-lift)/(-d),0,40*c);
            float3 r0=p-V*tb;
            float2 cellIndex=floor(r0.xz/c);
            float2 dir=float2(V.x>=0?1.0:-1.0,V.z>=0?1.0:-1.0);
            float2 inv=1.0/max(abs(V.xz),1e-6);
            float2 next=((cellIndex+max(dir,0))*c-r0.xz)*dir*inv;
            float2 delta=c*inv;
            float tEnter=0;int axis=-1;float previousTop=0;
            [loop] for(int i=0;i<24;i++)
            {
                float top=VirtualTop(cellIndex,q,g,c,H);
                float yEnter=r0.y+V.y*tEnter;
                float tExit=min(next.x,next.y);
                if(yEnter<=top&&axis>=0)
                {
                    hit.position=r0+V*tEnter;
                    hit.normal=axis==0?half3(-dir.x,0,0):half3(0,0,-dir.y);
                    // Riser: soft vertical occlusion towards the lower step, a bright rounded lip at the top.
                    float v=saturate((hit.position.y-previousTop)/max(top-previousTop,c));
                    hit.ao=(half)lerp(.5,1.0,pow(v*v*(3-2*v),.7));
                    hit.bevel=(half)smoothstep(.8,1.0,v);
                    hit.bevelNormal=normalize(hit.normal+half3(0,1.2h,0)*hit.bevel);
                    hit.cell=float3(cellIndex.x,round(hit.position.y/c-.5),cellIndex.y);
                    hit.center=float3((cellIndex.x+.5)*c,(hit.cell.y+.5)*c,(cellIndex.y+.5)*c)-phase;
                    hit.position-=phase;return 1;
                }
                if(V.y<0)
                {
                    float tTop=tEnter+(top-yEnter)/V.y;
                    if(tTop<=tExit)
                    {
                        hit.position=r0+V*max(tTop,tEnter);hit.normal=half3(0,1,0);
                        float2 f=frac(hit.position.xz/c);half4 lower;
                        hit.ao=VirtualTopAO(cellIndex,q,g,c,H,top,f,lower);
                        // Rounded exposed edges catch the light: tilt the normal outward near them.
                        half4 e=lower*half4(smoothstep(.2,0,f.x),smoothstep(.8,1,f.x),smoothstep(.2,0,f.y),smoothstep(.8,1,f.y));
                        hit.bevel=max(max(e.x,e.y),max(e.z,e.w));
                        hit.bevelNormal=normalize(half3(e.y-e.x,1.4h,e.w-e.z));
                        hit.cell=float3(cellIndex.x,round(top/c),cellIndex.y);
                        hit.center=float3((cellIndex.x+.5)*c,top,(cellIndex.y+.5)*c)-phase;
                        hit.position-=phase;return 1;
                    }
                }
                previousTop=top;
                if(next.x<next.y){cellIndex.x+=dir.x;tEnter=next.x;next.x+=delta.x;axis=0;}
                else{cellIndex.y+=dir.y;tEnter=next.y;next.y+=delta.y;axis=1;}
                // A lifted shell ray that climbs clear of the curved surface has passed over the
                // ridge: no step lies under this pixel, so the silhouette is stepped here.
                if(lift>0)
                {
                    // Only step-sized gaps are cut: a shell ray that has travelled more than two
                    // blocks sideways from its own pixel is kept (the curvature estimate from a
                    // coarse LOD's vertex normals is not trustworthy that far out).
                    float3 r=r0+V*tEnter;float side=length(r.xz-p.xz);
                    if(side>2.0*c)return 0;
                    if(r.y-SurfaceAt(r.xz,q,g,H)>lift+margin)return -1;
                }
            }
            return 0;
        }
        bool VirtualStep(float3 positionWS,half3 n,out VirtualStepHit hit)
        {return VirtualStepAt(positionWS,n,max(_AshenVirtualSteps.w,.01),0,(float2x2)0,hit)>0;}
        // Smallest whole-voxel block (0.1 m x 2^k) that is at least `want` metres; t blends to the next.
        float VoxelLevel(float want,out float t)
        {
            float lv=log2(max(want,.1)/.1);float k=floor(lv);t=lv-k;
            return .1*exp2(k);
        }
        float VoxelHash(float3 p){p=frac(p*.1031);p+=dot(p,p.yzx+33.33);return frac((p.x+p.y)*p.z);}
        // Smooth value noise for soft, sand-like mottling inside a block face.
        float VoxelValueNoise(float3 p)
        {
            float3 i=floor(p),f=frac(p);f=f*f*(3-2*f);
            float a=VoxelHash(i),b=VoxelHash(i+float3(1,0,0)),c=VoxelHash(i+float3(0,1,0)),d=VoxelHash(i+float3(1,1,0));
            float e=VoxelHash(i+float3(0,0,1)),g=VoxelHash(i+float3(1,0,1)),h=VoxelHash(i+float3(0,1,1)),k=VoxelHash(i+float3(1,1,1));
            return lerp(lerp(lerp(a,b,f.x),lerp(c,d,f.x),f.y),lerp(lerp(e,g,f.x),lerp(h,k,f.x),f.y),f.z);
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back ZWrite On
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half fog:TEXCOORD1; };
            Varyings Vert(uint vertexID:SV_VertexID)
            {
                Varyings output;
                output.positionWS=CmPosition(CmVertexLocal(vertexID));
                output.positionCS=TransformWorldToHClip(output.positionWS);
                output.fog=ComputeFogFactor(output.positionCS.z);
                return output;
            }
            float Hash(float3 p)
            { p=frac(p*.1031);p+=dot(p,p.yzx+33.33);return frac((p.x+p.y)*p.z); }
            half3 CmMainLighting(BRDFData brdf,Light sun,half3 normal,half3 view,half3 albedo,
                half transmission,half indirectOcclusion,half directOcclusion)
            {
                half3 color=brdf.diffuse*max(MaterialAmbient(normal),half3(.02,.02,.02))*indirectOcclusion;
                color+=LightingPhysicallyBased(brdf,sun,normal,view)*directOcclusion;
                return color+MaterialTransmission(albedo,normal,sun,transmission);
            }
            half4 Frag(Varyings input):SV_Target
            {
                float2 local=CmLocalAt(input.positionWS);
                CmSurfaceData surface=CmSurface(local);
                half3 normal=CmNormal(local);
                half3 secondFaceNormal=normal;half faceLightBlend=0;
                half3 baseColor=surface.color;half vertexOcclusion=1;
                uint material=surface.material;half2 optics=MaterialOptics(material);
                half roughness=surface.roughness>=.1?saturate(surface.roughness):_Roughness;
                CmClipView(input.positionWS,surface);
                // A tiny inward bias makes cell grain continuous on negative-facing surfaces.
                float3 cell = floor((input.positionWS + _SurfaceOriginPhase.xyz - normal * 0.0001) / max(0.001, _VoxelSize));
                float3 footprint = fwidth(input.positionWS);
                float pixelSize = max(footprint.x, max(footprint.y, footprint.z));
                float cellFade = saturate(1.0 - pixelSize / max(0.001, _VoxelSize));
                float3 shadePosition=input.positionWS;half grainStrength=_GrainStrength;
                half micro=0;half rim=0;
                [branch] if(_AshenVoxelLook.x>.5&&normal.y>.3)
                {
                    float t;float c0=VoxelLevel(pixelSize*max(_AshenVoxelLook.y,1),t);
                    float maxCell=max(_AshenVoxelLook.w,.1);half w1=(half)smoothstep(0,1,t);
                    if(c0>=maxCell){c0=maxCell;w1=0;}
                    float c1=min(2*c0,maxCell);if(c1<=c0)w1=0;
                    float cellPx=lerp(c0,c1,w1)/max(pixelSize,1e-5);
                    // Full blocks near the player, a light touch on the largest (most distant) blocks,
                    // and nothing once a block shrinks below a pixel (only per-block grain remains).
                    half strength=(half)lerp(1.0,_AshenVoxelLook.z,saturate(log2(max(c0,.2)/.2)/max(log2(maxCell/.2),1)));
                    strength*=(half)(saturate((cellPx-.6)/.9)*saturate((normal.y-.3)/.15));
                    float2x2 H=(float2x2)0;
                    VirtualStepHit ha=(VirtualStepHit)0,hb=(VirtualStepHit)0;int ra=0,rb=0;
                    [branch] if(strength>0)
                    {
                        ra=VirtualStepAt(input.positionWS,normal,c0,0,H,ha);
                        if(w1>.01)rb=VirtualStepAt(input.positionWS,normal,c1,0,H,hb);
                    }
                    if(ra>0)
                    {
                        if(rb<=0){hb=ha;w1=0;}
                        half detail=(half)saturate((cellPx-3)/4);// rounded edges once a block is a few pixels
                        half3 na=normalize(lerp(ha.normal,ha.bevelNormal,detail)),nb=normalize(lerp(hb.normal,hb.bevelNormal,detail));
                        // Different grids can hit a top and a riser at this pixel. Blending
                        // their normals invents a diagonal face/highlight. Blend their light
                        // instead, retaining each face's bevel and smooth-surface fade.
                        if(rb>0&&w1>0&&w1<1&&dot(ha.normal,hb.normal)<.5h)
                        {
                            secondFaceNormal=normalize(lerp(normal,nb,strength));
                            normal=normalize(lerp(normal,na,strength));faceLightBlend=w1;
                        }
                        else normal=normalize(lerp(normal,normalize(lerp(na,nb,w1)),strength));
                        vertexOcclusion*=lerp(1.0h,lerp(ha.ao,hb.ao,w1),strength);
                        shadePosition=lerp(input.positionWS,lerp(ha.position,hb.position,w1),strength);
                        cell=w1>.5?hb.cell+float3(0,4099.0*c1,0):ha.cell+float3(0,4099.0*c0,0);cellFade=strength;
                        grainStrength=lerp(_GrainStrength,max(_GrainStrength,.06h),strength);
                        rim=lerp(ha.bevel,hb.bevel,w1)*detail*strength;
                        // Soft mottling inside each face, like settled sand: only once blocks are large on screen.
                        float cm=lerp(c0,c1,w1);
                        micro=(half)((VoxelValueNoise((shadePosition+_SurfaceOriginPhase.xyz)*(3.0/cm))-.5)*.10*saturate((cellPx-5)/6))*strength;
                        // World-grid pigment: one colour per block, taken at the block centre, so colour
                        // follows the voxel grid instead of stretching across large triangles.
                        half snap=(half)(_AshenVoxelLook2.w*saturate((cellPx-1)/2))*strength;
                        if(snap>0)
                            baseColor=lerp(baseColor,CmPigment(lerp(ha.center,hb.center,w1).xz,baseColor),snap);
                    }
                }
                else if(_AshenVirtualSteps.x>.5&&pixelSize<_AshenVirtualSteps.z)
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
                half3 albedo = saturate(baseColor * (1.0h + grain + micro + rim*.07h));
                half3 view = SafeNormalize(GetWorldSpaceViewDir(shadePosition));
                // Continuous surface appearance and categorical optics come from resident textures.
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                float4 shadowCoord = TransformWorldToShadowCoord(shadePosition);
                #endif
                Light sun = GetMainLight(shadowCoord);
                AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(input.positionCS));
                half indirectOcclusion = min(lerp(.45h,1.0h,saturate(vertexOcclusion)),lerp(.35h,1.0h,ao.indirectAmbientOcclusion));
                half alpha=1;BRDFData brdf;InitializeBRDFData(albedo,0.0h,half3(.04,.04,.04),1.0h-roughness,alpha,brdf);
                half3 color=CmMainLighting(brdf,sun,normal,view,albedo,optics.y,indirectOcclusion,ao.directAmbientOcclusion);
                [branch] if(faceLightBlend>0)
                    color=lerp(color,CmMainLighting(brdf,sun,secondFaceNormal,view,albedo,optics.y,indirectOcclusion,ao.directAmbientOcclusion),faceLightBlend);
                #ifdef _ADDITIONAL_LIGHTS
                uint count = GetAdditionalLightsCount();
                for (uint index = 0u; index < count; index++)
                {
                    Light light = GetAdditionalLight(index, shadePosition, half4(1,1,1,1));
                    half3 direct=LightingPhysicallyBased(brdf,light,normal,view);
                    [branch] if(faceLightBlend>0)
                        direct=lerp(direct,LightingPhysicallyBased(brdf,light,secondFaceNormal,view),faceLightBlend);
                    color+=direct*ao.directAmbientOcclusion;
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
            #pragma target 4.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection, _LightPosition;
            struct ShadowVaryings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; };
            ShadowVaryings ShadowVert(uint vertexID:SV_VertexID)
            {
                float2 local=CmVertexLocal(vertexID);
                float3 positionWS=CmPosition(local), normalWS=CmNormal(local);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirection=normalize(_LightPosition-positionWS);
                #else
                float3 lightDirection=_LightDirection;
                #endif
                float4 positionCS=TransformWorldToHClip(ApplyShadowBias(positionWS,normalWS,lightDirection));
                #if UNITY_REVERSED_Z
                positionCS.z=min(positionCS.z,UNITY_NEAR_CLIP_VALUE*positionCS.w);
                #else
                positionCS.z=max(positionCS.z,UNITY_NEAR_CLIP_VALUE*positionCS.w);
                #endif
                ShadowVaryings output;output.positionCS=positionCS;output.positionWS=positionWS;return output;
            }
            half4 ShadowFrag(ShadowVaryings input):SV_Target
            {
                ClipVoxelTerrain(input.positionWS);
                half2 optics=MaterialOptics(CmSurface(CmLocalAt(input.positionWS)).material);
                ClipMaterialStable(MaterialShadowCoverage(optics.x,optics.y),GroundCoveragePosition(input.positionWS));
                return 0;
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            struct DepthVaryings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; };
            DepthVaryings DepthVert(uint vertexID:SV_VertexID)
            {
                DepthVaryings output;output.positionWS=CmPosition(CmVertexLocal(vertexID));
                output.positionCS=TransformWorldToHClip(output.positionWS);return output;
            }
            half4 DepthFrag(DepthVaryings input):SV_Target
            { CmClipView(input.positionWS,CmSurface(CmLocalAt(input.positionWS)));return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex NormalVert
            #pragma fragment NormalFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
            struct NormalVaryings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; };
            NormalVaryings NormalVert(uint vertexID:SV_VertexID)
            {
                NormalVaryings output;output.positionWS=CmPosition(CmVertexLocal(vertexID));
                output.positionCS=TransformWorldToHClip(output.positionWS);return output;
            }
            half4 NormalFrag(NormalVaryings input):SV_Target
            {
                float2 local=CmLocalAt(input.positionWS);CmClipView(input.positionWS,CmSurface(local));
                float3 normalWS=CmNormal(local);
                #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormalWS=PackNormalOctQuadEncode(normalWS);
                half3 packed=PackFloat2To888(saturate(octNormalWS*.5+.5));
                return half4(packed,0);
                #else
                return half4(normalWS,0);
                #endif
            }
            ENDHLSL
        }
    }
    Fallback Off
}
