from pathlib import Path
import datetime,hashlib,json,shutil,subprocess
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[1]
frozen=ROOT/'.terrain-cache/root-contact-mesh-prototype'
immutable=[frozen/name for name in ['SharedUpperSplit.cs','SplitChecks.cs','split_check.py','shared-upper.json']]
pack=ROOT.parent/'AshenVoxel/Assets/AshenVoxel/Exploration/Prototypes/ExplorationPrototypes.bytes'
assert hashlib.sha256(pack.read_bytes()).hexdigest()=='41556b474ce7a26342e4dd7bcff2e4fe658ab0333e8360058262ee21c0f011aa'
sources=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))+[ROOT/'Assets/AshenVoxel/WorldScaleData/ExplorationPrototypePack.cs',frozen/'SharedUpperSplit.cs',HERE/'AlignedSeamChecks.cs']
immutable += [HERE/name for name in ['AllLodChecks.cs','run.py','all-lods.json','provenance.json','README.md','seam30-manifest.json']]
inputs=list(dict.fromkeys(sources+immutable+[pack,HERE/'aligned_run.py']))
hashes={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs}
dotnet=Path(shutil.which('dotnet'));version=lambda p:tuple(map(int,p.name.split('.')))
sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1];refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
assembly=HERE/'AlignedSeamChecks.dll';q=lambda p:'"'+str(p)+'"'
options=['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:exe','-out:'+q(assembly)]+['-r:'+q(p) for p in refs.glob('*.dll')]+[q(p) for p in sources]
rsp=HERE/'aligned-compile.rsp';rsp.write_text('\n'.join(options),encoding='utf8')
result=subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],capture_output=True,text=True,encoding='utf8');(HERE/'aligned-compile.log').write_text(result.stdout+result.stderr,encoding='utf8');print(result.stdout+result.stderr);result.check_returncode()
assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}),encoding='utf8')
result=subprocess.run([str(dotnet),str(assembly),str(pack),str(HERE/'aligned-seams.json')],capture_output=True,text=True,encoding='utf8');(HERE/'aligned-run.log').write_text(result.stdout+result.stderr,encoding='utf8');print(result.stdout+result.stderr);result.check_returncode()
assert hashes=={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs},'Immutable input changed during supplemental check'
(HERE/'aligned-provenance.json').write_text(json.dumps({'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'status':'PASS','scope':'All outputs confined to supplemental folder; original helper/tests/report, Core/reader sources and main AVPM bytes unchanged. Windows .NET CPU verification only.','source_and_input_sha256':hashes,'report_sha256':hashlib.sha256((HERE/'aligned-seams.json').read_bytes()).hexdigest()},indent=2),encoding='utf8')
