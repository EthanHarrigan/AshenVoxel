"""Create only a small terrain benchmark project. No main Editor/scene interaction."""
from pathlib import Path
import shutil,json
from prepare import CACHE,HERE,replace_once

def main():
    project=CACHE/'native';frozen=CACHE/'frozen';candidate=CACHE/'parallel-unboxed'
    project.mkdir(exist_ok=False)
    for folder in ['Core','WorldScaleData']:
        dest=project/'Assets'/folder;dest.mkdir(parents=True)
        for source in (frozen/'Assets/AshenVoxel'/folder).rglob('*.cs'):
            target=dest/source.relative_to(frozen/'Assets/AshenVoxel'/folder);target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,target)
    shutil.copy2(candidate/'VoxelLodSurface.cs',project/'Assets/WorldScaleData/VoxelLodSurface.cs')
    for f in ['BaselineSurfaceBuilder.cs','UnboxedSurfaceBuilder.cs']:shutil.copy2(candidate/f,project/'Assets/WorldScaleData'/f)
    shutil.copy2(HERE/'Probe.cs',project/'Assets/Probe.cs')
    (project/'Assets/Editor').mkdir();shutil.copy2(HERE/'NativeBenchmark.cs',project/'Assets/Editor/NativeBenchmark.cs')
    (project/'Packages').mkdir();(project/'Packages/manifest.json').write_text(json.dumps({'dependencies':{'com.unity.render-pipelines.universal':'17.3.0','com.unity.modules.jsonserialize':'1.0.0','com.unity.modules.imgui':'1.0.0','com.unity.modules.physics':'1.0.0'}}))
    (project/'ProjectSettings').mkdir();shutil.copy2(frozen/'ProjectSettings/ProjectVersion.txt',project/'ProjectSettings/ProjectVersion.txt')
    shutil.copytree(frozen/'Assets/AshenVoxel/Exploration/Samples',project/'Samples')
    shader=(frozen/'Assets/AshenVoxel/WorldScaleRuntime/GroundSurface.shader').read_text()
    (project/'Assets/GroundSurface.shader').write_text(shader)
    shader=shader.replace('Shader "AshenVoxel/GroundSurface"','Shader "AshenVoxel/BenchmarkQuad"').replace('#pragma target 3.5','#pragma target 4.5')
    decode='''
        struct QuadRecord { float3 position; float2 extent; uint tint; uint ao; uint properties; };
        StructuredBuffer<QuadRecord> _Quads;
        void ReadQuad(uint id, out float4 position, out float3 normal, out half4 color, out float4 surface)
        {
            QuadRecord q=_Quads[id/6];uint tri=id%6;
            uint axis=(q.properties>>24)&3,positive=(q.properties>>26)&1;
            uint a0=q.ao&255,a1=(q.ao>>8)&255,a2=(q.ao>>16)&255,a3=q.ao>>24;
            if(positive==0){if(tri%3==1)tri+=1;else if(tri%3==2)tri-=1;}
            uint corner;
            if(a0+a2>a1+a3){const uint corners[6]={0,1,3,1,2,3};corner=corners[tri];}
            else {const uint corners[6]={0,1,2,0,2,3};corner=corners[tri];}
            float3 u=(axis==0?float3(0,1,0):axis==1?float3(0,0,1):float3(1,0,0))*q.extent.x;
            float3 v=(axis==0?float3(0,0,1):axis==1?float3(1,0,0):float3(0,1,0))*q.extent.y;
            float3 p=q.position;
            if(corner==1)p+=u;else if(corner==2){p+=u;p+=v;}else if(corner==3)p+=v;
            position=float4(p,1);normal=(axis==0?float3(1,0,0):axis==1?float3(0,1,0):float3(0,0,1))*(positive?1:-1);
            color=half4((q.tint>>16)&255,(q.tint>>8)&255,q.tint&255,(q.ao>>(8*corner))&255)/255.0;
            uint material=(q.properties>>16)&255;
            surface=float4(q.properties&255,(q.properties>>8)&255,material==15?255:0,255)/255.0;
        }
'''
    # Preserve original material enum for snow rather than assuming the packed value.
    enum=(frozen/'Assets/AshenVoxel/Core/Materials.cs').read_text()
    import re
    snow=int(re.search(r'Snow\s*=\s*(\d+)',enum)[1]);decode=decode.replace('material==15?',f'material=={snow}?')
    shader=replace_once(shader,'        CBUFFER_START(UnityPerMaterial)',decode+'\n        CBUFFER_START(UnityPerMaterial)')
    shader=shader.replace('struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; float4 surface : TEXCOORD0; };','struct Attributes { uint id : SV_VertexID; };')
    shader=shader.replace('struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };','struct Attributes { uint id : SV_VertexID; };')
    for name in ['Vert','ShadowVert','NormalVert']:
        signature=('Varyings ' if name in ['Vert','NormalVert'] else 'ShadowVaryings ')+name+'(Attributes input)\n            {'
        shader=replace_once(shader,signature,signature+'\n                float4 positionOS,surface;float3 normalOS;half4 color;ReadQuad(input.id,positionOS,normalOS,color,surface);')
    shader=shader.replace('input.positionOS','positionOS').replace('input.normalOS','normalOS').replace('input.color','color').replace('input.surface','surface')
    # The replacements above also affect fragment inputs: restore those separately.
    frag_start=shader.index('            half4 Frag(');frag_end=shader.index('            ENDHLSL',frag_start)
    old_frag=(frozen/'Assets/AshenVoxel/WorldScaleRuntime/GroundSurface.shader').read_text();a=old_frag.index('            half4 Frag(');b=old_frag.index('            ENDHLSL',a)
    shader=shader[:frag_start]+old_frag[a:b]+shader[frag_end:]
    shader=replace_once(shader,'DepthVaryings DepthVert(float4 positionOS : POSITION)\n            {','DepthVaryings DepthVert(uint id : SV_VertexID)\n            { float4 positionOS,surface;float3 normalOS;half4 color;ReadQuad(id,positionOS,normalOS,color,surface);')
    (project/'Assets/BenchmarkQuad.shader').write_text(shader)
    (project/'Assets/DepthCopy.shader').write_text('''Shader "Hidden/BenchmarkDepthCopy" { SubShader { Pass { ZTest Always ZWrite Off Cull Off
HLSLPROGRAM
#pragma vertex vert_img
#pragma fragment frag
#include "UnityCG.cginc"
UNITY_DECLARE_DEPTH_TEXTURE(_Depth);
float4 frag(v2f_img i):SV_Target { float d=SAMPLE_DEPTH_TEXTURE(_Depth,i.uv);return float4(d,d,d,1); }
ENDHLSL
} } }''')
    print(project)
if __name__=='__main__':main()
