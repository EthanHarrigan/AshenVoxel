Shader "AshenVoxel/ExplorationSky"
{
    Properties
    {
        _SkyTint("Zenith tint",Color)=(.42,.53,.65,1)
        _GroundColor("Ground",Color)=(.12,.13,.12,1)
        _SunDirection("Sun direction",Vector)=(.4,.6,.3,0)
        _Exposure("Exposure",Float)=1
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _SkyTint,_GroundColor,_SunDirection;float _Exposure;
            struct A { float4 positionOS:POSITION; };
            struct V { float4 positionCS:SV_POSITION;float3 direction:TEXCOORD0; };
            V Vert(A a){V o;o.positionCS=TransformObjectToHClip(a.positionOS.xyz);o.direction=a.positionOS.xyz;return o;}
            float Hash(float2 p){p=frac(p*float2(.1031,.11369));p+=dot(p,p.yx+19.19);return frac(p.x*p.y);}
            float Noise(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y);}
            float Cloud(float2 p){return Noise(p)*.57+Noise(p*2.03+13)*.27+Noise(p*4.11+47)*.11+Noise(p*8.17+97)*.05;}
            half4 Frag(V i):SV_Target
            {
                float3 d=normalize(i.direction);float h=saturate(d.y);
                float3 zenith=float3(.13,.32,.63)*lerp(float3(1,1,1),_SkyTint.rgb/float3(.42,.53,.65),.35);
                float3 sky=lerp(float3(.68,.79,.88),zenith,pow(h,.45));
                float sun=saturate(dot(d,normalize(_SunDirection.xyz)));
                sky+=float3(1,.84,.61)*(pow(sun,96)*.22+smoothstep(.9995,.99985,sun)*5);
                if(d.y>.08)
                {
                    float2 p=d.xz/max(.08,d.y)*2.2+float2(14,37);
                    float density=smoothstep(.52,.69,Cloud(p));
                    float shade=Cloud(p+normalize(_SunDirection.xz+float2(.001,0))*.16);
                    float3 cloud=lerp(float3(.55,.64,.74),float3(1.13,1.12,1.07),saturate(.25+(shade-.4)*2.5));
                    sky=lerp(sky,cloud,density*smoothstep(.08,.20,d.y));
                }
                sky=lerp(_GroundColor.rgb*.6,sky,smoothstep(-.16,.02,d.y));
                return half4(sky*_Exposure,1);
            }
            ENDHLSL
        }
    }
}
