#!/usr/bin/env python3
"""Survey actual shipped world habitats and safe example coordinates without Unity."""
from pathlib import Path
import datetime,hashlib,json,os,shutil,subprocess
ROOT=Path(__file__).resolve().parents[1]
def ver(p):return tuple(int(x) if x.isdigit() else 0 for x in p.name.split('.'))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 options=[Path(os.environ['DOTNET_ROOT'])/'dotnet' if os.environ.get('DOTNET_ROOT') else None,Path(shutil.which('dotnet')) if shutil.which('dotnet') else None,ROOT.parent/'.dotnet/dotnet'];dotnet=next(p.resolve() for p in options if p and p.is_file());sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=ver)[-1];refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=ver)[-1]/'ref/net8.0';out=ROOT/'Tools/ExplorationWorldSurvey/bin';out.mkdir(parents=True,exist_ok=True)
 files=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))+sorted((ROOT/'Assets/AshenVoxel/WorldScaleData').glob('*.cs'))+[ROOT/'Tools/ExplorationWorldSurvey/Program.cs'];hashes={str(p.relative_to(ROOT)):sha(p) for p in files};q=lambda p:'"'+str(p)+'"';assembly=out/'ExplorationWorldSurvey.dll';rsp=out/'compile.rsp';rsp.write_text('\n'.join(['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:exe','-out:'+q(assembly)]+['-r:'+q(p) for p in sorted(refs.glob('*.dll'))]+[q(p) for p in files]));subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
 assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}));report=ROOT/'Docs/exploration-world-survey.json';subprocess.run([str(dotnet),str(assembly),str(ROOT),str(report)],check=True)
 assert hashes=={str(p.relative_to(ROOT)):sha(p) for p in files},'Source changed during survey'
 assets=list((ROOT/'Assets/AshenVoxel/Exploration/Samples').glob('*.bytes'))+[ROOT/'Assets/AshenVoxel/Exploration/Samples/exploration-world.json',ROOT/'Assets/AshenVoxel/Exploration/Prototypes/ExplorationPrototypes.bytes'];data=json.loads(report.read_text());data.update({'generated_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'source_sha256':hashes,'asset_sha256':{str(p.relative_to(ROOT)):sha(p) for p in assets}});report.write_text(json.dumps(data,indent=2)+'\n')
if __name__=='__main__':main()
