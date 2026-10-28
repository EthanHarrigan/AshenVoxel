using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Text.Json;
using System.Collections.Generic;
using System.Reflection;
using AshenVoxel.Core;
using AshenVoxel.WorldScale;

static class Program
{
    sealed class Field:IWorldField
    {public Func<double,double,double> Height;public string SourceIdentity=>"explicit-voxel-lod-fixture";public SurfaceSample Sample(double x,double z)=>new SurfaceSample(Height(x,z),6,6,true);}
    static int checks;
    static void Check(bool value,string name){if(!value)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
    static LodTile Tile(long x=0,long z=0,int level=0)=>new LodTile(new TileKey(x,z,level),x*25.6*Math.Pow(2,level),z*25.6*Math.Pow(2,level),25.6*Math.Pow(2,level),-1000,1000);
    static SurfaceMaterialSampler Sampler(Func<double,double,double> h)=>new SurfaceMaterialSampler(new Field{Height=h},new SurfaceProfile(enableSnow:false));
    static void Main(string[] args)
    {
        var timer=Stopwatch.StartNew();var flat=VoxelLodSurface.Build(Sampler((x,z)=>10),Tile(),256,9);
        Check(flat.CellStride==1&&flat.Heights.Length==65536,"full tile resolves individual10cm columns beyond the former22m square");
        Check(flat.Heights.All(h=>Math.Abs(h)<1e-6)&&Math.Abs(flat.AnchorY-10)<1e-6,"flat top lies on exact global10cm occupancy boundary");
        var top=flat.Faces.Where(f=>f.Axis==1).ToArray();double area=top.Sum(f=>(double)f.U*f.V);
        Check(Math.Abs(area-25.6*25.6)<.001&&top.Length<65536,"greedy top faces cover entire tile once and reduce matching pigment regions");
        Check(top.All(f=>f.Positive&&f.A0==255&&f.A1==255&&f.A2==255&&f.A3==255),"flat tops retain exact outward orientation and unoccluded corners");
        Check(flat.Faces.Where(f=>f.Axis!=1).All(f=>f.X==0||Math.Abs(f.X-25.6)<.00001||f.Z==0||Math.Abs(f.Z-25.6)<.00001),"flat internal and buried faces are absent");
        Check(flat.Faces.All(f=>MaterialCatalog.IsValid(f.Material)&&f.Material!=MaterialId.Air&&(f.Tint>>24)==255&&f.U>0&&f.V>0),"every face carries existing opaque material pigment and nonzero area");
        var high=VoxelLodSurface.Build(Sampler((x,z)=>6010),Tile(),32,6009);
        Check(high.Heights.Length==1024&&high.Faces.Count<10000,"mountain altitude never allocates buried mountain volume");
        var sampler=Sampler((x,z)=>10+.17*x+.08*z);var slope=VoxelLodSurface.Build(sampler,Tile(-1,-1),256,-5);
        bool same=true;
        for(int z=0;z<256;z+=7)for(int x=0;x<256;x+=7)
        {
            double px=(-256+x+.5)*.1,pz=(-256+z+.5)*.1;
            same&=Math.Abs(slope.AnchorY+slope.Heights[x+z*256]-VoxelTerrainPage.SurfaceTop(sampler.EvaluateColumn(px,pz).SurfaceHeightMeters))<.000002;
        }
        Check(same,"negative coordinates and fine heights match the actual implicit matter/collision authority");
        Check(slope.Faces.Any(f=>f.Axis!=1&&f.X>0&&f.X<25.5f&&f.Z>0&&f.Z<25.5f),"slopes contain exposed voxel risers instead of interpolated triangles");
        Check(slope.Faces.Any(f=>f.Axis==1&&(f.A0<255||f.A1<255||f.A2<255||f.A3<255)),"fine slopes preserve occlusion corners during greedy meshing");
        var cliff=VoxelLodSurface.Build(Sampler((x,z)=>x<12.8?10:14),Tile(),64,8);
        Check(cliff.Faces.Any(f=>f.Axis==0&&!f.Positive&&Math.Abs(f.X-12.8)<.001&&SurfaceProfile.IsRock(f.Material)),"exposed cliff walls use geological material below the cover");
        Check(cliff.Faces.Count<VoxelLodSurface.MaximumFaces,"material bands and boundary closure remain bounded");
        var coarse=VoxelLodSurface.Build(sampler,Tile(0,0,1),32,-20);
        Check(coarse.CellStride==16&&coarse.Heights.All(h=>Math.Abs(h*10-Math.Round(h*10))<.00002),"far LOD aggregates horizontal cells while retaining10cm vertical heights");
        Check(coarse.Faces.Where(f=>f.Axis!=1&&(f.X==0||f.Z==0)).Min(f=>f.Y+coarse.AnchorY)<-20,"coarser LOD boundary closures extend beneath conservative neighbor floor");
        bool canceled=false;var cts=new CancellationTokenSource();cts.Cancel();try{VoxelLodSurface.Build(sampler,Tile(),256,-20,cts.Token);}catch(OperationCanceledException){canceled=true;}
        Check(canceled,"obsolete work cancels cooperatively before allocating geometry");
        var parallel=VoxelLodSurface.Build(sampler,Tile(-1,-1),256,-5,parallelSampling:true);
        Check(parallel.CellStride==slope.CellStride&&parallel.AnchorY==slope.AnchorY&&parallel.SourceIdentity==slope.SourceIdentity&&
            parallel.Heights.SequenceEqual(slope.Heights)&&parallel.Faces.SequenceEqual(slope.Faces),"parallel rows preserve exact heights, ordered faces, material, pigment, AO and source identity");
        int caller=Thread.CurrentThread.ManagedThreadId;bool serialOnly=true;
        var serialSampler=Sampler((x,z)=>{if(Thread.CurrentThread.ManagedThreadId!=caller)serialOnly=false;return 10;});
        VoxelLodSurface.Build(serialSampler,Tile(),128,9);
        VoxelLodSurface.Build(serialSampler,Tile(),32,9,parallelSampling:true);
        Check(serialOnly,"default calls and small tiles access mutable fields serially");
        canceled=false;try{VoxelLodSurface.Build(sampler,Tile(),256,-20,cts.Token,parallelSampling:true);}catch(OperationCanceledException){canceled=true;}
        Check(canceled,"parallel sampling respects pre-cancellation");
        using(var activeCancel=new CancellationTokenSource())
        {
            int reads=0;var stopping=Sampler((x,z)=>{if(Interlocked.Increment(ref reads)==250)activeCancel.Cancel();return 10;});
            canceled=false;try{VoxelLodSurface.Build(stopping,Tile(),256,9,activeCancel.Token,parallelSampling:true);}catch(OperationCanceledException){canceled=true;}
            Check(canceled&&reads<10000,"in-flight parallel sampling cancels without finishing the tile");
        }
        bool rejected=false;try{VoxelLodSurface.Build(sampler,Tile(),48,-20);}catch(ArgumentOutOfRangeException){rejected=true;}
        Check(rejected,"non-dyadic sampling cannot silently break the global voxel grid");
        Check(VoxelLodQuality.Select(25.6,1,1000,1.5,32,true,false)==256,"quality profile requests full near resolution");
        Check(VoxelLodQuality.Select(1024,3000,1000,.5,32,true,false)>=VoxelLodQuality.Select(1024,3000,1000,4,32,true,false),"smaller pixel target never reduces detail");
        Check(VoxelLodQuality.Select(1024,3000,1000,1.5,32,false,false)==32&&VoxelLodQuality.Select(1024,3000,1000,1.5,32,true,false)==128,"offscreen cache stays coarser while camera-facing detail is prioritized");
        Check(VoxelLodQuality.Select(25.6,1,1000,1.5,32,true,true)==8,"startup produces complete coarse voxel coverage before refinement");
        Check(VoxelLodQuality.InView(Tile(0,200),new WorldPoint(0,10,0),0,1,1)&&!VoxelLodQuality.InView(Tile(0,-200),new WorldPoint(0,10,0),0,1,1),"conservative view priority distinguishes front and rear terrain");
        Check(VoxelLodQuality.InView(Tile(0,-200),new WorldPoint(0,10,0),0,0,1),"vertical camera view retains all heading candidates");
        var merge=typeof(VoxelLodSurface).GetMethod("MergeSides",BindingFlags.NonPublic|BindingFlags.Static);
        var fixture=new List<VoxelLodFace>();
        for(int axis=0;axis<=2;axis+=2)for(int sign=0;sign<2;sign++)for(int a=0;a<16;a++)for(int b=0;b<16;b++)
            fixture.Add(new VoxelLodFace(axis==0?0:a*.1f,b*.1f,axis==0?a*.1f:0,.1f,.1f,axis,sign==1,new MatterSample(MaterialId.Granite,a<8?0xff778899u:0xff667788u),(byte)(b<8?100:200)));
        string[] Covered(List<VoxelLodFace> fs)
        {
            var keys=new List<string>();foreach(var f in fs)
            {
                int start=(int)Math.Round((f.Axis==0?f.Z:f.X)*10),bottom=(int)Math.Round(f.Y*10);
                int width=(int)Math.Round((f.Axis==0?f.V:f.U)*10),height=(int)Math.Round((f.Axis==0?f.U:f.V)*10);
                for(int a=start;a<start+width;a++)for(int b=bottom;b<bottom+height;b++)keys.Add($"{f.Axis}/{f.Positive}/{a}/{b}/{f.Material}/{f.Tint}/{f.Wetness}");
            }
            keys.Sort(StringComparer.Ordinal);return keys.ToArray();
        }
        var before=Covered(fixture);merge.Invoke(null,new object[]{fixture,true,CancellationToken.None});merge.Invoke(null,new object[]{fixture,false,CancellationToken.None});
        Check(before.SequenceEqual(Covered(fixture)),"side merging exactly preserves unit-face coverage, winding, material, pigment and wetness");
        Check(fixture.Count==16,"1024 coplanar side faces collapse to16 exact appearance rectangles");
        Check(slope.Faces.Count<slope.UnmergedFaceCount&&flat.Faces.Count<flat.UnmergedFaceCount,"real slope and flat conversion reduce side geometry without discarding cells");
        Check(coarse.Faces.Where(f=>f.Axis==1).All(f=>Math.Min(Math.Min(f.A0,f.A1),Math.Min(f.A2,f.A3))>=243),"coarse AO averages10cm crevices instead of darkening entire large aggregate faces");
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{status="PASS",checks,scope="Actual engine-free voxel LOD conversion; synthetic fixtures, no Unity frame-time claim",elapsed_ms=timer.Elapsed.TotalMilliseconds,flat_faces=flat.Faces.Count,flat_unmerged_tops=65536,flat_merged_tops=top.Length,slope_faces=slope.Faces.Count,cliff_faces=cliff.Faces.Count},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("All "+checks+" world voxel LOD checks passed.");
    }
}
