"""Engine-free loading progress contracts; not Unity UI/render acceptance."""
from pathlib import Path
import datetime,hashlib,json,shutil,subprocess
root=Path(__file__).resolve().parents[2]
out=root/'.terrain-cache/loading-screen-2026-10-27/checks';out.mkdir(parents=True,exist_ok=True)
dotnet=Path(shutil.which('dotnet'));version=lambda p:tuple(map(int,p.name.split('.')))
sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1]
refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
sources=[root/'Assets/AshenVoxel/Exploration/WorldLoadingProgress.cs',Path(__file__).with_name('Program.cs')]
hashes={str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}
q=lambda p:'"'+str(p)+'"'
dll=out/'Checks.dll';rsp=out/'compile.rsp'
rsp.write_text('\n'.join(['-nologo','-warnaserror+','-langversion:9.0','-target:exe','-out:'+q(dll)]+['-r:'+q(p) for p in refs.glob('*.dll')]+[q(p) for p in sources]))
subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
dll.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
r=subprocess.run([str(dotnet),str(dll)],capture_output=True,text=True)
print(r.stdout+r.stderr);r.check_returncode()
assert hashes=={str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}
(out/'result.json').write_text(json.dumps(dict(status='PASS',utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),scope=__doc__,output=r.stdout,source_sha256=hashes),indent=2)+'\n')
