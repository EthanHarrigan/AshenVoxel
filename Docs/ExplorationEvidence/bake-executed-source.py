#!/usr/bin/env python3
"""Bake pinned InfiniteDiffusion coarse neural world plus canonical decoded spawn terrain.

World X/Z follow Minecraft scale5: a30m model pixel occupies5m. Vertical heights
stay real meters. Coarse output is the ORIGINAL pretrained coarse network, not
an authored/noise field and not claimed to contain decoded fine residuals.
"""
from pathlib import Path
import argparse,hashlib,json,math,os,struct,sys,time,zlib
import numpy as np
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'Tools/DiffusionBridge'));sys.path.insert(0,str(ROOT/'Tools/WorldScaleImport'))
import bridge as b
from convert_world import write_avwf
WORLD_WIDTH=299520.;WORLD_DEPTH=149760.;MODEL_TO_WORLD=1/6

def sha(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def pair(path,h,c,biomes,origin,spacing,job,stage,extra=None):
 rows,cols=h.shape;meta={'provider':'InfiniteDiffusion','backend':'upstream-pretrained/'+stage,'world_id':job['world_id'],
  'upstream_commit':b.UPSTREAM_COMMIT,'model_revision':b.MODEL_REVISION,'seed':job['spec']['seed'],
  'resolution_stage':stage,'world_meters_per_native_sample':5.,'world_spacing_m':spacing,
  'native_to_world_xz':MODEL_TO_WORLD,'vertical_height_scale':1.,'height_units':'meters'}
 if extra:meta.update(extra)
 path=Path(path);path.parent.mkdir(parents=True,exist_ok=True)
 write_avwf(path,h,biomes,(h>=0).astype('u1'),origin[0],origin[1],(cols-1)*spacing,(rows-1)*spacing,meta)
 identity=b.canonical({'schema':'ashen-baked-physical-climate-v1','world_id':job['world_id'],'resolution_stage':stage,
  'height_sha256':sha(path),'temperature_is_surface_adjusted':True,'climate_value_units':b.climate_units(b.CLIMATE_PHYSICAL)})
 raw=b'AVCL0001'+struct.pack('<ii4d',cols,rows,origin[0],origin[1],(cols-1)*spacing,(rows-1)*spacing)
 raw+=bytes.fromhex(sha(path))+struct.pack('<H',len(identity))+identity+np.asarray(c,dtype='<f4').tobytes()
 raw+=struct.pack('<I',zlib.crc32(raw)&0xffffffff);climate=path.with_name(path.stem+'.climate.bytes');climate.write_bytes(raw)
 return {'height_file':path.name,'climate_file':climate.name,'height_sha256':sha(path),'climate_sha256':sha(climate),
  'columns':cols,'rows':rows,'origin_x_m':origin[0],'origin_z_m':origin[1],'extent_x_m':(cols-1)*spacing,'extent_z_m':(rows-1)*spacing,
  'spacing_m':spacing,'height_range_m':[float(h.min()),float(h.max())],'climate_ranges':[[float(v.min()),float(v.max())] for v in c], 'source_identity':meta}

def macro(backend,job,out):
 import torch
 from scipy.ndimage import map_coordinates
 from terrain_diffusion.inference.postprocessing import local_baseline_temperature_torch
 # Exact map box is234×117 native coarse cells. Its centered Z edge falls at a
 # half cell; resampling below preserves that exact box rather than rounding it.
 x=np.linspace(-WORLD_WIDTH/2,WORLD_WIDTH/2,235)/MODEL_TO_WORLD/7680
 z=np.linspace(-WORLD_DEPTH/2,WORLD_DEPTH/2,118)/MODEL_TO_WORLD/7680
 r0,c0=math.floor(z[0])-1,math.floor(x[0])-1;r1,c1=math.ceil(z[-1])+2,math.ceil(x[-1])+2
 pad=7;t=time.monotonic()
 with torch.no_grad():
  values=backend.pipeline.coarse[:,r0-pad:r1+pad,c0-pad:c1+pad]
  values=values[:-1]/values[-1:]
  height=torch.sign(values[0])*values[0].square()
  baseline,beta=local_baseline_temperature_torch(values[2],torch.maximum(height,torch.zeros_like(height)),win=15,fallback_threshold=.02)
  features=torch.cat([height[pad:-pad,pad:-pad][None],baseline,beta,values[3:,pad:-pad,pad:-pad]],dim=0).numpy()
 coords=np.meshgrid(z-r0,x-c0,indexing='ij');sample=np.stack([map_coordinates(v,coords,order=1,mode='nearest',prefilter=False) for v in features])
 h=sample[0].astype('f4');climate=np.stack([sample[1]+sample[2]*np.maximum(h,0),sample[3]*.01,sample[4],sample[5],sample[2]]).astype('f4')
 corrections={}
 for i,(lo,hi) in enumerate([(-150,100),(0,150),(0,100000),(0,10000),(-.1,.1)]):
  count=int(((climate[i]<lo)|(climate[i]>hi)).sum());corrections[str(i)]=count
  if count:climate[i]=np.clip(climate[i],lo,hi)
 b.validate_climate_values(climate,b.CLIMATE_PHYSICAL)
 cond=job['spec']['conditioning'];biomes=backend.biomes
 ri=np.floor(z).astype(int)-cond['origin_i'];ci=np.floor(x).astype(int)-cond['origin_j']
 masks=(ri[:,None]>=0)&(ri[:,None]<biomes.shape[0])&(ci[None,:]>=0)&(ci[None,:]<biomes.shape[1])
 authored=biomes[np.clip(ri,0,biomes.shape[0]-1)[:,None],np.clip(ci,0,biomes.shape[1]-1)[None,:]];source=np.where(masks,authored,0).astype('u1')
 result=pair(out/'ExplorationMacro.bytes',h,climate,source,(-WORLD_WIDTH/2,-WORLD_DEPTH/2),1280.,job,'coarse-network',{'climate_rectification_counts':corrections})
 result['elapsed_seconds']=time.monotonic()-t
 np.savez_compressed(out/'macro-data.npz',height=h,climate=climate,biomes=source)
 # Select gentle warm forest, with good margin to both map and fine-region edges.
 dz,dx=np.gradient(h.astype('f8'),1280,1280);slope=np.degrees(np.arctan(np.hypot(dx,dz)))
 candidates=(np.isin(source,[6,8,9,4,5]))&(h>30)&(h<1800)&(climate[0]>5)&(climate[2]>400)&(slope<18)
 candidates[:6]=False;candidates[-6:]=False;candidates[:,:6]=False;candidates[:,-6:]=False
 zz,xx=np.nonzero(candidates)
 if not len(zz):raise RuntimeError('No warm gentle land candidate in executed coarse neural field')
 scores=slope[zz,xx]*10+np.abs(h[zz,xx]-300)*.01+np.hypot(xx-h.shape[1]*.5,zz-h.shape[0]*.5)*.02
 at=int(np.argmin(scores));r,c=int(zz[at]),int(xx[at]);spawn={'x_m':float(x[c]*7680*MODEL_TO_WORLD),'z_m':float(z[r]*7680*MODEL_TO_WORLD),'height_m':float(h[r,c]),'source_biome':int(source[r,c]),'temperature_c':float(climate[0,r,c]),'coarse_slope_degrees':float(slope[r,c])}
 return result,spawn

def fine(backend,job,out,spawn,side):
 import torch
 canonical=b.CanonicalBackend(job,backend,out/'owners')
 native_c=int(round(spawn['x_m']/5));native_r=int(round(spawn['z_m']/5));c0=(native_c//256-side//512)*256;r0=(native_r//256-side//512)*256
 h=np.empty((side,side),dtype='f4');climate=np.empty((5,side,side),dtype='f4');biomes=np.empty((side,side),dtype='u1');times=[]
 for iz in range(side//256):
  for ix in range(side//256):
   start=time.monotonic();tile=canonical.query((r0+iz*256,c0+ix*256,256,256));dest=(slice(iz*256,(iz+1)*256),slice(ix*256,(ix+1)*256));h[dest]=tile[0];climate[(slice(None),)+dest]=tile[1];biomes[dest]=tile[2]
   elapsed=time.monotonic()-start;times.append(elapsed);print('FINE OWNER',iz,ix,'seconds',round(elapsed,2),flush=True)
 result=pair(out/'ExplorationFine.bytes',h,climate,biomes,(c0*5.,r0*5.),5.,job,'canonical-owner-v1',{'native_window':{'row0':r0,'col0':c0,'rows':side,'cols':side}})
 np.savez_compressed(out/'fine-data.npz',height=h,climate=climate,biomes=biomes)
 # Fine-surface spawn: avoid water, high slopes, frozen surfaces and the fade band.
 dz,dx=np.gradient(h.astype('f8'),5,5);slope=np.degrees(np.arctan(np.hypot(dx,dz)))
 margin=min(160,side//4);ok=np.isin(biomes,[6,8,9,4,5])&(h>5)&(h<2200)&(climate[0]>4)&(slope<22)
 ok[:margin]=False;ok[-margin:]=False;ok[:,:margin]=False;ok[:,-margin:]=False
 rows,cols=np.nonzero(ok)
 if not len(rows):raise RuntimeError('Decoded terrain has no suitable forest/grassland spawn; choose another coarse candidate')
 score=slope[rows,cols]*2+np.hypot(cols-side*.5,rows-side*.5)*.05+np.abs(h[rows,cols]-300)*.002
 index=int(np.argmin(score));r,c=int(rows[index]),int(cols[index]);spawn={'x_m':float((c0+c)*5),'z_m':float((r0+r)*5),'height_m':float(h[r,c]),'source_biome':int(biomes[r,c]),'temperature_c':float(climate[0,r,c]),'fine_slope_degrees':float(slope[r,c])}
 result['owner_timings_seconds']=times;return result,spawn

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--job',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--fine-side',type=int,choices=(512,1024),default=1024);p.add_argument('--macro-only',action='store_true');args=p.parse_args()
 job=json.loads(args.job.read_text());out=args.output;out.mkdir(parents=True,exist_ok=True);started=time.monotonic();backend=b.RealBackend(job)
 macro_report,spawn=macro(backend,job,out);report={'schema':'ashen-exploration-world-v1','world_id':job['world_id'],'world_width_m':WORLD_WIDTH,'world_depth_m':WORLD_DEPTH,'world_meters_per_native_sample':5.,'model_native_spacing_m':30.,'native_to_world_xz':MODEL_TO_WORLD,'vertical_height_scale':1.,'macro':macro_report,'spawn':spawn,'job':job,'scope':'Actual pinned pretrained coarse network for whole map; original full latent+decoder canonical output in local fine region. No analytic/noise fallback.'}
 (out/'exploration-world.json').write_text(json.dumps(report,indent=2)+'\n');print('MACRO DONE',json.dumps(spawn),flush=True)
 if not args.macro_only:
  fine_report,spawn=fine(backend,job,out,spawn,args.fine_side);report['fine']=fine_report;report['spawn']=spawn
 report['elapsed_seconds']=time.monotonic()-started;report['source_sha256']={str(Path(__file__).relative_to(ROOT)):sha(__file__),'Tools/DiffusionBridge/bridge.py':sha(ROOT/'Tools/DiffusionBridge/bridge.py')};report['status']='PASS';(out/'exploration-world.json').write_text(json.dumps(report,indent=2)+'\n');print('DONE',report['elapsed_seconds'],flush=True)
if __name__=='__main__':main()
