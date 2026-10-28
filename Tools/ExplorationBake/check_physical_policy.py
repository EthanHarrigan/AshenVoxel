#!/usr/bin/env python3
"""Negative-climate regression using a labeled fake model and real cache/provider."""
from pathlib import Path
import argparse,hashlib,json,sys,tempfile
import numpy as np
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(Path(__file__).parent))
from serve_exploration import b,CoordinateView,view_identity
from physical_climate_policy import POLICY,PhysicalClimateBackend,canonical_backend,rectify
checks=0
def check(value,label):
 global checks
 if not value:raise AssertionError(label)
 checks+=1;print('PASS',label)
class NegativeFake:
 kind='FAKE-NEGATIVE-CLIMATE-REGRESSION'
 def __init__(self,negative=True):self.calls=0;self.negative=negative;self.last=None
 def query(self,window):
  self.calls+=1;r,c,h,w=window
  height=(np.arange(r,r+h)[:,None]*.5+np.arange(c,c+w)[None,:]*.125).astype('f4')
  climate=np.empty((5,h,w),dtype='f4')
  for i,v in enumerate([-12.5,-.25,-220,-3,.008] if self.negative else [-12.5,7,1300,35,.008]):climate[i]=v
  climate[1,0,0]=3.25;climate[2,0,0]=870.;climate[3,0,0]=15.
  self.last=height,climate,np.full((h,w),2,dtype='u1'),(height>=0).astype('u1')
  return self.last

def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--job',type=Path,help='Prepared local native job; needed outside the authoring environment');args=parser.parse_args()
 job=b.read_job(args.job) if args.job is not None else b.make_job(ROOT.parent/'reference-code/terrain-diffusion',stats_file=ROOT.parent/'infinite-diffusion-smoke/pipeline_data.json',model_dir=ROOT.parent/'infinite-diffusion-smoke/model',torch_threads=2)
 source=NegativeFake();arrays=source.query((5,7,3,4));original=arrays[1].copy();normalized,counts=rectify(original)
 check(np.array_equal(original,arrays[1]) and np.any(original[2]<0),'policy never mutates raw model source planes')
 check(np.array_equal(normalized[0],original[0]) and np.array_equal(normalized[4],original[4]),'signed temperature and positive lapse remain bit exact')
 check(all(np.all(normalized[i]>=0) for i in (1,2,3)) and all(counts[n]==11 for n in counts),'only negative declared nonnegative channels become zero with exact counts')
 check(all(normalized[i,0,0]==original[i,0,0] for i in (1,2,3)),'positive physical values remain bit exact')
 rejected=False
 try:b.encode_region(job,(5,7,3,4),arrays,source.kind)
 except ValueError:rejected=True
 check(rejected,'regression input actually fails the old strict native encoder')
 with tempfile.TemporaryDirectory() as tmp:
  source=NegativeFake();canonical=canonical_backend(b,job,source,Path(tmp));native=b.RegionProvider(job,canonical);view=CoordinateView(native)
  try:
   request={'world_id':view.world_id,'row0':5,'col0':7,'rows':3,'cols':4};raw=view.request(request).result(timeout=5);meta,h,c,biome,land=b.decode_region(raw,view.world_id)
   check(view.status()['ready'] and view.status()['failure'] is None,'negative climate no longer latches canonical/HTTP provider failure')
   check(np.all(c[1:4]==0) and np.all(c[0]==-12.5) and np.all(c[4]==np.float32(.008)),'canonical response rectifies only the three declared physical planes')
   expected=(np.arange(5,8)[:,None]*.5+np.arange(7,11)[None,:]*.125).astype('f4')
   check(np.array_equal(h,expected) and np.all(biome==2) and np.array_equal(land,(h>=0).astype('u1')),'heights, source biomes and land flags survive real owner crop/cache encoding')
   check(meta['backend']==view.status()['backend'] and '/physical-nonnegative-v1/' in meta['backend'],'policy-specific backend identity agrees across strict status and region')
   evidence=canonical.physical_policy.summary();check(all(v>0 for v in evidence['rectified_values'].values()) and evidence['last_query']['raw_physical_climate_sha256']!=evidence['last_query']['published_physical_climate_sha256'],'bounded provenance records raw/published climate hashes and actual correction counts')
   check(canonical.directory.parent.name==POLICY['cache_namespace'] and canonical.directory.name==job['world_id'],'rectified owner cache has a separate versioned namespace retaining native source ID')
   before=source.calls;canonical.query((5,7,3,4));check(source.calls==before,'policy owner reload validates and reuses cache without model execution')
   old=hashlib.sha256(b.canonical({'schema':'ashen-model-coordinate-view-v1','native_model_world_id':job['world_id'],'world_spacing_m':5.,'vertical_scale':1.,'world_width_m':299520.,'world_depth_m':149760.})).hexdigest()
   check(view.world_id!=old and view.status()['physical_climate_policy']==POLICY,'view identity and status explicitly include the changed physical publication policy')
  finally:view.close()
 with tempfile.TemporaryDirectory() as tmp:
  source=NegativeFake(False);legacy=b.CanonicalBackend(job,source,Path(tmp));old=legacy.query((5,7,3,4));before=source.calls
  migrated=canonical_backend(b,job,source,Path(tmp));new=migrated.query((5,7,3,4))
  check(migrated.migrated_legacy_owners==1 and source.calls==before and all(np.array_equal(a,z) for a,z in zip(old,new)),'valid historical cache migrates with bit-exact arrays and no inference')
  check(len(list((Path(tmp)/job['world_id']).glob('*.atdr')))==1,'migration leaves historical source cache untouched')
 invalid=normalized.copy();invalid[0,0,0]=np.nan;rejected=False
 try:rectify(invalid)
 except ValueError:rejected=True
 check(rejected,'nonfinite model outputs are rejected instead of invented physical values')
 invalid=normalized.copy();invalid[2,0,0]=100001;rejected=False
 try:b.encode_region(job,(5,7,3,4),(arrays[0],rectify(invalid)[0],arrays[2],arrays[3]),source.kind)
 except ValueError:rejected=True
 check(rejected,'upper contract violations still fail explicitly instead of silently saturating')
 rejected=False
 try:PhysicalClimateBackend(source,b.CLIMATE_NATIVE)
 except ValueError:rejected=True
 check(rejected,'native centi-C encoding cannot masquerade as a physical policy source')
 result={'status':'PASS','checks':checks,'scope':'Labeled fake negative model input through real canonical cache/provider/view; not pretrained inference or Unity','physical_climate_policy':POLICY,'source_sha256':{str(Path(__file__).relative_to(ROOT)):hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),**{f'Tools/ExplorationBake/{name}':hashlib.sha256((Path(__file__).parent/name).read_bytes()).hexdigest() for name in ['physical_climate_policy.py','serve_exploration.py']}}}
 (ROOT/'Docs/exploration-physical-policy-validation.json').write_text(json.dumps(result,indent=2)+'\n');print('All',checks,'physical policy checks passed.')
if __name__=='__main__':main()
