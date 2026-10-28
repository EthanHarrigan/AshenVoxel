using System;
namespace AshenVoxel.WildlifeDraft
{
    // Complete coverage of the same center-sampled 10cm columns used by terrain.
    // Work is resumable; a partially checked envelope is never certified.
    public sealed class HabitatGridCertificate
    {
        readonly HabitatEnvelope box;
        readonly long x0,z0,x1,z1;
        long x,z;
        public int Samples {get;private set;}
        public bool Complete {get;private set;}
        public bool Clear {get;private set;}=true;
        public HabitatGridCertificate(HabitatEnvelope volume)
        {
            box=volume;
            if(volume.MaxX-volume.MinX>4||volume.MaxZ-volume.MinZ>4||Math.Abs(volume.MinX)>1e8||Math.Abs(volume.MinZ)>1e8)
                throw new ArgumentOutOfRangeException(nameof(volume));
            x0=(long)Math.Floor(volume.MinX*10);z0=(long)Math.Floor(volume.MinZ*10);
            x1=(long)Math.Ceiling(volume.MaxX*10)-1;z1=(long)Math.Ceiling(volume.MaxZ*10)-1;x=x0;z=z0;
        }
        public void Step(Func<double,double,double> terrainTop,int budget)
        {
            if(terrainTop==null||budget<1||budget>128)throw new ArgumentOutOfRangeException();
            while(!Complete&&budget-->0)
            {
                double height=terrainTop((x+.5)*.1,(z+.5)*.1);Samples++;
                if(double.IsNaN(height)||double.IsInfinity(height)||height+.05>=box.MinY){Clear=false;Complete=true;return;}
                if(++x>x1){x=x0;if(++z>z1)Complete=true;}
            }
        }
    }
}
