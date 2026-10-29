using System;
using System.Collections.Generic;
using System.IO;
using Random = System.Random;
using AshenVoxel.WorldScale;
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
#endif

namespace AshenVoxel.Exploration.Editor
{
    public static class ClipmapChecks
    {
        [Serializable] public sealed class Report
        {
            public string status="RUNNING",error,scope="CPU clipmap state and independent surface oracle; no native rendering or timing";
            public int assertions,publications;
            public double nearP99Meters,farP99Relative,maximumOracleError;
            public long residentBytes,highGpuPayloadEstimateBytes;
            public string utc;public string[] sources,sha256;
        }
        sealed class Published
        {
            public long X,Z;public ClipmapSample[] Samples;
        }
        sealed class Fixture
        {
            public readonly TerrainClipmapState State;
            public readonly int Size,Levels;
            public readonly double Finest;
            public readonly Published[] Published;
            public Func<double,double,double> Field;
            public Fixture(int size,double finest,int levels,Func<double,double,double> field,int flight=4096,int jobs=4)
            {Size=size;Finest=finest;Levels=levels;Field=field;State=new TerrainClipmapState(size,finest,levels,0,0,flight,jobs);Published=new Published[levels];}
            public ClipmapSample[] Samples(ClipmapJob job)
            {
                var result=new ClipmapSample[job.SampleCount];double spacing=Finest*Math.Pow(2,job.Level);
                for(int z=0;z<job.Rect.Height;z++)for(int x=0;x<job.Rect.Width;x++)
                    result[x+z*job.Rect.Width]=new ClipmapSample((float)Field((job.Rect.X+x)*spacing,(job.Rect.Z+z)*spacing),0xff806040,0xff804020);
                return result;
            }
            public bool PublishOne(Report report)
            {
                if(!State.TryTakePublication(out var p))return false;
                var copy=new Published{X=p.OriginX,Z=p.OriginZ,Samples=(ClipmapSample[])p.Samples.Clone()};
                Require(State.Publish(p),"Current completed publication rejected",report);
                Published[p.Level]=copy;report.publications++;
                Require(!State.Publish(p),"Duplicate publication acknowledged twice",report);return true;
            }
            public void Drain(Report report)
            {
                for(int round=0;round<50000;round++)
                {
                    var jobs=new List<ClipmapJob>();while(State.TryTakeJob(512,out var job))jobs.Add(job);
                    Require(State.InFlightJobs<=4&&State.InFlightSamples<=4096,"In-flight budget exceeded",report);
                    for(int i=jobs.Count-1;i>=0;i--)Require(State.Complete(jobs[i],Samples(jobs[i])),"Current reordered job rejected",report);
                    bool published=PublishOne(report);
                    if(State.IsReady){Require(State.PendingJobs==0&&State.InFlightJobs==0,"Ready with unfinished work",report);return;}
                    Require(jobs.Count>0||published,"Scheduler deadlock while not ready",report);
                }
                throw new InvalidDataException("Clipmap drain exceeded bounded test rounds");
            }
            double Raw(int level,long x,long z)
            {
                var p=Published[level];if(p==null)throw new InvalidOperationException("Oracle requested unpublished level");
                if(x<p.X||z<p.Z||x>=p.X+Size||z>=p.Z+Size)throw new InvalidOperationException("Oracle sampled outside published window");
                // Independent modulo, deliberately not TerrainClipmapMath.ToroidalIndex.
                int xx=(int)(x%Size);if(xx<0)xx+=Size;int zz=(int)(z%Size);if(zz<0)zz+=Size;
                return p.Samples[xx+zz*Size].Height;
            }
            public double Parent(int level,double x,double z)
            {
                double s=Finest*Math.Pow(2,level);long ix=(long)Math.Floor(x/s),iz=(long)Math.Floor(z/s);
                return ClipmapSurfaceOracle.Quad(ix*s,iz*s,s,x,z,Raw(level,ix,iz),Raw(level,ix+1,iz),Raw(level,ix,iz+1),Raw(level,ix+1,iz+1));
            }
            double Vertex(int level,long x,long z)
            {
                double fine=Raw(level,x,z);if(level==Levels-1)return fine;
                var p=Published[level];double edge=Math.Min(Math.Min(x-p.X-2,p.X+Size-2-x),Math.Min(z-p.Z-2,p.Z+Size-2-z));
                double alpha=Math.Max(0,Math.Min(1,1-(edge-2)/(Size/8.0)));
                if(alpha<=0)return fine;double s=Finest*Math.Pow(2,level);
                return fine*(1-alpha)+Parent(level+1,x*s,z*s)*alpha;
            }
            public double Oracle(int level,double x,double z)
            {
                double s=Finest*Math.Pow(2,level);long ix=(long)Math.Floor(x/s),iz=(long)Math.Floor(z/s);
                return ClipmapSurfaceOracle.Quad(ix*s,iz*s,s,x,z,Vertex(level,ix,iz),Vertex(level,ix+1,iz),Vertex(level,ix,iz+1),Vertex(level,ix+1,iz+1));
            }
            public int FinestAt(double x,double z)
            {
                int found=-1;foreach(var visible in State.GetVisibleLevels())
                {int level=visible.Level;var p=State.GetLevel(level);double s=p.Spacing;if(x>=(p.OriginX+2)*s&&z>=(p.OriginZ+2)*s&&x<(p.OriginX+Size-2)*s&&z<(p.OriginZ+Size-2)*s)found=Math.Min(found<0?level:found,level);}
                return found;
            }
        }
        static void Require(bool value,string message,Report report)
        {report.assertions++;if(!value)throw new InvalidDataException(message);}
        static void MathChecks(Report report)
        {
            foreach(int size in new[]{32,64,512})foreach(double spacing in new[]{.2,.8,1.6,51.2})
                foreach(double camera in new[]{-100000.13,-.401,-.4,-.001,0,.001,.4,99999.99})
                {
                    long origin=TerrainClipmapMath.WindowOrigin(camera,spacing,size);
                    Require((origin&1)==0,"Window origin must be even",report);
                    long center=origin+size/2;
                    Require(center*spacing<=camera+1e-8&&(center+2)*spacing>camera-1e-8,"Negative origin floor alignment",report);
                }
            for(long z=-100;z<=100;z+=7)for(long x=-100;x<=100;x+=3)
            {
                int actual=TerrainClipmapMath.ToroidalIndex(x,z,32);
                int xx=(int)x;while(xx<0)xx+=32;while(xx>=32)xx-=32;
                int zz=(int)z;while(zz<0)zz+=32;while(zz>=32)zz-=32;
                Require(actual==xx+32*zz,"Negative toroidal address mismatch",report);
            }
            foreach(int size in new[]{256,512,1024})foreach(double spacing in new[]{.1,.2,.8})foreach(double view in new[]{40000.0,100000.0})
            {
                int levels=0;bool rejected=false;
                try{levels=TerrainClipmapMath.ComputeLevelCount(size,spacing,view,149760,74880);}catch(ArgumentException){rejected=true;}
                int n=rejected?16:levels;Require(n>=2&&n<=16,"Level cap violated",report);
                double topSpacing=spacing*Math.Pow(2,n-1);
                var anchor=new ClipmapLevelInfo(n-1,topSpacing,TerrainClipmapMath.WindowOrigin(0,topSpacing,size),TerrainClipmapMath.WindowOrigin(0,topSpacing,size),true,1);
                bool all=true;
                foreach(double x in new[]{-149760.0,149760.0})foreach(double z in new[]{-74880.0,74880.0})
                {
                    var child=new ClipmapLevelInfo(n-2,topSpacing/2,TerrainClipmapMath.WindowOrigin(x,topSpacing/2,size),TerrainClipmapMath.WindowOrigin(z,topSpacing/2,size),true,1);
                    all&=TerrainClipmapMath.Contains(anchor,size,x,z,view)&&TerrainClipmapMath.CanNest(anchor,child,size);
                }
                Require(rejected?!all:all,"Configuration accepted impossible anchor or rejected usable one",report);
            }
            var random=new Random(9713);
            for(int i=0;i<1000;i++)
            {
                double x=random.NextDouble(),z=random.NextDouble();double a=random.NextDouble()*100,b=random.NextDouble()*100,c=random.NextDouble()*100,d=random.NextDouble()*100;
                double expected=ClipmapSurfaceOracle.Quad(0,0,1,x,z,a,b,c,d);
                double actual=TerrainClipmapMath.TriangleHeight(a,b,c,d,x,z);
                Require(Math.Abs(expected-actual)<1e-9,"Triangle diagonal/barycentric mismatch",report);
            }
        }
        static void Lifecycle(Report report)
        {
            var f=new Fixture(64,.2,10,(x,z)=>20+.07*x-.03*z);
            f.State.Move(-.37,.91);
            Require(!f.State.IsReady&&!f.State.TryRenderedHeight(0,0,out _),"Unpublished clipmap exposed height",report);
            Require(f.State.TryTakeJob(256,out var first),"Initial job missing",report);
            Require(f.State.Fail(first),"Current failed job was not requeued",report);
            Require(!f.State.Complete(first,f.Samples(first)),"Failed job completion accepted later",report);
            Require(f.State.TryTakeJob(256,out first),"Failed job did not retry",report);
            var data=f.Samples(first);Require(f.State.Complete(first,data),"Initial completion rejected",report);
            Require(!f.State.Complete(first,data),"Duplicate job accepted",report);
            Require(!f.State.TryRenderedHeight(0,0,out _),"Sampling completion published before GPU ack",report);
            f.Drain(report);
            Require(f.State.TryRenderedHeight(-.1,.2,out double old)&&Math.Abs(old-f.Field(-.1,.2))<1e-4,"Planar field changed by triangle/morph",report);
            f.State.Move(111.7,-83.1);
            Require(f.State.TryRenderedHeight(111.7,-83.1,out _),"Move lost retained valid ancestor",report);
            Require(f.State.TryTakeJob(256,out var stale),"Move did not schedule strips",report);
            f.State.Move(1000000,1000000);
            Require(!f.State.Complete(stale,f.Samples(stale)),"Obsolete window job accepted",report);
            Require(!f.State.TryRenderedHeight(1000000,1000000,out _),"Out-of-domain teleport exposed wrapped stale texels",report);
            f.State.Move(-301.2,221.7);
            f.Drain(report);
            f.State.Move(-.37,.91);f.Drain(report);
            f.Field=(x,z)=>120+.07*x-.03*z;f.State.Invalidate(-100,-100,100,100);
            Require(f.State.TryRenderedHeight(-.1,.2,out double retained)&&Math.Abs(retained-old)<1e-4,"Invalidation discarded published data",report);
            Require(f.State.TryTakeJob(256,out var sourceStale),"Source edit did not schedule job",report);
            f.Field=(x,z)=>220+.07*x-.03*z;f.State.Invalidate(-100,-100,100,100);
            Require(!f.State.Complete(sourceStale,f.Samples(sourceStale)),"Stale source completion accepted",report);
            f.Drain(report);
            Require(f.State.TryRenderedHeight(-.1,.2,out double updated)&&Math.Abs(updated-f.Field(-.1,.2))<1e-3,"Latest source not published",report);
            Require(f.State.PendingSamples==0&&f.State.InFlightSamples==0,"Quiescent work ledger not empty",report);
            Require(f.State.ResidentBytes<64L*64*12*20,"Small fixture memory unexpectedly unbounded",report);
            report.residentBytes=f.State.ResidentBytes;
        }
        static void DisjointAndPublication(Report report)
        {
            var f=new Fixture(128,1,6,(x,z)=>0);f.State.Move(0,0);f.Drain(report);
            f.Field=(x,z)=>Math.Abs(z)<=3?(x>=-56&&x<=-50?10:x>=50&&x<=56?20:0):0;
            f.State.Invalidate(-56,-3,-50,3);f.State.Invalidate(50,-3,56,3);f.Drain(report);
            Require(f.State.TryRenderedHeight(-53,0,out double left)&&Math.Abs(left-10)<1e-4,"First disjoint invalidation was lost",report);
            Require(f.State.TryRenderedHeight(53,0,out double right)&&Math.Abs(right-20)<1e-4,"Second disjoint invalidation was lost",report);
            f.Field=(x,z)=>30;f.State.Invalidate(-10,-10,10,10);
            ClipmapPublication held=null;
            for(int round=0;round<10000&&held==null;round++)
            {
                while(f.State.TryTakeJob(512,out var job))Require(f.State.Complete(job,f.Samples(job)),"Prepare held publication",report);
                f.State.TryTakePublication(out held);
            }
            Require(held!=null,"Completed candidate not offered for publication",report);
            f.State.Invalidate(-10,-10,10,10);
            Require(!f.State.Publish(held),"Invalidated publication was acknowledged",report);
            Require(f.State.TryRenderedHeight(0,0,out double retained)&&retained==0,"Rejected publication changed visible data",report);
            f.Drain(report);
            Require(f.State.TryRenderedHeight(0,0,out double final)&&Math.Abs(final-30)<1e-4,"Replacement generation not visible",report);
            f.State.Invalidate(-1,-1,1,1);Require(f.State.TryTakeJob(128,out var bad),"Invalid-data fixture admission",report);
            var samples=f.Samples(bad);samples[samples.Length-1]=new ClipmapSample(float.NaN,0,0);bool rejected=false;
            try{f.State.Complete(bad,samples);}catch(ArgumentException){rejected=true;}
            Require(rejected,"Nonfinite height accepted",report);Require(f.State.Fail(bad),"Invalid job cannot be retried",report);f.Drain(report);
        }
        static void Topology(Fixture f,Report report)
        {
            var chain=f.State.GetVisibleLevels();
            for(int at=0;at<chain.Length;at++)
            {
                var parent=chain[at];ClipmapLevelInfo? child=at==0?(ClipmapLevelInfo?)null:chain[at-1];
                var rects=TerrainClipmapMath.GetRingRects(parent,child,f.Size);var counts=new byte[f.Size*f.Size];
                foreach(var rect in rects)
                    for(long z=rect.Z;z<rect.EndZ;z++)for(long x=rect.X;x<rect.EndX;x++)
                    {
                        Require(x>=2&&z>=2&&x<f.Size-2&&z<f.Size-2,"Ring index outside mesh extent",report);
                        counts[(int)(x+z*f.Size)]++;
                    }
                for(int z=0;z<f.Size;z++)for(int x=0;x<f.Size;x++)
                {
                    double wx=(parent.OriginX+x+.5)*parent.Spacing,wz=(parent.OriginZ+z+.5)*parent.Spacing;
                    bool expected=x>=2&&z>=2&&x<f.Size-2&&z<f.Size-2;
                    if(child.HasValue)
                    {
                        var c=child.Value;
                        bool inside=wx>(c.OriginX+2)*c.Spacing&&wx<(c.OriginX+f.Size-2)*c.Spacing&&wz>(c.OriginZ+2)*c.Spacing&&wz<(c.OriginZ+f.Size-2)*c.Spacing;
                        expected&=!inside;
                    }
                    Require(counts[x+z*f.Size]==(expected?1:0),"Ring topology has overlap or missing cell",report);
                }
                if(!child.HasValue)continue;var fine=child.Value;
                for(int i=2;i<=f.Size-2;i++)for(int edge=0;edge<4;edge++)
                {
                    long ix=fine.OriginX+(edge<2?i:edge==2?2:f.Size-2),iz=fine.OriginZ+(edge>=2?i:edge==0?2:f.Size-2);
                    double x=ix*fine.Spacing,z=iz*fine.Spacing;
                    Require(f.State.TryRenderedHeight(x,z,out double actual),"Seam vertex uncovered",report);
                    Require(Math.Abs(actual-f.Parent(parent.Level,x,z))<1e-4,"Fine edge does not coincide with parent triangle",report);
                }
            }
        }
        static void SurfaceChecks(Report report)
        {
            var f=new Fixture(64,.2,11,ClipmapSurfaceOracle.Terrain);f.State.Move(123.45,-98.725);f.Drain(report);
            var random=new Random(81021);
            for(int step=0;step<20;step++)
            {
                double cx=123.45+step*13.7,cz=-98.725-step*8.1;
                f.State.Move(cx,cz);f.Drain(report);Topology(f,report);
                for(int i=0;i<500;i++)
                {
                    double x=cx+(random.NextDouble()-.5)*1000,z=cz+(random.NextDouble()-.5)*1000;int level=f.FinestAt(x,z);
                    Require(level>=0,"Published ancestor failed to cover query",report);
                    Require(f.State.TryRenderedHeight(x,z,out double actual),"Covered height query failed",report);
                    double error=Math.Abs(actual-f.Oracle(level,x,z));report.maximumOracleError=Math.Max(report.maximumOracleError,error);
                    Require(error<1e-4,"Rendered-height query differs from emitted triangles: "+error,report);
                }
            }
        }
        static void HeightTargets(Report report)
        {
            int levels=TerrainClipmapMath.ComputeLevelCount(512,.2,40000,149760,74880);
            var f=new Fixture(512,.2,levels,ClipmapSurfaceOracle.Terrain);
            const double cx=1234.5,cz=-987.25;f.State.Move(cx,cz);f.Drain(report);
            var near=new double[10000];var far=new double[10000];var random=new Random(53019);
            for(int i=0;i<near.Length;i++)
            {
                double theta=random.NextDouble()*Math.PI*2,radius=400*Math.Sqrt(random.NextDouble());
                double x=cx+Math.Cos(theta)*radius,z=cz+Math.Sin(theta)*radius;
                Require(f.State.TryRenderedHeight(x,z,out double value),"Near target query uncovered",report);
                near[i]=Math.Abs(value-ClipmapSurfaceOracle.Terrain(x,z));
                radius=400+(40000-400)*Math.Sqrt(random.NextDouble());theta=random.NextDouble()*Math.PI*2;
                x=cx+Math.Cos(theta)*radius;z=cz+Math.Sin(theta)*radius;
                Require(f.State.TryRenderedHeight(x,z,out value),"40km target query uncovered",report);
                far[i]=Math.Abs(value-ClipmapSurfaceOracle.Terrain(x,z))/radius;
            }
            report.nearP99Meters=ClipmapSurfaceOracle.Percentile(near,.99);
            report.farP99Relative=ClipmapSurfaceOracle.Percentile(far,.99);
            Require(report.nearP99Meters<=.15,"Synthetic near p99 exceeds .15m: "+report.nearP99Meters,report);
            Require(report.farP99Relative<=.006,"Synthetic far p99 exceeds .6%: "+report.farP99Relative,report);
            report.residentBytes=Math.Max(report.residentBytes,f.State.ResidentBytes);
            report.highGpuPayloadEstimateBytes=f.State.ResidentBytes+4L*1048576;
            Require(report.highGpuPayloadEstimateBytes<=64L*1048576,"High payload estimate exceeds 64MiB GPU gate",report);
            // Exercise the real published/draw chain at opposite world corners.
            foreach(var point in new[]{new[]{-149760.0,-74880.0},new[]{149760.0,74880.0},new[]{-149760.0,74880.0}})
            {
                f.State.Move(point[0],point[1]);
                Require(f.State.TryRenderedHeight(point[0],point[1],out _),"World-corner teleport lost anchor coverage",report);
                f.Drain(report);var visible=f.State.GetVisibleLevels();
                Require(visible.Length==levels&&visible[0].Level==0,"Ready terrain cannot draw full finest chain at world corner",report);
                Require(f.State.IsReadyArea(point[0],point[1],1),"Ready-area false after settled corner teleport",report);
                Require(f.State.TryRenderedHeight(point[0]+.13,point[1]-.17,out _),"Corner query failed after settled readiness",report);
            }
        }
        public static Report Run()
        {
            var report=new Report();try{MathChecks(report);Lifecycle(report);DisjointAndPublication(report);SurfaceChecks(report);HeightTargets(report);report.status="PASS";}
            catch(Exception e){report.status="FAIL";report.error=e.ToString();}return report;
        }
#if UNITY_EDITOR
        [MenuItem("Ashen Voxel/Terrain/Clipmap CPU state and surface checks")]
        public static void RunEditor()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Use Edit mode for CPU clipmap checks");
            string root=Path.GetDirectoryName(Application.dataPath);
            string[] sources={"Assets/AshenVoxel/WorldScaleData/TerrainClipmapMath.cs","Assets/AshenVoxel/WorldScaleData/TerrainClipmapState.cs","Assets/AshenVoxel/Exploration/Editor/ClipmapChecks.cs","Assets/AshenVoxel/Exploration/Editor/ClipmapSurfaceOracle.cs"};
            string Hash(string path){using var sha=System.Security.Cryptography.SHA256.Create();return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Path.Combine(root,path)))).Replace("-","").ToLowerInvariant();}
            var hashes=Array.ConvertAll(sources,Hash);var report=Run();report.sources=sources;report.sha256=hashes;report.utc=DateTime.UtcNow.ToString("O");
            for(int i=0;i<sources.Length;i++)if(Hash(sources[i])!=hashes[i]){report.status="FAIL";report.error="Source changed during Editor CPU checks";}
            string dir=Path.Combine(root,"Docs/ClipmapEvidence/cpu-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir,"report.json"),JsonUtility.ToJson(report,true));
            if(report.status!="PASS")throw new InvalidOperationException(report.error);
            Debug.Log("PASS "+report.assertions+" clipmap CPU assertions; native render gates remain unexecuted. "+dir);
        }
#endif
    }
}
