using System;
using System.Collections.Generic;
using System.Globalization;

namespace AshenVoxel.WorldScale
{
    /// <summary>Bounded, yaw-independent learned-source overview demand. Call on
    /// the controller's planning cadence, not per frame. Coverage must come from
    /// the matching verified overview field; mesh spacing is unrelated.</summary>
    public static class ExplorationOverviewDemand
    {
        public const int MaximumCells=8192;
        public const double CoreMeters=2560,InfluencePaddingMeters=320,ProtectionMeters=2048;
        public readonly struct Cell
        {
            public readonly long CellX,CellZ;
            public readonly ExplorationRefinementBounds CoreBounds,InfluenceBounds;
            public readonly double DistanceSquared;
            public string Key=>CellX.ToString(CultureInfo.InvariantCulture)+"/"+CellZ.ToString(CultureInfo.InvariantCulture)+"/2560";
            internal Cell(long x,long z,WorldPoint observer)
            {
                CellX=x;CellZ=z;CoreBounds=new ExplorationRefinementBounds(x*CoreMeters,z*CoreMeters,(x+1)*CoreMeters,(z+1)*CoreMeters);
                InfluenceBounds=CoreBounds.Expanded(InfluencePaddingMeters);
                double dx=(x+.5)*CoreMeters-observer.X,dz=(z+.5)*CoreMeters-observer.Z;DistanceSquared=dx*dx+dz*dz;
            }
        }
        public sealed class Plan
        {
            public readonly int PassSpacingMeters,CompletedCells,TotalCells,Completed160Cells,Completed80Cells,Completed40Cells,ScannedCells;
            public readonly Cell[] Desired;
            public readonly Cell? NextRequest;
            internal Plan(Cell[] desired,int completed160,int completed80,int completed40,int scanned,Cell? next,int spacing)
            {
                Desired=desired;TotalCells=desired.Length;Completed160Cells=completed160;Completed80Cells=completed80;Completed40Cells=completed40;ScannedCells=scanned;
                PassSpacingMeters=spacing;CompletedCells=spacing==160?completed160:spacing==80?completed80:spacing==40?completed40:desired.Length;NextRequest=next;
            }
        }
        public static Plan Build(WorldPoint observer,double radiusMeters,ExplorationRefinementBounds bakedBounds,ExplorationOverviewPatchInfo[] coverage)
        {
            Numeric.Finite(radiusMeters,nameof(radiusMeters));
            if(radiusMeters<20000||radiusMeters>100000)throw new ArgumentOutOfRangeException(nameof(radiusMeters),"Overview horizon must be20..100km");
            if(coverage==null||coverage.Length>MaximumCells)throw new ArgumentException("Bounded overview coverage is required",nameof(coverage));
            double[] coordinates={observer.X,observer.Z,bakedBounds.MinimumX,bakedBounds.MinimumZ,bakedBounds.MaximumX,bakedBounds.MaximumZ};
            foreach(double value in coordinates){Numeric.Finite(value,nameof(bakedBounds));if(Math.Abs(value)>1e9)throw new ArgumentOutOfRangeException(nameof(bakedBounds));}
            if(bakedBounds.MinimumX>=bakedBounds.MaximumX||bakedBounds.MinimumZ>=bakedBounds.MaximumZ)throw new ArgumentException("Positive baked bounds required",nameof(bakedBounds));
            long x0=(long)Math.Floor(bakedBounds.MinimumX/CoreMeters),z0=(long)Math.Floor(bakedBounds.MinimumZ/CoreMeters);
            long x1=(long)Math.Ceiling(bakedBounds.MaximumX/CoreMeters)-1,z1=(long)Math.Ceiling(bakedBounds.MaximumZ/CoreMeters)-1;
            long count=checked((x1-x0+1)*(z1-z0+1));
            if(count>MaximumCells)throw new InvalidOperationException("Whole-world overview scan requires "+count.ToString(CultureInfo.InvariantCulture)+" cells; limit is8192. Demand was not silently truncated.");
            var spacingByCell=new Dictionary<(long,long),int>(coverage.Length);
            foreach(var patch in coverage)
            {
                if(patch.SpacingMeters!=160&&patch.SpacingMeters!=80&&patch.SpacingMeters!=40)throw new ArgumentException("Verified overview coverage spacing must be160, 80 or 40 m",nameof(coverage));
                var core=new ExplorationRefinementBounds(patch.CellX*CoreMeters,patch.CellZ*CoreMeters,(patch.CellX+1)*CoreMeters,(patch.CellZ+1)*CoreMeters);
                if(patch.CoreBounds.MinimumX>core.MinimumX||patch.CoreBounds.MinimumZ>core.MinimumZ||patch.CoreBounds.MaximumX<core.MaximumX||patch.CoreBounds.MaximumZ<core.MaximumZ)
                    throw new ArgumentException("Coverage must contain its complete canonical overview core",nameof(coverage));
                var key=(patch.CellX,patch.CellZ);
                if(!spacingByCell.TryGetValue(key,out int previous)||patch.SpacingMeters<previous)spacingByCell[key]=patch.SpacingMeters;
            }
            var desired=new List<Cell>((int)count);double radiusSquared=radiusMeters*radiusMeters,protectionSquared=ProtectionMeters*ProtectionMeters;
            for(long z=z0;z<=z1;z++)for(long x=x0;x<=x1;x++)
            {
                var cell=new Cell(x,z,observer);
                // Edge cores are intentionally not clipped: the provider owns a
                // full canonical core/halo just outside the map's visible bounds.
                if(cell.CoreBounds.DistanceSquared(observer.X,observer.Z)>radiusSquared||cell.InfluenceBounds.DistanceSquared(observer.X,observer.Z)<=protectionSquared)continue;
                desired.Add(cell);
            }
            desired.Sort((a,b)=>{int distance=b.DistanceSquared.CompareTo(a.DistanceSquared);if(distance!=0)return distance;int z=a.CellZ.CompareTo(b.CellZ);return z!=0?z:a.CellX.CompareTo(b.CellX);});
            int ready160=0,ready80=0,ready40=0;Cell? missing160=null,missing80=null,missing40=null;
            foreach(var cell in desired)
            {
                bool ready=spacingByCell.TryGetValue((cell.CellX,cell.CellZ),out int spacing);
                if(ready&&spacing<=160)ready160++;else if(!missing160.HasValue)missing160=cell;
                if(ready&&spacing<=80)ready80++;else if(!missing80.HasValue)missing80=cell;
                if(ready&&spacing<=40)ready40++;else if(!missing40.HasValue)missing40=cell;
            }
            return new Plan(desired.ToArray(),ready160,ready80,ready40,(int)count,missing160??missing80??missing40,missing160.HasValue?160:missing80.HasValue?80:missing40.HasValue?40:0);
        }
    }
}
