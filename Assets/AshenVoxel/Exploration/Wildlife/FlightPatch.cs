using System;

namespace AshenVoxel.WildlifeDraft
{
    public enum FlightKind : byte { Bee, Bird }
    public readonly struct FlightPose
    {
        public readonly int Slot;
        public readonly FlightKind Kind;
        public readonly double X, Y, Z, VX, VY, VZ, Wing;
        public FlightPose(int slot, FlightKind kind, double x, double y, double z,
            double vx, double vy, double vz, double wing)
        { Slot=slot; Kind=kind; X=x; Y=y; Z=z; VX=vx; VY=vy; VZ=vz; Wing=wing; }
    }

    // One caller-owned habitat patch. Global metres remain double precision.
    // Caller supplies an actual terrain/flower anchor; this does not discover
    // habitat, query terrain, collide, allocate renderers or simulate ecology.
    public sealed class FlightPatch
    {
        public const int MaximumActors=128;
        readonly int bees, birds;
        readonly uint seed;
        readonly double beeRadiusScale;
        double x,y,z;
        bool enabled;
        public int Capacity => bees+birds;
        public int LastVisited { get; private set; }
        public int LastVisible { get; private set; }
        public int LastOmitted { get; private set; }
        public FlightPatch(int bees=16,int birds=8,uint seed=714103,double beeRadiusScale=1)
        {
            if(bees<0||birds<0||bees>MaximumActors||birds>MaximumActors-bees)
                throw new ArgumentOutOfRangeException(nameof(bees));
            if(double.IsNaN(beeRadiusScale)||double.IsInfinity(beeRadiusScale)||beeRadiusScale<.1||beeRadiusScale>1)throw new ArgumentOutOfRangeException(nameof(beeRadiusScale));
            this.bees=bees;this.birds=birds;this.seed=seed;this.beeRadiusScale=beeRadiusScale;
        }
        static void Finite(double value)
        { if(double.IsNaN(value)||double.IsInfinity(value))throw new ArgumentOutOfRangeException(nameof(value)); }
        public void SetHabitat(double globalX,double groundY,double globalZ)
        { Finite(globalX);Finite(groundY);Finite(globalZ);x=globalX;y=groundY;z=globalZ;enabled=true; }
        public void Unload()
        { enabled=false;LastVisited=LastVisible=LastOmitted=0; }
        static uint Mix(uint v)
        { unchecked { v^=v>>16;v*=0x7feb352du;v^=v>>15;v*=0x846ca68bu;return v^(v>>16); } }
        double Unit(int slot,uint channel)
        { unchecked { return (Mix(seed^(uint)slot*0x9e3779b9u^channel)>>8)*(1.0/16777216.0); } }

        // Pure evaluation from bounded local phase: same time gives same pose,
        // independent of frame partition, visit order and render origin.
        FlightPose Evaluate(int slot,double seconds)
        {
            bool bee=slot<bees;
            double phase=Unit(slot,1)*Math.PI*2;
            double speed=bee?1.1+Unit(slot,2):.12+Unit(slot,2)*.12;
            double radius=bee?(1+Unit(slot,3)*5)*beeRadiusScale:16+Unit(slot,3)*20;
            double t=Math.IEEERemainder(seconds,Math.PI*2/speed)*speed+phase;
            double sn=Math.Sin(t),co=Math.Cos(t);
            double height=bee?.7+Unit(slot,4)*1.5:12+Unit(slot,4)*12;
            double bob=bee?.25:2;
            // Double-frequency bob and flutter stay below the stated height.
            double st=Math.Sin(2*t),ct=Math.Cos(2*t);
            double wingPhase=Math.IEEERemainder(seconds,bee?.2:.8)*(bee?10*Math.PI:2.5*Math.PI);
            // Birds periodically glide with folded oscillation amplitude.
            double glidePhase=Math.IEEERemainder(seconds,Math.PI*4/speed)*speed*.5+phase;
            double flap=bee?1:Math.Max(0,Math.Sin(glidePhase));
            return new FlightPose(slot,bee?FlightKind.Bee:FlightKind.Bird,
                x+radius*co,y+height+bob*st,z+radius*.65*sn,
                -radius*speed*sn,2*bob*speed*ct,radius*.65*speed*co,
                flap*Math.Sin(wingPhase+phase));
        }

        // Visits at most128 actors, writes at most output.Length. Omitted visible
        // actors are reported. No per-call heap allocations or hidden backlog.
        // No actor state depends on visibility. Viewer culling is spherical.
        public int Fill(double seconds,double eyeX,double eyeY,double eyeZ,
            double beeDistance,double birdDistance,Span<FlightPose> output)
        {
            Finite(seconds);Finite(eyeX);Finite(eyeY);Finite(eyeZ);Finite(beeDistance);Finite(birdDistance);
            if(seconds<0||beeDistance<0||birdDistance<0)throw new ArgumentOutOfRangeException(nameof(seconds));
            LastVisited=LastVisible=LastOmitted=0;
            if(!enabled)return 0;
            int written=0;
            for(int ordinal=0;ordinal<Capacity;ordinal++)
            {
                // Under a reduced actor budget, interleave species so bees do
                // not consume every slot before any bird can be considered.
                int paired=Math.Min(bees,birds),slot=ordinal;
                if(output.Length==1&&birds>0)
                    slot=ordinal==0?bees:ordinal<=bees?ordinal-1:ordinal;
                else if(output.Length<Capacity)
                    slot=ordinal<paired*2?(ordinal%2==0?ordinal/2:bees+ordinal/2):
                        (bees>paired?paired+ordinal-paired*2:bees+paired+ordinal-paired*2);
                LastVisited++;
                FlightPose p=Evaluate(slot,seconds);
                double dx=p.X-eyeX,dy=p.Y-eyeY,dz=p.Z-eyeZ;
                double distance=slot<bees?beeDistance:birdDistance;
                // Normalize before squaring so extreme finite distances never
                // turn the comparison into infinity > infinity (false).
                if(distance==0)
                {if(dx!=0||dy!=0||dz!=0)continue;}
                else
                {
                    if(Math.Abs(dx)>distance||Math.Abs(dy)>distance||Math.Abs(dz)>distance)continue;
                    double nx=dx/distance,ny=dy/distance,nz=dz/distance;
                    if(nx*nx+ny*ny+nz*nz>1)continue;
                }
                LastVisible++;
                if(written<output.Length)output[written++]=p;else LastOmitted++;
            }
            return written;
        }
    }
}
