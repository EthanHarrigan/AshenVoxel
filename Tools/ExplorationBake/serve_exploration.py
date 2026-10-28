#!/usr/bin/env python3
"""Expose the original canonical provider in explicit scale5 Unity coordinates.

Native pixel indices, arrays and owner caches are unchanged. Only explicit
view identity, physical spacing and origins differ. A4/5m view is never passed
to RealBackend as if the pretrained model natively used that spacing.
"""
from pathlib import Path
import argparse,concurrent.futures,copy,hashlib,http.server,json,sys
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'Tools/DiffusionBridge'))
import bridge as b
from physical_climate_policy import POLICY,canonical_backend

def view_identity(job,world_spacing=5.,width=299520.,depth=149760.):
 return hashlib.sha256(b.canonical({'schema':'ashen-model-coordinate-view-v1','native_model_world_id':job['world_id'],'physical_climate_policy':POLICY,'world_spacing_m':float(world_spacing),'vertical_scale':1.,'world_width_m':float(width),'world_depth_m':float(depth)})).hexdigest()
class CoordinateView:
 def __init__(self,native,spacing=5.,width=299520.,depth=149760.):
  b.finite(spacing,'world spacing',.1,30);b.finite(width,'world width',1,1e9);b.finite(depth,'world depth',1,1e9);self.native=native;self.spacing=float(spacing);self.width=width;self.depth=depth;self.world_id=view_identity(native.job,spacing,width,depth)
  self.job=copy.deepcopy(native.job);self.job['world_id']=self.world_id;self.job['spec']['spacing_m']=self.spacing
 def status(self):
  d=dict(self.native.status());d.update({'world_id':self.world_id,'spacing_m':self.spacing,'native_model_spacing_m':30.,'native_model_world_id':self.native.job['world_id'],'world_width_m':self.width,'world_depth_m':self.depth,'coordinate_view':'ashen-model-coordinate-view-v1','physical_climate_policy':POLICY,'note':'Original pinned pretrained canonical output; modelpixelindices unchanged, horizontalworld spacing explicit; heights remain meters.'})
  if hasattr(self.native.backend,'physical_policy'):d['physical_climate_rectification']=self.native.backend.physical_policy.summary()
  return d
 def request(self,request):
  if request.get('world_id')!=self.world_id:raise b.IdentityMismatch('World coordinate-view identity does not match')
  original=dict(request);original['world_id']=self.native.job['world_id'];incoming=self.native.request(original);result=concurrent.futures.Future()
  def done(source):
   try:
    meta,h,c,biome,land=b.decode_region(source.result(),self.native.job['world_id']);win=meta['window'];window=(win['row0'],win['col0'],win['rows'],win['cols'])
    # encode_region checks the physical arrays/layout, and computes a freshCRC.
    result.set_result(b.encode_region(self.job,window,(h,c,biome,land),meta['backend']))
   except Exception as error:result.set_exception(error)
  incoming.add_done_callback(done);return result
 def close(self):self.native.close()
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--job',type=Path,required=True);p.add_argument('--owner-cache-dir',type=Path,required=True);p.add_argument('--port',type=int,default=8787);p.add_argument('--host',default='127.0.0.1');p.add_argument('--world-spacing-m',type=float,default=5);p.add_argument('--world-width-m',type=float,default=299520);p.add_argument('--world-depth-m',type=float,default=149760);p.add_argument('--allow-download',action='store_true');a=p.parse_args()
 job=b.read_job(a.job);backend=canonical_backend(b,job,b.RealBackend(job,a.allow_download),a.owner_cache_dir);provider=CoordinateView(b.RegionProvider(job,backend),a.world_spacing_m,a.world_width_m,a.world_depth_m)
 server=http.server.ThreadingHTTPServer((a.host,a.port),b.handler_for(provider));print('Exploration provider ready',json.dumps(provider.status()),flush=True)
 try:server.serve_forever()
 finally:server.server_close();provider.close()
if __name__=='__main__':main()
