using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using AshenVoxel.Core;
using CompositeRootCandidate;

static class PolicyChecks
{
    static int checks;static void Check(bool b,string s){checks++;if(!b)throw new Exception(s);}
    static VoxelWorld World()=>new VoxelWorld(470,978,470,.1f,11);
    static RootCell Root(int x,int y,int z,uint color)=>new RootCell(new Int3(x-235,y-4,z-235),color);
    static readonly MethodInfo capture=typeof(CompositeChecks).GetMethod("Capture",BindingFlags.NonPublic|BindingFlags.Static),reference=typeof(CompositeChecks).GetMethod("Reference",BindingFlags.NonPublic|BindingFlags.Static),reduce=typeof(ExportPrototypes).GetMethod("Reduce",BindingFlags.NonPublic|BindingFlags.Static);
    static void Finish(Job j,int r=17,int f=23,int m=1){int steps=0;while(!j.Terminal){j.Step(r,f,m,"policy");Check(++steps<100000,"test continuation bound");}}
    static object MaterialCase(int variant)
    {
        uint red=0xffff0000,blue=0xff0000ff,green=0xff00ff00;var w=World();RootCell[] root=Array.Empty<RootCell>();MaterialId wanted=MaterialId.Wood;uint color=blue;
        switch(variant)
        {
            case 0:w.Set(225,32,225,MaterialId.Leaves,red);w.Set(241,32,225,MaterialId.Fruit,blue);wanted=MaterialId.Leaves;color=red;break;
            case 1:w.Set(241,32,225,MaterialId.Fruit,blue);w.Set(225,32,225,MaterialId.Leaves,red);wanted=MaterialId.Fruit;break;
            case 2:w.Set(225,32,225,MaterialId.Leaves,red);root=new[]{Root(241,32,225,blue)};break;
            case 3:w.Set(225,32,225,MaterialId.Wood,red);w.Set(241,40,225,MaterialId.Fruit,blue);wanted=MaterialId.Fruit;break;
            case 4:w.Set(241,40,225,MaterialId.Wood,blue);w.Set(225,32,225,MaterialId.Leaves,red);root=new[]{Root(225,32,225,red)};break;
            case 5:w.Set(225,40,225,MaterialId.Wood,blue);root=new[]{Root(225,32,225,red)};color=red;break;
            case 6:w.Set(225,40,225,MaterialId.Leaves,green);root=new[]{Root(249,32,225,red),Root(241,32,225,blue)};break;
            case 7:w.Set(225,40,225,MaterialId.Leaves,green);root=new[]{Root(209,32,225,red),Root(193,32,225,blue)};break;
        }
        var order=new CanonicalOrder(w.Chunks.Keys);var t=(Template)capture.Invoke(null,new object[]{w,order,64,124,11});var j=new Job(t,root,"policy");Finish(j);Check(j.State==Phase.Complete,"material case completion");
        var full=(VoxelWorld)reference.Invoke(null,new object[]{w,order,root});var oracle=(VoxelWorld)reduce.Invoke(null,new object[]{full,64});var p=new Int3(3,Job.Shift/64,3);
        Check(oracle.Get(p)==wanted&&oracle.GetColor(p)==color,"explicit material/tint expectation from actual reducer");
        Check(j.PublishedWorld.Get(p)==oracle.Get(p)&&j.PublishedWorld.GetColor(p)==oracle.GetColor(p),"candidate equals actual reducer after removal/insertion");
        return new{variant,material=wanted.ToString(),color=color.ToString("x8"),exact=true};
    }
    static Template Samples(IEnumerable<Int3> positions,int factor=1)
    {
        var ps=positions.ToArray();var chunks=ps.Select(p=>new Int3(p.x>>4,p.y>>4,p.z>>4)).Distinct().ToArray();var order=new CanonicalOrder(chunks);
        return new Template(factor,factor*(32/factor+2)-4,11,"synthetic-policy",order,ps.Select(p=>new FixedSample(p,MaterialId.Stone,0xff778899,order.Rank(new Int3(p.x>>4,p.y>>4,p.z>>4)),Chunk.Index(p.x&15,p.y&15,p.z&15))),Array.Empty<RootCell>());
    }
    static void Main(string[] args)
    {
        try
        {
            var cases=new List<object>();for(int i=0;i<8;i++)cases.Add(MaterialCase(i));
            var two=Samples(new[]{new Int3(1,1,1),new Int3(17,1,1)});var pause=new Job(two,Array.Empty<RootCell>(),"policy");
            pause.Step(1,0,0,"policy");Check(pause.RemainingFixed==1&&pause.PublishedWorld==null,"fixed candidate queue retained");pause.Step(1,1,0,"policy");Check(pause.RemainingFaces==11&&pause.PublishedWorld==null,"face queue retained exactly");Finish(pause,1,1,1);Check(pause.State==Phase.Complete&&pause.FaceCandidates==12&&pause.FixedCandidates==2,"small-step exact work completion");
            foreach(bool cancel in new[]{true,false})
            {
                var j=new Job(two,Array.Empty<RootCell>(),"policy");j.Step(100,100,1,"policy");Check(j.State==Phase.Mesh&&j.MeshChunkCalls==1&&j.RemainingMeshChunks==1&&j.PublishedMeshes==null,"partial mesh remains unpublished");if(cancel)j.Cancel();else j.Step(1,1,1,"stale");Check(j.Terminal&&j.PublishedWorld==null&&j.PublishedMeshes==null,"cancel/stale after partial meshing remains unpublished");
            }
            var empty=Samples(Array.Empty<Int3>(),64);
            foreach(var bad in new[]{new[]{new RootCell(new Int3(0,30,0),0xff123456)},new[]{new RootCell(new Int3(0,0,0),0xff123456),new RootCell(new Int3(0,0,0),0xff123456)}})
            {var j=new Job(empty,bad,"policy");Finish(j);Check(j.State==Phase.Rejected&&j.Failure=="RootDomainOrDuplicate"&&j.PublishedWorld==null,"invalid root domain/duplicate rejected");}
            bool cellCap=false;try{new Job(empty,new RootCell[65537],"policy");}catch(ArgumentException){cellCap=true;}Check(cellCap,"root cell cap before cloning");
            int consumed=0;IEnumerable<FixedSample> Endless(){while(true){consumed++;yield return default;}}
            bool fixedCap=false;try{new Template(1,30,11,"synthetic",new CanonicalOrder(Array.Empty<Int3>()),Endless(),Array.Empty<RootCell>());}catch(ArgumentException e){fixedCap=e.Message.Contains("cap");}Check(fixedCap&&consumed==65537,"shared ingestion bounded while enumerating");
            var sparse=new List<Int3>();for(int z=0;z<470&&sparse.Count<129;z+=16)for(int x=0;x<470&&sparse.Count<129;x+=16)sparse.Add(new Int3(x,1,z));var chunkJob=new Job(Samples(sparse),Array.Empty<RootCell>(),"policy");Finish(chunkJob,1000,1000,1);Check(chunkJob.State==Phase.Rejected&&chunkJob.Failure=="CoarseChunkCap"&&chunkJob.ChunkCount==128&&chunkJob.PublishedWorld==null,"derived chunk cap");
            var rootSpread=new List<RootCell>();for(int z=-128;z<=127&&rootSpread.Count<129;z+=16)for(int x=-128;x<=127&&rootSpread.Count<129;x+=16)rootSpread.Add(new RootCell(new Int3(x,0,z),0xff8899aa));var spreadJob=new Job(empty,rootSpread.ToArray(),"policy");Finish(spreadJob,1000,1000,1);Check(spreadJob.State==Phase.Rejected&&spreadJob.Failure=="RootChunkCap","canonical-phase root chunk cap");
            var dense=new List<Int3>();for(int z=160;z<192;z++)for(int y=0;y<32;y++)for(int x=160;x<192;x++)if((x+y+z)%2==0)dense.Add(new Int3(x,y,z));var faceJob=new Job(Samples(dense),Array.Empty<RootCell>(),"policy");Finish(faceJob,int.MaxValue,int.MaxValue,1);Check(faceJob.State==Phase.Rejected&&faceJob.Failure=="RawFaceCap"&&faceJob.RawFaces==65537&&faceJob.PublishedWorld==null,"raw face cap no partial publication");
            var pinnedOrder=new CanonicalOrder(new[]{new Int3(14,2,14)});var pinned=new Template(1,30,11,"pinned",pinnedOrder,new[]{new FixedSample(new Int3(235,33,235),MaterialId.Wood,0xff123456,0,Chunk.Index(11,1,11))},new[]{new RootCell(new Int3(0,29,0),0xff123456)});
            foreach(bool missing in new[]{true,false}){var j=new Job(pinned,missing?Array.Empty<RootCell>():new[]{new RootCell(new Int3(0,29,0),0xff654321)},"policy");Finish(j);Check(j.State==Phase.Rejected&&j.Failure==(missing?"MissingPinnedRow":"ChangedPinnedRow"),"pinned row complete and exact");}
            File.WriteAllText(args[0],JsonSerializer.Serialize(new{status="PASS",checks,material_cases=cases,scope="Small synthetic actual-reducer parity and bounded candidate admission/continuation/cancel tests; no tree generation or native Unity objects."},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("PASS "+checks+" policy assertions;8 material/order/fallback cases.");
        }
        catch(Exception e){File.WriteAllText(args[0],JsonSerializer.Serialize(new{status="FAIL",checks,error=e.ToString()},new JsonSerializerOptions{WriteIndented=true}));Console.Error.WriteLine(e);Environment.ExitCode=1;}
    }
}
