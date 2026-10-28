Shader "AshenVoxel/WildlifeDraftVertexColor"
{
    Properties { _BaseColor("Color multiplier",Color)=(1,1,1,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "WildlifeForward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END
            struct Input { float3 positionOS:POSITION;float3 normalOS:NORMAL;half4 color:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varying { float4 positionCS:SV_POSITION;half3 normalWS:TEXCOORD0;half3 color:TEXCOORD1; };
            Varying Vert(Input v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Varying o;o.positionCS=TransformObjectToHClip(v.positionOS);o.normalWS=TransformObjectToWorldNormal(v.normalOS);o.color=v.color.rgb*_BaseColor.rgb;return o;
            }
            half4 Frag(Varying v):SV_Target
            {
                half3 n=normalize(v.normalWS);Light sun=GetMainLight();
                half3 lighting=max(SampleSH(n),half3(.12,.12,.12))+sun.color*saturate(dot(n,sun.direction));
                return half4(v.color*lighting,1);
            }
            ENDHLSL
        }
    }
}
