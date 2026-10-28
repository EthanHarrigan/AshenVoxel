using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using AshenVoxel.Core;

namespace AshenVoxel.WorldScale
{
    public sealed class HeightProxyCacheKey
    {
        public const int FormatVersion=1,MaximumKeyBytes=65536;
        // Update on any change to Build/material sampling semantics. Source/profile
        // revisions remain separate, and caller must never reuse their identities.
        public const string AlgorithmIdentity="height-proxy-build/fa270bc21c1f1612bd8af36624aa47992cf292d0ffc9e0942528b0c0630ee980;sampler/a66ea39db657d365ebb3b897df72ee7146a28caee652270a7da789e062a2832b;profile/255a9049ed90b0585f523d51b5f82647eda3a46e8cb6ef1a4f9f7913d1453922";
        readonly byte[] bytes,digest;
        public readonly string Hash,SurfaceIdentity;
        public readonly int Quads,VertexCount,IndexCount,HeightCount;
        public readonly double SizeMeters,SkirtDepth;
        internal ReadOnlySpan<byte> Bytes=>bytes;
        internal ReadOnlySpan<byte> Digest=>digest;
        internal long PayloadBytes=>16L+24L*VertexCount+4L*IndexCount+4L*HeightCount;
        internal long FileBytes=>108+bytes.Length+PayloadBytes;
        HeightProxyCacheKey(byte[] bytes,string identity,int n,double size,double skirt)
        {
            this.bytes=bytes;using var sha=SHA256.Create();digest=sha.ComputeHash(bytes);Hash=Hex(digest);SurfaceIdentity=identity;Quads=n;SizeMeters=size;SkirtDepth=skirt;
            HeightCount=(n+1)*(n+1);VertexCount=HeightCount+8*(n+1);IndexCount=n*n*6+n*24;
        }
        internal static string Hex(ReadOnlySpan<byte> data){var s=new StringBuilder(data.Length*2);foreach(byte b in data)s.Append(b.ToString("x2",System.Globalization.CultureInfo.InvariantCulture));return s.ToString();}
        public static HeightProxyCacheKey Create(SurfaceMaterialSampler sampler,LodTile tile,int quads,double skirtDepth)
        {
            if(sampler==null||tile==null)throw new ArgumentNullException();
            if(!BitConverter.IsLittleEndian||Marshal.SizeOf<HeightProxySurface.Vertex>()!=24)throw new NotSupportedException("Height proxy cache layout/endianness");
            if(quads<8||quads>256||(quads&(quads-1))!=0||tile.Key.Level<0||tile.Key.Level>14)throw new ArgumentOutOfRangeException(nameof(quads));
            Numeric.Positive(skirtDepth,nameof(skirtDepth));Numeric.Positive(tile.SizeMeters,nameof(tile.SizeMeters));
            Numeric.Finite(tile.OriginXMeters,nameof(tile.OriginXMeters));Numeric.Finite(tile.OriginZMeters,nameof(tile.OriginZMeters));Numeric.Finite(tile.MinimumHeight,nameof(tile.MinimumHeight));Numeric.Finite(tile.MaximumHeight,nameof(tile.MaximumHeight));
            if(tile.MinimumHeight>tile.MaximumHeight)throw new ArgumentException("Inverted tile bounds");
            long cells=256L<<tile.Key.Level;
            double expectedX=(double)tile.Key.X*cells*.1,expectedZ=(double)tile.Key.Z*cells*.1;
            if(Math.Abs(tile.SizeMeters-cells*.1)>1e-5||Math.Abs(tile.OriginXMeters-expectedX)>1e-4||Math.Abs(tile.OriginZMeters-expectedZ)>1e-4)throw new ArgumentException("Proxy dyadic grid mismatch");
            string source=sampler.SourceIdentity,profile=sampler.Profile.Identity;
            if(string.IsNullOrEmpty(source)||source.Length>16384||string.IsNullOrEmpty(profile)||profile.Length>4096)throw new ArgumentException("Bounded source/profile identities required");
            string surface=source+"/height-proxy-cache-v"+HeightProxySurface.Version+"/"+tile.Key+"/"+quads;
            using var stream=new MemoryStream();using(var w=new BinaryWriter(stream,new UTF8Encoding(false,true),true))
            {
                w.Write("height-proxy-disk-key-v1");w.Write(AlgorithmIdentity);w.Write(HeightProxySurface.Version);w.Write(HeightProxySurface.VertexStride);w.Write(MaterialCatalog.Version);w.Write(VoxelTerrainPage.Version);w.Write(WorldCell.SizeMeters);w.Write(true);
                w.Write(source);w.Write(profile);w.Write(surface);w.Write(tile.Key.X);w.Write(tile.Key.Z);w.Write(tile.Key.Level);w.Write(tile.OriginXMeters);w.Write(tile.OriginZMeters);w.Write(tile.SizeMeters);w.Write(tile.MinimumHeight);w.Write(tile.MaximumHeight);w.Write(quads);w.Write(skirtDepth);
            }
            if(stream.Length>MaximumKeyBytes)throw new ArgumentException("Cache key byte limit");
            return new HeightProxyCacheKey(stream.ToArray(),surface,quads,tile.SizeMeters,skirtDepth);
        }
    }

