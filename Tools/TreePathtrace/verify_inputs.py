#!/usr/bin/env python3
"""Verify immutable actual voxel input, simple surface cases, provenance, and final image hashes."""
from pathlib import Path
import argparse,hashlib,json
import numpy as np
from PIL import Image
from render_pathtrace import exposed_geometry
ROOT=Path(__file__).resolve().parents[2];HERE=Path(__file__).resolve().parent

def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--require-current-source',action='store_true');a=p.parse_args()
 inputs=HERE/'inputs';manifest=json.loads((inputs/'manifest.json').read_text())
 for name,digest in manifest['sha256'].items():assert sha(inputs/name)==digest,name+' hash mismatch'
 assert sha(inputs/'source-provenance.json')==manifest['source_provenance_sha256']
 provenance=json.loads((inputs/'source-provenance.json').read_text())
 if a.require_current_source:
  for name,digest in provenance['source_sha256'].items():assert sha(ROOT/name)==digest,name+' no longer matches frozen source'
 # Independent exact-count and channel checks for the vectorized exporter.
 v=np.zeros((3,3,3),dtype=np.uint8);c=np.zeros_like(v,dtype=np.uint32)
 v[1,1,1]=6;c[1,1,1]=0xffff0000
 points,colors,triangles,quads=exposed_geometry(v,c,.1)
 assert quads==6 and len(triangles)==12 and len(points)==24
 assert np.all(colors==np.array([1,0,0],dtype=np.float32))
 v[1,1,2]=7;c[1,1,2]=0xff0000ff
 _,colors,triangles,quads=exposed_geometry(v,c,.1)
 assert quads==10 and len(triangles)==20
 assert np.count_nonzero(colors[:,0]==1)==20 and np.count_nonzero(colors[:,2]==1)==20
 dtype=np.dtype([('x','<u2'),('y','<u2'),('z','<u2'),('id','u1'),('color','<u4')])
 for image_name,export in [('birch','birch-0'),('oak','oak-fruit-0'),('redwood','redwood-0'),('pine','pine-0')]:
  raw=(inputs/(export+'.bin')).read_bytes();dims=np.frombuffer(raw,dtype='<i4',count=3)
  scale=float(np.frombuffer(raw,dtype='<f4',offset=12,count=1)[0]);n=int(np.frombuffer(raw,dtype='<i4',offset=16,count=1)[0])
  assert abs(scale-.1)<1e-7 and len(raw)==20+n*dtype.itemsize
  r=np.frombuffer(raw,dtype=dtype,offset=20,count=n)
  assert np.all((r['id']>=1)&(r['id']<=10)) and np.all(r['color']>>24==255)
  for i,axis in enumerate(('x','y','z')):assert np.all(r[axis]<dims[i])
  report=json.loads((HERE/'reports'/('pathtraced-'+image_name+'.json')).read_text())
  assert report['export_sha256']==manifest['sha256'][export+'.bin']
  path=ROOT/report['image'];assert sha(path)==report['image_sha256']
  assert Image.open(path).size==(800,900) and report['spp']==64
  assert report['source_provenance']==provenance
 base=HERE/'reports/pathtraced-redwood-base.json'
 if base.is_file():
  report=json.loads(base.read_text())
  assert report['export_sha256']==manifest['sha256']['redwood-0.bin']
  assert report['source_provenance']==provenance
  assert report['camera']['view']=='base' and report['spp']==64
  assert report['camera']['camera_target_m'][1]<10
  path=ROOT/report['image'];assert sha(path)==report['image_sha256']
  assert Image.open(path).size==(1000,1000)
 print('PASS: four frozen actual datasets, source provenance, exact surface/channel cases, four 800x900/64spp images, and optional 1000x1000 basal-detail image')
if __name__=='__main__':main()
