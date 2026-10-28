from pathlib import Path
import json,hashlib,datetime,shutil,subprocess
root=Path.cwd(); out=root/'.terrain-cache/coarse320-install/api';out.mkdir(parents=True,exist_ok=True)
dotnet=Path(shutil.which('dotnet')); ver=lambda p:tuple(map(int,p.name.split('.')))
sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=ver)[-1]
std=sorted((dotnet.parent/'packs/NETStandard.Library.Ref').glob('2.1.*'),key=ver)[-1]/'ref/netstandard2.1'
unity=Path('C:/Program Files/Unity/Hub/Editor/6000.3.27f1/Editor/Data/Managed')
external=list(std.glob('*.dll'))+list((unity/'UnityEngine').glob('*.dll'))+list((unity/'UnityEditor').glob('*.dll'))
external += [p for p in (root/'Library/ScriptAssemblies').glob('*.dll') if not p.name.startswith('AshenVoxel.')]
records=[];built={}
for name in ['AshenVoxel.Rendering','AshenVoxel.WorldScaleData','AshenVoxel.WorldScaleRuntime','AshenVoxel.Exploration','AshenVoxel.Exploration.Editor']:
    asm=next((root/'Assets').rglob(name+'.asmdef'));desc=json.loads(asm.read_text())
    sources=[]
    for p in asm.parent.rglob('*.cs'):
        folder=p.parent
        while folder!=asm.parent and not list(folder.glob('*.asmdef')):folder=folder.parent
        if folder==asm.parent:sources.append(p)
    references=list(external)
    for dep in desc['references']:
        if dep.startswith('AshenVoxel.'):references.append(built.get(dep,root/'Library/ScriptAssemblies'/(dep+'.dll')))
    q=lambda p:'"'+str(p)+'"'
    output=out/(name+'.dll');rsp=out/(name+'.rsp')
    hashes={str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}
    opts=['-nologo','-nostdlib+','-warnaserror-' if name.endswith('.Editor') else '-warnaserror+','-langversion:9.0','-target:library','-define:UNITY_EDITOR,UNITY_5_3_OR_NEWER,UNITY_6000_0_OR_NEWER,UNITY_EDITOR_WIN,UNITY_STANDALONE_WIN,ASHEN_NVIDIA_MODULE,ENABLE_NVIDIA,ENABLE_UPSCALER_FRAMEWORK','-out:'+q(output)]
    rsp.write_text('\n'.join(opts+['-r:'+q(p) for p in references]+[q(p) for p in sources]))
    r=subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],capture_output=True,text=True)
    print(name,r.returncode,r.stdout+r.stderr)
    records.append(dict(assembly=name,status='PASS' if r.returncode==0 else 'FAIL',output=r.stdout+r.stderr,source_sha256=hashes))
    (out/'result.json').write_text(json.dumps(dict(utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),scope='C#9 full owned assembly source against actual Unity APIs; not import/render/play',records=records),indent=2))
    if r.returncode:raise SystemExit(r.returncode)
    built[name]=output






