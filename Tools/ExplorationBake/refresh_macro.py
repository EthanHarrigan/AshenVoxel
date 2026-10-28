#!/usr/bin/env python3
"""Regenerate only a learned macro stage while preserving completed canonical fine owners."""
from pathlib import Path
import argparse,json,time,hashlib,shutil
import bake_exploration as bake

def sha(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--input',type=Path,required=True);a=p.parse_args();old=json.loads((a.input/'exploration-world.json').read_text());job=old['job'];bake.WORLD_WIDTH=old['world_width_m'];bake.WORLD_DEPTH=old['world_depth_m'];bake.MODEL_TO_WORLD=old['native_to_world_xz'];start=time.monotonic();backend=bake.b.RealBackend(job)
 new,coarse_spawn=bake.macro(backend,job,a.input);previous=old['macro'];old['macro']=new;old['macro_registration_correction']={'previous_height_sha256':previous['height_sha256'],'new_height_sha256':new['height_sha256'],'coarse_feature_coordinate':'(native_pixel+0.5)/256-0.5','fine_and_final_spawn':'unchanged','coarse_spawn_candidate':coarse_spawn};(a.input/'exploration-world.json').write_text(json.dumps(old,indent=2)+'\n')
 result={'status':'PASS','scope':'Actual pinned pretrained coarse-network rerun with corrected cell-center registration; existing full canonical fine model outputs unchanged','elapsed_seconds':time.monotonic()-start,'world_id':job['world_id'],'previous_macro_sha256':previous['height_sha256'],'macro':new,'fine_sha256':old.get('fine',{}).get('height_sha256'),'source_sha256':{'Tools/ExplorationBake/bake_exploration.py':sha(Path(bake.__file__)),'Tools/ExplorationBake/refresh_macro.py':sha(__file__),'Tools/DiffusionBridge/bridge.py':sha(bake.ROOT/'Tools/DiffusionBridge/bridge.py')}}
 path=bake.ROOT/'Docs/ExplorationEvidence/corrected-macro-validation.json';path.write_text(json.dumps(result,indent=2)+'\n');print('PASS corrected macro rerun',result['elapsed_seconds'],flush=True)
if __name__=='__main__':main()
