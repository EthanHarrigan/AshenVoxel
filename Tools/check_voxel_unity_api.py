"""Compile affected runtime assemblies against installed real Unity APIs.
Does not import assets, compile HLSL, run the Editor, or measure player frames.
"""
from pathlib import Path
import argparse, datetime, hashlib, json, shutil, subprocess
ROOT=Path(__file__).resolve().parents[1]
def version(p):return tuple(map(int,p.name.split('.')))
def main():
    parser=argparse.ArgumentParser();parser.add_argument('--editor',type=Path,default=Path('C:/Program Files/Unity/Hub/Editor/6000.3.27f1/Editor'));parser.add_argument('--report',type=Path,default=ROOT/'Docs/VoxelTerrainEvidence/unity-api.json');args=parser.parse_args()
    dotnet=Path(shutil.which('dotnet')).resolve();sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1]
    refs=sorted((dotnet.parent/'packs/NETStandard.Library.Ref').glob('2.1.*'),key=version)[-1]/'ref/netstandard2.1'
    out=ROOT/'Tools/VoxelTerrainChecks/bin/unity-api';out.mkdir(parents=True,exist_ok=True)
    external=sorted((args.editor/'Data/Managed/UnityEngine').glob('*.dll'))
    external += [ROOT/'Library/ScriptAssemblies/Unity.InputSystem.dll']
    external += [ROOT/'Library/ScriptAssemblies'/name for name in ['Unity.RenderPipelines.Core.Runtime.dll','Unity.RenderPipelines.Universal.Runtime.dll']]
    built=[];hashes={};q=lambda p:'"'+str(p)+'"'
    for folder in ['Core','WorldScaleData','Rendering','WorldScaleRuntime','FiniteMatter','Exploration']:
        directory=ROOT/'Assets/_Validation/FiniteMatterSlice/Runtime' if folder=='FiniteMatter' else ROOT/'Assets/AshenVoxel'/folder
        sources=sorted(p for p in directory.rglob('*.cs') if 'Editor' not in p.relative_to(directory).parts)
        assembly=out/('AshenVoxel.'+folder+'.dll')
        files=sorted(refs.glob('*.dll'))+external+built
        options=['-nologo','-nostdlib+','-define:UNITY_5_3_OR_NEWER,UNITY_STANDALONE_WIN,ENABLE_UPSCALER_FRAMEWORK,ENABLE_NVIDIA,ASHEN_NVIDIA_MODULE','-langversion:9.0','-target:library','-optimize+','-out:'+q(assembly)]
        options+=['-r:'+q(p) for p in files]+[q(p) for p in sources]
        rsp=assembly.with_suffix('.rsp');rsp.write_text('\n'.join(options),encoding='utf-8')
        subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
        built.append(assembly);hashes.update({p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in sources})
        print('PASS official Unity API compile:',folder,flush=True)
    report=args.report
    report.parent.mkdir(parents=True,exist_ok=True);report.write_text(json.dumps(dict(status='PASS',scope='Real installed Unity6000.3.27f1 references, six runtime assembly boundaries including FiniteMatter, C#9/.NETStandard2.1. No native import, shader or play test.',source_sha256=hashes,generated_utc=datetime.datetime.now(datetime.timezone.utc).isoformat()),indent=2)+'\n')
if __name__=='__main__':main()
