"""Bake a resumable 320m learned source overview outside Unity Assets.

Runs pinned coarse/base models, reads their learned low-frequency elevation,
and filters to a fixed world lattice. The expensive fine decoder is omitted.
Climate is explicitly retained from the existing macro, with height adjustment.
This is a separately identified approximate overview, not full decoded terrain.
"""
from pathlib import Path
import argparse
import hashlib
import json
import math
import os
import struct
import time
import zlib

import numpy as np
from scipy.ndimage import map_coordinates
from coarse_overview import (ROOT, VERSION, b, atomic_json, available_commit,
                             job_for_batch, load_height_pipeline, model_query,
                             release_temporary_gpu_memory, sample_overview, sha)
from convert_world import read_avwf, write_avwf


def read_climate(path, height_path):
    raw=Path(path).read_bytes()
    if raw[:8]!=b'AVCL0001' or zlib.crc32(raw[:-4])&0xffffffff!=struct.unpack_from('<I',raw,len(raw)-4)[0]:
        raise ValueError('Invalid baseline climate checksum/magic')
    cols,rows,ox,oz,ex,ez=struct.unpack_from('<ii4d',raw,8)
    count=struct.unpack_from('<H',raw,80)[0]
    if len(raw)!=82+count+5*cols*rows*4+4 or raw[48:80].hex()!=sha(height_path):
        raise ValueError('Baseline height/climate pairing mismatch')
    data=np.frombuffer(raw,dtype='<f4',count=5*cols*rows,offset=82+count).reshape(5,rows,cols).copy()
    b.validate_climate_values(data,b.CLIMATE_PHYSICAL)
    return data


