using System;
using System.Collections.Generic;
using AshenVoxel.Core.Trees;
namespace AshenVoxel.Core
{
    /// <summary>Physical generation controls. Meter values preserve the same landscape when voxel resolution changes.</summary>
    public sealed class GenerationProfile
    {
        public static readonly GenerationProfile Default=new GenerationProfile();
        public readonly float BaseHeight, HillAmplitude, RidgeAmplitude, ArenaHeight, ArenaRadius;
        public readonly float GrassDepth, LitterDepth, SoilDepth, MudDepth;
        public readonly float TreeSpacing, TreeHeight, TrunkRadius, CrownRadius;
        public GenerationProfile(float baseHeight=2.4f,float hillAmplitude=1.35f,float ridgeAmplitude=2.9f,
            float arenaHeight=3.1f,float arenaRadius=3.8f,float grassDepth=.08f,float litterDepth=.22f,
            float soilDepth=.55f,float mudDepth=.95f,float treeSpacing=2.8f,float treeHeight=3.8f,
            float trunkRadius=.17f,float crownRadius=1.15f)
        {
            float[] values={baseHeight,hillAmplitude,ridgeAmplitude,arenaHeight,arenaRadius,grassDepth,litterDepth,soilDepth,mudDepth,treeSpacing,treeHeight,trunkRadius,crownRadius};
            foreach(float n in values)if(!WorldEdits.Finite(n)||n<0)throw new ArgumentOutOfRangeException("Generation values must be finite and nonnegative.");
            if(arenaRadius<=0||treeSpacing<=0||treeHeight<=0||trunkRadius<=0||crownRadius<=0||grassDepth>litterDepth||litterDepth>soilDepth||soilDepth>mudDepth)
                throw new ArgumentException("Positive radii/spacing and increasing strata depths required.");
            BaseHeight=baseHeight;HillAmplitude=hillAmplitude;RidgeAmplitude=ridgeAmplitude;ArenaHeight=arenaHeight;ArenaRadius=arenaRadius;
            GrassDepth=grassDepth;LitterDepth=litterDepth;SoilDepth=soilDepth;MudDepth=mudDepth;
            TreeSpacing=treeSpacing;TreeHeight=treeHeight;TrunkRadius=trunkRadius;CrownRadius=crownRadius;
        }
    }
    /// <summary>Deterministic physical-space terrain and trees. No Unity/global random state; quantization is confined to rasterization.</summary>
    public static class Generation
    {
        public const int Version=5;
        public static void Generate(VoxelWorld world,GenerationProfile profile=null,ICollection<TreeResult> treeResults=null)
        {
            if(world==null)throw new ArgumentNullException(nameof(world));
            var p=profile??GenerationProfile.Default;double s=world.VoxelSize;
            world.Clear();
            world.GenerationVersion=Version;
            for(int z=0;z<world.SizeZ;z++)for(int x=0;x<world.SizeX;x++)
            {
                double px=x*s,pz=z*s;
                int h=SurfaceHeight(world,x,z,p);
                double wet=Noise(px*.48,pz*.48,world.Seed+187);
                double altitude=h*s;
                double slope=Math.Max(Math.Abs(HeightMeters(world,px+.4,pz,p)-HeightMeters(world,px-.4,pz,p)),Math.Abs(HeightMeters(world,px,pz+.4,p)-HeightMeters(world,px,pz-.4,p)))/.8;
                for(int y=0;y<=h;y++)
                {
                    double depth=(h-y)*s;
                    MaterialId id=depth>=p.MudDepth?MaterialId.Stone:depth>=p.SoilDepth?(wet>.12?MaterialId.Mud:MaterialId.Stone):depth>=p.LitterDepth?MaterialId.Dirt:depth>=p.GrassDepth?MaterialId.Litter:MaterialId.Grass;
                    if((altitude>6.0||slope>1.05)&&depth<.28)id=MaterialId.Stone;
                    if(y==0)id=MaterialId.Stone;
                    world.Set(x,y,z,id,EnvironmentColors.Sample(id,x,y,z,world.VoxelSize,world.Seed));
                }
            }
            // Hash lattice coordinates in meters, never cell coordinates, to keep tree locations stable across resolutions.
            double width=world.SizeX*s,length=world.SizeZ*s;
            for(int gz=1;gz*p.TreeSpacing<length-p.TreeSpacing;gz++)
            for(int gx=1;gx*p.TreeSpacing<width-p.TreeSpacing;gx++)
            {
                uint hash=Hash(gx,gz,world.Seed+911);
                double px=gx*p.TreeSpacing+(((hash&65535)/65535.0)-.5)*1.1;
                double pz=gz*p.TreeSpacing+(((hash>>16)/65535.0)-.5)*1.1;
                int x=(int)Math.Round(px/s),z=(int)Math.Round(pz/s);
                double dx=px-width*.5,dz=pz-length*.5;
                if(dx*dx+dz*dz<p.ArenaRadius*p.ArenaRadius)continue;
                int ground=SurfaceHeight(world,x,z,p);
                if(ground*s>6.0||Math.Abs(HeightMeters(world,px+.4,pz,p)-HeightMeters(world,px-.4,pz,p))>.65)continue;
                double desiredHeight=p.TreeHeight+(hash%997)/997.0*.8;
                double groundMeters=(ground+1)*s;
                double available=world.SizeY*s-groundMeters-p.CrownRadius*.5;
                double treeHeight=Math.Min(desiredHeight,available);
                if(treeHeight<1.8)continue;
                TreeSpecies species=hash%10<4?TreeSpecies.Oak:hash%10<7?TreeSpecies.Birch:TreeSpecies.Pine;
                TreeParameters template=TreePresets.Get(species);
                TreeParameters genome=template.Scaled((float)(treeHeight/template.Height));
                genome=genome.With(trunkRadius:genome.TrunkRadius*(p.TrunkRadius/.17f),crownRadius:genome.CrownRadius*(p.CrownRadius/1.15f));
                TreeResult tree=TreeGenerator.Generate(world,genome,(float)((x+.5)*s),(float)groundMeters,(float)((z+.5)*s),unchecked((int)hash));
                treeResults?.Add(tree);
            }
            // Meter-sized loose mound in the arena demonstrates cellular gravity after excavation.
            double sandX=width*.5+Math.Min(1.9,p.ArenaRadius*.5),sandZ=length*.5,radius=.9;
            int sx=(int)Math.Round(sandX/s),sz=(int)Math.Round(sandZ/s),r=(int)Math.Ceiling(radius/s);
            for(int z=sz-r;z<=sz+r;z++)for(int x=sx-r;x<=sx+r;x++)
            {
                if(!world.InBounds(x,0,z))continue;
                double dx=x*s-sandX,dz=z*s-sandZ;
                int rise=(int)Math.Floor((radius-Math.Sqrt(dx*dx+dz*dz))/s);
                int h=SurfaceHeight(world,x,z,p);
                for(int y=1;y<=rise;y++)world.Set(x,h+y,z,MaterialId.Sand,EnvironmentColors.Sample(MaterialId.Sand,x,h+y,z,world.VoxelSize,world.Seed));
            }
        }
        /// <summary>Top occupied terrain cell, before trees and the loose mound. Coordinates are cell lattice positions.</summary>
        public static int SurfaceHeight(VoxelWorld w,int x,int z,GenerationProfile profile=null)
        {
            double meters=HeightMeters(w,x*(double)w.VoxelSize,z*(double)w.VoxelSize,profile);
            return Math.Max(0,Math.Min(w.SizeY-1,(int)Math.Floor(meters/w.VoxelSize)));
        }
        /// <summary>Continuous terrain height in meters, independent of cell resolution for an equal physical domain.</summary>
        public static double HeightMeters(VoxelWorld w,double x,double z,GenerationProfile profile=null)
        {
            var p=profile??GenerationProfile.Default;
            double qx=x+Noise(x*.08,z*.08,w.Seed+25)*1.8,qz=z+Noise(x*.08,z*.08,w.Seed+27)*1.8;
            double broad=Noise(qx*.11,qz*.11,w.Seed);
            double ridge=1-Math.Abs(Noise(qx*.23,qz*.23,w.Seed+43));
            double fine=Noise(x*.55,z*.55,w.Seed+61)*.14+Noise(x*1.3,z*1.3,w.Seed+62)*.05;
            double dx=x-w.SizeX*(double)w.VoxelSize*.5,dz=z-w.SizeZ*(double)w.VoxelSize*.5;
            double distance=Math.Sqrt(dx*dx+dz*dz);
            double arena=1-SmoothStep(p.ArenaRadius*.65,p.ArenaRadius*1.5,distance);
            double height=p.BaseHeight+broad*p.HillAmplitude+ridge*p.RidgeAmplitude+fine;
            height=height*(1-arena)+p.ArenaHeight*arena;
            return Math.Max(0,Math.Min(w.SizeY*(double)w.VoxelSize-w.VoxelSize,height));
        }
        static double SmoothStep(double a,double b,double x){double t=Math.Max(0,Math.Min(1,(x-a)/(b-a)));return t*t*(3-2*t);}
        internal static uint Hash(int x,int z,int seed)
        {
            unchecked { uint h=(uint)x*374761393u+(uint)z*668265263u+(uint)seed*1442695041u;h=(h^(h>>13))*1274126177u;return h^(h>>16); }
        }
        public static double Noise(double x,double z,int seed)
        {
            int ix=(int)Math.Floor(x), iz=(int)Math.Floor(z);
            double fx=x-ix,fz=z-iz;fx=fx*fx*(3-2*fx);fz=fz*fz*(3-2*fz);
            double a=Hash(ix,iz,seed)/(double)uint.MaxValue*2-1,b=Hash(ix+1,iz,seed)/(double)uint.MaxValue*2-1;
            double c=Hash(ix,iz+1,seed)/(double)uint.MaxValue*2-1,d=Hash(ix+1,iz+1,seed)/(double)uint.MaxValue*2-1;
            return (a+(b-a)*fx)*(1-fz)+(c+(d-c)*fx)*fz;
        }
    }
}
