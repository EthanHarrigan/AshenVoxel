"""Freeze and profile the real pack; private reader variants only, no Assets edits."""
from pathlib import Path
import argparse, datetime, difflib, hashlib, json, shutil, subprocess

ROOT=Path(__file__).resolve().parents[2]
HERE=Path(__file__).resolve().parent
CACHE=ROOT/'.terrain-cache/startup-throughput/prototype-decode'
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def once(text,old,new):
    if text.count(old)!=1:raise RuntimeError('Expected unique fragment: '+old[:70])
    return text.replace(old,new)
def main():
    parser=argparse.ArgumentParser();parser.add_argument('--variant',choices=['baseline','bounds','nocopy','combined','unrolled','crc','fast'],default='baseline');args=parser.parse_args()
    out=CACHE/(datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%d-%H%M%S-%f')+'-'+args.variant);out.mkdir(parents=True)
    sources=[];hashes={}
    for folder in ['Core','WorldScaleData']:
        for original in sorted((ROOT/'Assets/AshenVoxel'/folder).rglob('*.cs')):
            rel=original.relative_to(ROOT);target=out/'source'/rel;target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(original,target);sources.append(target);hashes[rel.as_posix()]=digest(target)
    pack=ROOT/'Assets/AshenVoxel/Exploration/Prototypes/ExplorationPrototypes.bytes'
    shutil.copyfile(pack,out/'pack.bytes')
    rel=Path('Assets/AshenVoxel/WorldScaleData/ExplorationPrototypePack.cs');reader=out/'source'/rel
    baseline=reader.read_text();s=baseline
    if args.variant in ['nocopy','combined']:
        s=once(s,'readonly BinaryReader input;readonly HashSet<int> ids', 'readonly byte[] packBytes;readonly BinaryReader input;readonly HashSet<int> ids')
        s=once(s,'input=new BinaryReader(new MemoryStream(bytes,false));','packBytes=bytes;input=new BinaryReader(new MemoryStream(bytes,false));')
        s=once(s,'byte[] payload=new byte[raw];byte[] compressed=input.ReadBytes(packed);\n            using(var gzip=new GZipStream(new MemoryStream(compressed,false),CompressionMode.Decompress))',
          'byte[] payload=new byte[raw];int packedStart=(int)input.BaseStream.Position;input.BaseStream.Position+=packed;\n            using(var gzip=new GZipStream(new MemoryStream(packBytes,packedStart,packed,false),CompressionMode.Decompress))')
    if args.variant in ['bounds','combined','unrolled','fast']:
        s=once(s,'int u=(normal/2+1)%3,v=(normal/2+2)%3;\n                for(int corner=0;corner<4;corner++)',
          '''int u=(normal/2+1)%3,v=(normal/2+2)%3;
                // Positive extents: checking both endpoints on each axis encloses all four corners.
                for(int axis=0;axis<3;axis++)
                {
                    long minimum=previous[axis],maximum=minimum+(axis==u?width:axis==v?height:0);
                    if(minimum<short.MinValue||maximum>short.MaxValue||minimum*.1<bounds[axis]-6.5||maximum*.1>bounds[axis+3]+6.5)
                        throw new InvalidDataException("Quad vertex exceeds conservative bounds.");
                }
                for(int corner=0;corner<4;corner++)''')
        s=once(s,'                        if(value<short.MinValue||value>short.MaxValue||value*.1<bounds[axis]-6.5||value*.1>bounds[axis+3]+6.5)throw new InvalidDataException("Quad vertex exceeds conservative bounds.");\n','')
    if args.variant in ['unrolled','fast']:
        s=once(s,'''                for(int corner=0;corner<4;corner++)
                {
                    int vertex=q*4+corner;
                    for(int axis=0;axis<3;axis++)
                    {
                        long value=previous[axis]+(axis==u&&(corner==1||corner==2)?width:0)+(axis==v&&(corner==2||corner==3)?height:0);
                        positions[vertex*3+axis]=(short)value;
                    }
                    normals[vertex]=normal;uint alpha=(uint)((material<<2)|((ao>>(corner*2))&3));colors[vertex]=palette[color]|alpha<<24;
                }''','''                // Start all corners at the same origin, then advance along the two face axes.
                int p=q*12;
                positions[p]=positions[p+3]=positions[p+6]=positions[p+9]=(short)previous[0];
                positions[p+1]=positions[p+4]=positions[p+7]=positions[p+10]=(short)previous[1];
                positions[p+2]=positions[p+5]=positions[p+8]=positions[p+11]=(short)previous[2];
                positions[p+3+u]=positions[p+6+u]=(short)(previous[u]+width);
                positions[p+6+v]=positions[p+9+v]=(short)(previous[v]+height);
                int vertex=q*4;normals[vertex]=normals[vertex+1]=normals[vertex+2]=normals[vertex+3]=normal;
                uint pigment=palette[color]|((uint)material<<26);
                colors[vertex]=pigment|((uint)(ao&3)<<24);colors[vertex+1]=pigment|((uint)((ao>>2)&3)<<24);
                colors[vertex+2]=pigment|((uint)((ao>>4)&3)<<24);colors[vertex+3]=pigment|((uint)((ao>>6)&3)<<24);''')
    if args.variant in ['crc','fast']:
        s=once(s,'''        static readonly uint[] CrcTable=MakeCrc();
        static uint[] MakeCrc(){var table=new uint[256];for(int i=0;i<256;i++){uint n=(uint)i;for(int b=0;b<8;b++)n=(n>>1)^(0xedb88320u&(uint)-(int)(n&1));table[i]=n;}return table;}
        public static uint Crc(byte[] bytes){uint value=0xffffffff;foreach(byte b in bytes)value=CrcTable[(value^b)&255]^(value>>8);return ~value;}''','''        static readonly uint[][] CrcTables=MakeCrc();
        static uint[][] MakeCrc()
        {
            var tables=new uint[8][];for(int i=0;i<8;i++)tables[i]=new uint[256];
            for(int i=0;i<256;i++)
            {
                uint n=(uint)i;for(int bit=0;bit<8;bit++)n=(n>>1)^(0xedb88320u&(uint)-(int)(n&1));tables[0][i]=n;
            }
            for(int slice=1;slice<8;slice++)for(int i=0;i<256;i++)
            {uint previous=tables[slice-1][i];tables[slice][i]=tables[0][previous&255]^(previous>>8);}
            return tables;
        }
        public static uint Crc(byte[] bytes)
        {
            uint value=0xffffffff;int offset=0;
            var t0=CrcTables[0];var t1=CrcTables[1];var t2=CrcTables[2];var t3=CrcTables[3];
            var t4=CrcTables[4];var t5=CrcTables[5];var t6=CrcTables[6];var t7=CrcTables[7];
            // Same reflected IEEE CRC32 and byte order; eight independent lookups
            // shorten the dependency chain. The fixed tables total8KiB.
            for(;offset<=bytes.Length-8;offset+=8)
            {
                uint first=value^(uint)(bytes[offset]|bytes[offset+1]<<8|bytes[offset+2]<<16|bytes[offset+3]<<24);
                value=t7[first&255]^t6[(first>>8)&255]^t5[(first>>16)&255]^t4[first>>24]^
                    t3[bytes[offset+4]]^t2[bytes[offset+5]]^t1[bytes[offset+6]]^t0[bytes[offset+7]];
            }
            for(;offset<bytes.Length;offset++)value=t0[(value^bytes[offset])&255]^(value>>8);
            return ~value;
        }''')
    # Preserve the clean, adoptable source separately from timing instrumentation.
    clean=out/'candidate'/rel;clean.parent.mkdir(parents=True);clean.write_text(s)
    (out/'candidate.diff').write_text(''.join(difflib.unified_diff(baseline.splitlines(True),s.splitlines(True),fromfile='installed/'+rel.as_posix(),tofile='candidate/'+rel.as_posix())))
    s=once(s,'            byte[] payload=new byte[raw];','            long inflateStamp=DecodeMetrics.Start;\n            byte[] payload=new byte[raw];')
    s=once(s,'            if(Crc(payload)!=crc)','            DecodeMetrics.Inflate+=DecodeMetrics.Elapsed(inflateStamp);long crcStamp=DecodeMetrics.Start;\n            if(Crc(payload)!=crc)')
    s=once(s,'            expanded+=raw;','            DecodeMetrics.Crc+=DecodeMetrics.Elapsed(crcStamp);\n            expanded+=raw;')
    s=once(s,'                var collision=ReadColumns(r,bounds);','                long columnStamp=DecodeMetrics.Start;\n                var collision=ReadColumns(r,bounds);')
    s=once(s,'                if(r.BaseStream.Position!=r.BaseStream.Length)','                DecodeMetrics.Columns+=DecodeMetrics.Elapsed(columnStamp);\n                if(r.BaseStream.Position!=r.BaseStream.Length)')
    s=once(s,'            int quads=r.ReadInt32(),paletteCount=r.ReadInt32();','            long meshStamp=DecodeMetrics.Start;\n            int quads=r.ReadInt32(),paletteCount=r.ReadInt32();')
    s=once(s,'            return new PrototypeMeshData(positions,normals,colors,indices);','            DecodeMetrics.Lods.Add(DecodeMetrics.Elapsed(meshStamp));\n            return new PrototypeMeshData(positions,normals,colors,indices);')
    reader.write_text(s)
    probe=out/'source/Tools/StartupThroughput/PrototypeDecodeProbe.cs';probe.parent.mkdir(parents=True);shutil.copyfile(HERE/'PrototypeDecodeProbe.cs',probe);sources.append(probe)
    dotnet=Path(shutil.which('dotnet')).resolve();version=lambda p:tuple(map(int,p.name.split('.')))
    sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1];refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
    dll=out/'DecodeProbe.dll';q=lambda p:'"'+str(p)+'"';rsp=out/'compile.rsp'
    rsp.write_text('\n'.join(['-nologo','-langversion:9.0','-optimize+','-target:exe','-out:'+q(dll)]+['-r:'+q(p) for p in refs.glob('*.dll')]+[q(p) for p in sources]))
    subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
    dll.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
    completed=subprocess.run([str(dotnet),str(dll),str(out/'pack.bytes'),str(out/'result.json')],text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=60)
    (out/'console.txt').write_text(completed.stdout);print(completed.stdout)
    if completed.returncode:raise RuntimeError('Probe failed; evidence retained at '+str(out))
    result=json.loads((out/'result.json').read_text());result.update(variant=args.variant,utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),baseline_sha256=hashes,
        source_sha256={p.relative_to(out/'source').as_posix():digest(p) for p in sources},candidate_sha256=digest(clean),pack_source=str(pack),runner_sha256=digest(Path(__file__)))
    assert digest(pack)==digest(out/'pack.bytes'),'Live pack changed while profiling'
    (out/'result.json').write_text(json.dumps(result,indent=2)+'\n');print(out/'result.json')
if __name__=='__main__':main()
