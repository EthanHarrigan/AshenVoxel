Shader "AshenVoxel/ExplorationSky"
{
    Properties
    {
        _SkyTint("Zenith tint", Color) = (.42,.53,.65,1)
        _GroundColor("Ground", Color) = (.12,.13,.12,1)
        _SunDirection("Direction toward sun", Vector) = (.4,.6,.3,0)
        _Exposure("Sky exposure multiplier", Range(0,8)) = 1
        _HorizonColor("Horizon", Color) = (.68,.77,.84,1)
        [HDR] _SunColor("Sun tint", Color) = (1,.92,.81,1)
        _SunAngularDiameter("Sun angular diameter (degrees)", Range(.1,5)) = .53
        _SunRadiance("Sun radiance", Range(0,100)) = 12
        _Haze("Atmosphere haze", Range(0,1)) = .35
        _CloudCoverage("Cloud coverage", Range(0,1)) = .42
        _CloudDensity("Cloud density", Range(0,2)) = .8
        _CloudScale("Cloud frequency", Range(.25,4)) = 1
        _CloudWind("Wind direction (xy)", Vector) = (1,.25,0,0)
        _CloudSpeed("Wind speed (noise units/second)", Range(0,.1)) = .012
        [Enum(Off,0,Layered,1,Detailed,2)] _CloudQuality("Cloud quality", Float) = 2
        [HDR] _CloudLightColor("Cloud lit", Color) = (1.05,1.03,.98,1)
        [HDR] _CloudShadowColor("Cloud shade", Color) = (.30,.36,.44,1)
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

            CBUFFER_START(UnityPerMaterial)
                float4 _SkyTint, _GroundColor, _SunDirection, _HorizonColor, _SunColor;
                float4 _CloudWind, _CloudLightColor, _CloudShadowColor;
                float _Exposure, _SunAngularDiameter, _SunRadiance, _Haze;
                float _CloudCoverage, _CloudDensity, _CloudScale, _CloudSpeed, _CloudQuality;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V Vert(A a)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(a);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(a.positionOS.xyz);
                o.direction = a.positionOS.xyz;
                return o;
            }
            float3 SafeDirection(float3 v)
            {
                float l2 = dot(v,v);
                return l2 > 1e-12 ? v * rsqrt(l2) : float3(0,1,0);
            }
            float Hash(float3 p)
            {
                // Integer lattice repeats every 64 in each axis; positive modulo.
                p -= floor(p * (1.0 / 64.0)) * 64.0;
                p = frac(p * .1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }
            float Noise(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                f = f*f*(3-2*f);
                float a = lerp(Hash(i),Hash(i+float3(1,0,0)),f.x);
                float b = lerp(Hash(i+float3(0,1,0)),Hash(i+float3(1,1,0)),f.x);
                float c = lerp(Hash(i+float3(0,0,1)),Hash(i+float3(1,0,1)),f.x);
                float d = lerp(Hash(i+float3(0,1,1)),Hash(i+float3(1,1,1)),f.x);
                return lerp(lerp(a,b,f.y),lerp(c,d,f.y),f.z);
            }
            float Cloud(float3 p, float footprint, bool detailed)
            {
                // Fixed upper bound: five octaves, all calls inlined/unrolled.
                // Unresolved octaves tend to their mean rather than sparkling.
                float n = lerp(.5,Noise(p),1-smoothstep(.25,.75,footprint)) * .56;
                n += lerp(.5,Noise(p*2+13),1-smoothstep(.25,.75,footprint*2)) * .27;
                n += lerp(.5,Noise(p*4+47),1-smoothstep(.25,.75,footprint*4)) * .12;
                if (detailed)
                {
                    n += lerp(.5,Noise(p*8+97),1-smoothstep(.25,.75,footprint*8)) * .04;
                    n += lerp(.5,Noise(p*16+23),1-smoothstep(.25,.75,footprint*16)) * .01;
                }
                else n += .025; // omitted octave mean, same coverage distribution
                return n;
            }
            float CloudTau(float shape)
            {
                float coverage = saturate(_CloudCoverage);
                float mask = smoothstep(1-coverage,1.14-coverage,shape);
                mask *= smoothstep(0,.025,coverage);
                return mask * clamp(_CloudDensity,0,2) * 4;
            }
            float3 CloudPoint(float3 view)
            {
                // Intersection with dimensionless 1.03-radius shell from radius 1.
                // Rationalized root, no view.y divisor or longitude singularity.
                float t = .0609 / (sqrt(view.y*view.y+.0609)+view.y);
                return (float3(0,1,0)+t*view) * (32*clamp(_CloudScale,.25,4));
            }
            float4 Frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 d = SafeDirection(i.direction);
                float3 sun = SafeDirection(_SunDirection.xyz);
                float mu = clamp(dot(d,sun),-1,1);
                float haze = saturate(_Haze);
                float day = smoothstep(-.10,.08,sun.y);
                float twilight = (1-smoothstep(.02,.38,abs(sun.y)))
                    * smoothstep(-.20,-.04,sun.y);
                float up = saturate(d.y);
                float horizon = exp2(-up*lerp(7,2.5,haze));
                // _SkyTint preserves original tint semantics and default palette.
                #if defined(UNITY_COLORSPACE_GAMMA)
                    const float3 defaultSkyTint = float3(.42,.53,.65);
                #else
                    // Runtime uploads already-linear values with SetVector.
                    const float3 defaultSkyTint = float3(.147318999244,.242866813904,.380056336450);
                #endif
                float3 zenith = float3(.13,.32,.63)
                    * lerp(float3(1,1,1),max(_SkyTint.rgb,0)/defaultSkyTint,.65);
                // Runtime owns horizon day/night tint to match shared surface fog.
                float3 sky = lerp(zenith*lerp(.018,1,day),
                    max(_HorizonColor.rgb,0),horizon);
                float sunward = pow(saturate(mu*.5+.5),6);
                float3 warm = float3(1,.25,.055)*max(_SunColor.rgb,0);
                sky += warm * (twilight*horizon*(1-horizon)*lerp(.15,.85,sunward));
                float halo = pow(saturate(mu),lerp(100,14,haze));
                sky += max(_SunColor.rgb,0)*halo*day*lerp(.025,.12,haze);
                float radius = clamp(_SunAngularDiameter,.1,5)*.00872664626;
                float angle = atan2(length(cross(d,sun)),mu);
                // Derivatives before all cloud branches. Footprint in radians/pixel.
                float aa = clamp(max(length(ddx(d)),length(ddy(d))),1e-5,.01);
                float disc = 1-smoothstep(radius-aa,radius+aa,angle);
                // Clip the disc at the view horizon, preserving partial sunsets.
                float visibility = smoothstep(-aa,aa,d.y);
                sky += max(_SunColor.rgb,0) * (disc*visibility*max(_SunRadiance,0));

                // Calculate derivatives uniformly even when clouds are disabled.
                float3 p = CloudPoint(d);
                float footprint = max(length(ddx(p)),length(ddy(p)));
                float2 wind = _CloudWind.xy;
                wind *= rsqrt(max(dot(wind,wind),1e-12));
                float2 offset = _Time.y * clamp(_CloudSpeed,0,.1) * wind;
                offset -= floor(offset*(1.0/64.0))*64.0;
                p += float3(offset.x,0,offset.y);
                if (_CloudQuality >= .5 && d.y > 0 && _CloudCoverage > 0 && _CloudDensity > 0)
                {
                    bool detailed = _CloudQuality >= 1.5;
                    float shape = Cloud(p,footprint,detailed);
                    float tau = CloudTau(shape);
                    float lightTau = CloudTau(Cloud(p+sun*.55,footprint,detailed));
                    float lighting = exp(-lightTau);
                    float3 cloud = lerp(max(_CloudShadowColor.rgb,0),
                        max(_CloudLightColor.rgb,0),lighting*day);
                    cloud *= lerp(.045,1,day);
                    cloud += warm * twilight * .18;
                    // Restrained forward rim, extinguished as optical depth grows.
                    cloud += max(_SunColor.rgb,0)*pow(saturate(mu),24)
                        * exp(-tau*.7)*(1-exp(-tau))*.18*day;
                    float fade = smoothstep(0,.06,d.y);
                    float transmission = exp(-tau*fade);
                    // Sun disc and halo are behind the cloud extinction.
                    sky = sky*transmission + cloud*(1-transmission);
                }
                sky = lerp(sky,max(_GroundColor.rgb,0)*lerp(.018,.6,day),
                    smoothstep(0,.16,-d.y));
                // Linear HDR until ordinary URP post; no second sRGB conversion.
                return float4(clamp(sky*clamp(_Exposure,0,8),0,60000),1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
