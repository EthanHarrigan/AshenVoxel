using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
namespace AshenVoxel.WorldScale
{
    public readonly struct TileKey : IEquatable<TileKey>
    {
        public readonly long X,Z;public readonly int Level;
        public TileKey(long x,long z,int level){X=x;Z=z;Level=level;}
        public bool Equals(TileKey k)=>X==k.X&&Z==k.Z&&Level==k.Level;
        public override bool Equals(object other)=>other is TileKey k&&Equals(k);
        public override int GetHashCode(){unchecked{return ((X.GetHashCode()*397)^Z.GetHashCode())*397^Level;}}
        public override string ToString()=>Level+":"+X+","+Z;
    }
    public sealed class LodTile
    {
        public readonly TileKey Key;
        public readonly double OriginXMeters,OriginZMeters,SizeMeters,MinimumHeight,MaximumHeight;
        public LodTile(TileKey key,double originX,double originZ,double size,double minimumHeight,double maximumHeight)
        {Key=key;OriginXMeters=originX;OriginZMeters=originZ;SizeMeters=size;MinimumHeight=minimumHeight;MaximumHeight=maximumHeight;}
    }
    public sealed class LodSettings
    {
        public readonly double BaseTileSizeMeters,FarDistanceMeters,SplitFactor,MinimumHeightMeters,MaximumHeightMeters;
        public readonly int MaxLevel,MaxTiles;
        public LodSettings(double baseTileSizeMeters=64,int maxLevel=10,double farDistanceMeters=32000,double splitFactor=2.2,int maxTiles=384,double minimumHeightMeters=-2000,double maximumHeightMeters=12000)
        {
            Numeric.Positive(baseTileSizeMeters,nameof(baseTileSizeMeters));Numeric.Positive(farDistanceMeters,nameof(farDistanceMeters));Numeric.Positive(splitFactor,nameof(splitFactor));
            Numeric.Finite(minimumHeightMeters,nameof(minimumHeightMeters));Numeric.Finite(maximumHeightMeters,nameof(maximumHeightMeters));
            if(baseTileSizeMeters>1e9||farDistanceMeters>1e9||maxLevel<0||maxLevel>24||maxTiles<9||maxTiles>4096||minimumHeightMeters>=maximumHeightMeters||splitFactor>20||farDistanceMeters>baseTileSizeMeters*Math.Pow(2,maxLevel))throw new ArgumentOutOfRangeException("LOD limits require farDistance <= root tile size,9..4096 tiles and valid height bounds.");
            BaseTileSizeMeters=baseTileSizeMeters;MaxLevel=maxLevel;FarDistanceMeters=farDistanceMeters;SplitFactor=splitFactor;MaxTiles=maxTiles;MinimumHeightMeters=minimumHeightMeters;MaximumHeightMeters=maximumHeightMeters;
        }
    }
    public sealed class LodPlan
    {
        public IReadOnlyList<LodTile> Tiles {get;internal set;}
        public int PendingSplits {get;internal set;}
        public bool CapacityLimited=>PendingSplits>0;
        public int SampleCount {get;internal set;}
        public string SourceIdentity {get;internal set;}
    }
    /// <summary>Bounded 2:1 balanced height-proxy selection. Uses conservative configured
    /// height bounds for visibility and conservative field bounds for refinement when supported.
    /// Custom fields without bounds use nine samples (not a conservative mountain enclosure).
    /// Unity still performs frustum/depth culling; height proxies still need silhouette/error LOD.</summary>
    public static class LodPlanner
    {
        public static LodPlan Build(IWorldField field,WorldPoint camera,LodSettings settings=null)
        {
            if(field==null)throw new ArgumentNullException(nameof(field));var s=settings??new LodSettings();
            var leaves=new List<LodTile>();int samples=0;
            double rootSize=s.BaseTileSizeMeters*Math.Pow(2,s.MaxLevel);
            long cx=Grid(camera.X,rootSize),cz=Grid(camera.Z,rootSize);
            // 3x3 aligned roots cover the complete far-distance disk at any camera offset.
            for(long z=cz-1;z<=cz+1;z++)for(long x=cx-1;x<=cx+1;x++)
            {
                double ox=x*rootSize,oz=z*rootSize;
                if(Distance(camera,ox,oz,rootSize,s.MinimumHeightMeters,s.MaximumHeightMeters)>s.FarDistanceMeters)continue;
                leaves.Add(Create(field,new TileKey(x,z,s.MaxLevel),s,ref samples));
            }
            while(true)
            {
                LodTile chosen=null;List<LodTile> closure=null;double best=-1;
                foreach(var leaf in leaves)
                {
                    double priority=Priority(leaf,camera,s);if(priority<=1||priority<=best)continue;
                    var required=new List<LodTile>();Collect(leaf,leaves,required);
                    if(leaves.Count+required.Count*3>s.MaxTiles)continue;
                    chosen=leaf;closure=required;best=priority;
                }
                if(chosen==null)break;
                closure.Sort((a,b)=>b.Key.Level.CompareTo(a.Key.Level));
                foreach(var tile in closure)
                {
                    leaves.Remove(tile);int level=tile.Key.Level-1;
                    for(int z=0;z<2;z++)for(int x=0;x<2;x++)leaves.Add(Create(field,new TileKey(tile.Key.X*2+x,tile.Key.Z*2+z,level),s,ref samples));
                }
            }
            int pending=0;foreach(var leaf in leaves)if(Priority(leaf,camera,s)>1)pending++;
            leaves.Sort((a,b)=>{int v=a.Key.Level.CompareTo(b.Key.Level);if(v==0)v=a.Key.Z.CompareTo(b.Key.Z);return v==0?a.Key.X.CompareTo(b.Key.X):v;});
            return new LodPlan{Tiles=new ReadOnlyCollection<LodTile>(leaves),PendingSplits=pending,SampleCount=samples,SourceIdentity=field.SourceIdentity};
        }
        static long Grid(double n,double step)
        {double v=Math.Floor(n/step);if(v<long.MinValue+2.0||v>long.MaxValue-2.0||Math.Abs(n)>1e12)throw new ArgumentOutOfRangeException(nameof(n),"Prototype world coordinates are limited to +/-1e12m.");return (long)v;}
        static LodTile Create(IWorldField field,TileKey key,LodSettings settings,ref int count)
        {
            double size=settings.BaseTileSizeMeters*Math.Pow(2,key.Level),x=key.X*size,z=key.Z*size;
            double lo=double.MaxValue,hi=double.MinValue;
            if(field is IWorldFieldBounds bounded)
            {HeightBounds range=bounded.QueryBounds(x,z,x+size,z+size);lo=range.Minimum;hi=range.Maximum;count++;}
            else for(int dz=0;dz<=2;dz++)for(int dx=0;dx<=2;dx++)
            {double h=field.Sample(x+dx*size*.5,z+dz*size*.5).HeightMeters;lo=Math.Min(lo,h);hi=Math.Max(hi,h);count++;}
            if(lo<settings.MinimumHeightMeters||hi>settings.MaximumHeightMeters)throw new InvalidOperationException("World field exceeds conservative LOD height bounds; expand settings before rendering.");
            return new LodTile(key,x,z,size,lo,hi);
        }
        static double Priority(LodTile tile,WorldPoint camera,LodSettings settings)
        {
            if(tile.Key.Level==0)return 0;
            double distance=Distance(camera,tile.OriginXMeters,tile.OriginZMeters,tile.SizeMeters,tile.MinimumHeight-32,tile.MaximumHeight+32);
            return tile.SizeMeters*settings.SplitFactor/Math.Max(1,distance);
        }
        static void Collect(LodTile target,List<LodTile> leaves,List<LodTile> required)
        {
            if(required.Contains(target))return;required.Add(target);
            foreach(var neighbor in leaves)
                if(neighbor.Key.Level>target.Key.Level&&Adjacent(target,neighbor))Collect(neighbor,leaves,required);
        }
        public static bool Adjacent(LodTile a,LodTile b)
        {
            double ax=a.OriginXMeters,az=a.OriginZMeters,bx=b.OriginXMeters,bz=b.OriginZMeters,ae=ax+a.SizeMeters,aze=az+a.SizeMeters,be=bx+b.SizeMeters,bze=bz+b.SizeMeters;
            return ((ae==bx||be==ax)&&Math.Min(aze,bze)>Math.Max(az,bz))||((aze==bz||bze==az)&&Math.Min(ae,be)>Math.Max(ax,bx));
        }
        static double Distance(WorldPoint p,double x,double z,double size,double minY,double maxY)
        {
            double dx=Math.Max(0,Math.Max(x-p.X,p.X-(x+size))),dz=Math.Max(0,Math.Max(z-p.Z,p.Z-(z+size))),dy=Math.Max(0,Math.Max(minY-p.Y,p.Y-maxY));
            return Math.Sqrt(dx*dx+dy*dy+dz*dz);
        }
    }
}
