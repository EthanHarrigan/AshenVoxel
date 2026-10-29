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
        // ---- Voxel look at every distance (virtual steps on the smooth connected mesh) -------
        // The smooth surface stays the geometry. Per pixel, the view ray is marched through a
        // world-anchored column grid of the local tangent plane, each column's top rounded to the
        // cell size, and the first top or riser it meets is shaded instead. Near the player the
        // cells are 10 cm; farther away they grow in powers of two (always whole 10 cm voxel
        // multiples on the same world grid) so each block stays about _AshenVoxelLook.y pixels.
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
        // ---- Stepped silhouettes: lift grazing vertices by half a block, clip where no step is met.
        float VoxelShellLift(float3 positionWS,float3 normalWS)
        {
            if(_AshenVoxelLook.x<.5||_AshenVoxelLook2.x<.5||StepEnabled())return 0;
            float3 toCam=_WorldSpaceCameraPos.xyz-positionWS;float dist=length(toCam);
            float pixel=dist*max(_AshenVoxelPixel.x,1e-6);
            float t;float c=VoxelLevel(pixel*max(_AshenVoxelLook.y,1),t);c*=lerp(1.0,2.0,t);
            if(c>_AshenVoxelLook2.y||normalWS.y<.3)return 0;
            float grazing=1-saturate(abs(dot(normalize(normalWS),toCam/max(dist,1e-4)))/max(_AshenVoxelLook2.z,.01));
            return .5*c*grazing;
        }
        void ClipVoxelShell(float3 positionWS,float3 normalWS,float lift)
        {
            float3 footprint=fwidth(positionWS);float pixel=max(footprint.x,max(footprint.y,footprint.z));
            float2x2 H=SurfaceHessian(positionWS,normalize(normalWS));
            [branch] if(lift<=1e-4)return;
            float t;float c=VoxelLevel(pixel*max(_AshenVoxelLook.y,1),t);c=min(c*(t>.5?2.0:1.0),_AshenVoxelLook2.y);
            VirtualStepHit hit;
            if(VirtualStepAt(positionWS,(half3)normalize(normalWS),c,lift,H,hit)<0)clip(-1);
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
                float lift : TEXCOORD7;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float morph;float3 positionOS=GroundPositionOS(input.positionOS,morph);
                output.stepData=float4(positionOS,morph);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 positionWS=TransformObjectToWorld(positionOS);
                output.lift=VoxelShellLift(positionWS,output.normalWS);positionWS.y+=output.lift;
                output.positionWS=positionWS;output.positionCS=TransformWorldToHClip(positionWS);
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
                output.fog = ComputeFogFactor(output.positionCS.z);
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
                ClipVoxelShell(input.positionWS,input.normalWS,input.lift);
                // A tiny inward bias makes cell grain continuous on negative-facing surfaces.
                float3 cell = floor((input.positionWS + _SurfaceOriginPhase.xyz - normal * 0.0001) / max(0.001, _VoxelSize));
                float3 footprint = fwidth(input.positionWS);
                float pixelSize = max(footprint.x, max(footprint.y, footprint.z));
                float cellFade = saturate(1.0 - pixelSize / max(0.001, _VoxelSize));
                float3 shadePosition=input.positionWS;half grainStrength=_GrainStrength;
                half micro=0;half rim=0;
                // Derivatives for world-grid pigment and surface curvature (uniform control flow).
                float3 dpx=ddx(input.positionWS),dpy=ddy(input.positionWS);half3 dcx=ddx(baseColor),dcy=ddy(baseColor);
                float2x2 curvature=SurfaceHessian(input.positionWS,normal);
                [branch] if(_AshenVoxelLook.x>.5&&!StepEnabled()&&normal.y>.3)
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
                    float2x2 H=input.lift>0?curvature:(float2x2)0;
                    VirtualStepHit ha=(VirtualStepHit)0,hb=(VirtualStepHit)0;int ra=0,rb=0;
                    [branch] if(strength>0)
                    {
                        ra=VirtualStepAt(input.positionWS,normal,c0,input.lift,H,ha);
                        if(w1>.01)rb=VirtualStepAt(input.positionWS,normal,c1,input.lift,H,hb);
                    }
                    if(ra>0)
                    {
                        if(rb<=0){hb=ha;w1=0;}
                        half detail=(half)saturate((cellPx-3)/4);// rounded edges once a block is a few pixels
                        half3 na=normalize(lerp(ha.normal,ha.bevelNormal,detail)),nb=normalize(lerp(hb.normal,hb.bevelNormal,detail));
                        normal=normalize(lerp(normal,normalize(lerp(na,nb,w1)),strength));
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
                        float a11=dot(dpx,dpx),a12=dot(dpx,dpy),a22=dot(dpy,dpy),det=a11*a22-a12*a12;
                        half snap=(half)(_AshenVoxelLook2.w*saturate((cellPx-1)/2))*strength;
                        if(det>1e-14&&snap>0)
                        {
                            float3 D=lerp(ha.center,hb.center,w1)-input.positionWS;float b1=dot(D,dpx),b2=dot(D,dpy);
                            float u=clamp((a22*b1-a12*b2)/det,-6,6),v=clamp((a11*b2-a12*b1)/det,-6,6);
                            baseColor=max(0,baseColor+(dcx*(half)u+dcy*(half)v)*snap);
                        }
                    }
                }
                else if(_AshenVirtualSteps.x>.5&&!StepEnabled()&&pixelSize<_AshenVirtualSteps.z)
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
            struct DepthVaryings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half opacity:TEXCOORD1; nointerpolation uint material:TEXCOORD2; float3 normalWS:TEXCOORD3; float lift:TEXCOORD4; };
            DepthVaryings DepthVert(float4 positionOS : POSITION,float3 normalOS:NORMAL,half4 color:COLOR,float4 surface:TEXCOORD0)
            { DepthVaryings output; float morph; output.positionWS = TransformObjectToWorld(GroundPositionOS(positionOS,morph)); output.normalWS=TransformObjectToWorldNormal(normalOS);output.lift=VoxelShellLift(output.positionWS,output.normalWS);output.positionWS.y+=output.lift; output.positionCS = TransformWorldToHClip(output.positionWS);output.opacity=StepEnabled()?1:surface.w>.5?GroundOptics(surface.w).x:VertexOptics(color.a).x;output.material=StepEnabled()?0u:GroundMaterialId(surface.w,color.a);return output; }
            half4 DepthFrag(DepthVaryings input) : SV_Target { ClipVoxelTerrain(input.positionWS); ClipVoxelShell(input.positionWS,input.normalWS,input.lift); ClipMaterialStable(MaterialViewOpacity(input.opacity,input.material,input.positionWS),GroundCoveragePosition(input.positionWS));return 0; }
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
            struct Varyings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; half opacity:TEXCOORD2; nointerpolation uint material:TEXCOORD3; float lift:TEXCOORD4; };
            Varyings NormalVert(Attributes input)
            {
                Varyings output;
                float morph;float3 positionOS=GroundPositionOS(input.positionOS,morph);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionWS = TransformObjectToWorld(positionOS);
                output.lift=VoxelShellLift(output.positionWS,output.normalWS);output.positionWS.y+=output.lift;
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.opacity=StepEnabled()?1:input.surface.w>.5?GroundOptics(input.surface.w).x:VertexOptics(input.color.a).x;
                output.material=StepEnabled()?0u:GroundMaterialId(input.surface.w,input.color.a);
                return output;
            }
            half4 NormalFrag(Varyings input) : SV_Target
            {
                ClipVoxelTerrain(input.positionWS);ClipMaterialStable(MaterialViewOpacity(input.opacity,input.material,input.positionWS),GroundCoveragePosition(input.positionWS));
                ClipVoxelShell(input.positionWS,input.normalWS,input.lift);
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
