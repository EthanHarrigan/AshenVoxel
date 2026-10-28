#!/usr/bin/env python3
"""Compile/run actual ground-cover habitat and materialization checks without Unity or package restore."""
from pathlib import Path
import datetime,hashlib,json,os,shutil,subprocess
ROOT=Path(__file__).resolve().parents[1]
def version(p):return tuple(int(v) if v.isdigit() else 0 for v in p.name.split('.'))
def main():
 candidates=[Path(os.environ['DOTNET_ROOT'])/'dotnet' if os.environ.get('DOTNET_ROOT') else None,Path(shutil.which('dotnet')) if shutil.which('dotnet') else None,ROOT.parent/'.dotnet/dotnet']
 dotnet=next((p.resolve() for p in candidates if p and p.is_file()),None)
 if dotnet is None:raise RuntimeError('Install .NET8 SDK or set DOTNET_ROOT')
 sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1];refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
 out=ROOT/'Tools/GroundCoverChecks/bin';out.mkdir(parents=True,exist_ok=True);q=lambda p:'"'+str(p)+'"'
 core=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))+sorted((ROOT/'Assets/AshenVoxel/WorldScaleData').glob('*.cs'))
 files=core+[ROOT/'Tools/GroundCoverChecks/Program.cs'];hashes={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in files}
 def compile(assembly,reference,sources,target):
  options=['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:'+target,'-out:'+q(assembly)]
  options+=['-r:'+q(p) for p in sorted(reference.glob('*.dll'))]+[q(p) for p in sources]
  rsp=out/(assembly.stem+'.rsp');rsp.write_text('\n'.join(options));subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
 assembly=out/'GroundCoverChecks.dll';compile(assembly,refs,files,'exe')
 (out/'GroundCoverChecks.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
 result=ROOT/'Docs/groundcover-validation.json';subprocess.run([str(dotnet),str(assembly),str(result)],check=True)
 standard=sorted((dotnet.parent/'packs/NETStandard.Library.Ref').glob('2.1.*'),key=version)
 if standard:compile(out/'GroundCoverNetStandard.dll',standard[-1]/'ref/netstandard2.1',core,'library');print('PASS C#9/.NET Standard2.1 ground-cover API compile')
 if hashes!={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in files}:raise RuntimeError('Source changed during checks; retry after edits settle')
 d=json.loads(result.read_text());d.update({'source_sha256':hashes,'generated_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'sdk':sdk.name});result.write_text(json.dumps(d,indent=2)+'\n')
if __name__=='__main__':main()
