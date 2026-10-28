from pathlib import Path
import hashlib,json,subprocess,shutil,datetime
HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[1]
main=ROOT.parent/'AshenVoxel'
rows=json.loads((ROOT/'.terrain-cache/grounding-root-variants/candidate-actual-world.json').read_text())['rows']
sites=[next(r for r in rows if r['id']==i and r['selection'].startswith('naturally') and 5<r['slope']<8) for i in [10,11]]
(HERE/'sites.json').write_text(json.dumps(sites,indent=2),encoding='utf-8')
version=lambda p:tuple(int(n) for n in p.name.split('.'))
dotnet=Path(shutil.which('dotnet'));sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1]
refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
sources=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))+sorted((ROOT/'Assets/AshenVoxel/WorldScaleData').glob('*.cs'))+[HERE/'Export.cs']
samples=main/'Assets/AshenVoxel/Exploration/Samples';meta=json.loads((samples/'exploration-world.json').read_text())
inputs=sources+[samples/'exploration-world.json']+[samples/meta[part][key] for part in ['macro','fine'] for key in ['height_file','climate_file']]+[samples/meta['source_biomes']['file']]
hashes={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs}
assembly=HERE/'export.dll';q=lambda p:'"'+str(p)+'"'
opts=['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:exe','-out:'+q(assembly)]+['-r:'+q(p) for p in refs.glob('*.dll')]+[q(p) for p in sources]
rsp=HERE/'compile.rsp';rsp.write_text('\n'.join(opts),encoding='utf-8')
subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
subprocess.run([str(dotnet),str(assembly),str(HERE),str(main)],check=True)
assert hashes=={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs},'Inputs changed during export'
(HERE/'provenance.json').write_text(json.dumps({'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'source_sha256':hashes,'scope':'Offline CPU root contact shape comparison. No Assets edits, no Unity.'},indent=2),encoding='utf-8')
