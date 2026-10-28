#!/usr/bin/env python3
"""Compare actual running coordinate-view output against executed bundled data."""
from pathlib import Path
import argparse,hashlib,json,struct,sys,urllib.request
import numpy as np
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'Tools/DiffusionBridge'));sys.path.insert(0,str(ROOT/'Tools/WorldScaleImport'))
import bridge as b
from convert_world import read_avwf

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--url',default='http://127.0.0.1:8787');p.add_argument('--samples',type=Path,default=ROOT/'Assets/AshenVoxel/Exploration/Samples');p.add_argument('--output',type=Path,default=ROOT/'Docs/ExplorationEvidence/live-view-validation.json');a=p.parse_args()
 report=json.loads((a.samples/'exploration-world.json').read_text());fine=report['fine'];spacing=fine['spacing_m'];spawn=report['spawn'];r=int(round(spawn['z_m']/spacing));col=int(round(spawn['x_m']/spacing))
 status=json.load(urllib.request.urlopen(a.url+'/status',timeout=10))
 request={'world_id':report['world_id'],'row0':r,'col0':col,'rows':3,'cols':3};req=urllib.request.Request(a.url+'/region',data=json.dumps(request).encode(),headers={'Content-Type':'application/json'})
 raw=urllib.request.urlopen(req,timeout=300).read();metadata,height,climate,biome,land=b.decode_region(raw,report['world_id'])
 h=read_avwf(a.samples/fine['height_file']);ir=int(round((spawn['z_m']-fine['origin_z_m'])/spacing));ic=int(round((spawn['x_m']-fine['origin_x_m'])/spacing))
 archived=(a.samples/fine['climate_file']).read_bytes();n=struct.unpack_from('<H',archived,80)[0];planes=np.frombuffer(archived,dtype='<f4',count=5*fine['rows']*fine['columns'],offset=82+n).reshape(5,fine['rows'],fine['columns'])
 checks={'height_bit_exact_bundled_fine':height.tobytes()==h['heights'][ir:ir+3,ic:ic+3].astype('<f4').tobytes(),
 'climate_bit_exact_bundled_fine':climate.tobytes()==planes[:,ir:ir+3,ic:ic+3].tobytes(),
 'region_status_backend_agree':metadata['backend']==status['backend'],
 'world_spacing_and_origin_exact':metadata['spacing_m']==spacing and metadata['origin_x_m']==spawn['x_m'] and metadata['origin_z_m']==spawn['z_m'],
 'known_spawn_height_exact':float(height[0,0])==spawn['height_m'],
 'versioned_physical_policy_explicit':status.get('physical_climate_policy')==report.get('physical_climate_policy')}
 result={'status':'PASS' if all(checks.values()) else 'FAIL','scope':'Actual cached original pretrained canonical model through versioned physical coordinate view; not fresh inference or native Unity','checks':checks,'view_status':status,'region_metadata':metadata,'payload_sha256':hashlib.sha256(raw).hexdigest(),'spawn_height_m':float(height[0,0]),'spawn_temperature_c':float(climate[0,0,0]),'source_biome':int(biome[0,0]),'source_sha256':{str(Path(__file__).relative_to(ROOT)):hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),**{f'Tools/ExplorationBake/{name}':hashlib.sha256((Path(__file__).parent/name).read_bytes()).hexdigest() for name in ['serve_exploration.py','physical_climate_policy.py']}}}
 a.output.parent.mkdir(parents=True,exist_ok=True);a.output.write_text(json.dumps(result,indent=2)+'\n')
 for name,value in checks.items():print('PASS' if value else 'FAIL',name)
 if not all(checks.values()):raise SystemExit(1)
 print('All',len(checks),'actual cached coordinate-view checks passed.')
if __name__=='__main__':main()