    /// <summary>Opt-in, single-directory replaceable render cache. Use ONE instance
    /// on the existing serialized terrain worker. No Unity, task, queue or matter.</summary>
    public sealed class HeightProxyDiskCache:IDisposable
    {
        public const long DefaultBudgetBytes=1L<<30,MaximumBudgetBytes=4L<<30;
        public const int MaximumRecordBytes=4*1024*1024,MaximumEntries=4096,MaximumScannedFiles=8192;
        const string Prefix="ashen-heightproxy-v1-",Extension=".ahpc",Magic="AHPC0001";
        readonly string directory;readonly bool enabled;readonly long budget;readonly object gate=new object();
        sealed class Entry{public string Path,Hash;public long Bytes,Use;}
        readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>();FileStream ownerLock;bool initialized,disposed;long diskBytes,clock;int entryCount;
        public bool Enabled=>enabled&&!disposed;
        public long BudgetBytes=>budget;
        public long OwnedDiskBytes=>Interlocked.Read(ref diskBytes);
        public int RecordCount=>Volatile.Read(ref entryCount);
        public string DirectoryPath=>directory;
        public HeightProxyDiskCache(string directory,bool enabled=false,long budgetBytes=DefaultBudgetBytes)
        {
            if(string.IsNullOrEmpty(directory)||!Path.IsPathRooted(directory))throw new ArgumentException("Absolute owner-supplied cache directory required");
            if(budgetBytes<1||budgetBytes>MaximumBudgetBytes)throw new ArgumentOutOfRangeException(nameof(budgetBytes));
            this.directory=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
            if(this.directory.Length<4||string.Equals(this.directory,Path.GetPathRoot(this.directory)?.TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Cache cannot own a drive root");
            foreach(string part in this.directory.Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar))if(string.Equals(part,"Assets",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Cache must be outside Assets");
            this.enabled=enabled;budget=budgetBytes;
        }
        static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        static bool Finite(double v)=>!double.IsNaN(v)&&!double.IsInfinity(v);
        static void Require(bool b,string why){if(!b)throw new InvalidDataException(why);}
        static bool Hex(string s,int length){if(s==null||s.Length!=length)return false;foreach(char c in s)if(!(c>='0'&&c<='9'||c>='a'&&c<='f'))return false;return true;}
        static bool RecordName(string name,out string hash)
        {hash=null;if(!name.StartsWith(Prefix,StringComparison.Ordinal)||!name.EndsWith(Extension,StringComparison.Ordinal))return false;string h=name.Substring(Prefix.Length,name.Length-Prefix.Length-Extension.Length);if(!Hex(h,64))return false;hash=h;return true;}
        static bool TempName(string name)
        {if(!name.StartsWith(Prefix,StringComparison.Ordinal)||!name.EndsWith(".tmp",StringComparison.Ordinal))return false;string rest=name.Substring(Prefix.Length,name.Length-Prefix.Length-4);return rest.Length==97&&rest[64]=='-'&&Hex(rest.Substring(0,64),64)&&Hex(rest.Substring(65),32);}
        static bool Regular(string path)=>File.Exists(path)&&(File.GetAttributes(path)&(FileAttributes.ReparsePoint|FileAttributes.Directory))==0;
        static void CheckAncestors(string path)
        {int count=0;for(var d=new DirectoryInfo(path);d!=null;d=d.Parent){if(++count>256)throw new IOException("Directory depth limit");if(d.Exists&&(d.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Reparse cache path refused");}}
        string PathFor(string hash)=>Path.Combine(directory,Prefix+hash+Extension);
        bool Owns(string path,out Entry entry)
        {
            entry=null;if(!RecordName(Path.GetFileName(path),out string hash)||!Regular(path))return false;
            var f=new FileInfo(path);if(f.Length<108||f.Length>MaximumRecordBytes)return false;
            using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);using var r=new BinaryReader(stream,Encoding.UTF8,true);
            if(Encoding.ASCII.GetString(r.ReadBytes(8))!=Magic||r.ReadInt32()!=1)return false;
            stream.Position=44;byte[] key=r.ReadBytes(32);if(key.Length!=32||HeightProxyCacheKey.Hex(key)!=hash)return false;
            entry=new Entry{Path=path,Hash=hash,Bytes=f.Length,Use=f.LastWriteTimeUtc.Ticks};return true;
        }
        bool Ready(out string why,CancellationToken cancel)
        {
            why="";if(disposed){why="disposed";return false;}if(!enabled){why="disabled";return false;}if(initialized)return true;
            CheckAncestors(directory);Directory.CreateDirectory(directory);CheckAncestors(directory);
            string lockPath=Path.Combine(directory,".ashen-heightproxy-v1.lock");
            if(File.Exists(lockPath)&&!Regular(lockPath))throw new IOException("Reparse cache lock refused");
            ownerLock=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            try
            {
                int scanned=0;foreach(string p in Directory.EnumerateFiles(directory,Prefix+"*",SearchOption.TopDirectoryOnly))
                {
                    cancel.ThrowIfCancellationRequested();if(++scanned>MaximumScannedFiles)throw new IOException("Owned cache scan limit");
                    if(TempName(Path.GetFileName(p))&&Regular(p)){File.Delete(p);continue;}
                    if(Owns(p,out var e)){entries.Add(e.Hash,e);diskBytes+=e.Bytes;clock=Math.Max(clock,e.Use);}
                }
                while(entries.Count>MaximumEntries||diskBytes>budget){cancel.ThrowIfCancellationRequested();if(!Evict(null))throw new IOException("Cache pruning failed");}
                entryCount=entries.Count;initialized=true;return true;
            }
            catch{ownerLock.Dispose();ownerLock=null;entries.Clear();diskBytes=0;entryCount=0;throw;}
        }
        void Remove(Entry e){if(entries.Remove(e.Hash))diskBytes-=e.Bytes;entryCount=entries.Count;}
        bool Evict(string except)
        {
            Entry oldest=null;foreach(var e in entries.Values)if(e.Hash!=except&&(oldest==null||e.Use<oldest.Use||e.Use==oldest.Use&&string.CompareOrdinal(e.Hash,oldest.Hash)<0))oldest=e;
            if(oldest==null)return false;
            if(Owns(oldest.Path,out _))File.Delete(oldest.Path); // Never delete unknown/replaced files.
            Remove(oldest);return true;
        }
        void Touch(Entry e)
        {clock=Math.Max(clock+1,DateTime.UtcNow.Ticks);e.Use=clock;try{File.SetLastWriteTimeUtc(e.Path,new DateTime(clock,DateTimeKind.Utc));}catch(IOException){}catch(UnauthorizedAccessException){}}
        static void ReadExactly(Stream s,Span<byte> data,CancellationToken cancel)
        {while(data.Length!=0){cancel.ThrowIfCancellationRequested();int n=s.Read(data.Slice(0,Math.Min(65536,data.Length)));if(n==0)throw new EndOfStreamException();data=data.Slice(n);}}
        static void WriteAll(Stream s,ReadOnlySpan<byte> data,CancellationToken cancel)
        {while(data.Length!=0){cancel.ThrowIfCancellationRequested();int n=Math.Min(65536,data.Length);s.Write(data.Slice(0,n));data=data.Slice(n);}}
        static byte[] Metadata(HeightProxySurface s)
        {var b=new byte[16];BinaryPrimitives.WriteInt64LittleEndian(b,BitConverter.DoubleToInt64Bits(s.AnchorY));BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(8),BitConverter.SingleToInt32Bits(s.MinimumLocalY));BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(12),BitConverter.SingleToInt32Bits(s.MaximumLocalY));return b;}
        static byte[] PayloadHash(HeightProxySurface s,CancellationToken cancel)
        {
            using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);hash.AppendData(Metadata(s));
            void Add(ReadOnlySpan<byte> data){while(data.Length!=0){cancel.ThrowIfCancellationRequested();int n=Math.Min(65536,data.Length);hash.AppendData(data.Slice(0,n));data=data.Slice(n);}}
            Add(MemoryMarshal.AsBytes(s.Vertices.AsSpan()));Add(MemoryMarshal.AsBytes(s.Indices.AsSpan()));Add(MemoryMarshal.AsBytes(s.Heights.AsSpan()));return hash.GetHashAndReset();
        }
        static bool Same(HeightProxySurface.Vertex a,HeightProxySurface.Vertex b)=>a.X==b.X&&a.Y==b.Y&&a.Z==b.Z&&a.NX==b.NX&&a.NY==b.NY&&a.NZ==b.NZ&&a.NW==b.NW&&a.R==b.R&&a.G==b.G&&a.B==b.B&&a.A==b.A&&a.Roughness==b.Roughness&&a.Wetness==b.Wetness&&a.Snow==b.Snow&&a.Material==b.Material;
        static void Validate(HeightProxyCacheKey k,HeightProxySurface s,CancellationToken cancel)
        {
            Require(s!=null&&s.Quads==k.Quads&&s.SourceIdentity==k.SurfaceIdentity,"Surface identity");
            Require(s.Vertices!=null&&s.Indices!=null&&s.Heights!=null&&s.Vertices.Length==k.VertexCount&&s.Indices.Length==k.IndexCount&&s.Heights.Length==k.HeightCount,"Surface counts");
            Require(Finite(s.AnchorY)&&Finite(s.MinimumLocalY)&&Finite(s.MaximumLocalY)&&s.MinimumLocalY<=s.MaximumLocalY,"Surface bounds");
            int n=k.Quads,stride=n+1;double spacing=k.SizeMeters/n;float lo=float.MaxValue,hi=float.MinValue;
            for(int i=0;i<s.Vertices.Length;i++)
            {
                if((i&1023)==0)cancel.ThrowIfCancellationRequested();var v=s.Vertices[i];int material=v.Material-128;
                Require(Finite(v.X)&&Finite(v.Y)&&Finite(v.Z)&&v.NX!=-128&&v.NY!=-128&&v.NZ!=-128&&v.NW==0&&v.A==255&&material>0&&material<MaterialCatalog.Count&&MaterialCatalog.IsValid((MaterialId)material),"Vertex fields");
                Require(v.Snow==((MaterialId)material==MaterialId.Snow?255:0),"Snow flag");lo=Math.Min(lo,v.Y);hi=Math.Max(hi,v.Y);
                if(i<k.HeightCount)Require(Finite(s.Heights[i])&&v.Y==s.Heights[i]&&v.X==(float)((i%stride)*spacing)&&v.Z==(float)((i/stride)*spacing),"Height/grid fields");
            }
            Require(lo==s.MinimumLocalY&&hi==s.MaximumLocalY,"Exact bounds");
            int at=0;
            void Tri(int a,int b,int c){Require(s.Indices[at++]==a&&s.Indices[at++]==b&&s.Indices[at++]==c,"Index/topology");}
            for(int z=0;z<n;z++)for(int x=0;x<n;x++){if((at&2047)==0)cancel.ThrowIfCancellationRequested();int a=x+z*stride,b=a+stride;Tri(a,b,a+1);Tri(a+1,b,b+1);}
            for(int edge=0;edge<4;edge++)
            {
                int rim=k.HeightCount+edge*2*stride;double nx=edge==1?1:edge==3?-1:0,nz=edge==0?-1:edge==2?1:0;
                for(int i=0;i<=n;i++)
                {
                    cancel.ThrowIfCancellationRequested();int a=edge==0?i:edge==1?i*stride+n:edge==2?n*stride+n-i:(n-i)*stride;var top=s.Vertices[a];var expected=top.With(top.Y,nx,0,nz);var bottom=top.With((float)(top.Y-k.SkirtDepth),nx,0,nz);
                    Require(Same(s.Vertices[rim+2*i],expected)&&Same(s.Vertices[rim+2*i+1],bottom),"Skirt fields");
                }
                for(int i=0;i<n;i++){int a=rim+i*2;Tri(a,a+2,a+1);Tri(a+2,a+3,a+1);}
            }
            Require(at==s.Indices.Length,"Index count");
        }
        public bool TryLoad(HeightProxyCacheKey key,out HeightProxySurface surface,out string reason,CancellationToken cancel=default)
        {
            lock(gate)
            {
                surface=null;reason="";
                try
                {
                    cancel.ThrowIfCancellationRequested();if(key==null){reason="null-key";return false;}if(!Ready(out reason,cancel))return false;
                    string path=PathFor(key.Hash);if(!entries.TryGetValue(key.Hash,out var entry)||!Regular(path)){if(entry!=null)Remove(entry);reason="miss";return false;}
                    using var fs=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);using var r=new BinaryReader(fs,Encoding.UTF8,true);
                    Require(fs.Length==key.FileBytes&&fs.Length<=MaximumRecordBytes,"File length");
                    Require(Encoding.ASCII.GetString(r.ReadBytes(8))==Magic&&r.ReadInt32()==1&&r.ReadInt32()==key.Bytes.Length&&r.ReadInt32()==24&&r.ReadInt32()==key.Quads&&r.ReadInt32()==key.VertexCount&&r.ReadInt32()==key.IndexCount&&r.ReadInt32()==key.HeightCount&&r.ReadInt64()==key.PayloadBytes,"Header counts/layout");
                    byte[] digest=r.ReadBytes(32),payloadHash=r.ReadBytes(32);Require(digest.AsSpan().SequenceEqual(key.Digest)&&payloadHash.Length==32,"Header key");
                    byte[] encodedKey=new byte[key.Bytes.Length];ReadExactly(fs,encodedKey,cancel);Require(encodedKey.AsSpan().SequenceEqual(key.Bytes),"Exact key");
                    double anchor=r.ReadDouble();float lo=r.ReadSingle(),hi=r.ReadSingle();Require(Finite(anchor)&&Finite(lo)&&Finite(hi)&&lo<=hi,"Metadata bounds");
                    // All counts, lengths and key bytes are checked BEFORE mesh allocation.
                    var vertices=new HeightProxySurface.Vertex[key.VertexCount];var indices=new int[key.IndexCount];var heights=new float[key.HeightCount];
                    ReadExactly(fs,MemoryMarshal.AsBytes(vertices.AsSpan()),cancel);ReadExactly(fs,MemoryMarshal.AsBytes(indices.AsSpan()),cancel);ReadExactly(fs,MemoryMarshal.AsBytes(heights.AsSpan()),cancel);Require(fs.Position==fs.Length,"Trailing bytes");
                    var restored=HeightProxySurface.RestoreCached(key.Quads,anchor,vertices,indices,heights,lo,hi,key.SurfaceIdentity);
                    Require(PayloadHash(restored,cancel).AsSpan().SequenceEqual(payloadHash),"Payload checksum");Validate(key,restored,cancel);cancel.ThrowIfCancellationRequested();
                    Touch(entry);surface=restored;reason="hit";return true;
                }
                catch(Exception e){surface=null;reason=e is OperationCanceledException?"cancelled":"cache-miss:"+e.GetType().Name;return false;}
            }
        }
        public bool Store(HeightProxyCacheKey key,HeightProxySurface surface,out string reason,CancellationToken cancel=default)
        {
            lock(gate)
            {
                string temp=null;reason="";
                try
                {
                    cancel.ThrowIfCancellationRequested();if(key==null){reason="null-key";return false;}if(!Ready(out reason,cancel))return false;
                    if(key.FileBytes>MaximumRecordBytes||key.FileBytes>budget){reason="record-budget";return false;}Validate(key,surface,cancel);byte[] payloadHash=PayloadHash(surface,cancel);
                    string target=PathFor(key.Hash);entries.TryGetValue(key.Hash,out var old);
                    if(File.Exists(target)&&!Owns(target,out _)){reason="unowned-collision";return false;}
                    if(old!=null&&TryLoad(key,out var existing,out _,cancel)&&PayloadHash(existing,cancel).AsSpan().SequenceEqual(payloadHash)){reason="already-stored";return true;}
                    cancel.ThrowIfCancellationRequested();entries.TryGetValue(key.Hash,out old);
                    // Reserve the temporary file TOO: actual owned bytes never exceed
                    // budget. Atomic replacement can refuse when only one copy fits.
                    while(diskBytes+key.FileBytes>budget||entries.Count+(old==null?1:0)>MaximumEntries){cancel.ThrowIfCancellationRequested();if(!Evict(key.Hash)){reason="staging-budget";return false;}}
                    temp=Path.Combine(directory,Prefix+key.Hash+"-"+Guid.NewGuid().ToString("N")+".tmp");
                    using(var fs=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.WriteThrough))
                    {
                        using(var w=new BinaryWriter(fs,Encoding.UTF8,true)){w.Write(Encoding.ASCII.GetBytes(Magic));w.Write(1);w.Write(key.Bytes.Length);w.Write(24);w.Write(key.Quads);w.Write(key.VertexCount);w.Write(key.IndexCount);w.Write(key.HeightCount);w.Write(key.PayloadBytes);w.Write(key.Digest);w.Write(payloadHash);w.Write(key.Bytes);w.Write(Metadata(surface));}
                        WriteAll(fs,MemoryMarshal.AsBytes(surface.Vertices.AsSpan()),cancel);WriteAll(fs,MemoryMarshal.AsBytes(surface.Indices.AsSpan()),cancel);WriteAll(fs,MemoryMarshal.AsBytes(surface.Heights.AsSpan()),cancel);fs.Flush(true);Require(fs.Length==key.FileBytes,"Written length");
                    }
                    cancel.ThrowIfCancellationRequested();
                    if(File.Exists(target))File.Replace(temp,target,null);else File.Move(temp,target);temp=null;
                    if(old!=null)Remove(old);var entry=new Entry{Hash=key.Hash,Path=target,Bytes=key.FileBytes};entries[key.Hash]=entry;diskBytes+=entry.Bytes;entryCount=entries.Count;Touch(entry);reason="stored";return true;
                }
                catch(Exception e){reason=e is OperationCanceledException?"cancelled":"cache-write-failed:"+e.GetType().Name;return false;}
                finally{if(temp!=null){try{if(TempName(Path.GetFileName(temp))&&Regular(temp))File.Delete(temp);}catch(IOException){}catch(UnauthorizedAccessException){}}}
            }
        }
        public void Dispose(){lock(gate){if(disposed)return;disposed=true;ownerLock?.Dispose();ownerLock=null;entries.Clear();}}
    }
}
