#!/usr/bin/env python3
"""Bake actual voxel generators and greedy meshes into checksummed shared instancing assets."""
from pathlib import Path
import argparse,hashlib,json,subprocess,shutil,os,struct,datetime
os.environ.setdefault("PYTHONUTF8","1")
ROOT=Path(__file__).resolve().parents[2];BUILD=ROOT/'Tools/ExplorationPrototypes/bin';OUT=ROOT/'Assets/AshenVoxel/Exploration/Prototypes'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def version(p):return tuple(int(v) if v.isdigit() else 0 for v in p.name.split('.'))
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--interaction-source',type=Path,required=True);p.add_argument('--interaction-provenance',type=Path);p.add_argument('--mode',choices=['all','trees','plants','rocks','pack'],default='all');a=p.parse_args();
 reference_path=a.interaction_provenance or a.interaction_source.with_suffix('.json');reference=json.loads(reference_path.read_text());
 # Render-only migration: changing authoritative generator/occupancy code needs a fresh interaction bake.
 source_hashes=reference['bakes'][0]['source_sha256']
 for name,digest in source_hashes.items():
  name=name.replace('\\','/')
  if name.startswith('Assets/AshenVoxel/Core/') and not name.endswith(('Materials.cs','VoxelColor.cs')):assert sha(ROOT/name)==digest,'Authoritative source changed; rebuild interaction channels: '+name
 BUILD.mkdir(parents=True,exist_ok=True);OUT.mkdir(parents=True,exist_ok=True)
 if a.mode!='pack':
  options=[Path(os.environ['DOTNET_ROOT'])/'dotnet' if os.environ.get('DOTNET_ROOT') else None,Path(shutil.which('dotnet')) if shutil.which('dotnet') else None,ROOT.parent/'.dotnet/dotnet'];dotnet=next(p.resolve() for p in options if p and p.is_file());sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1];refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
  files=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))+[ROOT/'Assets/AshenVoxel/WorldScaleData/ExplorationPrototypePack.cs',ROOT/'Assets/AshenVoxel/Rendering/GreedyChunkMesher.cs',ROOT/'Tools/ExplorationPrototypes/UnityShim.cs',ROOT/'Tools/ExplorationPrototypes/ExportPrototypes.cs'];hashes={str(p.relative_to(ROOT)):sha(p) for p in files}
  snapshots=[]
  for source in files:
   dest=BUILD/('source-'+a.mode)/source.relative_to(ROOT);dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(source.read_bytes());snapshots.append(dest)
  dll=BUILD/'ExportPrototypes.dll';q=lambda p:'"'+str(p)+'"';opts=['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:exe','-out:'+q(dll)]+['-r:'+q(p) for p in sorted(refs.glob('*.dll'))]+[q(p) for p in snapshots]
  rsp=BUILD/'compile.rsp';rsp.write_text('\n'.join(opts));dll.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}));subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
  subprocess.run([str(dotnet),str(dll),str(BUILD),a.mode],check=True)
  assert hashes=={str(p.relative_to(ROOT)):sha(p) for p in files},'Prototype source changed during bake'
  (BUILD/('provenance-'+a.mode+'.json')).write_text(json.dumps({'created_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'source_sha256':hashes,'tool_sha256':sha(Path(__file__))},indent=2)+'\n')
 if a.mode in ['all','pack']:
  subprocess.run([os.sys.executable,str(ROOT/'Tools/ExplorationPrototypes/quad_pack.py')],check=True)
  records=sorted(BUILD.glob('*.avpr'));assert len(records)==99, f'Expected99 full prototypes, have{len(records)}'
  with (OUT/'ExplorationPrototypes.bytes').open('wb') as w:
   w.write(b'AVPM0004');w.write(struct.pack('<I',len(records)))
   for r in records:w.write(r.read_bytes())
  from preserve_interactions import preserve
  interaction_report=preserve(a.interaction_source,OUT/'ExplorationPrototypes.bytes')
  report={'status':'PASS','prototypes':99,'created_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'format':'AVPM0004 material-aware exact greedy quads', 'interaction_preservation':interaction_report, 'scope':'Actual Core voxel generators, actual greedy mesher with numerical Unity substitutes; not native Unity or FPS','asset_sha256':sha(OUT/'ExplorationPrototypes.bytes'),'bytes':(OUT/'ExplorationPrototypes.bytes').stat().st_size,'records':[json.loads(r.with_suffix('.json').read_text()) for r in records],'bakes':[json.loads(p.read_text()) for p in sorted(BUILD.glob('provenance-*.json')) if p.name=='provenance-all.json'], 'encoding_validation':json.loads((BUILD/'quad-encoding-validation.json').read_text())}
  (ROOT/'Docs/exploration-prototype-bake.json').write_text(json.dumps(report,indent=2)+'\n');print('PACKED',report['bytes'],'bytes,99 actual prototypes')
if __name__=='__main__':main()
