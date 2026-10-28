using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Diagnostics;
using AshenVoxel.WorldScale;
using UnityEngine;
using UnityEngine.Rendering;

namespace AshenVoxel.Exploration
{
    /// <summary>View-only stand HLOD. Immutable source snapshots, one worker,
    /// capped 256 tiles / 36864 samples, shared two-triangle prototype images.
    /// Stand aggregates are deliberately approximate, never physical instances.</summary>
    public sealed class ForestHorizon:IDisposable
    {
        // View-only representation version; no population, matter or atlas change.
        public const int CanopyRepresentationVersion=1;
        public const int CanopySamplesPerAxis=12,MaximumCanopySamples=256*12*12;
        public const double MaximumPatchWidthMeters=2048;
        readonly SurfaceMaterialSampler surface;readonly int seed;
        readonly Material material;readonly Dictionary<int,Mesh> meshes;
        readonly Dictionary<int,ExplorationPrototype> prototypes;
        readonly Plane[] planes=new Plane[6];
        readonly struct Sample
        {
            public readonly int Id;public readonly WorldPoint Position;public readonly float Yaw,Width,Height,PackedId;
            public readonly uint Appearance;
            public Sample(int id,WorldPoint p,float yaw,float width,float height,float packedId,uint appearance)
            {Id=id;Position=p;Yaw=yaw;Width=width;Height=height;PackedId=packedId;Appearance=appearance;}
        }
        sealed class Batch
        {
            public int Id,Count;public Matrix4x4[] Matrices=new Matrix4x4[1023];public Bounds Bounds;
        }
        Task<List<Sample>> job;List<Sample> samples;int upload;
        List<Batch> batches=new List<Batch>(),next=new List<Batch>();readonly Dictionary<int,Batch> open=new Dictionary<int,Batch>();
        readonly Stack<Batch> pool=new Stack<Batch>();long retryAt;
        #if UNITY_5_3_OR_NEWER
        readonly ForestGpuRenderer gpu;
        ForestGpuRenderer.Instance[] pendingGpu;
        #endif
        public bool UsingGpu
        {
            get
            {
                #if UNITY_5_3_OR_NEWER
                return gpu!=null;
                #else
                return false;
                #endif
            }
        }
        public double PublishedFarDistance {get;private set;}
        public double FarthestStandDistance {get;private set;}
        public long GpuBufferBytes
        {
            get
            {
                #if UNITY_5_3_OR_NEWER
                return gpu==null?0:gpu.BufferBytes;
                #else
                return 0;
                #endif
            }
        }
        double pendingFarthest;
        WorldPoint planned,origin;bool havePlan,disposed;double plannedNear,plannedFar,plannedDensity;
        public int DrawCalls {get;private set;}
        public int VisibleStands {get;private set;}
        public int ResidentStands {get;private set;}
        public int PendingSamples=>samples==null?job==null?0:-1:samples.Count-upload;
        public double LastWorkMilliseconds {get;private set;}
        public string Error {get;private set;}
        public ForestHorizon(SurfaceMaterialSampler surface,int seed,Material template,Dictionary<int,ExplorationPrototype> prototypes,Dictionary<int,Mesh> meshes,bool preferGpu=true)
        {
            this.surface=surface;this.seed=seed;this.prototypes=prototypes;this.meshes=meshes;material=new Material(template);material.enableInstancing=true;
            #if UNITY_5_3_OR_NEWER
            if(preferGpu)gpu=ForestGpuRenderer.TryCreate(template);
            #endif
        }
        public void Tick(Camera camera,WorldPoint eye,WorldPoint renderOrigin,double near,double far,double density)
        {
            if(disposed)return;long start=Stopwatch.GetTimestamp();
            if(origin.X!=renderOrigin.X||origin.Y!=renderOrigin.Y||origin.Z!=renderOrigin.Z)
            {
                Vector3 delta=new Vector3((float)(origin.X-renderOrigin.X),(float)(origin.Y-renderOrigin.Y),(float)(origin.Z-renderOrigin.Z));
                Rebase(batches,delta);Rebase(next,delta);origin=renderOrigin;
            }
            if(job!=null&&job.IsCompleted)
            {
                if(job.IsFaulted){Error=job.Exception.GetBaseException().Message;havePlan=false;retryAt=Stopwatch.GetTimestamp()+Stopwatch.Frequency*5;}
                else if(!job.IsCanceled)
                {
                    samples=job.Result;upload=0;Recycle(next);open.Clear();Error=null;pendingFarthest=0;
                    #if UNITY_5_3_OR_NEWER
                    if(gpu!=null)pendingGpu=new ForestGpuRenderer.Instance[samples.Count];
                    #endif
                }
                job=null;
            }
            if(job==null&&samples==null&&Stopwatch.GetTimestamp()>=retryAt&&(!havePlan||Travel(eye,planned)>256*256||near!=plannedNear||far!=plannedFar||density!=plannedDensity))
            {
                IWorldField source=surface.BaseField is IWorldFieldSnapshotProvider snapshots?snapshots.CaptureSnapshot():surface.BaseField;
                planned=eye;plannedNear=near;plannedFar=far;plannedDensity=density;havePlan=true;
                var sampler=new SurfaceMaterialSampler(source,surface.Profile);
                job=Task.Run(()=>Build(sampler,eye,near,far,density));
            }
            if(samples!=null)
            {
                int work=0;
                while(upload<samples.Count&&work++<1000)
                {
                    Sample s=samples[upload++];
                    pendingFarthest=Math.Max(pendingFarthest,Math.Sqrt(Travel(s.Position,planned)));
                    #if UNITY_5_3_OR_NEWER
                    if(gpu!=null)
                    {
                        var p=prototypes[s.Id];
                        pendingGpu[upload-1]=new ForestGpuRenderer.Instance{
                            PositionSpan=new Vector4((float)(s.Position.X-planned.X),(float)(s.Position.Y-planned.Y)+(p.MinY+p.MaxY)*.5f*s.Height,(float)(s.Position.Z-planned.Z),ForestBillboard.Span(p)),
                            Style=new Vector4(s.Width,s.PackedId,s.Yaw*(float)(Math.PI/180),s.Appearance)};
                    }
                    else
                    #endif
                    {
                    if(!open.TryGetValue(s.Id,out Batch batch)||batch.Count==1023){batch=pool.Count>0?pool.Pop():new Batch();batch.Id=s.Id;batch.Count=0;open[s.Id]=batch;next.Add(batch);}
                    Vector3 position=new Vector3((float)(s.Position.X-origin.X),(float)(s.Position.Y-origin.Y),(float)(s.Position.Z-origin.Z));
                    // The unchanged legacy CPU atlas shader has no cluster/tint
                    // payload. Retain real-size tree cards in that fallback.
                    batch.Matrices[batch.Count++]=Matrix4x4.TRS(position,Quaternion.Euler(0,s.Yaw,0),new Vector3(s.Height,s.Height,s.Height));
                    var p=prototypes[s.Id];float extent=ForestBillboard.Span(p)*s.Height*1.415f;
                    Bounds b=new Bounds(position+Vector3.up*((p.MinY+p.MaxY)*.5f*s.Height),Vector3.one*extent);
                    if(batch.Count==1)batch.Bounds=b;else batch.Bounds.Encapsulate(b);
                    }
                    if((Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency>=1.5)break;
                }
                if(upload==samples.Count)
                {
                    #if UNITY_5_3_OR_NEWER
                    if(gpu!=null){gpu.Publish(pendingGpu,planned);pendingGpu=null;}
                    #endif
                    var old=batches;batches=next;next=old;Recycle(next);ResidentStands=samples.Count;
                    PublishedFarDistance=plannedFar;FarthestStandDistance=pendingFarthest;samples=null;open.Clear();
                }
            }
            material.SetFloat("_HorizonNear",(float)near);material.SetFloat("_HorizonFar",(float)far);
            DrawCalls=VisibleStands=0;
            #if UNITY_5_3_OR_NEWER
            if(gpu!=null)
            {
                DrawCalls=gpu.Draw(camera,eye,renderOrigin,near,far);
                // Actual visible count stays on the GPU. Resident count remains
                // observable; there is deliberately no gameplay readback/stall.
                VisibleStands=-1;
            }
            else
            #endif
            {
            GeometryUtility.CalculateFrustumPlanes(camera,planes);
            foreach(Batch b in batches)if(GeometryUtility.TestPlanesAABB(planes,b.Bounds))
            {
                var render=new RenderParams(material){camera=camera,worldBounds=b.Bounds,shadowCastingMode=ShadowCastingMode.Off,receiveShadows=false,lightProbeUsage=LightProbeUsage.Off};
                Graphics.RenderMeshInstanced(render,meshes[b.Id],0,b.Matrices,b.Count);DrawCalls++;VisibleStands+=b.Count;
            }
            }
            LastWorkMilliseconds=(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;
        }
        List<Sample> Build(SurfaceMaterialSampler sampler,WorldPoint eye,double near,double far,double density)
        {
            var result=new List<Sample>();if(far<=near||density<=0)return result;
            var field=new SurfaceWorldField(sampler.BaseField,sampler.Profile,new HeightBounds(-20000,30000));
            const double rootSize=128*1024;
            double rootX=Math.Floor(eye.X/rootSize),rootZ=Math.Floor(eye.Z/rootSize);
            HeightBounds bounds=field.QueryBounds((rootX-1)*rootSize,(rootZ-1)*rootSize,(rootX+2)*rootSize,(rootZ+2)*rootSize);
            LodPlan plan=LodPlanner.Build(field,eye,new LodSettings(128,10,far,2.2,256,bounds.Minimum-100,bounds.Maximum+100));
            foreach(LodTile tile in plan.Tiles)
            {
                double spacing=tile.SizeMeters/CanopySamplesPerAxis;
                for(int z=0;z<CanopySamplesPerAxis;z++)for(int x=0;x<CanopySamplesPerAxis;x++)
                {
                    double px=tile.OriginXMeters+(x+.5)*spacing,pz=tile.OriginZMeters+(z+.5)*spacing;
                    double horizontal=(px-eye.X)*(px-eye.X)+(pz-eye.Z)*(pz-eye.Z);
                    if(horizontal>far*far)continue;
                    double noise=ForestBiomes.Noise(px/17,pz/17,seed+4049);
                    px+=(noise-.5)*spacing*.7;pz+=(ForestBiomes.Noise(px/19,pz/19,seed+4051)-.5)*spacing*.7;
                    SurfaceColumn c=sampler.EvaluateColumn(px,pz);ForestStand stand=ForestBiomes.Sample(c,px,pz,seed);
                    if(!CanopyEligible(c,stand,sampler.Profile))continue;
                    int id=(int)stand.Species*4+(stand.Stage==ForestStage.MatureOpen?2:0)+(noise>.5?1:0);
                    var p=prototypes[id];double dx=eye.X-px,dz=eye.Z-pz,dy=eye.Y-(c.GroundHeightMeters+(p.MinY+p.MaxY)*.5);
                    double distance=Math.Sqrt(dx*dx+dy*dy+dz*dz);
                    if(distance<near-160||distance>far+160)continue;
                    double span=ForestBillboard.Span(p),crown=Math.Min(span,p.Radius*2*1.04);
                    // These are metres of forest patch, never tree dimensions.
                    // The old radius divisor stretched the height-based square
                    // by Span/(2*Radius), especially on mature conifers.
                    double patchWidth=Math.Min(MaximumPatchWidthMeters,Math.Max(crown,spacing*1.10));
                    double gx=0,gz=0;
                    if(patchWidth>crown*2&&!PatchFits(sampler,stand,c,px,pz,patchWidth,(p.MaxY-p.MinY)*.35,seed,out gx,out gz))
                    {patchWidth=crown;gx=gz=0;}
                    double region=ForestBiomes.Noise(px/640,pz/640,unchecked(seed+5101));
                    double local=ForestBiomes.Noise(px/96,pz/96,unchecked(seed+5107));
                    double growth=Clamp(.58*region+.27*local+.15*noise,0,1);
                    uint h=Quantize(growth),tint=Quantize(.72*region+.28*local);
                    float height=(float)(.86+.28*h/63.0);
                    double cover=1-Math.Exp(-Math.Max(0,stand.Density*density)*3.2);
                    cover*=.82+.18*ForestBiomes.Noise(px/180,pz/180,unchecked(seed+5113));
                    uint appearance=Quantize(crown/span)|(h<<6)|(tint<<12)|(Quantize(cover)<<18);
                    uint slope=(uint)(Math.Round(Clamp(gx,-1,1)*511)+511)|((uint)(Math.Round(Clamp(gz,-1,1)*511)+511)<<10);
                    float packedId=id+slope/1048576f;
                    result.Add(new Sample(id,new WorldPoint(px,c.GroundHeightMeters-.05,pz),(float)(noise*360),(float)(patchWidth/span),height,packedId,appearance));
                }
            }
            if(result.Count>MaximumCanopySamples)throw new InvalidOperationException("Canopy sample budget exceeded.");
            return result;
        }
        static bool CanopyEligible(SurfaceColumn c,ForestStand stand,SurfaceProfile profile)
        {
            // Preserve source biome/water/cold/slope exclusions, and do not paint
            // dense forest into the configured alpine band even on coarse data.
            return stand.HasTrees&&c.IsLand&&!c.HasWater&&c.GroundHeightMeters<profile.SnowlineMeters-350;
        }
        static bool PatchFits(SurfaceMaterialSampler sampler,ForestStand stand,SurfaceColumn center,double x,double z,double width,double tolerance,int seed,out double gx,out double gz)
        {
            double half=width*.707107;
            var a=sampler.EvaluateColumn(x-half,z-half);var b=sampler.EvaluateColumn(x+half,z-half);
            var c=sampler.EvaluateColumn(x-half,z+half);var d=sampler.EvaluateColumn(x+half,z+half);
            gx=((b.GroundHeightMeters+d.GroundHeightMeters)-(a.GroundHeightMeters+c.GroundHeightMeters))/(4*half);
            gz=((c.GroundHeightMeters+d.GroundHeightMeters)-(a.GroundHeightMeters+b.GroundHeightMeters))/(4*half);
            bool Same(SurfaceColumn q,double px,double pz)
            {var s=ForestBiomes.Sample(q,px,pz,seed);return CanopyEligible(q,s,sampler.Profile)&&s.Species==stand.Species;}
            if(!Same(a,x-half,z-half)||!Same(b,x+half,z-half)||!Same(c,x-half,z+half)||!Same(d,x+half,z+half))return false;
            double sx=gx,sz=gz;
            double Error(SurfaceColumn q,double dx,double dz)=>Math.Abs(q.GroundHeightMeters-center.GroundHeightMeters-dx*sx-dz*sz);
            // A patch follows its sampled terrain plane. Curved/cliff patches
            // fall back to a real-size crown instead of floating across a valley.
            return Math.Abs(gx)<=1&&Math.Abs(gz)<=1&&Math.Max(Math.Max(Error(a,-half,-half),Error(b,half,-half)),Math.Max(Error(c,-half,half),Error(d,half,half)))<=tolerance;
        }
        static double Clamp(double x,double lo,double hi)=>Math.Max(lo,Math.Min(hi,x));
        static uint Quantize(double x)=>(uint)Math.Round(Clamp(x,0,1)*63);
        static double Travel(WorldPoint a,WorldPoint b)=>(a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z)+(a.Y-b.Y)*(a.Y-b.Y);
        static void Rebase(List<Batch> batches,Vector3 delta)
        {foreach(Batch b in batches){for(int i=0;i<b.Count;i++){Matrix4x4 m=b.Matrices[i];m.m03+=delta.x;m.m13+=delta.y;m.m23+=delta.z;b.Matrices[i]=m;}b.Bounds.center+=delta;}}
        public void Invalidate(){havePlan=false;}
        public uint ReadGpuVisibleCountForValidation()
        {
            #if UNITY_5_3_OR_NEWER
            return gpu==null?0:gpu.ReadVisibleCountForValidation();
            #else
            return 0;
            #endif
        }
        void Recycle(List<Batch> list){foreach(Batch b in list){b.Count=0;pool.Push(b);}list.Clear();}
        public void Dispose()
        {
            disposed=true;batches.Clear();next.Clear();pool.Clear();samples=null;
            #if UNITY_5_3_OR_NEWER
            gpu?.Dispose();pendingGpu=null;
            #endif
            if(job!=null)job.ContinueWith(t=>{var e=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);job=null;UnityEngine.Object.Destroy(material);
        }
    }
}
