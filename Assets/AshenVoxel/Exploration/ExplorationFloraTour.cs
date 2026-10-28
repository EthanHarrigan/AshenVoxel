using System;
using System.Threading.Tasks;
using AshenVoxel.Core;
using AshenVoxel.WorldScale;

namespace AshenVoxel.Exploration
{
    /// <summary>F1 "Flora tour": finds the nearest place where a plant community (or a young
    /// thicket) grows, on a worker thread over a snapshot of the world field, so biomes can be
    /// reviewed without hunting for them. Search is a square-ring scan outward from the player.</summary>
    public sealed class ExplorationFloraTour
    {
        public const string DenseThicket="Dense young thicket";
        static readonly string[] targets=BuildTargets();
        static string[] BuildTargets()
        {
            var names=Enum.GetNames(typeof(FloraCommunity));var list=new string[names.Length];list[0]=DenseThicket;
            for(int i=1;i<names.Length;i++)list[i]=names[i];return list;
        }
        public int Selected {get;private set;}=1;
        public string Target=>targets[Selected];
        public string Status {get;private set;}="";
        public bool Searching=>job!=null;
        Task<WorldPoint?> job;
        public void Next(){if(job==null)Selected=(Selected+1)%targets.Length;}
        public void Begin(IWorldField field,SurfaceProfile profile,int seed,WorldPoint from,double halfWidth,double halfDepth)
        {
            if(job!=null||field==null||profile==null)return;
            IWorldField source=field is IWorldFieldSnapshotProvider snapshots?snapshots.CaptureSnapshot():field;
            string target=Target;Status="Searching for "+target+"…";
            job=Task.Run(()=>Find(new SurfaceMaterialSampler(source,profile),target,seed,from,halfWidth-64,halfDepth-64));
        }
        /// <summary>Returns a destination once the search finishes (null while running or when nothing was found).</summary>
        public WorldPoint? Poll()
        {
            if(job==null||!job.IsCompleted)return null;
            WorldPoint? result=null;
            if(job.IsFaulted)Status="Search failed: "+job.Exception?.GetBaseException().Message;
            else if(job.Result.HasValue){result=job.Result;Status=Target+" found "+Math.Round(Distance(result.Value))+" m away.";}
            else Status="No "+Target+" within 48 km.";
            job=null;return result;
        }
        WorldPoint origin;double Distance(WorldPoint p)=>Math.Sqrt((p.X-origin.X)*(p.X-origin.X)+(p.Z-origin.Z)*(p.Z-origin.Z));
        WorldPoint? Find(SurfaceMaterialSampler sampler,string target,int seed,WorldPoint from,double hx,double hz)
        {
            origin=from;const double step=160;const int rings=300;bool thicket=target==DenseThicket;
            FloraCommunity wanted=thicket?FloraCommunity.None:(FloraCommunity)Enum.Parse(typeof(FloraCommunity),target);
            for(int r=1;r<=rings;r++)
            {
                for(int i=-r;i<=r;i++)for(int side=0;side<4;side++)
                {
                    double ox=side==0?i:side==1?i:side==2?-r:r,oz=side==0?-r:side==1?r:i;
                    if((side>=2)&&(i==-r||i==r))continue;
                    double x=from.X+ox*step,z=from.Z+oz*step;if(Math.Abs(x)>hx||Math.Abs(z)>hz)continue;
                    SurfaceColumn c=sampler.EvaluateColumn(x,z);
                    bool hit=thicket?ForestBiomes.Sample(c,x,z,seed).Density>1.2:BiomeFlora.Classify(c,x,z,seed)==wanted;
                    if(!hit)continue;
                    // Confirm a neighbouring sample agrees so the destination is inside the community.
                    SurfaceColumn d=sampler.EvaluateColumn(x+12,z+12);
                    bool again=thicket?ForestBiomes.Sample(d,x+12,z+12,seed).Density>1.1:BiomeFlora.Classify(d,x+12,z+12,seed)==wanted;
                    if(!again)continue;
                    double ground=Math.Max(c.GroundHeightMeters,BiomeFlora.IsMarine(wanted)?c.GroundHeightMeters:c.WaterLevelMeters);
                    return new WorldPoint(x,ground+2,z);
                }
            }
            return null;
        }
    }
}
