using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using AshenVoxel.Core;
using AshenVoxel.Core.Trees;
using AshenVoxel.Core.Rocks;
using AshenVoxel.Core.Details;
namespace AshenVoxel.Exploration.Experimental
{
    // Exact full-resolution replay recipes from ExportPrototypes, never decoded render meshes.
    public static class NaturalTreeTemplate
    {
        public const int MaximumCells=2000000;
        public const string PackIdentity="41556b474ce7a26342e4dd7bcff2e4fe658ab0333e8360058262ee21c0f011aa";
        static readonly int[] ExpectedTrees={3395,3408,22983,23152,6874,6682,47257,45450,66957,68590,1111103,1159639,7363,7620,50442,51228};
        static readonly int[] ExpectedRocks={152,1547,75,433,122,1144,80,873,189,1991,152,1523,152,1540,75,430,122,1135,82,874,189,1977,152,1524,152,1521,75,430,122,1113,80,863,189,1971,152,1504,147,1509,75,395,119,1105,80,857,181,1940,146,1476,146,1507,75,431,121,1100,78,860,185,1944,146,1475};
        static readonly string[] ExpectedMatterHashes={
            "35bde1d1cd0f3902edf93025c01ef6c5ba3b47a35c296aede3ec8c22f33fa077",
            "a41e209f5508c5ae8dc73e08fe35ac21b471fa7cac88a08d2f59ed456bc6fb11",
            "4757d30c140c6c4e3748102eba1c947a6e6ac8baecca0d3af0350bb11cafcab9",
            "5c5381fb25883923d9132296e4ecc898730425eb879166d46e838b5329204762",
            "1117989d5e8aff8c509a36f26f79a17c8bf7488dfd00fc7952024d2f87cb3357",
            "194c582ad17679053288cc2e73c25c445c4e216c94795ca0bb5a5a1ce060951c",
            "7f343214f87b06c78a0c280cd9ebfb19cbeb7da9c0a2c4e3a1a24386457a3203",
            "5eb4d8d04d5227429ef60265d4cbbcc7921ba2691c74277b657f182169774415",
            "df3950c4df00073c4a015d2037589f829b94968c7b64c35ddde91998faa00654",
            "b72b484467e1829ebe5aeba4c8ded00155a5c803ef9ebf6e474af17bfee221cc",
            "90a3543895e57170202f758bf97248e9af164ec19b2861fecabab86bfdc856f6",
            "4b119bcd14b4779e480379e1359f9eacc1152773e014208e6f95cc45792f2043",
            "5e07ce5b7277a70a783a3c7d7657fbe5d08f6c013f9e26dad2c924fbeeee47ad",
            "bbdecfe40689f641366827ff4f34f6b96fb80f952883baec8e0463450c16312f",
            "58216fe474711bea037c0489f0d52edb43ff387f5f175be7960bec5037607ae5",
            "79ef6587ed66e833994bde4ce55713646d520668364d38407ee4d52c68c599a4",
            "6271c16755c7f78e05ee7fc8124b59c316deefc2d017f5b4753495a33ea1a08f",
            "eb3c1779b167f1b321d7979f1b15e7db5c88d8cf5fac17a51f7aeb87427e8288",
            "ea540bee402319b0a0464697516e5e33d66db5833a3dcce1b9cbdee989c6a8f9",
            "eed645574277476d50ca7868d9b78a51bcdce7b30bb438aa662ca606e993d212",
            "f7771c08170035025e31d7f8931f6842bedf41bb104a598f42e65d80218f24e9",
            "1bf5a5cc74b9d5dc47eddcb2b970f9982a3bf2683e2a6f6c826c5417479cded7",
            "a97eaf2fa12daa221237bb11b036c41136d432168a15ac6b95929e7e5895897f",
            "a3558a810b159cadb7c3f3d2729347fc3b581c9efda480a275f7c231b0c82863",
            "ae2b9f03966d57d0151a99d18b186734a0b863a3ac7e56f78ddc28499e3a525d",
            "49e87164a9996d774bbdbd513e4d0b29cb644959a260ee059a167c2578765b5c",
            "6c47ce95e04cf7589a8e3ced2ffbdcbc607909f54de3dcc49172100eea44f5c8",
            "348d86967981b67085b97d8d442a0f958f1f1c22d4c5e5ae31c5bf2646d29456",
            "0149bb74337151801a12656fddad70d289cd9375b6efc1caf06e75b1b0fea74b",
            "b96c1c1742c196d423c83beb172affeed54a85b42a6cb25b33d0a319ab12e161",
            "1c86f74dcf60ac8fc769165c88a2816c1fe26575dfdddfd9df9e7d5958b5b0b3",
            "3b1bbe596bae1e4595a0640e3d92c303482b6e0f3a82148af1a171ee0f2170a4",
            "430d692c94635b6c439547bc116e147a42daedc05e853881b1dbb79a1a506199",
            "6aace66258d5a4a93de5aeabe710dafd4fa6f6344a442fc6e9fb37bdefdb8615",
            "22d4af0445d5c35af6f73c035c208eb1a4a9ae6bc4d7eb2fee089e28e2c16251",
            "dbd3db9a6f3ea517234cd5df5ed5e89589cd32848f7647c7e2e761b837328264",
            "87677e1dfb3b587349338a1080444873326ad3bf8f52c1094236d4f7dd2764b4",
            "86fb130a40d1b31d040aab9bda718be6116892f697b45e65ed6f4733c8ed225f",
            "1003a667e849213862d2e08d76b613dfea4bb8057824e4c30df065fb03aedfc6",
            "1360de0a5146547b42976f993dcc95ed23066ebe8cf65a6f0c505aed2c0f5d19",
            "97bd58b93fa30d25fb423a8e999179aefb46d7c4591ab858eac4bb8a7afc15ce",
            "d71135a13b68a877cdfbe01cf520680cff4e7ea480cd19a410eea24c74259814",
            "8c1b5a23d5649a1da9035af26f13835e0fe2cff42fe4e1e0eeee7a329796a458",
            "76179d24e6cc2fa4321bb3431718d5d2a6d4a6ef45d26a87c46aaa95b2d058cc",
            "f147078e93f753f24b7e04c7485059943a3038cab6b17be93f8c5807072cad15",
            "641c0b730d3b739491e0b89b3164b854584a8b22529116ece70806e43de62481",
            "378124141e9740365f249ae7d8c3e4fb86c39df91b33a9c8317e782333c5dd17",
            "103dbc9bb252b8a61e5133fe04c86af9a22b8fa2cb865d7983964bd79c97e025",
            "8832230097547a7cd2b766197641bcf303d2d5ef6e0feb17eabd7e8a2948d1db",
            "e894bdc2ac7b1143d4323044271e69be71b0430a21b7193ce28af75461259d9a",
            "080035425311ba2707cfe44321b5771a311e835ae63d673b6b8a48af2523ff41",
            "8e777fff38a036980f18587e8036270751422b047a5a400ec2d15c122e95f5f6",
            "fe6a18b02a7492e90a4df8787386e35b4e5f00adee5b572bc2081e6d71803066",
            "e6280b2707a7e157a5057fa4e64ddb74d3db488b17aab72ece52173499278146",
            "b0e8e6b5d4740d61a45b9cea6f4046a2d4b1bcb56a7e3cd13725bc153644b448",
            "23423974f8d0478cc861840e03ecc80b68bfb1d9dde050bfe9a469104d52e1cf",
            "862806fd064fa3ab004fb0aa75801e487e998b88e6d68bef7f87506eb096f546",
            "272a5165c8be11b64909f37e6db88be60a510a295cb1247365cdf37a5999b4ac",
            "6f9abd6edd2810294b0a4092863ece543ee165b1a36a906c01e505183ce53edf",
            "6a39db13acdfaba0508dec7b6558c7042ec91f68f85937826179b60d0de3a9df",
            "82103f5b1d864aa3ebf4475873c5b7c6d9d26f6b6baafec58972bfb9ac5b6bbc",
            "f129774208b138626b88b53cc10dc09fffc43c551685d978c96f9ec176f8f92c",
            "3ed9cc800d6e0660aef482a2761394455db39da6198797cca30961acec2ebbb4",
            "a81b9f3b47ef231e35f2a81d931a1aec08afbf07a47fa42ffb94809401c93ae2",
            "27896a40db18d01f7f67e75ed2f0a38a40443eed5cef29940894cc5bcd0117f9",
            "73f5f21bdb351807168ab7f750312ce98374d351236aa55ccbfecda4a3005152",
            "2d33f3cdb09f06803062e2543bd5206eabd0fc81e13d86942e7734316498fd08",
            "09f5679641d5d21925d0f49396831be81cba5a513343bf0991576b5140394526",
            "ef9d55a3768973bba29248ab23408151a800793608c423673782fcc82949b3e1",
            "8a2c5586af0b7ab9219404cec41843e74a608a4d37cb6dd59f272de067667cd5",
            "a14a2cab60fbf96e02e7c7b8f60585a33f82601009c4f95cb75cd81b4a4574da",
            "66b281ecbe3bfaf2d10b787461994449a04b0ed225056692079ccbf3235f89a7",
            "8f0c0dc38aab16258dfa8652691c09635288ff4cd51b30133282f48244369746",
            "290b4f8f2452fe33544203697d5e64a2cafb654fdcc57424c0b5b862843d1fbf",
            "b26d36308335e8cb5f9361afa762236ca5960fdf10fffaab1199c45421e9f541",
            "6505ea56d54c8ab890a88302a3eda655120dc5f6f7d57776a5f2557bc245c697"};
        static readonly int[] ExpectedPlants={170,175,510,497,56,53,176,162,42,44,33,36,48,46,74,69,67,68,48,49,1,1,1};
        static readonly string[] ExpectedPlantHashes={
            "644d2ae8de1859c5ae68caf62ce83aca1fcc97a9bb4d9ac5fb27e2c1fe9706a2",
            "884b616e1b4dd3055377c0fb8531d8d76a1daae425aaa26fce9d9df9b1825f68",
            "71ac1a662f62b8ac4f5038e3bdb89277dde386987d8026138103bd3f955667bc",
            "994f55195ed0970e5e352cebc2df9da2a40ddff62b9d05e8f37638b484d7278a",
            "36f90bc8750a6bedd8a948934597bbe9d3eef1f86828f0de4de2ab303b23a2c2",
            "e02ad562a9877ace80ac544425c7a6f50e138749e38f059fd8ccb9db239dd151",
            "d0bb0a335c43217ebb0489f26ea6cbdc484b923da3c02f65800e5fb771c5d6b8",
            "5ecee524efc2b4de975c7fb5ea13ce5bd74346fcde2f9c14445cb5f62b856706",
            "01946273354451c5fd4f390353a3ce91dc3c7df76da6ce1e08cae7f13bd9e708",
            "05bdaacc4a7e652d5ec4b7b209e268832e33447e517a76c246cbd0edc6b2ac24",
            "e5c415c8efe00c0d9e93fd58c770c501ff79a76c5e086d87cc4c495660217e38",
            "e4e9b8ae0325a71512c8b68f788eb3859f54c1d4a9137ac1e0a9da7dfe8c4adb",
            "b447652da5e181ca29c0477750c3df4f764342da1ddbf023a422cc3418c34f77",
            "687f80623313e3197a97ff07737536b2ca985f418e37c8e9ae52767f587cbcc9",
            "42ab56aefdac13dc6b8e525b7c42c9698aec3c7729c6b44dbe6c444169656f11",
            "682ec92eb16bf42cead167893f93b6b04fa3ecd6c7d30d3e7992366e88815a9b",
            "b326637e9519518dd612d79cd0b4ce875cb19cc599a97d7437808fe26ad81a34",
            "7800c209ced840c26cef8d868bc3e7632e530b9e55306058940741b1ac5bc2b6",
            "5f742ad88905ffe96f44939859f96a09ead73fa9e9a02efd8230549fec22a242",
            "af02b1e19609342fe7c0aba1f0b08d125f750274e7f899cafa03eaf7edb0474c",
            "ee3804c27da248f500cfdca719c9e20ee8771b31741c65f2e2d108779cc6de4c",
            "9d23d1137fb5d074465cd1246e93448a9f3ac1d71ce62e65e78fbaefaa8827ef",
            "48be3dd27482b953f5fc39ddfd19a9ff050540686b74b7547d41711977494261"};
        static readonly object Gate=new object();
        static Task<Prepared> pending;
        static int pendingId=-1;
        // Completed immutable templates, least-recently-used first. Bounded by total cells so a
        // walk through a mixed forest reuses replays instead of regenerating each species.
        const long CacheCells=3000000;const int CacheEntries=24;
        static readonly System.Collections.Generic.List<Prepared> cache=new System.Collections.Generic.List<Prepared>();
        static long cachedCells;
        public static int CachedTemplates{get{lock(Gate)return cache.Count;}}
        public static long CachedCells{get{lock(Gate)return cachedCells;}}
        public static bool Busy{get{lock(Gate)return pending!=null&&!pending.IsCompleted;}}
        /// <summary>Cells in the complete specimen (known before generation).</summary>
        public static int ExpectedCells(int id)=>!Supports(id)?0:id<16?ExpectedTrees[id]:id<200?ExpectedPlants[id-100]:ExpectedRocks[id-200];
        /// <summary>Giant specimens (mature redwoods, ~1.1M cells) get memory guards and are never warmed in parallel.</summary>
        public static bool IsHuge(int id)=>ExpectedCells(id)>200000;
        /// <summary>Optional folder for replay results. A giant redwood takes ~4 s to regenerate; the
        /// cached replay loads in a fraction of that and is re-verified against the same pinned hash.</summary>
        public static string DiskCacheDirectory;
        public static bool Supports(int id)=>(id>=0&&id<16)||(id>=100&&id<=122)||(id>=200&&id<260);
        public static bool IsCached(int id){lock(Gate){foreach(var p in cache)if(p.PrototypeId==id)return true;return false;}}
        static void Store(Prepared prepared)
        {
            lock(Gate)
            {
                for(int i=0;i<cache.Count;i++)if(cache[i].PrototypeId==prepared.PrototypeId){cachedCells-=cache[i].CellCount;cache.RemoveAt(i);break;}
                if(prepared.CellCount>CacheCells)return;
                cache.Add(prepared);cachedCells+=prepared.CellCount;
                while(cache.Count>CacheEntries||cachedCells>CacheCells){cachedCells-=cache[0].CellCount;cache.RemoveAt(0);}
            }
        }
        // One admitted build at a time; completed templates come from the cache immediately.
        public static bool TryRequest(int id,out Task<Prepared> task)
        {
            if(!Supports(id))throw new ArgumentOutOfRangeException(nameof(id));
            lock(Gate)
            {
                for(int i=0;i<cache.Count;i++)if(cache[i].PrototypeId==id)
                {var hit=cache[i];cache.RemoveAt(i);cache.Add(hit);task=Task.FromResult(hit);return true;}
                if(pending!=null&&pendingId==id&&!pending.IsFaulted){task=pending;return true;}
                // A background warm-up never blocks a specimen the player actually punched.
                if(pending!=null&&!pending.IsCompleted&&(!pendingPrewarm||IsHuge(pendingId)||IsHuge(id))){task=null;return false;}
                pendingId=id;pendingPrewarm=false;pending=Task.Run(()=>{var p=Prepare(id);Store(p);return p;});task=pending;return true;
            }
        }
        static bool pendingPrewarm;
        /// <summary>Background warm-up for a specimen the player is likely to break. Never waits.</summary>
        public static bool Prewarm(int id)
        {
            if(!Supports(id)||IsCached(id))return false;
            lock(Gate)
            {
                if(pending!=null&&!pending.IsCompleted)return false;
                foreach(var p in cache)if(IsHuge(p.PrototypeId)&&IsHuge(id))return false;
                pendingId=id;pendingPrewarm=true;pending=Task.Run(()=>{var p=Prepare(id);Store(p);return p;});
                pending.ContinueWith(t=>{var observed=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);return true;
            }
        }
        public sealed class Prepared
        {
            readonly Int3[] positions;readonly MaterialId[] materials;readonly uint[] tints;
            readonly int sx,sy,sz,seed;
            public readonly Int3 Anchor;internal readonly TreeResult Result;public readonly int PrototypeId;
            public int CellCount=>positions.Length;
            public readonly string MatterIdentity;
            static void Put(byte[] bytes,int offset,uint value){for(int i=0;i<4;i++)bytes[offset+i]=(byte)(value>>(i*8));}
            internal Prepared(int id,VoxelWorld world,Int3 anchor,TreeResult result)
            {
                PrototypeId=id;Anchor=anchor;Result=result;sx=world.SizeX;sy=world.SizeY;sz=world.SizeZ;seed=world.Seed;
                int count=0;foreach(var c in world.Chunks.Values)count=checked(count+c.NonAirCount);
                if(count!=(id<16?ExpectedTrees[id]:id<200?ExpectedPlants[id-100]:ExpectedRocks[id-200])||count>MaximumCells)throw new InvalidOperationException("Complete specimen exceeds matter admission limit");
                positions=new Int3[count];materials=new MaterialId[count];tints=new uint[count];int cursor=0;
                using(var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                // Same byte stream as before (17 bytes per cell), hashed in 64 KiB blocks.
                var bytes=new byte[17*3855];int filled=0;
                foreach(var pair in world.Chunks.OrderBy(p=>p.Key.z).ThenBy(p=>p.Key.y).ThenBy(p=>p.Key.x))
                {
                var cells=pair.Value.Cells;bool tinted=pair.Value.HasTints;var chunkTints=pair.Value.Tints;
                for(int i=0;i<Chunk.Volume;i++)
                {
                    var material=cells[i];if(material==MaterialId.Air)continue;var local=Chunk.Decode(i);
                    var p=new Int3(pair.Key.x*16+local.x,pair.Key.y*16+local.y,pair.Key.z*16+local.z);
                    uint tint=tinted?chunkTints[i]:0u;
                    positions[cursor]=p;materials[cursor]=material;tints[cursor++]=tint;
                    Put(bytes,filled,(uint)p.x);Put(bytes,filled+4,(uint)p.y);Put(bytes,filled+8,(uint)p.z);bytes[filled+12]=(byte)material;Put(bytes,filled+13,tint);filled+=17;
                    if(filled==bytes.Length){hash.AppendData(bytes,0,filled);filled=0;}
                }
                }
                if(filled>0)hash.AppendData(bytes,0,filled);
                MatterIdentity=BitConverter.ToString(hash.GetHashAndReset()).Replace("-","").ToLowerInvariant();
                }
                if(MatterIdentity!=(id>=100&&id<200?ExpectedPlantHashes[id-100]:ExpectedMatterHashes[id<16?id:id-200+16]))throw new InvalidOperationException("Authoritative generator replay differs from pinned complete specimen; original remains intact");
                if(cursor!=count)throw new InvalidOperationException("Matter count mismatch");
            }
            internal void Write(System.IO.BinaryWriter w)
            {
                w.Write(sx);w.Write(sy);w.Write(sz);w.Write(seed);w.Write(Anchor.x);w.Write(Anchor.y);w.Write(Anchor.z);w.Write(positions.Length);
                for(int i=0;i<positions.Length;i++){var p=positions[i];w.Write((ushort)p.x);w.Write((ushort)p.y);w.Write((ushort)p.z);w.Write((byte)materials[i]);w.Write(tints[i]);}
            }
            public Task<VoxelWorld> CreateWorldAsync()=>Task.Run(()=>{Int3 ignored;return CreateWorld(out ignored);});
            public VoxelWorld CreateWorld(out Int3 anchor)=>CreateWorld(out anchor,int.MinValue,0);
            internal VoxelWorld CreateWorld(out Int3 anchor,int minimumRelativeY,int verticalShift)
            {
                if(verticalShift<0)throw new ArgumentOutOfRangeException(nameof(verticalShift));
                anchor=new Int3(Anchor.x,checked(Anchor.y+verticalShift),Anchor.z);var world=new VoxelWorld(sx,checked(sy+verticalShift),sz,.1f,seed);
                for(int i=0;i<positions.Length;i++){var p=positions[i];if(p.y-Anchor.y>=minimumRelativeY)world.Set(p.x,p.y+verticalShift,p.z,materials[i],tints[i]);}
                return world;
            }
        }
        static string CachePath(int id)
        {
            string dir=DiskCacheDirectory;if(string.IsNullOrEmpty(dir))return null;
            string hash=id>=100&&id<200?ExpectedPlantHashes[id-100]:ExpectedMatterHashes[id<16?id:id-200+16];
            return System.IO.Path.Combine(dir,"matter-"+id+"-"+hash.Substring(0,16)+".avmt");
        }
        public static Prepared Prepare(int id)
        {
            if(!Supports(id))throw new ArgumentOutOfRangeException(nameof(id));
            string path=ExpectedCells(id)>=20000?CachePath(id):null;
            if(path!=null&&System.IO.File.Exists(path))
            {
                try{return Load(id,path);}
                catch(Exception){try{System.IO.File.Delete(path);}catch(Exception){}}
            }
            var generated=Generate(id);
            if(path!=null){try{Save(generated,path);}catch(Exception){/* cache is optional */}}
            return generated;
        }
        const string CacheMagic="AVMT0001";
        static void Save(Prepared p,string path)
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));string temp=path+".tmp";
            using(var file=System.IO.File.Create(temp))using(var gz=new System.IO.Compression.GZipStream(file,System.IO.Compression.CompressionLevel.Fastest))using(var w=new System.IO.BinaryWriter(gz))
            {
                w.Write(System.Text.Encoding.ASCII.GetBytes(CacheMagic));w.Write(p.PrototypeId);p.Write(w);
            }
            if(System.IO.File.Exists(path))System.IO.File.Delete(path);System.IO.File.Move(temp,path);
        }
        static Prepared Load(int id,string path)
        {
            using var file=System.IO.File.OpenRead(path);using var gz=new System.IO.Compression.GZipStream(file,System.IO.Compression.CompressionMode.Decompress);using var r=new System.IO.BinaryReader(gz);
            if(System.Text.Encoding.ASCII.GetString(r.ReadBytes(8))!=CacheMagic||r.ReadInt32()!=id)throw new System.IO.InvalidDataException("Template cache mismatch");
            int sx=r.ReadInt32(),sy=r.ReadInt32(),sz=r.ReadInt32(),seed=r.ReadInt32();var anchor=new Int3(r.ReadInt32(),r.ReadInt32(),r.ReadInt32());int count=r.ReadInt32();
            if(sx<1||sy<1||sz<1||sx>4096||sy>4096||sz>4096||count!=ExpectedCells(id))throw new System.IO.InvalidDataException("Template cache bounds");
            var world=new VoxelWorld(sx,sy,sz,.1f,seed);
            for(int i=0;i<count;i++){int x=r.ReadUInt16(),y=r.ReadUInt16(),z=r.ReadUInt16();var m=(MaterialId)r.ReadByte();uint tint=r.ReadUInt32();if(!world.InBounds(x,y,z)||m==MaterialId.Air)throw new System.IO.InvalidDataException("Template cache cell");world.Set(x,y,z,m,tint);}
            // The constructor recounts and re-hashes every cell against the pinned identity.
            return new Prepared(id,world,anchor,null);
        }
        static Prepared Generate(int id)
        {
            int seed=(id<16||id>=100&&id<120)&&id%2==1?271828:714103;VoxelWorld world;Int3 anchor;TreeResult result=null;
            if(id<16)
            {
                var species=(TreeSpecies)(id/4);int stage=id%4/2;
                var p=TreePresets.Get(species).With(age:stage==0?(species==TreeSpecies.Redwood?.32f:.45f):1f);
                double reach=p.CrownRadius*Math.Pow(p.Age,.85)*1.5+p.LeafClusterRadius*Math.Pow(p.Age,.65)*3+p.TrunkRadius*Math.Pow(p.Age,1.15)*(1.7+p.RootFlare*1.1)+3;
                int side=(int)Math.Ceiling(reach*2/.1);if(side%2!=0)side++;
                int height=(int)Math.Ceiling((p.Height*p.Age*1.25+4)/.1);
                world=new VoxelWorld(side,height,side,.1f,seed);anchor=new Int3(side/2,4,side/2);
                result=TreeGenerator.Generate(world,p,anchor.x*.1f,anchor.y*.1f,anchor.z*.1f,seed,new TreeGenerationBudget(8192,160000000,16384));
                if(result.Truncated)throw new InvalidOperationException("Truncated tree refused intact: "+result.TruncationReason);
            }
            else if(id<200)
            {
                bool micro=id>=120;world=new VoxelWorld(micro?16:80,micro?16:64,micro?16:80,.1f,seed);anchor=new Int3(micro?8:40,4,micro?8:40);
                PlantProfile p;
                if(!micro){var kind=(PlantKind)((id-100)/2);p=PlantPresets.For(kind);if(kind==PlantKind.Shrub)p=p.With(fruitProbability:.35);}
                else {var kind=id==120?PlantKind.Mushroom:id==121?PlantKind.BroadleafHerb:PlantKind.Flower;p=PlantPresets.For(kind).With(heightMeters:.1,radiusMeters:.05,stems:1);}
                var r=PlantGenerator.Generate(world,p,anchor,seed);if(r.Truncated)throw new InvalidOperationException("Truncated plant refused intact");
            }
            else
            {
                MaterialId[] rocks={MaterialId.Stone,MaterialId.Granite,MaterialId.Limestone,MaterialId.Sandstone,MaterialId.Shale};
                int material=(id-200)/12,shape=(id-200)%12/2+1,size=(id-200)%2;
                world=new VoxelWorld(80,64,80,.1f,seed);anchor=new Int3(40,4,40);
                var p=RockPresets.For(rocks[material],seed).With(shape:(RockShape)shape,widthMeters:size==0?.8:2,heightMeters:(shape==2?.45:shape==4?2.7:1.25)*(size==0?.4:1),depthMeters:(shape==2?1.25:1.8)*(size==0?.4:1));
                var r=RockGenerator.Generate(world,anchor,p);if(r.Truncated||r.ClippedCells>0)throw new InvalidOperationException("Truncated rock refused intact");
            }
            return new Prepared(id,world,anchor,result);
        }
        // Compatibility for editor/CPU probes; gameplay uses TryRequest and polls completion.
        public static VoxelWorld Replay(int id,out Int3 anchor,out TreeResult result)
        {var prepared=Prepare(id);result=prepared.Result;return prepared.CreateWorld(out anchor);}
    }
}
