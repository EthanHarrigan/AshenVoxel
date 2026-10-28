#!/usr/bin/env python3
"""Publish executed exploration bakes as portable Unity samples with source hashes."""
from pathlib import Path
import argparse,hashlib,json,struct,sys,uuid,zlib,shutil
import numpy as np
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'Tools/DiffusionBridge'));sys.path.insert(0,str(ROOT/'Tools/WorldScaleImport'))
import bridge as b
from convert_world import read_avwf,write_avwf
from serve_exploration import view_identity
from physical_climate_policy import POLICY,rectify

def sha(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def meta(p,folder=False):
 p=Path(p);m=p.with_name(p.name+'.meta')
 if m.exists():return
 m.write_text('fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\n'+('folderAsset: yes\nDefaultImporter:\n' if folder else 'TextScriptImporter:\n')+'  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')
def convert_pair(source,output,record,view_id):
 h=read_avwf(source/record['height_file']);identity=json.loads(h['source_identity']);identity['native_model_world_id']=identity['world_id'];identity['world_id']=view_id;identity['physical_climate_policy']=POLICY
 destination=output/record['height_file'];write_avwf(destination,h['heights'],h['biomes'],h['land'],h['origin_x'],h['origin_z'],h['extent_x'],h['extent_z'],identity)
 raw=(source/record['climate_file']).read_bytes();cols,rows,ox,oz,ex,ez=struct.unpack_from('<ii4d',raw,8);n=struct.unpack_from('<H',raw,80)[0];old=json.loads(raw[82:82+n]);old['native_model_world_id']=old['world_id'];old['world_id']=view_id;old['physical_climate_policy']=POLICY;old['height_sha256']=sha(destination);encoded=b.canonical(old)
 planes=np.frombuffer(raw,dtype='<f4',count=5*rows*cols,offset=82+n).reshape(5,rows,cols);physical,corrections=rectify(planes)
 b.validate_climate_values(physical,b.CLIMATE_PHYSICAL)
 rebuilt=b'AVCL0001'+struct.pack('<ii4d',cols,rows,ox,oz,ex,ez)+bytes.fromhex(sha(destination))+struct.pack('<H',len(encoded))+encoded+physical.astype('<f4',copy=False).tobytes();rebuilt+=struct.pack('<I',zlib.crc32(rebuilt)&0xffffffff)
 climate=output/record['climate_file'];climate.write_bytes(rebuilt);meta(destination);meta(climate)
 result=dict(record);result['physical_policy_rectified_values']=corrections;result.update({'height_sha256':sha(destination),'climate_sha256':sha(climate),'source_identity':identity,'input_height_sha256':sha(source/record['height_file']),'input_climate_sha256':sha(source/record['climate_file'])})
 return result

def publish(source,output,source_map=None):
 source=Path(source);output=Path(output);output.mkdir(parents=True,exist_ok=True);meta(output,True)
 report=json.loads((source/'exploration-world.json').read_text())
 if 'fine' in report:
  macro=report['macro'];fine=report['fine']
  if fine['origin_x_m']<macro['origin_x_m'] or fine['origin_z_m']<macro['origin_z_m'] or fine['origin_x_m']+fine['extent_x_m']>macro['origin_x_m']+macro['extent_x_m'] or fine['origin_z_m']+fine['extent_z_m']>macro['origin_z_m']+macro['extent_z_m']:raise ValueError('Fine decode coverage must be wholly inside the macro world; increase map size or reduce fine side')
 job=report['job'];view_id=view_identity(job,report['world_meters_per_native_sample'],report['world_width_m'],report['world_depth_m']);result={k:v for k,v in report.items() if k not in ('job','source_sha256')}
 result['derived_publication']='Coordinate and versioned physical-climate publication of already executed native bakes; native execution identity and input hashes retained. This operation is not a new model inference run.';result['physical_climate_policy']=POLICY;result['native_model_world_id']=job['world_id'];result['world_id']=view_id;result['macro']=convert_pair(source,output,report['macro'],view_id)
 if 'fine' in report:result['fine']=convert_pair(source,output,report['fine'],view_id)
 cond=job['spec']['conditioning'];entry=cond['source_biome'];data=np.asarray(np.load(entry['path'],allow_pickle=False),dtype='u1');rows,cols=data.shape;spacing=256*report['world_meters_per_native_sample']
 raw=b'AVBI0001'+struct.pack('<ii3d',cols,rows,cond['origin_j']*spacing,cond['origin_i']*spacing,spacing)+view_id.encode('ascii')+data.tobytes();raw+=struct.pack('<I',zlib.crc32(raw)&0xffffffff)
 biome=output/'ExplorationBiomes.bytes';biome.write_bytes(raw);meta(biome);result['source_biomes']={'file':biome.name,'sha256':sha(biome),'columns':cols,'rows':rows,'spacing_m':spacing,'origin_x_m':cond['origin_j']*spacing,'origin_z_m':cond['origin_i']*spacing,'source_sha256':entry['sha256']}
 spawn=report['spawn'];result['recommended_spawn']={'x_m':spawn['x_m'],'y_m':spawn['height_m']+2,'z_m':spawn['z_m'],'yaw_degrees':20}
 result['default_fine_blend_m']=512.;result['producer_sha256']={'Tools/ExplorationBake/bake_exploration.py':sha(ROOT/'Tools/ExplorationBake/bake_exploration.py'),'Tools/ExplorationBake/finalize_samples.py':sha(__file__),'Tools/ExplorationBake/serve_exploration.py':sha(ROOT/'Tools/ExplorationBake/serve_exploration.py'),'Tools/ExplorationBake/physical_climate_policy.py':sha(ROOT/'Tools/ExplorationBake/physical_climate_policy.py')}
 result['native_job_spec']=b.identity_spec(job['spec']);result['native_job_spec']['statistics']=None if job['spec']['statistics'] is None else {'sha256':job['spec']['statistics']['sha256']}
 if source_map is not None:
  original=Path(source_map);destination=output/'Ceteviles_Source.json'
  if original.resolve()!=destination.resolve():shutil.copyfile(original,destination)
  meta(destination)
  result['authored_map']={'file':destination.name,'sha256':sha(destination),'original_filename':original.name,'native_model_width_m':report['world_width_m']/report['native_to_world_xz'],'native_model_depth_m':report['world_depth_m']/report['native_to_world_xz'],'scale_reference':'Minecraft scale5:5m per native30m sample; vertical model meters preserved'}
 path=output/'exploration-world.json';path.write_text(json.dumps(result,indent=2)+'\n');meta(path)
 print('Published',output,'world',view_id,'spawn',result['recommended_spawn']);return result
if __name__=='__main__':
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--input',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--source-map',type=Path);a=p.parse_args();publish(a.input,a.output,a.source_map)
