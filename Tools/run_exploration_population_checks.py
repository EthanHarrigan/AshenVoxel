#!/usr/bin/env python3
from pathlib import Path
import datetime,hashlib,json,os,shutil,subprocess
ROOT=Path(__file__).resolve().parents[1]
def ver(p):return tuple(int(x) if x.isdigit() else 0 for x in p.name.split('.'))
def main():
 opts=[Path(os.environ['DOTNET_ROOT'])/'dotnet' if os.environ.get('DOTNET_ROOT') else None,Path(shutil.which('dotnet')) if shutil.which('dotnet') else None,ROOT.parent/'.dotnet/dotnet'];dotnet=next(p.resolve() for p in opts if p and p.is_file());sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=ver)[-1];refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=ver)[-1]/'ref/net8.0';out=ROOT/'Tools/ExplorationPopulationChecks/bin';out.mkdir(parents=True,exist_ok=True)
 core=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))+sorted((ROOT/'Assets/AshenVoxel/WorldScaleData').glob('*.cs'));files=core+[ROOT/'Tools/ExplorationPopulationChecks/Program.cs'];hashes={p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in files};q=lambda p:'"'+str(p)+'"'
 def compile(assembly,reference,sources,target):
  options=['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:'+target,'-out:'+q(assembly)]+['-r:'+q(p) for p in sorted(reference.glob('*.dll'))]+[q(p) for p in sources];rsp=out/(assembly.stem+'.rsp');rsp.write_text('\n'.join(options));subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
 assembly=out/'ExplorationPopulationChecks.dll';compile(assembly,refs,files,'exe');assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}));report=ROOT/'Docs/exploration-population-validation.json';command=[str(dotnet),str(assembly),str(report)];pack=ROOT/'Assets/AshenVoxel/Exploration/Prototypes/ExplorationPrototypes.bytes'
 if pack.exists():command.extend([str(pack),str(ROOT/'Docs/exploration-prototype-bake.json')])
 subprocess.run(command,check=True);standard=sorted((dotnet.parent/'packs/NETStandard.Library.Ref').glob('2.1.*'),key=ver)
 if standard:compile(out/'ExplorationPopulationNetStandard.dll',standard[-1]/'ref/netstandard2.1',core,'library');print('PASS C#9/.NET Standard2.1 population and prototype API compile')
 assert hashes=={p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in files},'Source changed during checks'
 data=json.loads(report.read_text());data.update({'generated_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'source_sha256':hashes});report.write_text(json.dumps(data,indent=2)+'\n')
if __name__=='__main__':main()
