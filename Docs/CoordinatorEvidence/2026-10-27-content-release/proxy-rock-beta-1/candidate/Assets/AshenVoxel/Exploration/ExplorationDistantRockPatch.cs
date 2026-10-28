using System;
using System.Collections.Generic;
using System.Diagnostics;
using AshenVoxel.WorldScale;
using UnityEngine;
using UnityEngine.Rendering;

namespace AshenVoxel.Exploration
{
    /// <summary>Sparse exaggerated far-rock silhouettes, not physical placements.
    /// Reuses loaded authored meshes/material; no new mesh, collider, asset or worker.</summary>
    public sealed class ExplorationDistantRockPatch:MonoBehaviour,IDisposable
    {
        public const int MaximumResident=2048,MaximumDrawn=512,MaximumModels=8,GridMeters=256;
        public const long MaximumDrawnTriangles=300000;
        public const string RenderPolicyIdentity="far-rock-render-lod1-300k-v1";
        public bool ShowDistantRocks=true;
        [Range(.25f,2)] public float SizeMultiplier=1;
        [Range(0,.2f)] public float LargeOutcropFraction=.05f;
        [Range(.45f,.8f)] public float EmbedFraction=.6f;
        public int CandidatesPerTick=32;
        public double WorkMilliseconds=.5;
        public int ResidentRepresentatives=>publishedCount;
        public int RemainingCandidates=>planning?side*side-cursor:0;
        public int DrawnRepresentatives{get;private set;}
        public int DrawCalls{get;private set;}
        public long DrawnTriangles{get;private set;}
        public int TriangleBudgetSkips{get;private set;}
        public int RejectedSites{get;private set;}
        public int SourceResets{get;private set;}
        public int CapacityRejected{get;private set;}
        public double LastBuildMilliseconds{get;private set;}
        public string Status{get;private set;}="Not initialized";
        public string PublishedSource=>publishedSource;
        public const string RepresentationIdentity="far-rock-representatives-v1;grid256;range1000..10000;render-only;exaggerated";
        sealed class Model
        {public Mesh Mesh;public Material Material;public Bounds Bounds;public int Triangles;public readonly Matrix4x4[] Matrices=new Matrix4x4[MaximumDrawn];public int Count;public Bounds DrawBounds;}
        public readonly struct Representative
        {
            public readonly long GridX,GridZ;public readonly double X,Z,BaseY;public readonly float Width,Depth,Height,Yaw;public readonly int Model;
            public Representative(long gx,long gz,double x,double z,double y,float width,float depth,float height,float yaw,int model)
            {GridX=gx;GridZ=gz;X=x;Z=z;BaseY=y;Width=width;Depth=depth;Height=height;Yaw=yaw;Model=model;}
        }
        readonly struct Visible
        {public readonly int Index;public readonly double Score;public Visible(int index,double score){Index=index;Score=score;}}
        readonly Model[] models=new Model[MaximumModels];int modelCount;
        Representative[] published=new Representative[MaximumResident],pending=new Representative[MaximumResident];int publishedCount,pendingCount;
        readonly List<Visible> visible=new List<Visible>(MaximumResident);readonly Plane[] planes=new Plane[6];
        static readonly Comparison<Visible> Rank=(a,b)=>b.Score.CompareTo(a.Score);
        static readonly int[] PreferredModels={201,205,207,209,213,217,219,221};
        VegetationStreaming population;IWorldField live,snapshot;int seed;bool disposed=true,planning,havePublished;
        float planSize,planLarge,planEmbed,publishedSize,publishedLarge,publishedEmbed;
        string planSource="",publishedSource="";long gridX,gridZ;int side,cursor;WorldPoint planned,publishedEye;
        static long Now=>Stopwatch.GetTimestamp();
        static double Ms(long start)=>(Now-start)*1000.0/Stopwatch.Frequency;
        public void Initialize(VegetationStreaming owner,IWorldField source,int worldSeed)
        {Dispose();population=owner??throw new ArgumentNullException(nameof(owner));live=source??throw new ArgumentNullException(nameof(source));seed=worldSeed;disposed=false;Status="Waiting for existing rock prototypes";}
        static float Bounded(float v,float a,float b,float fallback)=>float.IsNaN(v)||float.IsInfinity(v)?fallback:Math.Max(a,Math.Min(b,v));
        static bool Finite(double x)=>!double.IsNaN(x)&&!double.IsInfinity(x);
        static double Distance2(WorldPoint a,WorldPoint b){double x=a.X-b.X,z=a.Z-b.Z;return x*x+z*z;}
        static ulong Hash(long x,long z,int seed)
        {unchecked{ulong h=(ulong)x*0x9e3779b185ebca87UL^(ulong)z*0xc2b2ae3d27d4eb4fUL^(uint)seed;h^=h>>30;h*=0xbf58476d1ce4e5b9UL;h^=h>>27;h*=0x94d049bb133111ebUL;return h^(h>>31);}}
        static double Unit(ulong h)=>(h>>11)*(1.0/9007199254740992.0);
        bool BorrowModels()
        {
            if(modelCount!=0)return true;
            for(int i=0;i<PreferredModels.Length;i++)
                if(population.TryBorrowCoarseRock(PreferredModels[i],out var mesh,out var material,out var bounds,out int triangles))
                    models[modelCount++]=new Model{Mesh=mesh,Material=material,Bounds=bounds,Triangles=triangles};
            return modelCount>0;
        }
        void BeginPlan(WorldPoint eye)
        {
            string before=live.SourceIdentity;
            if(string.IsNullOrEmpty(before)||before.Length>16384)throw new InvalidOperationException("Bounded source identity required");
            snapshot=live is IWorldFieldSnapshotProvider provider?provider.CaptureSnapshot():live;
            if(snapshot==null||snapshot.SourceIdentity!=before||live.SourceIdentity!=before)throw new InvalidOperationException("Distant-rock capture changed source");
            planSize=Bounded(SizeMultiplier,.25f,2,1);planLarge=Bounded(LargeOutcropFraction,0,.2f,.05f);planEmbed=Bounded(EmbedFraction,.45f,.8f,.6f);
            planned=eye;planSource=before;side=81;gridX=(long)Math.Floor(eye.X/GridMeters)-40;gridZ=(long)Math.Floor(eye.Z/GridMeters)-40;cursor=0;pendingCount=0;CapacityRejected=RejectedSites=0;planning=true;
        }
        // A candidate performs at most nine height queries. Frame count/time limits
        // are checked between candidates, never described as a query-time sandbox.
        public static bool TryRepresentative(IWorldField source,long gx,long gz,int seed,int modelCount,out Representative result,float sizeMultiplier=1,float largeFraction=.05f,float embedFraction=.6f)
        {
            result=default;if(!Finite(sizeMultiplier)||sizeMultiplier<.25f||sizeMultiplier>2||!Finite(largeFraction)||largeFraction<0||largeFraction>.2f||!Finite(embedFraction)||embedFraction<.45f||embedFraction>.8f)return false;
            if(source==null||modelCount<1||modelCount>MaximumModels||Math.Abs((double)gx)>4000000||Math.Abs((double)gz)>4000000)return false;
            ulong h=Hash(gx,gz,seed);if(Unit(h)>.22)return false;
            double x=(gx+.15+Unit(Hash(gx,gz,seed^0x2193))*.7)*GridMeters,z=(gz+.15+Unit(Hash(gx,gz,seed^0x3197))*.7)*GridMeters;
            bool large=Unit(Hash(gx,gz,seed^0x6947))<largeFraction;
            float width=(float)((large?90:18)+Unit(Hash(gx,gz,seed^0x4571))*(large?90:47))*sizeMultiplier,depth=(float)((large?70:15)+Unit(Hash(gx,gz,seed^0x5731))*(large?70:40))*sizeMultiplier,yaw=(float)(Unit(Hash(gx,gz,seed^0x1983))*360);
            double angle=yaw*Math.PI/180,co=Math.Abs(Math.Cos(angle)),si=Math.Abs(Math.Sin(angle));
            double hx=(co*width+si*depth)*.5,hz=(si*width+co*depth)*.5,minimum=double.PositiveInfinity,maximum=double.NegativeInfinity;
            for(int iz=-1;iz<=1;iz++)for(int ix=-1;ix<=1;ix++)
            {
                SurfaceSample s=source.Sample(x+ix*hx,z+iz*hz);if(!s.IsLand||!Finite(s.HeightMeters)||s.HeightMeters<=0)return false;
                minimum=Math.Min(minimum,s.HeightMeters);maximum=Math.Max(maximum,s.HeightMeters);
            }
            double relief=maximum-minimum;float height=(float)((large?90:30)+Unit(Hash(gx,gz,seed^0x7713))*(large?90:45))*sizeMultiplier;
            if(height>360||relief>height*.35)return false;
            // Default: bury most of the outcrop below the minimum sampled surface.
            // Reject footprints whose high side would consume most of the rock.
            double baseY=minimum-height*embedFraction;
            if(maximum>baseY+height*.98)return false;
            result=new Representative(gx,gz,x,z,baseY,width,depth,height,yaw,(int)(Hash(gx,gz,seed^0x5167)%(uint)modelCount));return true;
        }
        public static float Fade(double distance)
        {double a=Math.Max(0,Math.Min(1,(distance-1000)/600)),b=Math.Max(0,Math.Min(1,(10000-distance)/1500));a=a*a*(3-2*a);b=b*b*(3-2*b);return (float)(a*b);}
        public void TickAfterCamera(Camera camera,WorldPoint eye,WorldPoint renderOrigin)
        {
            DrawnRepresentatives=DrawCalls=TriangleBudgetSkips=0;DrawnTriangles=0;
            if(disposed||camera==null||!isActiveAndEnabled)return;
            if(!ShowDistantRocks){ClearPlans();Array.Clear(models,0,models.Length);modelCount=0;Status="Disabled";return;}
            if(population==null||!population.Ready){ClearPlans();modelCount=0;Array.Clear(models,0,models.Length);Status="Waiting for existing rock prototypes";return;}
            if(!Finite(eye.X)||!Finite(eye.Y)||!Finite(eye.Z)||!Finite(renderOrigin.X)||!Finite(renderOrigin.Y)||!Finite(renderOrigin.Z)||Math.Abs(eye.X)>1e9||Math.Abs(eye.Z)>1e9)return;
            try
            {
                if(!BorrowModels()){Status="Existing rock models unavailable";return;}
                string current=live.SourceIdentity;
                if(planning&&planSource!=current){planning=false;snapshot=null;pendingCount=0;SourceResets++;}
                if(!planning&&(!havePublished||current!=publishedSource||Distance2(eye,publishedEye)>512*512||publishedSize!=Bounded(SizeMultiplier,.25f,2,1)||publishedLarge!=Bounded(LargeOutcropFraction,0,.2f,.05f)||publishedEmbed!=Bounded(EmbedFraction,.45f,.8f,.6f)))BeginPlan(eye);
                if(planning)
                {
                    long start=Now;int count=0,cap=Math.Max(1,Math.Min(64,CandidatesPerTick));
                    while(cursor<side*side&&count++<cap)
                    {
                        int i=cursor++;long gx=gridX+i%side,gz=gridZ+i/side;double cx=(gx+.5)*GridMeters-planned.X,cz=(gz+.5)*GridMeters-planned.Z;
                        if(cx*cx+cz*cz<=10300d*10300&&TryRepresentative(snapshot,gx,gz,seed,modelCount,out var sample,planSize,planLarge,planEmbed))
                        {if(pendingCount<MaximumResident)pending[pendingCount++]=sample;else CapacityRejected++;}
                        else RejectedSites++;
                        if(Ms(start)>=Math.Max(.1,Math.Min(2,WorkMilliseconds)))break;
                    }
                    LastBuildMilliseconds=Ms(start);
                    if(cursor==side*side)
                    {
                        if(planSource==live.SourceIdentity){var old=published;published=pending;pending=old;publishedCount=pendingCount;publishedSource=planSource;publishedEye=planned;publishedSize=planSize;publishedLarge=planLarge;publishedEmbed=planEmbed;havePublished=true;}
                        planning=false;snapshot=null;pendingCount=0;
                    }
                }
                // Never render stale elevations while a replacement source is building.
                if(!havePublished||publishedSource!=live.SourceIdentity){Status="Preparing distant rock landmarks";return;}
                Draw(camera,eye,renderOrigin);Status="Render-only distant rocks: "+DrawnRepresentatives+" / "+publishedCount;
            }
            catch(Exception e){Status=e.GetType().Name+": "+e.Message;planning=false;snapshot=null;havePublished=false;publishedCount=0;}
        }
        void Draw(Camera camera,WorldPoint eye,WorldPoint origin)
        {
            GeometryUtility.CalculateFrustumPlanes(camera,planes);visible.Clear();for(int i=0;i<modelCount;i++)models[i].Count=0;
            for(int i=0;i<publishedCount;i++)
            {
                var s=published[i];double dx=s.X-eye.X,dz=s.Z-eye.Z,dy=s.BaseY+s.Height*.5-eye.Y,d=Math.Sqrt(dx*dx+dy*dy+dz*dz);float fade=Fade(d);if(fade<=.001f)continue;
                float footprint=(float)Math.Sqrt(s.Width*s.Width+s.Depth*s.Depth)*fade;
                var bounds=new Bounds(new Vector3((float)(s.X-origin.X),(float)(s.BaseY+s.Height*fade*.5-origin.Y),(float)(s.Z-origin.Z)),new Vector3(footprint,s.Height*fade,footprint));
                if(!GeometryUtility.TestPlanesAABB(planes,bounds))continue;visible.Add(new Visible(i,s.Height*fade/Math.Max(1,d)));
            }
            visible.Sort(Rank);int accepted=0;long submittedTriangles=0;
            for(int k=0;k<visible.Count&&accepted<MaximumDrawn;k++)
            {
                var s=published[visible[k].Index];var m=models[s.Model];
                if(m.Mesh==null||m.Material==null||m.Triangles<1)continue;
                if(submittedTriangles+m.Triangles>MaximumDrawnTriangles){TriangleBudgetSkips++;continue;}
                submittedTriangles+=m.Triangles;accepted++;
                double dx=s.X-eye.X,dz=s.Z-eye.Z,dy=s.BaseY+s.Height*.5-eye.Y;float fade=Fade(Math.Sqrt(dx*dx+dy*dy+dz*dz));
                var scale=new Vector3(s.Width/m.Bounds.size.x*fade,s.Height/m.Bounds.size.y*fade,s.Depth/m.Bounds.size.z*fade);
                double angle=s.Yaw*Math.PI/180,co=Math.Cos(angle),si=Math.Sin(angle);double lx=m.Bounds.center.x*scale.x,lz=m.Bounds.center.z*scale.z;
                var position=new Vector3((float)(s.X-origin.X-co*lx-si*lz),(float)(s.BaseY-origin.Y-m.Bounds.min.y*scale.y),(float)(s.Z-origin.Z+si*lx-co*lz));
                m.Matrices[m.Count++]=Matrix4x4.TRS(position,Quaternion.Euler(0,s.Yaw,0),scale);
                float radius=(float)Math.Sqrt(s.Width*s.Width+s.Depth*s.Depth)*fade;
                var bounds=new Bounds(new Vector3((float)(s.X-origin.X),(float)(s.BaseY+s.Height*fade*.5-origin.Y),(float)(s.Z-origin.Z)),new Vector3(radius,s.Height*fade,radius));
                if(m.Count==1)m.DrawBounds=bounds;else m.DrawBounds.Encapsulate(bounds);
            }
            for(int i=0;i<modelCount;i++)
            {
                var m=models[i];if(m.Count==0||m.Mesh==null||m.Material==null)continue;
                var render=new RenderParams(m.Material){camera=camera,worldBounds=m.DrawBounds,shadowCastingMode=ShadowCastingMode.Off,receiveShadows=true,layer=population.gameObject.layer,lightProbeUsage=LightProbeUsage.Off};
#if UNITY_5_3_OR_NEWER
                render.motionVectorMode=MotionVectorGenerationMode.Object;
#endif
                Graphics.RenderMeshInstanced(render,m.Mesh,0,m.Matrices,m.Count);DrawCalls++;DrawnRepresentatives+=m.Count;DrawnTriangles+=(long)m.Triangles*m.Count;
            }
        }
        void ClearPlans(){planning=havePublished=false;publishedCount=pendingCount=cursor=0;snapshot=null;publishedSource=planSource="";visible.Clear();DrawnRepresentatives=DrawCalls=TriangleBudgetSkips=0;DrawnTriangles=0;LastBuildMilliseconds=0;RejectedSites=CapacityRejected=SourceResets=0;}
        public void Dispose(){ClearPlans();Array.Clear(models,0,models.Length);modelCount=0;population=null;live=null;disposed=true;Status="Disposed";}
        void OnDisable(){ClearPlans();Array.Clear(models,0,models.Length);modelCount=0;}
        void OnDestroy(){Dispose();}
    }
}