def publish(output, heights, xs, zs, source, job, identity, elapsed, chunk_seconds):
    baseline=read_avwf(source/'ExplorationMacro.bytes')
    climate=read_climate(source/'ExplorationMacro.climate.bytes',source/'ExplorationMacro.bytes')
    coords=np.meshgrid((zs-baseline['origin_z'])/baseline['extent_z']*(baseline['rows']-1),
                       (xs-baseline['origin_x'])/baseline['extent_x']*(baseline['columns']-1),indexing='ij')
    old=map_coordinates(baseline['heights'],coords,order=1,prefilter=False,mode='nearest')
    planes=np.stack([map_coordinates(v,coords,order=1,prefilter=False,mode='nearest') for v in climate])
    planes[0]+=planes[4]*(np.maximum(heights,0)-np.maximum(old,0))
    b.validate_climate_values(planes,b.CLIMATE_PHYSICAL)
    cond=job['spec']['conditioning']
    categories=np.load(cond['source_biome']['path'],allow_pickle=False)
    ri=np.floor(zs/1280).astype(int)-cond['origin_i']
    ci=np.floor(xs/1280).astype(int)-cond['origin_j']
    valid=(ri[:,None]>=0)&(ri[:,None]<categories.shape[0])&(ci[None,:]>=0)&(ci[None,:]<categories.shape[1])
    biomes=np.where(valid,categories[np.clip(ri,0,categories.shape[0]-1)[:,None],np.clip(ci,0,categories.shape[1]-1)[None,:]],0).astype('u1')
    meta={'provider':'InfiniteDiffusion','backend':'upstream-pretrained/learned-lowfreq-overview-v1',
          'world_id':identity['bake_id'],'native_model_world_id':job['world_id'],
          'generator_version':VERSION,'world_spacing_m':320.,'height_units':'meters',
          'native_model_spacing_m':30.,'world_meters_per_native_sample':5.,
          'latent_spacing_world_m':40.,'filter':'meter elevation Gaussian sigma160m, truncate3; global bilinear sample',
          'scope':'Learned low-frequency overview; fine decoder residual omitted; not exact full-pipeline elevation',
          'baseline_world_id':identity['baseline_world_id'],
          'climate_policy':'Baseline macro interpolated; temperature adjusted using its lapse channel and positive elevation delta',
          'identity_manifest_sha256':sha(output/'identity.json')}
    height_file=output/'Coarse320.bytes'
    write_avwf(height_file,heights,biomes,heights>=0,float(xs[0]),float(zs[0]),float(xs[-1]-xs[0]),float(zs[-1]-zs[0]),meta)
    encoded=b.canonical({'schema':'ashen-baked-physical-climate-v1','world_id':identity['bake_id'],
                        'height_sha256':sha(height_file),'temperature_is_surface_adjusted':True,
                        'climate_value_units':b.climate_units(b.CLIMATE_PHYSICAL),'climate_source_spacing_m':1280.})
    raw=b'AVCL0001'+struct.pack('<ii4d',len(xs),len(zs),xs[0],zs[0],xs[-1]-xs[0],zs[-1]-zs[0])
    raw+=bytes.fromhex(sha(height_file))+struct.pack('<H',len(encoded))+encoded+planes.astype('<f4').tobytes()
    raw+=struct.pack('<I',zlib.crc32(raw)&0xffffffff)
    (output/'Coarse320.climate.bytes').write_bytes(raw)
    np.savez_compressed(output/'overview.npz',height=heights,x=xs,z=zs,biomes=biomes,land=heights>=0)
    import rasterio
    from rasterio.transform import from_origin
    with rasterio.open(output/'Coarse320-height.tif','w',driver='GTiff',width=len(xs),height=len(zs),
                       count=1,dtype='float32',transform=from_origin(xs[0]-160,zs[-1]+160,320,320),
                       compress='deflate',predictor=3,tiled=True,blockxsize=256,blockysize=256) as dst:
        dst.write(np.flipud(heights),1)
        dst.update_tags(AREA_OR_POINT='Point',height_units='meters',coordinate_system='Local world X/Z meters; no geographic CRS',
                        source=VERSION,bake_id=identity['bake_id'])
    reloaded=read_avwf(height_file)
    climate_back=read_climate(output/'Coarse320.climate.bytes',height_file)
    if not np.array_equal(heights,reloaded['heights']) or not np.array_equal(planes,climate_back):
        raise RuntimeError('Published archive round-trip failed')
    files=['Coarse320.bytes','Coarse320.climate.bytes','Coarse320-height.tif','overview.npz']
    result={'status':'PASS','scope':__doc__,'bake_id':identity['bake_id'],'generator_version':VERSION,
            'columns':len(xs),'rows':len(zs),'spacing_m':320.,'world_width_m':xs[-1]-xs[0],
            'world_depth_m':zs[-1]-zs[0],'height_range_m':[float(heights.min()),float(heights.max())],
            'rmse_difference_from_1280m_macro_m':float(np.sqrt(np.mean((heights.astype('f8')-old)**2))),
            'current_process_seconds':elapsed,'sum_chunk_generation_seconds':chunk_seconds,
            'files':{name:{'sha256':sha(output/name),'bytes':(output/name).stat().st_size} for name in files},
            'validation':{'finite':bool(np.isfinite(heights).all()),'height_climate_round_trip':True},
            'limitations':['Learned intermediate elevation, not full decoder output',
                           'Climate retains1280m source fidelity','Separate GPU/runtime identity from current playable CPU world',
                           'No runtime source replacement, LOD, rendering or gameplay acceptance']}
    atomic_json(output/'result.json',result)
    print(json.dumps({'complete':True,'height_bytes':result['files']['Coarse320.bytes']['bytes'],
                      'generation_seconds':chunk_seconds}),flush=True)


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--job',type=Path,required=True)
    p.add_argument('--source',type=Path,default=ROOT/'Assets/AshenVoxel/Exploration/Samples')
    p.add_argument('--output',type=Path,required=True)
    p.add_argument('--batch',type=int,default=8)
    p.add_argument('--block-nodes',type=int,choices=(16,32,64),default=64)
    p.add_argument('--max-new-blocks',type=int,default=0)
    a=p.parse_args()
    if (ROOT/'Assets').resolve() in a.output.resolve().parents:
        raise ValueError('Research bake must stay outside Assets')
    a.output.mkdir(parents=True,exist_ok=True)
    (a.output/'blocks').mkdir(exist_ok=True)
    job=job_for_batch(a.job,a.batch)
    old=json.loads((a.source/'exploration-world.json').read_text(encoding='utf-8-sig'))
    width,depth=old['world_width_m'],old['world_depth_m']
    if width%320 or depth%320:
        raise ValueError('320m lattice must exactly span the existing extent')
    xs=np.arange(int(width/320)+1,dtype='f8')*320-width/2
    zs=np.arange(int(depth/320)+1,dtype='f8')*320-depth/2
    profile={'schema':'ashen-coarse-overview-bake-v1','generator_version':VERSION,
             'native_job_world_id':job['world_id'],'baseline_world_id':old['world_id'],
             'baseline_height_sha256':sha(a.source/'ExplorationMacro.bytes'),
             'baseline_climate_sha256':sha(a.source/'ExplorationMacro.climate.bytes'),
             'world_width_m':width,'world_depth_m':depth,'spacing_m':320.,'block_nodes':a.block_nodes,
             'source_sha256':{name:sha(Path(__file__).with_name(name)) for name in ('coarse_overview.py','bake_coarse_overview.py')}}
    profile['bake_id']=hashlib.sha256(b.canonical(profile)).hexdigest()
    identity_path=a.output/'identity.json'
    if identity_path.exists() and json.loads(identity_path.read_text())!=profile:
        raise ValueError('Existing bake belongs to different source/settings; choose a new output directory')
    atomic_json(identity_path,profile)
    atomic_json(a.output/'job.json',job)
    heights=np.full((len(zs),len(xs)),np.nan,dtype='f4')
    plan=[(r,c) for r in range(0,len(zs),a.block_nodes) for c in range(0,len(xs),a.block_nodes)]
    pipeline=None;created=completed=0;chunk_seconds=0.;started=time.perf_counter()
    status={'status':'RUNNING','total_blocks':len(plan),'completed_blocks':0,'bake_id':profile['bake_id']}
    try:
        for r,c in plan:
            block=a.output/'blocks'/f'{r}_{c}.npz'
            record=block.with_suffix('.json')
            bx,bz=xs[c:c+a.block_nodes],zs[r:r+a.block_nodes]
            if block.exists() and record.exists():
                saved=json.loads(record.read_text())
                if saved['bake_id']!=profile['bake_id'] or saved['sha256']!=sha(block):
                    raise ValueError('Corrupt or mismatched completed block '+str(block))
                with np.load(block,allow_pickle=False) as data:
                    h=data['height']
            else:
                if a.max_new_blocks and created>=a.max_new_blocks:
                    status['status']='PARTIAL';break
                commit=available_commit()
                required=(4 if pipeline is None else 2)*1024**3
                if commit is not None and commit<required:
                    status['status']='MEMORY_WAIT';status['available_commit_bytes']=commit;break
                if pipeline is None:
                    t=time.perf_counter();pipeline,_=load_height_pipeline(job)
                    status['load_seconds']=time.perf_counter()-t
                    print(json.dumps({'loaded_seconds':status['load_seconds']}),flush=True)
                t=time.perf_counter()
                h=sample_overview(model_query(pipeline),bx,bz)
                generation_seconds=time.perf_counter()-t
                temp=block.with_suffix('.tmp.npz')
                np.savez_compressed(temp,height=h)
                os.replace(temp,block)
                saved={'bake_id':profile['bake_id'],'row0':r,'col0':c,'shape':list(h.shape),
                       'sha256':sha(block),'generation_seconds':generation_seconds,
                       'memory_release':release_temporary_gpu_memory()}
                atomic_json(record,saved);created+=1
            if h.shape!=(len(bz),len(bx)) or not np.isfinite(h).all():
                raise ValueError('Invalid block shape/data')
            heights[r:r+len(bz),c:c+len(bx)]=h
            completed+=1;chunk_seconds+=saved['generation_seconds']
            status.update(completed_blocks=completed,new_blocks=created,elapsed_seconds=time.perf_counter()-started,
                          sum_chunk_generation_seconds=chunk_seconds,available_commit_bytes=available_commit(),
                          last_block=[r,c],last_block_seconds=saved['generation_seconds'])
            atomic_json(a.output/'progress.json',status)
            print(json.dumps(status),flush=True)
        if completed==len(plan):
            if pipeline is not None: pipeline.close();pipeline=None
            publish(a.output,heights,xs,zs,a.source,job,profile,time.perf_counter()-started,chunk_seconds)
            status['status']='PASS'
    except Exception as error:
        status['status']='FAILED';status['error']=str(error);raise
    finally:
        if pipeline is not None:pipeline.close()
        status['elapsed_seconds']=time.perf_counter()-started
        atomic_json(a.output/'progress.json',status)
    if status['status']!='PASS':
        print(json.dumps(status),flush=True)


if __name__=='__main__':main()
