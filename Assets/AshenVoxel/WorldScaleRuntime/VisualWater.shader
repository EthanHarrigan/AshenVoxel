Shader "AshenVoxel/VisualWater"
{
    Properties
    {
        _ShallowColor("Shallow water", Color) = (0.08,0.31,0.32,0.52)
        _DeepColor("Deep water", Color) = (0.025,0.12,0.18,0.78)
        _MaximumDepthHint("Coarse depth color range", Float) = 24
        _WavePhase1("Wave phase 1", Float) = 0
        _WavePhase2("Wave phase 2", Float) = 0
        _WavePhase3("Wave phase 3", Float) = 0
        _WavePhase4("Wave phase 4", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "VisualWater"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest LEqual Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "../Rendering/AshenAtmosphere.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor, _DeepColor;
                float _MaximumDepthHint, _WavePhase1, _WavePhase2, _WavePhase3, _WavePhase4;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half depthHint : TEXCOORD1; half fog : TEXCOORD2; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = p.positionCS; output.positionWS = p.positionWS;
                output.depthHint = input.color.r * _MaximumDepthHint;
                output.fog = ComputeFogFactor(p.positionCS.z); return output;
            }
            half4 Frag(Varyings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float2 p = input.positionWS.xz;
                float footprint = max(length(ddx(p)), length(ddy(p)));
                float4 phase = float4(dot(p,float2(.1963495408,.0981747704)) + _WavePhase1,
                    dot(p,float2(.0490873852,-.1963495408)) + _WavePhase2,
                    dot(p,float2(-.3926990817,.0490873852)) + _WavePhase3,
                    dot(p,float2(.7853981634,.3926990817)) + _WavePhase4);
                // Analytic NORMAL waves only. Occupancy and mean water height do not move.
                float4 amplitude = float4(.12,.075,.035,.015) * saturate(1 - footprint / float4(32,16,8,4));
                float4 c = cos(phase) * amplitude;
                float dx = dot(c,float4(.1963495408,.0490873852,-.3926990817,.7853981634));
                float dz = dot(c,float4(.0981747704,-.1963495408,.0490873852,.3926990817));
                half3 normal = normalize(float3(-dx,1,-dz)) * IS_FRONT_VFACE(facing,1.0,-1.0);
                half3 view = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half fresnel = .02h + .98h * pow(1 - saturate(dot(normal,view)),5);
                half t = saturate(input.depthHint / max(1,_MaximumDepthHint));
                half4 water = lerp(_ShallowColor,_DeepColor,t);
                Light sun = GetMainLight();
                half3 halfVector = SafeNormalize(sun.direction + view);
                half highlight = pow(saturate(dot(normal,halfVector)),160) * saturate(dot(normal,sun.direction));
                half3 sky = max(SampleSH(normal), half3(.12,.17,.22));
                half3 reflection = sky * 1.35h + sun.color * highlight * .8h;
                half3 color = lerp(water.rgb * (sky + .45h),reflection,fresnel);
                return half4(AshenApplyFog(color,input.fog,input.positionWS),saturate(water.a + fresnel * .16h));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
