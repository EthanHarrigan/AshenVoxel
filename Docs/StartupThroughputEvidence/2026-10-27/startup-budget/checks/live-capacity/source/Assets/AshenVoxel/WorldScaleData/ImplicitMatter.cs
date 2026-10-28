using System;
using System.Collections.Generic;
using AshenVoxel.Core;
namespace AshenVoxel.WorldScale
{
    public readonly struct MatterSample
    {
        public readonly MaterialId Material;public readonly uint Tint;
        public MatterSample(MaterialId material,uint tint=0){Material=material;Tint=tint;}
    }
    /// <summary>Analytic stone/layers plus bounded sparse cell edits. No sea-level-to-summit allocation.
    /// This prototype is separate from the finite demo; material dynamics need a residency adapter.</summary>
    public sealed class ImplicitMatter
    {
        readonly IWorldField sourceField;
        public readonly SurfaceMaterialSampler SurfaceSampler;
        readonly Dictionary<WorldCell,MatterSample> edits=new Dictionary<WorldCell,MatterSample>();
        public readonly int MaximumEdits;
        public readonly double StoneDepthMeters;
        public int EditCount=>edits.Count;
        public IWorldField Field=>sourceField;
        public long Revision {get;private set;}
        public ImplicitMatter(IWorldField field,int maximumEdits=1000000,double stoneDepthMeters=64,SurfaceProfile surfaceProfile=null)
        {sourceField=field??throw new ArgumentNullException(nameof(field));if(maximumEdits<1||maximumEdits>10000000)throw new ArgumentOutOfRangeException(nameof(maximumEdits));Numeric.Positive(stoneDepthMeters,nameof(stoneDepthMeters));MaximumEdits=maximumEdits;StoneDepthMeters=stoneDepthMeters;
            SurfaceSampler=field is SurfaceWorldField decorated?decorated.Sampler:new SurfaceMaterialSampler(field,surfaceProfile);}
        public MatterSample Sample(WorldCell cell)
        {
            if(edits.TryGetValue(cell,out MatterSample edit))return edit;
            WorldPoint p=cell.Center;SurfaceColumn column=SurfaceSampler.EvaluateColumn(p.X,p.Z);
            // Deep geology remains implicit. Snow/soil/sediment are real strata;
            // visual sea water is intentionally not a fake solid material cell.
            return SurfaceSampler.SampleMatter(p,column);
        }
        public bool Set(WorldCell cell,MaterialId material,uint tint=0)
        {
            if(!MaterialCatalog.IsValid(material)||!VoxelColor.IsValid(tint)||(material==MaterialId.Air&&tint!=0))throw new ArgumentException("Invalid edited matter.");
            MatterSample current=Sample(cell);if(current.Material==material&&current.Tint==tint)return false;
            if(!edits.ContainsKey(cell)&&edits.Count>=MaximumEdits)throw new InvalidOperationException("Sparse world edit capacity reached; persist/evict a region before adding edits.");
            edits[cell]=new MatterSample(material,tint);Revision++;return true;
        }
        public bool Reset(WorldCell cell){if(!edits.Remove(cell))return false;Revision++;return true;}
        public bool TryGetEdit(WorldCell cell,out MatterSample sample)=>edits.TryGetValue(cell,out sample);
    }
}
