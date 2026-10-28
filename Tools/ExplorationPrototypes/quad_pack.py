#!/usr/bin/env python3
"""Lossless AVPM raw→4 encoding of exact greedy quads; verifies every expanded attribute/index."""
from pathlib import Path
import struct,gzip,io,zlib,hashlib,json
import numpy as np
ROOT=Path(__file__).resolve().parents[2];BUILD=ROOT/'Tools/ExplorationPrototypes/bin'
def main():
 reports=[]
 for p in sorted(BUILD.glob('*.avpr')):
  info=json.loads(p.with_suffix('.json').read_text())
  src=p.read_bytes();packed,rawlen,crc=struct.unpack_from('<III',src);raw=gzip.decompress(src[12:]);assert len(raw)==rawlen and zlib.crc32(raw)==crc
  if info.get('record_encoding')==4:
   reports.append({'id':info['id'],'reused_material_record':True,'crc_verified':True,'mesh_lods':info['roundtrip_meshes']});continue
  s=io.BytesIO(raw);new=io.BytesIO();id,kind,ln=struct.unpack('<IBH',s.read(7));new.write(struct.pack('<IBH',id,kind,ln));new.write(s.read(ln+24));lods=s.read(1);new.write(lods)
  hashes=[]
  def var(v):
   v=int(v)
   while v>=128:new.write(bytes([(v&127)|128]));v>>=7
   new.write(bytes([v]))
  for lod in range(lods[0]):
   vc,ic=struct.unpack('<II',s.read(8));assert vc%4==0 and ic==vc//4*6
   packed_v=s.read(vc*11);indices_b=s.read(ic*4);vertices=np.frombuffer(packed_v,dtype=[('p','<i2',(3,)),('n','u1'),('c','<u4')]);indices=np.frombuffer(indices_b,dtype='<i4').reshape(-1,6)
   quads=vc//4;positions=vertices['p'].reshape(quads,4,3);normals=vertices['n'].reshape(quads,4);colors=vertices['c'].reshape(quads,4)
   assert (normals==normals[:,0,None]).all() and ((colors&0xffffff)==(colors[:,0,None]&0xffffff)).all()
   palette=np.unique(colors[:,0]&0xffffff);color_indices=np.searchsorted(palette,colors[:,0]&0xffffff);new.write(struct.pack('<II',quads,len(palette)))
   for c in palette:new.write(int(c).to_bytes(3,'little'))
   patterns=np.array([[0,1,2,0,2,3],[0,2,1,0,3,2],[0,1,3,1,2,3],[0,3,1,1,3,2]],dtype=np.int32)
   local_indices=indices-np.arange(quads,dtype=np.int32)[:,None]*4;pattern_ids=np.full(quads,-1,dtype=np.int32)
   for i,pattern in enumerate(patterns):pattern_ids[(local_indices==pattern).all(axis=1)]=i
   assert (pattern_ids>=0).all();alpha=(colors>>24);ao_indices=alpha&3;materials=alpha>>2;assert (materials==materials[:,0,None]).all() and (materials>0).all() and (materials<25).all()
   previous=np.zeros(3,dtype=np.int32)
   # Reconstruct the uncompressed v1 mesh bytes while encoding. Hash equality
   # proves positions, normals, RGB, AO and exact triangulation are unchanged.
   decoded_v=np.zeros(vc,dtype=vertices.dtype);decoded_i=np.empty_like(indices)
   for q in range(quads):
    origin=positions[q,0].astype(np.int32);normal=int(normals[q,0]);axis=normal//2;u=(axis+1)%3;v=(axis+2)%3;width=int(positions[q,1,u])-int(origin[u]);height=int(positions[q,3,v])-int(origin[v]);assert width>0 and height>0
    for a in range(3):d=int(origin[a])-int(previous[a]);var((d<<1)^(d>>31))
    previous=origin;var(width);var(height);new.write(bytes([normal|(int(pattern_ids[q])<<3)]));var(color_indices[q]);ao=0
    vv=np.tile(origin,(4,1));vv[1,u]+=width;vv[2,u]+=width;vv[2,v]+=height;vv[3,v]+=height;assert np.array_equal(vv,positions[q])
    for corner in range(4):
     ao|=int(ao_indices[q,corner])<<(corner*2);i=q*4+corner;decoded_v['p'][i]=vv[corner];decoded_v['n'][i]=normal;decoded_v['c'][i]=int(palette[color_indices[q]])|(int((materials[q,corner]<<2)|ao_indices[q,corner])<<24)
    new.write(bytes([ao,int(materials[q,0])]));decoded_i[q]=patterns[pattern_ids[q]]+q*4
   assert decoded_v.tobytes()==packed_v and decoded_i.tobytes()==indices_b
   hashes.append({'lod':lod,'vertices':vc,'indices':ic,'exact_expanded_sha256':hashlib.sha256(packed_v+indices_b).hexdigest()})
  collision=s.read();new.write(collision);payload=new.getvalue();compressed=gzip.compress(payload,compresslevel=9,mtime=0);p.write_bytes(struct.pack('<III',len(compressed),len(payload),zlib.crc32(payload))+compressed)
  info.update({'record_encoding':4,'encoding':'Exact greedy quads, delta decimeter coordinates, original normal/triangulation, RGB palette and lossless2bit corner AO + exact material ID','expanded_v1_bytes':len(raw),'expanded_bytes':len(payload),'compressed_bytes':len(compressed),'roundtrip_meshes':hashes,'collision_sha256':hashlib.sha256(collision).hexdigest()});p.with_suffix('.json').write_text(json.dumps(info,indent=2)+'\n');reports.append({'id':id,'before_bytes':len(src),'after_bytes':p.stat().st_size,'mesh_lods':hashes})
  print('Losslessquad',id,len(src),'→',p.stat().st_size,flush=True)
 (BUILD/'quad-encoding-validation.json').write_text(json.dumps({'status':'PASS','tool_sha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),'records':reports},indent=2)+'\n')
if __name__=='__main__':main()
