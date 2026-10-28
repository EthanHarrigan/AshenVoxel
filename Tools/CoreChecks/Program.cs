using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using AshenVoxel.Core;
static class Program
{
    static int count;
    static void Check(bool condition,string name){if(!condition)throw new Exception("FAIL: "+name);count++;Console.WriteLine("PASS "+name);}
    static byte[] Save(VoxelWorld w){using(var m=new MemoryStream()){Persistence.Save(m,w);return m.ToArray();}}
    static int NonAir(VoxelWorld w){int n=0;foreach(var c in w.Chunks.Values)n+=c.NonAirCount;return n;}
    static void Reject(byte[] bytes,string name){try{Persistence.Load(new MemoryStream(bytes));throw new Exception("FAIL: accepted "+name);}catch(InvalidDataException){count++;Console.WriteLine("PASS "+name);}}
    sealed class NonSeekableWriteStream : Stream
    {
        readonly MemoryStream buffer=new MemoryStream();
        readonly int failAfter;
        public bool WasDisposed {get;private set;}
        public NonSeekableWriteStream(int failAfter=int.MaxValue){this.failAfter=failAfter;}
        public byte[] Bytes=>buffer.ToArray();
        public override bool CanRead=>false;public override bool CanWrite=>true;public override bool CanSeek=>false;
        public override long Length=>throw new NotSupportedException();
        public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override void Flush(){}
        public override void Write(byte[] bytes,int offset,int count){if(buffer.Length+count>failAfter)throw new IOException("Injected output failure");buffer.Write(bytes,offset,count);}
        public override int Read(byte[] bytes,int offset,int count)=>throw new NotSupportedException();
        public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
        public override void SetLength(long value)=>throw new NotSupportedException();
        protected override void Dispose(bool disposing){WasDisposed=true;base.Dispose(disposing);}
    }
    static uint TestCrc(ReadOnlySpan<byte> bytes)
    {uint crc=0xffffffff;foreach(byte value in bytes){crc^=value;for(int bit=0;bit<8;bit++)crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0u);}return ~crc;}
    static void RepairChecksum(byte[] bytes)
    {int length=BitConverter.ToInt32(bytes,8);byte[] crc=BitConverter.GetBytes(TestCrc(new ReadOnlySpan<byte>(bytes,12,length)));Array.Copy(crc,0,bytes,12+length,4);}
    static byte[] LegacySnapshot(VoxelWorld world,int generator=3)
    {
        using(var payload=new MemoryStream())
        {
            using(var p=new BinaryWriter(payload,System.Text.Encoding.UTF8,true))
            {
                p.Write(world.SizeX);p.Write(world.SizeY);p.Write(world.SizeZ);p.Write(world.VoxelSize);p.Write(world.Seed);p.Write(generator);p.Write(world.Chunks.Count);
                var keys=new List<Int3>(world.Chunks.Keys);keys.Sort((a,b)=>{int c=a.z.CompareTo(b.z);if(c==0)c=a.y.CompareTo(b.y);return c==0?a.x.CompareTo(b.x):c;});
                foreach(Int3 key in keys){p.Write(key.x);p.Write(key.y);p.Write(key.z);foreach(var material in world.Chunks[key].Cells)p.Write((byte)material);}
            }
            byte[] bytes=payload.ToArray();using(var output=new MemoryStream())using(var writer=new BinaryWriter(output))
            {writer.Write(0x4158564cu);writer.Write(1);writer.Write(bytes.Length);writer.Write(bytes);writer.Write(TestCrc(bytes));return output.ToArray();}
        }
    }
    static void Main()
    {
        var w=new VoxelWorld(33,24,35,.1f,42);
        Check(w.Get(-1,0,0)==MaterialId.Air&&w.Chunks.Count==0,"outside air and lazy allocation");
        Check(!w.Set(-1,2,2,MaterialId.Stone),"outside write rejected");
        w.Set(15,15,15,MaterialId.Stone);
        Check(w.Chunks.Count==1&&w.Get(15,15,15)==MaterialId.Stone,"chunk addressing");
        Check(w.DirtyChunks.Contains(new Int3(1,0,0))&&w.DirtyChunks.Contains(new Int3(0,1,0))&&w.DirtyChunks.Contains(new Int3(0,0,1)),"all positive boundary neighbors invalidated");
        Check(w.DirtyChunks.Count==8&&w.DirtyChunks.Contains(new Int3(1,1,0))&&w.DirtyChunks.Contains(new Int3(1,0,1))&&w.DirtyChunks.Contains(new Int3(0,1,1))&&w.DirtyChunks.Contains(new Int3(1,1,1)),"positive corner edit invalidates complete eight-chunk AO halo");
        w.ClearDirty();w.Set(16,16,16,MaterialId.Wood);
        Check(w.DirtyChunks.Contains(new Int3(0,1,1))&&w.DirtyChunks.Contains(new Int3(1,0,1))&&w.DirtyChunks.Contains(new Int3(1,1,0)),"all negative boundary neighbors invalidated");
        Check(w.DirtyChunks.Count==8&&w.DirtyChunks.Contains(new Int3(0,0,0))&&w.DirtyChunks.Contains(new Int3(0,0,1))&&w.DirtyChunks.Contains(new Int3(1,0,0))&&w.DirtyChunks.Contains(new Int3(0,1,0)),"negative corner edit invalidates complete eight-chunk AO halo");
        var haloWorld=new VoxelWorld(48,48,48,.1f,1);
        haloWorld.Set(15,7,15,MaterialId.Stone);
        Check(haloWorld.DirtyChunks.Count==4&&haloWorld.DirtyChunks.Contains(new Int3(1,0,1)),"edge edit invalidates four chunks including diagonal AO neighbor");
        haloWorld.ClearDirty();int changeEvents=0;haloWorld.Changed+=position=>changeEvents++;haloWorld.Set(8,8,8,MaterialId.Stone);
        Check(haloWorld.DirtyChunks.Count==1&&haloWorld.DirtyChunks.Contains(new Int3(0,0,0))&&changeEvents==1,"interior edit dirties only owner and emits one origin event");
        haloWorld.ClearDirty();haloWorld.Set(0,0,0,MaterialId.Stone);
        Check(haloWorld.DirtyChunks.Count==1,"AO halo invalidation clips negative finite-domain neighbors");
        long revision=w.Revision;Check(!w.Set(16,16,16,MaterialId.Wood)&&w.Revision==revision,"idempotent write");
        w.ClearDirty();w.Set(15,15,15,MaterialId.Air);Check(w.Chunks.Count==1&&w.DirtyChunks.Contains(new Int3(0,0,0)),"empty chunk reclaimed and invalidated");
        Check(VoxelWorld.FloorDiv(-1,16)==-1&&VoxelWorld.FloorDiv(-16,16)==-1&&VoxelWorld.FloorDiv(-17,16)==-2,"negative floor division");
        var a=new VoxelWorld(10,10,10,1,1);
        using(var simulation=new Simulation(a))
        {
            a.Set(5,7,5,MaterialId.Sand);
            int before=NonAir(a);simulation.Step(100);
            Check(a.Get(5,6,5)==MaterialId.Sand&&a.Get(5,5,5)==MaterialId.Air,"one grain moves at most one cell per tick");
            for(int i=0;i<20;i++)simulation.Step(100);
            Check(NonAir(a)==before&&a.Get(5,1,5)==MaterialId.Sand,"gravity conserves mass and preserves floor");
            Check(simulation.ActiveCount==0,"settled grains sleep");
            a.Set(7,4,7,MaterialId.Wood);simulation.Step(100);
            Check(a.Get(7,4,7)==MaterialId.Wood,"solid structure does not falsely act granular");
            a.Set(3,4,3,MaterialId.Sand);simulation.Step(0);Check(simulation.LastProcessed==0&&a.Get(3,4,3)==MaterialId.Sand,"zero budget does no work");
            simulation.Step(1);Check(simulation.LastProcessed<=1,"strict candidate budget");
        }
        var brush=new VoxelWorld(8,8,8,1,2);
        brush.Set(3,3,3,MaterialId.Stone);brush.Set(4,3,3,MaterialId.Dirt);brush.Set(3,0,3,MaterialId.Stone);
        Check(WorldEdits.CarveSphere(brush,4,3.5f,3.5f,1,1)==1&&brush.Get(3,3,3)==MaterialId.Stone,"brush material hardness");
        WorldEdits.CarveSphere(brush,3.5f,.5f,3.5f,1,100);Check(brush.Get(3,0,3)==MaterialId.Stone,"brush protects bedrock");
        Check(WorldEdits.PlaceSphere(brush,3.5f,3.5f,3.5f,.6f,MaterialId.Wood)==0,"placement preserves existing solids");
        var ray=new VoxelWorld(8,8,8,1,0);ray.Set(4,3,3,MaterialId.Stone);
        VoxelHit hit;
        Check(VoxelRaycast.TryCast(ray,0,3.5f,3.5f,2,0,0,10,out hit)&&hit.Cell==new Int3(4,3,3)&&hit.Previous==new Int3(3,3,3)&&Math.Abs(hit.Distance-4)<1e-5,"DDA axis ray normalized distance");
        Check(VoxelRaycast.TryCast(ray,10,3.5f,3.5f,-1,0,0,10,out hit)&&hit.Cell==new Int3(4,3,3)&&Math.Abs(hit.Distance-5)<1e-5,"DDA enters finite domain from outside");
        Check(!VoxelRaycast.TryCast(ray,0,3.5f,3.5f,1,0,0,3,out hit),"DDA maximum reach");
        Check(!VoxelRaycast.TryCast(ray,0,0,0,0,0,0,10,out hit),"DDA zero direction");
        Check(!VoxelRaycast.TryCast(ray,float.NaN,0,0,1,0,0,10,out hit),"DDA rejects NaN");
        ray.Set(7,2,2,MaterialId.Wood);
        Check(VoxelRaycast.TryCast(ray,8,2.5f,2.5f,-1,0,0,10,out hit)&&hit.Cell==new Int3(7,2,2)&&hit.Previous==new Int3(8,2,2),"DDA max boundary and previous air cell");
        Check(!VoxelRaycast.TryCast(ray,4,3.5f,3.5f,-1,0,0,3,out hit),"DDA boundary facing away ignores outward solid");
        for(int i=0;i<100;i++)VoxelRaycast.TryCast(ray,0,3.5f,3.5f,1,0,0,10,out hit);
        long allocatedBefore=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++)VoxelRaycast.TryCast(ray,0,3.5f,3.5f,1,0,0,10,out hit);
        long rayAllocated=GC.GetAllocatedBytesForCurrentThread()-allocatedBefore;
        Check(rayAllocated==0,"ten thousand warmed DDA picks allocate zero managed bytes");
        byte[] snapshot=Save(w);var restored=Persistence.Load(new MemoryStream(snapshot));
        Check(restored.SizeX==33&&restored.SizeZ==35&&restored.Seed==42&&restored.Get(16,16,16)==MaterialId.Wood,"snapshot round trip metadata and cells");
        Check(Convert.ToBase64String(snapshot)==Convert.ToBase64String(Save(restored)),"snapshot stable serialization");
        byte[] legacy=LegacySnapshot(w);
        Check(Convert.ToHexString(SHA256.HashData(legacy))=="1937460F92D2915CDE73D14AB368964F34FE97D4F229764F2C5DAF475AB9976E","historical Format1 fixture retains original golden bytes");
        var migrated=Persistence.Load(new MemoryStream(legacy));
        Check(migrated.Get(16,16,16)==MaterialId.Wood&&migrated.GetTint(16,16,16)==0&&migrated.GetColor(16,16,16)==VoxelColor.Base(MaterialId.Wood)&&BitConverter.ToInt32(Save(migrated),4)==2,"Format1 loads default appearance and migrates to Format2 without losing cells");
        bool historical=true;for(int gen=1;gen<=Generation.Version;gen++)historical&=Persistence.Load(new MemoryStream(LegacySnapshot(w,gen))).Get(16,16,16)==MaterialId.Wood;
        Check(historical,"self-contained snapshots accept historical and current generator versions");
        bool format2Historical=true;for(int gen=1;gen<=Generation.Version;gen++){byte[] historical2=(byte[])snapshot.Clone();Array.Copy(BitConverter.GetBytes(gen),0,historical2,32,4);RepairChecksum(historical2);format2Historical&=Persistence.Load(new MemoryStream(historical2)).Get(16,16,16)==MaterialId.Wood;}
        Check(format2Historical,"Format2 migration also accepts self-contained historical generator versions");
        var earliest=Persistence.Load(new MemoryStream(LegacySnapshot(w,1)));
        Check(earliest.GenerationVersion==1&&BitConverter.ToInt32(Save(earliest),32)==1,"resaving a historical world preserves generator provenance without relabeling its cells");
        using(var nonseek=new NonSeekableWriteStream())
        {Persistence.Save(nonseek,w);Check(!nonseek.WasDisposed&&Convert.ToBase64String(nonseek.Bytes)==Convert.ToBase64String(snapshot),"snapshot writes exact bytes to nonseekable stream and leaves caller stream open");}
        using(var broken=new NonSeekableWriteStream(100))
        {
            bool writeFailed=false;try{Persistence.Save(broken,w);}catch(IOException){writeFailed=true;}
            Check(writeFailed&&!broken.WasDisposed,"snapshot propagates output failure without disposing caller stream");
            Reject(broken.Bytes,"failed streaming write cannot load as complete snapshot");
        }
        bool readonlyRejected=false;using(var readOnly=new MemoryStream(new byte[0],false)){try{Persistence.Save(readOnly,w);}catch(ArgumentException){readonlyRejected=true;}}
        Check(readonlyRejected,"snapshot rejects nonwritable output before serialization");
        byte[] corrupt=(byte[])snapshot.Clone();corrupt[40]^=0x10;Reject(corrupt,"snapshot corruption rejected");
        byte[] truncated=new byte[snapshot.Length-1];Array.Copy(snapshot,truncated,truncated.Length);Reject(truncated,"snapshot truncation rejected");
        byte[] trailing=new byte[snapshot.Length+1];Array.Copy(snapshot,trailing,snapshot.Length);Reject(trailing,"snapshot trailing bytes rejected");
        byte[] version=(byte[])snapshot.Clone();version[4]=99;Reject(version,"snapshot future version rejected");
        byte[] futureGenerator=(byte[])snapshot.Clone();Array.Copy(BitConverter.GetBytes(Generation.Version+1),0,futureGenerator,32,4);RepairChecksum(futureGenerator);Reject(futureGenerator,"snapshot future generator rejected even with a valid checksum");
        var colored=new VoxelWorld(32,32,32,1,81);uint red=VoxelColor.Pack(220,40,65),green=VoxelColor.Pack(34,109,65);
        colored.Set(15,15,15,MaterialId.Wood,red);
        Check(red==0xffdc2841u&&VoxelColor.Pack(0,0,0)==0xff000000u&&colored.GetColor(15,15,15)==red,"packed appearance is opaqueRGB including explicit black");
        Check(colored.Chunks[new Int3(0,0,0)].HasTints&&colored.Chunks[new Int3(0,0,0)].Tints.Length==Chunk.Volume,"appearance storage allocated only for tinted chunks");
        long colorRevision=colored.Revision;colored.ClearDirty();int materialEvents=0,appearanceEvents=0;colored.Changed+=p=>materialEvents++;colored.AppearanceChanged+=p=>appearanceEvents++;
        colored.Set(15,15,15,MaterialId.Wood,green);
        Check(colored.Revision==colorRevision+1&&colored.DirtyChunks.Count==8&&materialEvents==0&&appearanceEvents==1,"color-only edits invalidate full AO halo without waking material physics");
        colorRevision=colored.Revision;
        Check(!colored.Set(15,15,15,MaterialId.Wood)&&colored.GetTint(15,15,15)==green&&colored.Revision==colorRevision,"material-only idempotent edit preserves existing tint");
        var coloredRestored=Persistence.Load(new MemoryStream(Save(colored)));
        Check(coloredRestored.GetTint(15,15,15)==green&&coloredRestored.GetColor(15,15,15)==green&&coloredRestored.Chunks[new Int3(0,0,0)].HasTints,"Format2 preserves raw and effective voxel appearance");
        Check(Convert.ToBase64String(Save(coloredRestored))==Convert.ToBase64String(Save(colored)),"colored Format2 snapshots serialize stably after load");
        using(var nonseekColored=new NonSeekableWriteStream()){Persistence.Save(nonseekColored,colored);Check(Convert.ToBase64String(nonseekColored.Bytes)==Convert.ToBase64String(Save(colored)),"optional appearance channels stream identically to nonseekable destinations");}
        byte[] invalidColor=Save(colored);invalidColor[4149+4095*4+3]=0x7f;RepairChecksum(invalidColor);Reject(invalidColor,"Format2 rejects nonopaque appearance even with valid checksum");
        byte[] airColor=Save(colored);Array.Copy(BitConverter.GetBytes(red),0,airColor,4149,4);RepairChecksum(airColor);Reject(airColor,"Format2 rejects appearance attached to air");
        byte[] badChannels=Save(colored);badChannels[52]=2;RepairChecksum(badChannels);Reject(badChannels,"Format2 rejects unknown appearance channel flags");
        bool invalidTint=false;try{colored.Set(2,2,2,MaterialId.Wood,0x00112233u);}catch(ArgumentOutOfRangeException){invalidTint=true;}
        Check(invalidTint,"world rejects nonopaque explicit tint");
        colored.Set(15,15,15,MaterialId.Stone);
        Check(colored.GetTint(15,15,15)==0&&colored.GetColor(15,15,15)==VoxelColor.Base(MaterialId.Stone)&&!colored.Chunks[new Int3(0,0,0)].HasTints,"material change resets appearance and releases unused color channel");
        colored.Set(15,15,15,MaterialId.Stone,red);colored.Set(15,15,15,MaterialId.Air);
        Check(colored.GetTint(15,15,15)==0&&colored.GetColor(15,15,15)==0&&colored.Chunks.Count==0,"removal clears appearance and reclaims empty chunk");
        colored.Set(1,1,1,MaterialId.Stone,red);colored.Set(2,1,1,MaterialId.Stone,green);colored.Set(1,1,1,MaterialId.Stone,0);
        Check(colored.GetTint(1,1,1)==0&&colored.GetColor(1,1,1)==VoxelColor.Base(MaterialId.Stone)&&colored.GetTint(2,1,1)==green&&colored.Chunks[new Int3(0,0,0)].HasTints,"explicit default clears only its tint while preserving neighboring appearance");
        colored.Set(2,1,1,MaterialId.Stone,0);
        Check(!colored.Chunks[new Int3(0,0,0)].HasTints&&colored.Chunks[new Int3(0,0,0)].Tints.Length==0,"last cleared tint releases optional channel without removing solids");
        colored.Set(1,1,1,MaterialId.Stone,VoxelColor.Pack(0,0,0));colored.Set(20,20,20,MaterialId.Fruit,red);colored.Set(18,8,4,MaterialId.Wood);
        var mixedColors=Persistence.Load(new MemoryStream(Save(colored)));
        Check(mixedColors.GetColor(1,1,1)==0xff000000u&&mixedColors.GetTint(20,20,20)==red&&mixedColors.GetTint(18,8,4)==0&&mixedColors.GetColor(18,8,4)==VoxelColor.Base(MaterialId.Wood),"mixed chunks preserve explicit black, fruit tint and uncolored material defaults");
        colored.Set(1,1,1,MaterialId.Fruit,red);colored.Clear();Check(colored.Chunks.Count==0&&colored.GetColor(1,1,1)==0,"world clear removes fruit appearance storage");
        Check((int)MaterialId.Fruit==10&&MaterialCatalog.Count==25&&VoxelColor.Base(MaterialId.Fruit)==VoxelColor.Pack(211,78,106),"expanded groundcover catalog preserves historical Fruit ID and appearance");
        Check(MaterialCatalog.Get(MaterialId.Leaves).Opacity<1&&MaterialCatalog.Get(MaterialId.Leaves).Transmission>0&&MaterialCatalog.Get(MaterialId.Stone).Opacity==1,"material visibility and light transmission have independent defaults");
        bool opticsRoundtrip=true;for(int id=0;id<MaterialCatalog.Count;id++)for(int ao=0;ao<4;ao++){byte packed=VertexAppearance.Pack((MaterialId)id,ao);opticsRoundtrip&=VertexAppearance.Material(packed)==id&&VertexAppearance.Occlusion(packed)==64+ao*191/3;}
        Check(opticsRoundtrip,"all material IDs and four AO levels roundtrip independently of intrinsic RGB");
        Check(!MaterialCatalog.BlocksActorBody(MaterialId.Web)&&MaterialCatalog.Get(MaterialId.Web).Solid,"webs remain matter but pass actor bodies");
        var webWorld=new VoxelWorld(16,16,16,.1f,42);webWorld.Set(2,3,4,MaterialId.Web);var webLoaded=Persistence.Load(new MemoryStream(Save(webWorld)));Check(webLoaded.Get(2,3,4)==MaterialId.Web&&webLoaded.GetColor(2,3,4)==VoxelColor.Base(MaterialId.Web),"append-only web ID roundtrips existing Format2 without changing pigment alpha");

        var coloredGrains=new VoxelWorld(8,8,8,1,71);
        using(var sim=new Simulation(coloredGrains))
        {
            coloredGrains.Set(2,6,2,MaterialId.Sand,red);coloredGrains.Set(5,5,5,MaterialId.Dirt,green);
            for(int tick=0;tick<100;tick++)sim.Step(3);
            Check(NonAir(coloredGrains)==2&&coloredGrains.GetTint(2,1,2)==red&&coloredGrains.GetTint(5,1,5)==green&&sim.ActiveCount==0,"granular movement conserves material mass and transports raw color");
            sim.BeginWakeAll();var resaved=Persistence.Load(new MemoryStream(Save(coloredGrains)));
            Check(resaved.GetColor(2,1,2)==red&&resaved.GetColor(5,1,5)==green,"moved voxel appearance survives snapshot round trip");
        }
        var g1=new VoxelWorld(64,64,64,.2f,811);var g2=new VoxelWorld(64,64,64,.2f,811);
        Generation.Generate(g1);Generation.Generate(g2);
        Check(Convert.ToBase64String(Save(g1))==Convert.ToBase64String(Save(g2)),"generation deterministic for identical seed");
        var materials=new HashSet<MaterialId>();foreach(var c in g1.Chunks.Values)foreach(var cell in c.Cells)materials.Add(cell);
        Check(materials.Contains(MaterialId.Stone)&&materials.Contains(MaterialId.Dirt)&&materials.Contains(MaterialId.Grass)&&materials.Contains(MaterialId.Wood)&&materials.Contains(MaterialId.Leaves),"generator includes strata and procedural trees");
        for(int z=0;z<g1.SizeZ;z++)for(int x=0;x<g1.SizeX;x++)if(g1.Get(x,0,z)!=MaterialId.Stone)throw new Exception("Incomplete bedrock");
        Check(true,"continuous generated bedrock");
        var fineWorld=new VoxelWorld(128,128,128,.1f,811);
        bool coherent=true;
        for(int z=0;z<64;z+=3)for(int x=0;x<64;x+=3)
        {
            double coarseHeight=Generation.HeightMeters(g1,x*(double)g1.VoxelSize,z*(double)g1.VoxelSize);
            double fineHeight=Generation.HeightMeters(fineWorld,x*(double)g1.VoxelSize,z*(double)g1.VoxelSize);
            if(Math.Abs(coarseHeight-fineHeight)>1e-10||Math.Abs(Generation.SurfaceHeight(g1,x,z)*g1.VoxelSize-Generation.SurfaceHeight(fineWorld,x*2,z*2)*fineWorld.VoxelSize)>.201)coherent=false;
        }
        Check(coherent,"physical terrain stable across 20cm and 10cm sampling");
        var fine5=new VoxelWorld(512,256,512,.05f,19);var coarse10=new VoxelWorld(256,128,256,.1f,19);
        Check(Math.Abs(Generation.HeightMeters(fine5,8.3,17.2)-Generation.HeightMeters(coarse10,8.3,17.2))<1e-10,"10cm and 5cm default domain share exact continuous height");
        Generation.Generate(fineWorld);
        int coarseGround=Generation.SurfaceHeight(g1,32,32),fineGround=Generation.SurfaceHeight(fineWorld,64,64);
        Check(g1.Get(32,coarseGround-2,32)==fineWorld.Get(64,fineGround-4,64)&&g1.Get(32,coarseGround-5,32)==fineWorld.Get(64,fineGround-10,64),"physical layer identities consistent at matched depths");
        bool profileRejected=false;try{new GenerationProfile(treeSpacing:0);}catch(ArgumentException){profileRejected=true;}
        Check(profileRejected,"generator rejects zero tree spacing");
        var budgetWorld=new VoxelWorld(12,12,12,1,17);
        using(var sim=new Simulation(budgetWorld))
        {for(int x=2;x<10;x++)for(int z=2;z<10;z++)budgetWorld.Set(x,10,z,MaterialId.Sand);int n=NonAir(budgetWorld);for(int i=0;i<1500;i++)sim.Step(17);Check(NonAir(budgetWorld)==n,"many-grain mass conservation across budget boundaries");}
        var singleBudget=new VoxelWorld(12,12,12,1,1234);
        using(var sim=new Simulation(singleBudget))
        {
            for(int x=2;x<10;x++)for(int z=2;z<10;z++)singleBudget.Set(x,9,z,MaterialId.Sand);
            int mass=NonAir(singleBudget),iterations=0;
            while(sim.ActiveCount>0&&iterations<10000){sim.Step(1);if(sim.LastProcessed>1)throw new Exception("Tiny budget violated");iterations++;}
            Check(sim.ActiveCount==0&&NonAir(singleBudget)==mass,"one-candidate budget eventually settles without lost wakeups");

        }
        var wakeWorld=new VoxelWorld(9,9,9,1,7);
        using(var sim=new Simulation(wakeWorld))
        {
            wakeWorld.Set(4,4,4,MaterialId.Stone);wakeWorld.Set(5,5,4,MaterialId.Stone);wakeWorld.Set(3,5,4,MaterialId.Stone);wakeWorld.Set(4,5,5,MaterialId.Stone);wakeWorld.Set(4,5,3,MaterialId.Stone);wakeWorld.Set(4,5,4,MaterialId.Sand);
            for(int i=0;i<10;i++)sim.Step(1);
            Check(sim.ActiveCount==0&&wakeWorld.Get(4,5,4)==MaterialId.Sand,"fully supported grain sleeps under tiny budget");
            wakeWorld.Set(4,4,4,MaterialId.Air);sim.Step(1);
            Check(wakeWorld.Get(4,4,4)==MaterialId.Sand&&sim.LastMoves==1,"support removal wakes sleeping grain under one-candidate budget");
        }
        var midFall=new VoxelWorld(32,24,32,1,123);
        byte[] midFallBytes;
        using(var sim=new Simulation(midFall))
        {
            midFall.Set(4,20,4,MaterialId.Sand);midFall.Set(5,18,4,MaterialId.Dirt);sim.Step(10);
            midFallBytes=Save(midFall);
        }
        var resumed=Persistence.Load(new MemoryStream(midFallBytes));
        using(var sim=new Simulation(resumed))
        {
            int mass=NonAir(resumed);sim.BeginWakeAll();long initial=sim.PendingWakeCandidates;
            Check(initial==resumed.Chunks.Count*(long)Chunk.Volume,"wake scan includes only allocated chunk candidates");
            Check(!sim.WakeBudget(0)&&sim.LastWakeCandidates==0&&sim.PendingWakeCandidates==initial,"zero wake budget does no scan work");
            sim.WakeBudget(7);
            Check(sim.LastWakeCandidates==7&&sim.PendingWakeCandidates==initial-7,"wake scan obeys exact candidate budget");
            int iterations=0;
            while((sim.PendingWakeCandidates>0||sim.ActiveCount>0)&&iterations<10000)
            {sim.WakeBudget(17);if(sim.LastWakeCandidates>17)throw new Exception("Wake budget exceeded");sim.Step(5);iterations++;}
            Check(sim.PendingWakeCandidates==0&&sim.ActiveCount==0&&NonAir(resumed)==mass&&resumed.Get(4,1,4)==MaterialId.Sand,"mid-fall material snapshot resumes to rest after bounded rescan");
        }
        var scanMutation=new VoxelWorld(32,32,32,1,4);scanMutation.Set(1,12,1,MaterialId.Sand);
        using(var sim=new Simulation(scanMutation))
        {
            sim.BeginWakeAll();scanMutation.Set(1,12,1,MaterialId.Air);scanMutation.Set(20,12,20,MaterialId.Sand);
            int n=0;while((sim.PendingWakeCandidates>0||sim.ActiveCount>0)&&n++<10000){sim.WakeBudget(13);sim.Step(3);}
            Check(sim.PendingWakeCandidates==0&&scanMutation.Get(20,1,20)==MaterialId.Sand,"wake scan tolerates deleted chunks and edits in new chunks");
            sim.BeginWakeAll();scanMutation.Clear();
            Check(sim.PendingWakeCandidates==0&&sim.ActiveCount==0&&sim.WakeBudget(1),"world clear cancels pending rescan safely");
        }
        var fixture=new VoxelWorld(256,128,256,.1f,714103);
        var stopwatch=Stopwatch.StartNew();Generation.Generate(fixture);stopwatch.Stop();
        long[] materialCounts=new long[MaterialCatalog.Count];
        foreach(var chunk in fixture.Chunks.Values)foreach(MaterialId id in chunk.Cells)materialCounts[(int)id]++;
        materialCounts[0]=(long)fixture.SizeX*fixture.SizeY*fixture.SizeZ-NonAir(fixture);
        Console.WriteLine($"DEFAULT FIXTURE: generationVersion={fixture.GenerationVersion}; snapshotFormat={Persistence.FormatVersion}; seed={fixture.Seed}; 256x128x256 cells; voxelSize=0.1m; generation={stopwatch.Elapsed.TotalMilliseconds:F1}ms; chunks={fixture.Chunks.Count}; allocatedCellBytes={(long)fixture.Chunks.Count*Chunk.Volume}; occupied={NonAir(fixture)}");
        int tintedChunks=0;foreach(var chunk in fixture.Chunks.Values)if(chunk.HasTints)tintedChunks++;
        Console.WriteLine($"APPEARANCE STORAGE: tintedChunks={tintedChunks}; allocatedTintBytes={(long)tintedChunks*Chunk.Volume*4}");
        for(int m=0;m<materialCounts.Length;m++)Console.WriteLine($"  {(MaterialId)m}: {materialCounts[m]}");
        Persistence.Save(Stream.Null,fixture);
        long saveAllocBefore=GC.GetAllocatedBytesForCurrentThread();Persistence.Save(Stream.Null,fixture);
        long saveAllocated=GC.GetAllocatedBytesForCurrentThread()-saveAllocBefore;
        Check(saveAllocated<128*1024,"default snapshot save auxiliary allocation stays below128KiB without destination buffer");
        Console.WriteLine($"SAVE AUXILIARY ALLOCATION: {saveAllocated} bytes (Stream.Null destination)");
        Check(materialCounts[(int)MaterialId.Wood]>0&&materialCounts[(int)MaterialId.Leaves]>0&&materialCounts[(int)MaterialId.Sand]>0&&materialCounts[(int)MaterialId.Mud]>0,"full default 10cm fixture contains forest, loose sand and wet strata");
        var remainingWood=new HashSet<Int3>();
        foreach(var entry in fixture.Chunks)for(int cell=0;cell<Chunk.Volume;cell++)if(entry.Value.Cells[cell]==MaterialId.Wood)
        {Int3 local=Chunk.Decode(cell);remainingWood.Add(new Int3(entry.Key.x*16+local.x,entry.Key.y*16+local.y,entry.Key.z*16+local.z));}
        Int3[] neighbors={new Int3(1,0,0),new Int3(-1,0,0),new Int3(0,1,0),new Int3(0,-1,0),new Int3(0,0,1),new Int3(0,0,-1)};
        int components=0,groundedComponents=0,branchUndersides=0;
        var flood=new Queue<Int3>();
        while(remainingWood.Count>0)
        {
            Int3 start=default;foreach(Int3 position in remainingWood){start=position;break;}
            remainingWood.Remove(start);flood.Enqueue(start);bool grounded=false;components++;
            while(flood.Count>0)
            {
                Int3 position=flood.Dequeue();MaterialId below=fixture.Get(position.x,position.y-1,position.z);
                if(below==MaterialId.Stone||below==MaterialId.Mud||below==MaterialId.Dirt||below==MaterialId.Litter||below==MaterialId.Grass)grounded=true;
                if((below==MaterialId.Air||below==MaterialId.Leaves)&&position.y-Generation.SurfaceHeight(fixture,position.x,position.z)>8)branchUndersides++;
                foreach(Int3 offset in neighbors){Int3 next=position+offset;if(remainingWood.Remove(next))flood.Enqueue(next);}
            }
            if(grounded)groundedComponents++;
        }
        Check(components>0&&components==groundedComponents,"all default tree wood components connect by six-neighbor paths to terrain");
        Check(branchUndersides>100,"default forest contains substantial elevated lateral branch skeletons");
        Console.WriteLine($"TREE STRUCTURE: groundedComponents={groundedComponents}/{components}; elevatedBranchUndersides={branchUndersides}");
        Console.WriteLine($"All {count} checks passed.");
    }
}
