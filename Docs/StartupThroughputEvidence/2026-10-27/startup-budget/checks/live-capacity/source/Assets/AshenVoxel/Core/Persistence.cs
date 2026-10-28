using System;
using System.IO;
using System.Collections.Generic;
namespace AshenVoxel.Core
{
    /// <summary>Versioned, checksummed finite-world snapshots. Single-writer, local snapshot format; not a streaming/network protocol.</summary>
    public static class Persistence
    {
        const uint Magic=0x4158564c; // AXVL
        public const int FormatVersion=2;
        public const int MaxChunks=8192;
        const int MaxPayload=MaxChunks*(Chunk.Volume*5+13)+28;
        public static void Save(Stream destination,VoxelWorld world)
        {
            if(destination==null||!destination.CanWrite)throw new ArgumentException("Writable stream required.",nameof(destination));
            if(world==null)throw new ArgumentNullException(nameof(world));
            if(world.Chunks.Count>MaxChunks)throw new InvalidOperationException("Snapshot chunk limit exceeded.");
            // Format 2 adds a one-byte channel flag and optional packed colors to each material chunk.
            int payloadLength=28;
            foreach(var chunk in world.Chunks.Values)payloadLength+=13+Chunk.Volume+(chunk.HasTints?Chunk.Volume*4:0);
            var keys=new List<Int3>(world.Chunks.Keys);
            keys.Sort((a,b)=>{int c=a.z.CompareTo(b.z);if(c==0)c=a.y.CompareTo(b.y);return c==0?a.x.CompareTo(b.x):c;});
            using(var output=new BinaryWriter(destination,System.Text.Encoding.UTF8,true))
            {
                output.Write(Magic);output.Write(FormatVersion);output.Write(payloadLength);
                using(var crcStream=new CrcWriteStream(destination))
                {
                    using(var writer=new BinaryWriter(crcStream,System.Text.Encoding.UTF8,true))
                    {
                        writer.Write(world.SizeX);writer.Write(world.SizeY);writer.Write(world.SizeZ);writer.Write(world.VoxelSize);writer.Write(world.Seed);writer.Write(world.GenerationVersion);writer.Write(keys.Count);
                        var cells=new byte[Chunk.Volume];
                        foreach(Int3 key in keys)
                        {
                            Chunk chunk=world.Chunks[key];
                            writer.Write(key.x);writer.Write(key.y);writer.Write(key.z);writer.Write((byte)(chunk.HasTints?1:0));
                            ReadOnlySpan<MaterialId> source=chunk.Cells;
                            for(int i=0;i<cells.Length;i++)cells[i]=(byte)source[i];
                            writer.Write(cells,0,cells.Length);
                            if(chunk.HasTints)
                            {
                                ReadOnlySpan<uint> colors=chunk.Tints;
                                // Reuse the material-sized buffer for color blocks, avoiding per-color stream calls.
                                for(int start=0;start<colors.Length;start+=cells.Length/4)
                                {
                                    int count=Math.Min(cells.Length/4,colors.Length-start);
                                    for(int i=0;i<count;i++){uint color=colors[start+i];int offset=i*4;cells[offset]=(byte)color;cells[offset+1]=(byte)(color>>8);cells[offset+2]=(byte)(color>>16);cells[offset+3]=(byte)(color>>24);}
                                    writer.Write(cells,0,count*4);
                                }
                            }
                        }
                    }
                    output.Write(crcStream.Checksum);
                }
            }
        }
        public static VoxelWorld Load(Stream source)
        {
            if(source==null||!source.CanRead)throw new ArgumentException("Readable stream required.",nameof(source));
            try
            {
                using(var reader=new BinaryReader(source,System.Text.Encoding.UTF8,true))
                {
                    if(reader.ReadUInt32()!=Magic)throw new InvalidDataException("Invalid voxel snapshot magic.");
                    int format=reader.ReadInt32();
                    if(format!=1&&format!=FormatVersion)throw new InvalidDataException("Unsupported voxel snapshot version.");
                    int size=reader.ReadInt32();int limit=format==1?28+MaxChunks*(Chunk.Volume+12):MaxPayload;
                    if(size<28||size>limit)throw new InvalidDataException("Invalid snapshot payload size.");
                    byte[] bytes=reader.ReadBytes(size);if(bytes.Length!=size)throw new InvalidDataException("Truncated snapshot payload.");
                    if(reader.ReadUInt32()!=Crc32(bytes))throw new InvalidDataException("Snapshot checksum mismatch.");
                    if(source.ReadByte()!=-1)throw new InvalidDataException("Unexpected bytes after snapshot.");
                    using(var data=new BinaryReader(new MemoryStream(bytes,false)))
                    {
                        int sx=data.ReadInt32(),sy=data.ReadInt32(),sz=data.ReadInt32();float voxel=data.ReadSingle();int seed=data.ReadInt32();
                        int generation=data.ReadInt32();
                        // Full material snapshots are self-contained: historical generators do not need to run again.
                        if(generation<1||generation>Generation.Version)throw new InvalidDataException("Unsupported generator version.");
                        int count=data.ReadInt32();
                        if(count<0||count>MaxChunks||(format==1?size!=28+count*(12+Chunk.Volume):size<28+count*(13+Chunk.Volume)))throw new InvalidDataException("Invalid chunk count.");
                        VoxelWorld world;
                        try{world=new VoxelWorld(sx,sy,sz,voxel,seed);}catch(ArgumentException e){throw new InvalidDataException("Invalid world metadata.",e);}
                        world.GenerationVersion=generation;
                        var seen=new HashSet<Int3>();
                        var materials=new byte[Chunk.Volume];
                        for(int n=0;n<count;n++)
                        {
                            Int3 key=new Int3(data.ReadInt32(),data.ReadInt32(),data.ReadInt32());
                            if(key.x<0||key.y<0||key.z<0||key.x>=(sx+15)/16||key.y>=(sy+15)/16||key.z>=(sz+15)/16||!seen.Add(key))throw new InvalidDataException("Invalid or duplicate chunk coordinate.");
                            byte channels=format==1?(byte)0:data.ReadByte();
                            if(channels>1)throw new InvalidDataException("Unknown chunk appearance channel flags.");
                            int read=0;while(read<materials.Length){int nread=data.Read(materials,read,materials.Length-read);if(nread==0)throw new EndOfStreamException();read+=nread;}
                            bool any=false,anyTint=false;
                            for(int i=0;i<Chunk.Volume;i++)
                            {
                                MaterialId value=(MaterialId)materials[i];
                                if(!MaterialCatalog.IsValid(value))throw new InvalidDataException("Unknown material identifier.");
                                uint tint=channels==0?0:data.ReadUInt32();
                                if(!VoxelColor.IsValid(tint)||(value==MaterialId.Air&&tint!=0))throw new InvalidDataException("Invalid appearance value or color on air.");
                                if(tint!=0)anyTint=true;
                                if(value==MaterialId.Air)continue;
                                Int3 local=Chunk.Decode(i);int x=key.x*16+local.x,y=key.y*16+local.y,z=key.z*16+local.z;
                                if(!world.InBounds(x,y,z))throw new InvalidDataException("Occupied cell beyond finite world boundary.");
                                world.Set(x,y,z,value,tint);any=true;
                            }
                            if(!any)throw new InvalidDataException("Empty chunks must not be serialized.");
                            if(channels!=0&&!anyTint)throw new InvalidDataException("Empty appearance channels must not be serialized.");
                        }
                        if(data.BaseStream.Position!=data.BaseStream.Length)throw new InvalidDataException("Unexpected payload bytes after chunks.");
                        return world;
                    }
                }
            }
            catch(EndOfStreamException e){throw new InvalidDataException("Truncated voxel snapshot.",e);}
        }
        static readonly uint[] CrcTable=BuildCrcTable();
        static uint[] BuildCrcTable()
        {
            var table=new uint[256];
            for(uint i=0;i<table.Length;i++)
            {uint crc=i;for(int bit=0;bit<8;bit++)crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0u);table[i]=crc;}
            return table;
        }
        static uint UpdateCrc(uint crc,ReadOnlySpan<byte> bytes)
        {foreach(byte value in bytes)crc=(crc>>8)^CrcTable[(crc^value)&255];return crc;}
        static uint Crc32(byte[] data)=>~UpdateCrc(0xffffffff,data);
        /// <summary>Pass-through payload writer; no ownership of the caller's stream and no seek requirement.</summary>
        sealed class CrcWriteStream : Stream
        {
            readonly Stream destination;
            uint crc=0xffffffff;
            public uint Checksum=>~crc;
            public CrcWriteStream(Stream destination){this.destination=destination;}
            public override bool CanRead=>false;
            public override bool CanSeek=>false;
            public override bool CanWrite=>true;
            public override long Length=>throw new NotSupportedException();
            public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
            public override void Flush()=>destination.Flush();
            public override void Write(byte[] buffer,int offset,int count)
            {destination.Write(buffer,offset,count);crc=UpdateCrc(crc,new ReadOnlySpan<byte>(buffer,offset,count));}
            public override void Write(ReadOnlySpan<byte> buffer)
            {destination.Write(buffer);crc=UpdateCrc(crc,buffer);}
            public override void WriteByte(byte value)
            {destination.WriteByte(value);crc=(crc>>8)^CrcTable[(crc^value)&255];}
            public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
            public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
            public override void SetLength(long value)=>throw new NotSupportedException();
        }
    }
}
