#!/usr/bin/env python3
"""Verify coarse-feature registration against the pinned upstream climate sampler.

Uses an analytic ramp only as a unit-test input, not as shipped terrain or model
execution evidence. No pretrained weights are loaded by this check.
"""
from pathlib import Path
import argparse,json,sys,hashlib,subprocess
ROOT=Path(__file__).resolve().parents[2]
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--upstream-source',type=Path,default=ROOT.parent/'reference-code/terrain-diffusion');a=p.parse_args();sys.path.insert(0,str(a.upstream_source));sys.path.insert(0,str(ROOT/'Tools/DiffusionBridge'));import bridge
 commit=subprocess.check_output(['git','-C',str(a.upstream_source),'rev-parse','HEAD'],text=True).strip();assert commit==bridge.UPSTREAM_COMMIT
 import torch
 from terrain_diffusion.inference.world_pipeline import WorldPipeline
 class CoarseRamp:
  def __getitem__(self,key):
   _,sy,sx=key;ys=torch.arange(sy.start,sy.stop,dtype=torch.float32);xs=torch.arange(sx.start,sx.stop,dtype=torch.float32);y,x=torch.meshgrid(ys,xs,indexing='ij');v=torch.ones((7,len(ys),len(xs)),dtype=torch.float32);v[0]=0;v[1]=0;v[2]=20+x*.1;v[3]=800;v[4]=1000;v[5]=30;return v
 pipeline=WorldPipeline.__new__(WorldPipeline);pipeline.coarse=CoarseRamp();results=[]
 for pixel in [-512,-256,0,64,128,255,256,512]:
  out=pipeline._compute_climate(0,pixel,1,pixel+1,torch.zeros((1,1)),scale=8);temperature=float(out[0,0,0]);coord=(pixel+.5)/256-.5;expected=20+coord*.1;assert abs(temperature-expected)<2e-6
  results.append({'native_pixel':pixel,'measured_temperature_c':temperature,'expected_coarse_coordinate':coord,'old_registration_error_world_m':(pixel/256-coord)*1280})
 report={'status':'PASS','checks':len(results),'scope':'Pinned upstream climate method with test ramp, no weights; registration regression not terrain generation','upstream_commit':commit,'cases':results,'source_sha256':{'Tools/ExplorationBake/check_coarse_registration.py':hashlib.sha256(Path(__file__).read_bytes()).hexdigest()}};(ROOT/'Docs/ExplorationEvidence/coarse-registration-validation.json').write_text(json.dumps(report,indent=2)+'\n');print('PASS',len(results),'native signed-coordinate registration cases; corrected half-cell offset')
if __name__=='__main__':main()
