#!/usr/bin/env python3
"""Execute actual exploration movement and controller with Unity API substitutes."""
from pathlib import Path
import datetime,hashlib,json,os,shutil,subprocess
ROOT=Path(__file__).resolve().parents[1]
def version(p):return tuple(int(v) if v.isdigit() else 0 for v in p.name.split('.'))
def main():
 candidates=[Path(os.environ['DOTNET_ROOT'])/'dotnet' if os.environ.get('DOTNET_ROOT') else None,Path(shutil.which('dotnet')) if shutil.which('dotnet') else None,ROOT.parent/'.dotnet/dotnet']
 dotnet=next((p.resolve() for p in candidates if p and p.is_file()),None)
 if dotnet is None:raise RuntimeError('Install .NET8 SDK or set DOTNET_ROOT')
 sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1];refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
 out=ROOT/'Tools/ExplorationMovementChecks/bin';out.mkdir(parents=True,exist_ok=True);q=lambda p:'"'+str(p)+'"'
 data=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))+sorted((ROOT/'Assets/AshenVoxel/WorldScaleData').glob('*.cs'))
 sources=data+[ROOT/'Assets/AshenVoxel/Exploration/ExplorationController.cs']+sorted((ROOT/'Tools/ExplorationMovementChecks').glob('*.cs'))
 hashes={p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}
 def compile(assembly,reference,sources,target):
  options=['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:'+target,'-out:'+q(assembly)]
  options+=['-r:'+q(p) for p in sorted(reference.glob('*.dll'))]+[q(p) for p in sources]
  rsp=out/(assembly.stem+'.rsp');rsp.write_text('\n'.join(options));subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
 assembly=out/'ExplorationMovementChecks.dll';compile(assembly,refs,sources,'exe')
 (out/'ExplorationMovementChecks.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
 report=ROOT/'Docs/exploration-movement-validation.json';subprocess.run([str(dotnet),str(assembly),str(report)],check=True)
 standard=sorted((dotnet.parent/'packs/NETStandard.Library.Ref').glob('2.1.*'),key=version)
 if standard:compile(out/'ExplorationMovementNetStandard.dll',standard[-1]/'ref/netstandard2.1',data,'library');print('PASS C#9/.NET Standard2.1 movement API compile')
 if hashes!={p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}:raise RuntimeError('Source changed during checks; retry after source freeze')
 d=json.loads(report.read_text());d.update({'source_sha256':hashes,'generated_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'sdk':sdk.name});report.write_text(json.dumps(d,indent=2)+'\n')
if __name__=='__main__':main()
