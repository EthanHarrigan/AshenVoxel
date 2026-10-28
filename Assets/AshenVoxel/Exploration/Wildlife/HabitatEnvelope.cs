using System;
namespace AshenVoxel.WildlifeDraft
{
    public readonly struct HabitatEnvelope
    {
        public readonly double MinX,MinY,MinZ,MaxX,MaxY,MaxZ;
        public HabitatEnvelope(double x,double y,double z,bool bird,double beeRadiusScale=1)
        {
            if(double.IsNaN(x)||double.IsInfinity(x)||double.IsNaN(y)||double.IsInfinity(y)||double.IsNaN(z)||double.IsInfinity(z))
                throw new ArgumentOutOfRangeException(nameof(x));
            if(double.IsNaN(beeRadiusScale)||double.IsInfinity(beeRadiusScale)||beeRadiusScale<.1||beeRadiusScale>1)throw new ArgumentOutOfRangeException(nameof(beeRadiusScale));
            double radius=bird?36.75:6*beeRadiusScale+.3;
            MinX=x-radius;MaxX=x+radius;MinZ=z-radius;MaxZ=z+radius;
            MinY=y+(bird?9.25:.15);MaxY=y+(bird?26.75:2.75);
        }
        public bool Contains(double x,double y,double z)=>x>=MinX&&x<=MaxX&&y>=MinY&&y<=MaxY&&z>=MinZ&&z<=MaxZ;
    }
    // Authority must certify the WHOLE conservative volume using actual terrain
    // and solid bounds. Sparse path samples or Physics-only terrain omissions
    // are not an acceptable implementation of this contract.
    public interface IHabitatAuthority
    {
        long Revision {get;}
        bool IsWholeVolumeClear(HabitatEnvelope volume);
    }
}
