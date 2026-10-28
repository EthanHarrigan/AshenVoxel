"""Retain exact AVPM3 interaction channels while updating render-only AVPM4 caches."""
import io, struct, gzip, zlib, hashlib
def sha(b): return hashlib.sha256(b).hexdigest()
def var(s):
    for _ in range(5):
        if s.read(1)[0]<128:return
    raise ValueError('bad varint')
def records(data):
    version=int(data[7:8]);assert data[:7]==b'AVPM000' and version in (3,4)
    s=io.BytesIO(data);s.seek(8);count=struct.unpack('<I',s.read(4))[0]
    for _ in range(count):
        n,r,crc=struct.unpack('<III',s.read(12));raw=gzip.decompress(s.read(n));assert len(raw)==r and zlib.crc32(raw)==crc
        q=io.BytesIO(raw);ident,kind,nm=struct.unpack('<IBH',q.read(7));q.seek(nm+24,1);lods=q.read(1)[0]
        for lod in range(lods):
            quads,palette=struct.unpack('<II',q.read(8));q.seek(palette*3,1)
            for _ in range(quads):
                for _ in range(5):var(q)
                q.read(1);var(q);q.read(2 if version==4 else 1)
        yield ident,raw[:q.tell()],raw[q.tell():]
    assert s.tell()==len(data)
def preserve(source,target):
    original=source.read_bytes();new=target.read_bytes();assert original[:8]==b'AVPM0003' and new[:8]==b'AVPM0004'
    old={i:(g,c) for i,g,c in records(original)};out=io.BytesIO();out.write(new[:12]);report=[]
    for ident,geo,_ in records(new):
        oldgeo,channels=old.pop(ident)
        # Identity, name, bounds and LOD count must match. Geometry itself now retains material interfaces.
        namebytes=struct.unpack_from('<H',geo,5)[0];end=7+namebytes+25
        assert geo[:end]==oldgeo[:end],f'prototype identity/bounds changed: {ident}'
        payload=geo+channels;compressed=gzip.compress(payload,compresslevel=9,mtime=0)
        out.write(struct.pack('<III',len(compressed),len(payload),zlib.crc32(payload))+compressed)
        report.append({'id':ident,'all_three_channels_sha256':sha(channels)})
    assert not old
    target.write_bytes(out.getvalue())
    return {'source_sha256':sha(original),'status':'byte-identical hard/leaf/stick channels','records':report}
