#!/usr/bin/env python3
"""Coordinate-view unit checks with an explicitly fake backend, not model evidence."""
from pathlib import Path
import argparse,json,sys,time,hashlib
import numpy as np
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(Path(__file__).parent))
from serve_exploration import b,CoordinateView,view_identity
checks=0
def check(value,label):
 global checks
 if not value:raise AssertionError(label)
 checks+=1;print('PASS',label)
class Fake:
 kind='FAKE-COORDINATE-VIEW-CONTRACT'
 def __init__(self):self.calls=0
 def query(self,window):
  self.calls+=1;r,c,h,w=window
  height=(np.arange(h)[:,None]*2+np.arange(w)[None,:]+123).astype('f4');climate=np.empty((5,h,w),dtype='f4')
  for channel,value in enumerate([12,7,1300,35,-.0065]):climate[channel]=value
  return height,climate,np.full((h,w),9,dtype='u1'),np.ones((h,w),dtype='u1')
def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--job',type=Path,help='Prepared local native job; needed outside the authoring environment');args=parser.parse_args()
 # A local job is only used to exercise the schema/identity wrapper; no weights
 # are loaded by Fake. The actual bake report is separate evidence.
 job=b.read_job(args.job) if args.job is not None else b.make_job(ROOT.parent/'reference-code/terrain-diffusion',stats_file=ROOT.parent/'infinite-diffusion-smoke/pipeline_data.json',model_dir=ROOT.parent/'infinite-diffusion-smoke/model',torch_threads=2)
 fake=Fake();native=b.RegionProvider(job,fake);view=CoordinateView(native)
 try:
  request={'world_id':view.world_id,'row0':-5,'col0':7,'rows':3,'cols':4};raw=view.request(request).result(timeout=5);meta,h,c,biomes,land=b.decode_region(raw,view.world_id)
  check(meta['spacing_m']==5 and meta['origin_x_m']==35 and meta['origin_z_m']==-25,'world coordinates use5m spacing and signed native pixel indices')
  check(np.array_equal(h,fake.query((-5,7,3,4))[0]) and np.all(c[1]==7),'height meters and physical climate values are not scaled with horizontal coordinates')
  check(np.all(biomes==9) and np.all(land==1),'categorical source biomes and land flags survive the view')
  check(view.world_id!=job['world_id'] and meta['world_id']==view.world_id,'coordinate transform has a separate immutable view identity')
  check(view.status()['native_model_world_id']==job['world_id'] and view.status()['native_model_spacing_m']==30,'native model identity and30m spacing remain observable')
  check(native.job['spec']['spacing_m']==30 and b.validate_job(native.job),'native model job is not falsely rewritten to5m')
  first_calls=fake.calls;again=view.request(request).result(timeout=5);check(raw==again and fake.calls==first_calls,'repeat view requests reuse the bounded native response cache')
  check(view_identity(job,4)!=view.world_id and view_identity(job,5,width=200000)!=view.world_id,'scale and map extent changes cannot reuse a different view identity')
  rejected=False
  try:view.request(dict(request,world_id=job['world_id']))
  except b.IdentityMismatch:rejected=True
  check(rejected,'native-world identity cannot masquerade as a scale5 view request')
  rejected=False
  try:CoordinateView(native,0)
  except ValueError:rejected=True
  check(rejected,'zero coordinate spacing is rejected')
  rejected=False
  try:CoordinateView(native,width=-1)
  except ValueError:rejected=True
  check(rejected,'invalid map-view dimensions are rejected before service publication')
  corrupted=bytearray(raw);corrupted[-5]^=1;rejected=False
  try:b.decode_region(corrupted,view.world_id)
  except ValueError:rejected=True
  check(rejected,'transformed payload CRC detects corruption')
  check(meta['backend']==view.status()['backend'],'region backend identity exactly matches advertised status for the strict C# consumer')
  check(meta['backend'].startswith('FAKE-'),'fake contract evidence is labelled distinctly from pretrained generation')
 finally:view.close()
 result={'status':'PASS','checks':checks,'scope':'Fake-backend coordinate view/identity/cache/units checks; not actual model inference or Unity','source_sha256':{str(Path(__file__).relative_to(ROOT)):hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),'Tools/ExplorationBake/serve_exploration.py':hashlib.sha256((Path(__file__).parent/'serve_exploration.py').read_bytes()).hexdigest()}}
 (ROOT/'Docs/exploration-provider-validation.json').write_text(json.dumps(result,indent=2)+'\n');print('All',checks,'coordinate-view checks passed.')
if __name__=='__main__':main()
