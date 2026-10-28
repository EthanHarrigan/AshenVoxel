Shader "Hidden/AshenVoxel/ForestAtlasBake"
{
    Properties { _BakeNormals("Normal and AO capture",Float)=0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            float _BakeNormals;
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; half4 color:COLOR; };
            struct V { float4 positionCS:SV_POSITION; half4 color:COLOR; half3 normal:TEXCOORD0; nointerpolation uint material:TEXCOORD1;half ao:TEXCOORD2; };
            V Vert(A a){V o;o.positionCS=TransformObjectToHClip(a.positionOS.xyz);o.color=a.color;uint packed=(uint)round(a.color.a*255);o.material=packed>>2;o.ao=packed&3;o.normal=TransformObjectToWorldNormal(a.normalOS);return o;}
            half4 Frag(V i):SV_Target
            {
                half3 pigment=SRGBToLinear(i.color.rgb);
                if(_BakeNormals>.5)return half4(normalize(i.normal)*.5+.5,(i.material*4+round(i.ao))/255.0);
                return half4(pigment,1);
            }
            ENDHLSL
        }
    }
}
