"""Derive immutable body/resistance spans from exact AVPM0002 occupancy.

No geometry, pigments, material IDs, generation or saved matter are changed.
AVPM0003 retains exact quad payloads and adds leaf/stick span channels.
"""
from pathlib import Path
import io, struct, gzip, zlib, hashlib, json, argparse
import numpy as np
from scipy import ndimage

ROOT=Path(__file__).resolve().parents[2]
ASSET=ROOT/'Assets/AshenVoxel/Exploration/Prototypes/ExplorationPrototypes.bytes'
SOURCE=ROOT/'Tools/LocalRevisionBackup/bin/before-stick-horizon'/ASSET.relative_to(ROOT)

def var(s):
    v=0
    for shift in range(0,35,7):
        b=s.read(1)[0];v|=(b&127)<<shift
        if b<128:return v
    raise ValueError('bad varint')

def columns(mask,offset):
    result=io.BytesIO();items=[]
    for x,z in np.argwhere(mask.any(axis=1)):
        ys=np.flatnonzero(np.diff(np.pad(mask[x,:,z].astype(np.int8),(1,1))))+offset[1]
        items.append((x+offset[0],z+offset[2],ys))
    result.write(struct.pack('<i',len(items)))
    for x,z,ys in items:result.write(struct.pack('<hhH',x,z,len(ys)//2)+ys.astype('<i2').tobytes())
    return result.getvalue()

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--source',type=Path);args=parser.parse_args()
    source=args.source or (ASSET if ASSET.read_bytes()[:8]==b'AVPM0002' else SOURCE)
    if not source.exists():raise SystemExit('Provide --source with the original AVPM0002 pack, or run bake_prototypes.py --mode all first.')
    original=source.read_bytes();assert original[:8]==b'AVPM0002'
    src=io.BytesIO(original);src.seek(8);count=struct.unpack('<i',src.read(4))[0]
    output=io.BytesIO();output.write(b'AVPM0003'+struct.pack('<i',count));reports=[]
    for _ in range(count):
        n,r,crc=struct.unpack('<III',src.read(12));raw=gzip.decompress(src.read(n));assert len(raw)==r and zlib.crc32(raw)==crc
        s=io.BytesIO(raw);model,kind,ln=struct.unpack('<iBH',s.read(7));name=s.read(ln).decode();bounds=np.array(struct.unpack('<6f',s.read(24)))
        offset=np.rint(bounds[:3]*10).astype(int);shape=np.rint(bounds[3:]*10).astype(int)-offset
        # One extra Y boundary holds the top-face exit event.
        events=np.zeros((shape[0],shape[1]+1,shape[2]),dtype=np.int8) if kind!=2 else None
        lods=s.read(1)[0]
        for lod in range(lods):
            quads,palette=struct.unpack('<ii',s.read(8));s.seek(palette*3,1);prev=np.zeros(3,dtype=int)
            for q in range(quads):
                for axis in range(3):
                    v=var(s);prev[axis]+=(v>>1)^-(v&1)
                width,height=var(s),var(s);normal=s.read(1)[0]&7;var(s);s.read(1)
                if lod==0 and events is not None and normal in (2,3):
                    p=prev-offset;x,y,z=p
                    events[x:x+height,y,z:z+width]+=1 if normal==3 else -1
        geometry_end=s.tell()
        hard=np.zeros(tuple(shape),dtype=bool)
        cols=struct.unpack('<i',s.read(4))[0]
        for c in range(cols):
            x,z,nsp=struct.unpack('<hhH',s.read(6))
            for j in range(nsp):
                a,b=struct.unpack('<hh',s.read(4));hard[x-offset[0],a-offset[1]:b-offset[1],z-offset[2]]=True
        assert s.tell()==len(raw)
        old_hard=int(hard.sum())
        if kind!=2:
            winding=np.cumsum(events,axis=1)[:,:-1,:];assert np.all((winding==0)|(winding==1)),name
            occupied=winding==1;assert np.all(~hard|occupied),name
            # A 30cm wood core dilated by 20cm retains trunk bark and attachment
            # margins. Unsupported 10–20cm shoots have no such core: sticks.
            core=ndimage.binary_erosion(hard,structure=np.ones((3,3,3),bool)) if kind==0 else np.zeros_like(hard)
            retained=hard & ndimage.binary_dilation(core,structure=np.ones((5,5,5),bool))
            if kind==0:
                # Preserve the supported upright leader even on slender birch
                # specimens. A thin trunk is not a peripheral branch. Follow
                # only contiguous nearby sections, never jump to a remote limb.
                center=-offset[[0,2]].astype(float)
                for y in range(max(0,-offset[1]),shape[1]):
                    points=np.argwhere(hard[:,y,:])
                    if not len(points):break
                    d=((points-center)**2).sum(axis=1);near=points[d<=16]
                    if not len(near):break
                    closest=points[d.argmin()];local=near[((near-closest)**2).sum(axis=1)<=9]
                    center=local.mean(axis=0)
                    keep=points[((points-center)**2).sum(axis=1)<=9]
                    retained[keep[:,0],y,keep[:,1]]=True
            sticks=hard & ~retained;leaves=occupied & ~hard;hard=retained
            channels=columns(hard,offset)+columns(leaves,offset)+columns(sticks,offset)
            leaf_count,stick_count=int(leaves.sum()),int(sticks.sum())
        else:
            channels=raw[geometry_end:]+struct.pack('<ii',0,0);leaf_count=stick_count=0
        payload=raw[:geometry_end]+channels;compressed=gzip.compress(payload,compresslevel=9,mtime=0)
        output.write(struct.pack('<III',len(compressed),len(payload),zlib.crc32(payload))+compressed)
        reports.append(dict(id=model,name=name,source_hard_cells=old_hard,hard_cells=int(hard.sum()),leaf_cells=leaf_count,stick_cells=stick_count,geometry_sha256=hashlib.sha256(raw[:geometry_end]).hexdigest()))
        print(name,'hard',int(hard.sum()),'leaf',leaf_count,'stick',stick_count,flush=True)
    ASSET.write_bytes(output.getvalue())
    report=dict(version=1,tool_sha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),scope='Derived prototype interaction cache; unchanged exact visual meshes and material saves',source_pack_sha256=hashlib.sha256(original).hexdigest(),asset_sha256=hashlib.sha256(ASSET.read_bytes()).hexdigest(),records=reports)
    (ROOT/'Docs/exploration-interactions-bake.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')

if __name__=='__main__':main()
