#!/usr/bin/env python3
"""Checks the actual upstream five-channel conversion without loading model weights."""
import argparse,copy,json,tempfile
from pathlib import Path
import numpy as np
import bridge as b
import prepare_azgaar as p

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--upstream-source',type=Path,required=True);args=parser.parse_args()
    source=json.loads((b.ROOT/'Tools/WorldScaleImport/fixtures/azgaar-full.synthetic.json').read_text())
    source['mapCoordinates']={'lonW':-1,'lonE':1,'latN':1,'latS':-1}
    source['grid']=copy.deepcopy(source['pack'])
    for i,cell in enumerate(source['grid']['cells']):cell.update(temp=18-i*10,prec=20+i*10)
    checks=0
    def check(condition,label):
        nonlocal checks
        if not condition:raise AssertionError(label)
        checks+=1;print('PASS '+label)
    def rejects(action,label):
        try:action()
        except ValueError:check(True,label);return
        raise AssertionError('Expected rejection: '+label)
    with tempfile.TemporaryDirectory() as temp:
        temp=Path(temp);path=temp/'full.json';path.write_text(json.dumps(source))
        result=p.prepare(path,temp/'aligned',args.upstream_source,width_km=61.44)
        check(result['origin_i']==-2 and result['origin_j']==-4,'aligned authored map uses centered signed global coarse coordinates')
        height=np.load(temp/'aligned/elevation_m.npy');biomes=np.load(temp/'aligned/source_biome.npy')
        check(height.shape==(4,8),'native lattice preserves authored2:1 map aspect')
        check(height[0,7]==4 and height[3,7]==6724 and height[3,0]==1764,'known land heights use actual upstream Azgaar conversion')
        check(abs(height[0,0]+4000*(.5**1.5))<.001,'ocean depth follows explicitly documented upstream power curve')
        check(np.array_equal(np.unique(biomes),[0,6,9,11]),'source biome IDs survive conditioning preparation')
        check(np.load(temp/'aligned/temperature_c.npy')[0,7]==8,'source temperature remains Celsius')
        check(np.load(temp/'aligned/precipitation_mm.npy')[3,7]==5000,'precipitation scale is explicit upstream factor100')
        check(result['provenance']['recommended_fork_snr']==[.35,.2,.2,.2,.2],'fork climate noise ratio applies to all four climate channels')
        scaled=p.prepare(path,temp/'scaled',args.upstream_source,width_km=61.44,elevation_scale=2)
        scaled_height=np.load(temp/'scaled/elevation_m.npy')
        check(scaled_height[3,7]==13448 and scaled_height[0,0]==height[0,0],'vertical land scale changes mountains while preserving ocean meters')
        check(scaled['provenance']['physical_width_m']==result['provenance']['physical_width_m'],'vertical height control does not alter horizontal landmass size')
        peaked=p.prepare(path,temp/'peaked',args.upstream_source,width_km=61.44,peak_meters=8000)
        check(float(np.load(temp/'peaked/elevation_m.npy').max())==8000 and peaked['provenance']['elevation_profile']['mode']=='coarse_land_peak','explicit target peak adjusts sampled conditioning and is recorded')
        rejects(lambda:p.prepare(path,temp/'both',args.upstream_source,elevation_scale=2,peak_meters=8000),'conflicting vertical mapping modes are rejected')
        unaligned=p.prepare(path,temp/'unaligned',args.upstream_source,width_km=60)
        check(unaligned['provenance']['physical_width_m']==60000 and unaligned['provenance']['coverage_width_m']==61440,'rounding coverage does not stretch requested landmass width')
        shifted=p.prepare(path,temp/'shifted',args.upstream_source,width_km=60,origin_x=1000,origin_z=-2000)
        check(shifted['origin_j']==0 and shifted['origin_i']==-1,'arbitrary physical map origin retains global native lattice alignment')
        check(np.load(temp/'shifted/elevation_m.npy')[0,0]==-1000,'conditioning centers outside authored map receive explicit water context')
        fixture=copy.deepcopy(source);fixture['grid']['cells'][0]['h']=float('nan');path.write_text(json.dumps(fixture))
        rejects(lambda:p.prepare(path,temp/'bad',args.upstream_source),'nonfinite source heights are rejected')
        fixture=copy.deepcopy(source);fixture['pack']['cells'][1]['biome']=256;path.write_text(json.dumps(fixture))
        rejects(lambda:p.prepare(path,temp/'bad',args.upstream_source),'out-of-range source biome IDs are rejected before uint8 rasterization')
        path.write_text(json.dumps({k:v for k,v in source.items() if k!='grid'}))
        rejects(lambda:p.prepare(path,temp/'bad',args.upstream_source),'neural conditioning rejects PackCells-only input without original climate grid')
        path.write_text(json.dumps(source))
        first=b.load_conditioning_manifest(temp/'aligned/conditioning.json')
        check(first['provenance']['source_sha256']==b.digest(path),'source map fingerprint survives prepared neural job metadata')
    print(f'PASS {checks} actual upstream Azgaar conditioning checks; no model weights loaded.')

if __name__=='__main__':main()
