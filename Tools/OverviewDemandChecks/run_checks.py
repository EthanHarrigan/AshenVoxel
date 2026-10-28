"""Engine-free bounded overview demand/pass checks, with source identities."""
from pathlib import Path
import datetime,hashlib,json,shutil,subprocess
ROOT=Path(__file__).resolve().parents[2]
stamp=datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%d-%H%M%S-%f');out=ROOT/'.terrain-cache/overview-demand'/stamp;out.mkdir(parents=True)
dotnet=Path(shutil.which('dotnet'));version=lambda p:tuple(map(int,p.name.split('.')))
sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1];refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
sources=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))+sorted((ROOT/'Assets/AshenVoxel/WorldScaleData').glob('*.cs'))+[Path(__file__).with_name('Program.cs')]
hashes={str(p.relative_to(ROOT)).replace('\\','/'):hashlib.sha256(p.read_bytes()).hexdigest() for p in sources};q=lambda p:'"'+str(p)+'"'
assembly=out/'OverviewDemandChecks.dll';rsp=out/'compile.rsp';rsp.write_text('\n'.join(['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:exe','-out:'+q(assembly)]+['-r:'+q(p) for p in sorted(refs.glob('*.dll'))]+[q(p) for p in sources]),encoding='utf-8')
result=subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],capture_output=True,text=True);(out/'compile.log').write_text(result.stdout+result.stderr,encoding='utf-8');print(result.stdout+result.stderr,end='');result.check_returncode()
assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
result=subprocess.run([str(dotnet),str(assembly),str(out/'result.json')],capture_output=True,text=True);(out/'console.log').write_text(result.stdout+result.stderr,encoding='utf-8');print(result.stdout+result.stderr,end='');result.check_returncode()
if hashes!={str(p.relative_to(ROOT)).replace('\\','/'):hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}:raise RuntimeError('Source changed during overview checks')
report=json.loads((out/'result.json').read_text());report.update(source_sha256=hashes,generated_utc=datetime.datetime.now(datetime.timezone.utc).isoformat());(out/'result.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8');print(out/'result.json')
