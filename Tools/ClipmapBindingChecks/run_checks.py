"""Production resource + attachment code with a modeled native-material boundary.
Red uses the original broad-copy attachment; green uses the current attachment.
This is an ownership/call-order regression, not native Unity material/render proof.
"""
from pathlib import Path
import datetime,hashlib,json,shutil,subprocess
ROOT=Path(__file__).resolve().parents[2]
def main():
    folder=Path(__file__).parent;out=folder/'bin';out.mkdir(exist_ok=True)
    dotnet=Path(shutil.which('dotnet'));sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=lambda p:tuple(map(int,p.name.split('.'))))[-1]
    refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'))[-1]/'ref/net8.0'
    resource=ROOT/'Assets/AshenVoxel/WorldScaleRuntime/TerrainClipmapResources.cs';attach=ROOT/'Assets/AshenVoxel/WorldScaleRuntime/WorldExplorerClipmap.cs'
    old=out/'OriginalAttach.cs';old.write_bytes(subprocess.check_output(['git','show','b13d147:Assets/AshenVoxel/WorldScaleRuntime/WorldExplorerClipmap.cs'],cwd=ROOT))
    results=[]
    for label,path in [('red',old),('green',attach)]:
        assembly=out/(label+'.dll');q=lambda p:'"'+str(p)+'"'
        args=['-nologo','-langversion:9.0','-define:UNITY_5_3_OR_NEWER','-target:exe','-out:'+q(assembly)]
        args+=['-r:'+q(p) for p in sorted(refs.glob('*.dll'))]+[q(p) for p in [resource,path,folder/'UnityShim.cs',folder/'Program.cs']]
        rsp=out/(label+'.rsp');rsp.write_text('\n'.join(args))
        subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
        assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
        result=subprocess.run([str(dotnet),str(assembly)],capture_output=True,text=True)
        results.append(dict(label=label,exit_code=result.returncode,stdout=result.stdout,stderr=result.stderr))
        print(label,result.returncode,result.stdout,result.stderr)
    assert results[0]['exit_code']!=0 and 'attach retains exact owned height texture' in results[0]['stderr']
    assert results[1]['exit_code']==0
    evidence=ROOT/'Docs/ClipmapEvidence/material-binding-20261029';evidence.mkdir(parents=True,exist_ok=True)
    files=[resource,attach,folder/'UnityShim.cs',folder/'Program.cs',Path(__file__)]
    (evidence/'binding-regression.json').write_text(json.dumps(dict(status='PASS',scope=__doc__,generated_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),source_sha256={p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in files},results=results),indent=2)+'\n')
if __name__=='__main__':main()
