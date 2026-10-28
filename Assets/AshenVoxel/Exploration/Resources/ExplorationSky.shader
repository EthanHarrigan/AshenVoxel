Shader "AshenVoxel/ExplorationSky"
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
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "ExplorationCloudVolume.hlsl"
            struct A { float4 positionOS:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; float3 direction:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V Vert(A a)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(a);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS=TransformObjectToHClip(a.positionOS.xyz);
                o.direction=a.positionOS.xyz;
                return o;
            }
            float Hash(float3 p)
            {
                p-=floor(p/64.0)*64.0;
                p=frac(p*.1031);
                p+=dot(p,p.yzx+33.33);
                return frac((p.x+p.y)*p.z);
            }
            float Noise(float3 p)
            {
                float3 i=floor(p),f=frac(p);
                f=f*f*(3-2*f);
                float a=lerp(Hash(i),Hash(i+float3(1,0,0)),f.x);
                float b=lerp(Hash(i+float3(0,1,0)),Hash(i+float3(1,1,0)),f.x);
                float c=lerp(Hash(i+float3(0,0,1)),Hash(i+float3(1,0,1)),f.x);
                float e=lerp(Hash(i+float3(0,1,1)),Hash(i+float3(1,1,1)),f.x);
                return lerp(lerp(a,b,f.y),lerp(c,e,f.y),f.z);
            }
            // Retain inexpensive layered fallback; no noise texture required.
            float LayerShape(float3 p,float footprint)
            {
                float n=lerp(.5,Noise(p),1-smoothstep(.25,.75,footprint))*.56;
                n+=lerp(.5,Noise(p*2+13),1-smoothstep(.25,.75,footprint*2))*.27;
                n+=lerp(.5,Noise(p*4+47),1-smoothstep(.25,.75,footprint*4))*.12;
                return n+.025;
            }
            float LayerTau(float shape)
            {
                float c=saturate(_CloudCoverage);
                float mask=smoothstep(1-c,1.14-c,shape)*smoothstep(0,.025,c);
                return mask*lerp(.35,1,smoothstep(.20,.80,shape))*clamp(_CloudDensity,0,2)*4;
            }
            float3 LayerPoint(float3 d)
            {
                float t=.0609/(sqrt(d.y*d.y+.0609)+d.y);
                return (float3(0,1,0)+t*d)*(32*clamp(_CloudScale,.25,4));
            }
            float3 Layered(float3 sky,float3 p,float footprint,float3 d,float3 sun,
                           float day,float twilight,float3 warm)
            {
                float shape=LayerShape(p,footprint);
                float tau=LayerTau(shape);
                float neighbor=LayerShape(p+sun*.55,footprint);
                float lighting=saturate(.30+.35*exp(-LayerTau(neighbor)*.35)
                    +.35*saturate(.5+2.5*(shape-neighbor)));
                float3 cloud=lerp(max(_CloudShadowColor.rgb,0),max(_CloudLightColor.rgb,0),lighting*day);
                cloud*=lerp(.045,1,day);
                cloud+=warm*twilight*.18;
                cloud+=max(_SunColor.rgb,0)*pow(saturate(dot(d,sun)),24)
                    *exp(-tau*.7)*(1-exp(-tau))*.18*day;
                float t=exp(-tau*smoothstep(0,.06,d.y));
                return sky*t+cloud*(1-t);
            }
            float3 Volumetric(float3 sky,float3 d,float3 sun,float day,float width,float2 wind)
            {
                float fade=smoothstep(.005,.045,abs(d.y));
                if(fade<=0) return sky;
                float3 light;
                float transmission;
                AVCloudIntegrateClouds(d,sun,day,width,wind,float3(0,0,0),
                    clamp(_CloudMaxDistance,3000,80000),light,transmission);
                return lerp(sky,light+transmission*sky,fade);
            }
            float4 Frag(V i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 d=AVCloudSafeDirection(i.direction),sun=AVCloudSafeDirection(_SunDirection.xyz);
                float mu=clamp(dot(d,sun),-1,1),haze=saturate(_Haze);
                float day=smoothstep(-.10,.08,sun.y);
                float twilight=(1-smoothstep(.02,.38,abs(sun.y)))*smoothstep(-.20,-.04,sun.y);
                float horizon=exp2(-saturate(d.y)*lerp(7,2.5,haze));
                #if defined(UNITY_COLORSPACE_GAMMA)
                    const float3 defaultSkyTint=float3(.42,.53,.65);
                #else
                    const float3 defaultSkyTint=float3(.147318999244,.242866813904,.380056336450);
                #endif
                float3 zenith=float3(.13,.32,.63)*lerp(float3(1,1,1),max(_SkyTint.rgb,0)/defaultSkyTint,.65);
                float3 sky=lerp(zenith*lerp(.018,1,day),max(_HorizonColor.rgb,0),horizon);
                float3 warm=float3(1,.25,.055)*max(_SunColor.rgb,0);
                sky+=warm*twilight*horizon*(1-horizon)*lerp(.15,.85,pow(saturate(mu*.5+.5),6));
                sky+=max(_SunColor.rgb,0)*pow(saturate(mu),lerp(100,14,haze))*day*lerp(.025,.12,haze);
                float radius=clamp(_SunAngularDiameter,.1,5)*.00872664626;
                float angle=atan2(length(cross(d,sun)),mu);
                float angularWidth=max(length(ddx(d)),length(ddy(d)));
                float aa=clamp(angularWidth,1e-5,.01);
                float disc=1-smoothstep(radius-aa,radius+aa,angle);
                sky+=max(_SunColor.rgb,0)*disc*smoothstep(-aa,aa,d.y)*max(_SunRadiance,0);
                sky=lerp(sky,max(_GroundColor.rgb,0)*lerp(.018,.6,day),smoothstep(0,.16,-d.y));
                // All derivative evaluation occurs before divergent volume loops.
                float3 p=LayerPoint(d);
                float footprint=max(length(ddx(p)),length(ddy(p)));
                float2 wind=AVCloudWindPhase();
                p+=float3(wind.x*64,0,wind.y*64);
                if(_CloudQuality>=.5 && _CloudCoverage>0 && _CloudDensity>0)
                {
                    if(_CloudQuality>=1.5 && _CloudNoiseReady>=.5)
                    {
                        // External mesh owns cloud compositing when enabled.
                        if(_CloudVolumeExternal<.5)
                            sky=Volumetric(sky,d,sun,day,angularWidth,wind);
                    }
                    else if(d.y>0)
                        sky=Layered(sky,p,footprint,d,sun,day,twilight,warm);
                }
                // No shader-side uniform sRGB conversion or custom tonemapping.
                return float4(clamp(sky*clamp(_Exposure,0,8),0,60000),1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
