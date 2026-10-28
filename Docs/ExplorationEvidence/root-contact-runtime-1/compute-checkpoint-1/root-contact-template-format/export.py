from pathlib import Path
import datetime,hashlib,io,json,struct
HERE=Path(__file__).resolve().parent;DEV=HERE.parents[1];CACHE=DEV/'.terrain-cache'
sha=lambda data:hashlib.sha256(data).hexdigest()
prior=json.loads((CACHE/'root-contact-cache-prototype/report.json').read_text())['source_sha256']
PACK='41556b474ce7a26342e4dd7bcff2e4fe658ab0333e8360058262ee21c0f011aa'
rows=[];inputs={}
def read(path,digest=None):
 data=path.read_bytes();actual=sha(data)
 if digest:assert actual==digest,(path,'source changed')
 inputs[str(path)]=actual;return data
for model,seed,run in [(10,714103,'specimen-10-20261027T130937232091Z'),(11,271828,'specimen-11-20261027T131159753179Z')]:
 rootpath=CACHE/f'root-contact-runtime-prototype/v2-template-{model}.bin'
 woodpath=CACHE/f'grounding-root-visual/wood-{model}-baseline.bin'
 root=read(rootpath,prior[str(rootpath)]);wood=read(woodpath,prior[str(woodpath)])
 folder=CACHE/'root-contact-composite-lods'/run
 manifest=json.loads((folder/'manifest.json').read_text());read(folder/'manifest.json')
 # The frozen manifest uses absolute source/input names and relative produced names.
 def captured(name):
  path=folder/name;data=read(path)
  dictionaries=[value for value in manifest.values() if isinstance(value,dict)]
  matches=[value.get(name) or value.get(str(path)) for value in dictionaries]
  assert sha(data) in matches,(name,'absent from frozen manifest')
  return data
 payload=io.BytesIO();write=lambda fmt,*v:payload.write(struct.pack('<'+fmt,*v))
 write('8i',model,seed,470,978,470,235,4,235)
 payload.write(bytes.fromhex(PACK));payload.write(bytes.fromhex(sha(root)));payload.write(bytes.fromhex(sha(wood)))
 write('2i',2,29)
 n=struct.unpack_from('<i',root)[0];write('i',n);offset=4
 for _ in range(n):
  x,z,unused_terrain_target,count=struct.unpack_from('<hhhi',root,offset);offset+=10
  write('hhi',x,z,count);payload.write(root[offset:offset+6*count]);offset+=6*count
 assert offset==len(root)
 n=struct.unpack_from('<i',wood)[0];assert len(wood)==4+10*n
 context=[v for v in struct.iter_unpack('<hhhI',wood[4:]) if 29<=v[1]<=32]
 write('i',len(context))
 for cell in context:write('hhhI',*cell)
 order=captured('canonical-order.bin');n=struct.unpack_from('<i',order)[0];assert len(order)==4+10*n;write('i',n)
 for rank,(x,y,z,recorded_rank) in enumerate(struct.iter_unpack('<hhhi',order[4:])):
  assert rank==recorded_rank;write('hhh',x,y,z)
 write('i',5)
 for factor,cut in zip([1,2,4,16,64],[30,32,36,60,124]):
  fixed=captured(f'fixed-F{factor}.bin');magic,f,c,s,count=struct.unpack_from('<5i',fixed)
  assert (magic,f,c,s)==(0x314c4352,factor,cut,seed) and len(fixed)==20+count*17
  write('3i',factor,cut,count);payload.write(fixed[20:])
 data=payload.getvalue();assert len(data)<=2*1024*1024
 encoded=struct.pack('<8sii',b'AVRC0001',1,len(data))+hashlib.sha256(data).digest()+data
 output=HERE/f'model-{model}.avrc';assert not output.exists() or output.read_bytes()==encoded
 output.write_bytes(encoded)
 rows.append(dict(model=model,seed=seed,file=output.name,file_sha256=sha(encoded),bytes=len(encoded),payload_sha256=sha(data),context_cells=len(context)))
manifest=dict(utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),cache_format='AVRC0001',version=1,pack_sha256=PACK,models=rows,input_sha256=inputs,
 migration='Replaceable derived cache: reject any unsupported version or mismatched pack/manifest/model. Regenerate explicitly from reviewed source, retain complete canonical tree fallback. AVPM4 and gameplay saves unchanged.',
 scope='Two reviewed mature redwood templates only. No world/pack/atlas regeneration or production asset installation. Binary data fixtures; not executables.')
(HERE/'export-manifest.json').write_text(json.dumps(manifest,indent=2))
print(json.dumps({'models':rows,'input_files':len(inputs)},indent=2))
