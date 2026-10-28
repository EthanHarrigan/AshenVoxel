using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AshenVoxel.Core;

static class Program
{
    const uint Seed = 0xA51E7141;
    static uint random = Seed;
    static int cases, ambiguous, hits, misses, outside, negative, insideSolid, scaledDirections;
    static double maxDistanceError;
    readonly struct Ray
    {
        public readonly float Ox, Oy, Oz, Dx, Dy, Dz, Reach;
        public Ray(float ox,float oy,float oz,float dx,float dy,float dz,float reach)
        { Ox=ox;Oy=oy;Oz=oz;Dx=dx;Dy=dy;Dz=dz;Reach=reach; }
        public override string ToString() => $"o=({Ox:R},{Oy:R},{Oz:R}) d=({Dx:R},{Dy:R},{Dz:R}) reach={Reach:R}";
    }
    readonly struct Expected
    {
        public readonly bool Hit, Ambiguous;
        public readonly Int3 Cell;
        public readonly double Distance;
        public Expected(bool hit,bool uncertain,Int3 cell,double distance)
        {Hit=hit;Ambiguous=uncertain;Cell=cell;Distance=distance;}
    }
    static uint Next(){unchecked{random^=random<<13;random^=random>>17;random^=random<<5;return random;}}
    static double Unit()=>Next()/(double)uint.MaxValue;
    static float Range(double lo,double hi)=>(float)(lo+(hi-lo)*Unit());

    // Exhaustive geometric oracle: independently intersect every occupied cell's
    // axis-aligned box, then choose the nearest positive-length intersection.
    // It has no grid stepping, boundary-event state, chunk traversal, or DDA calls.
    static Expected Oracle(VoxelWorld w,Ray r)
    {
        double magnitude=Math.Sqrt((double)r.Dx*r.Dx+(double)r.Dy*r.Dy+(double)r.Dz*r.Dz);
        if(magnitude<1e-12||r.Reach<0)return new Expected(false,false,default,0);
        double[] origin={r.Ox,r.Oy,r.Oz},direction={r.Dx/magnitude,r.Dy/magnitude,r.Dz/magnitude};
        double closest=double.PositiveInfinity;Int3 closestCell=default;bool uncertain=false;
        double epsilon=w.VoxelSize*1e-7;
        for(int z=0;z<w.SizeZ;z++)for(int y=0;y<w.SizeY;y++)for(int x=0;x<w.SizeX;x++)
        {
            if(!MaterialCatalog.Get(w.Get(x,y,z)).Solid)continue;
            int[] cell={x,y,z};double entry=double.NegativeInfinity,exit=double.PositiveInfinity;bool possible=true;
            for(int axis=0;axis<3;axis++)
            {
                double minimum=cell[axis]*(double)w.VoxelSize,maximum=(cell[axis]+1)*(double)w.VoxelSize;
                if(direction[axis]==0)
                {
                    // Half-open ownership for a ray lying exactly in an axis plane.
                    if(origin[axis]<minimum||origin[axis]>=maximum){possible=false;break;}
                }
                else
                {
                    double a=(minimum-origin[axis])/direction[axis],b=(maximum-origin[axis])/direction[axis];
                    double near=Math.Min(a,b),far=Math.Max(a,b);
                    if(near>entry)entry=near;if(far<exit)exit=far;
                    if(exit<entry){possible=false;break;}
                }
            }
            if(!possible||exit<0||entry>r.Reach)continue;
            double start=Math.Max(0,entry),span=exit-start;
            // Exact point/edge touches do not enter a cell. Very thin segments
            // and nearly coincident entry events are excluded from random tests.
            if(span<=0)continue;
            if(span<epsilon){uncertain=true;continue;}
            if(Math.Abs(start-r.Reach)<epsilon&&start!=r.Reach)uncertain=true;
            if(start<closest-epsilon){closest=start;closestCell=new Int3(x,y,z);}
            else if(Math.Abs(start-closest)<=epsilon&&!closestCell.Equals(new Int3(x,y,z)))uncertain=true;
        }
        return new Expected(!double.IsPositiveInfinity(closest),uncertain,closestCell,closest);
    }
    static bool Check(VoxelWorld w,Ray r,string label,bool allowAmbiguous=false)
    {
        Expected expected=Oracle(w,r);
        if(expected.Ambiguous&&!allowAmbiguous){ambiguous++;return false;}
        bool actual=VoxelRaycast.TryCast(w,r.Ox,r.Oy,r.Oz,r.Dx,r.Dy,r.Dz,r.Reach,out VoxelHit hit);
        if(actual!=expected.Hit)throw new Exception($"{label}: hit mismatch expected={expected.Hit} actual={actual}; {r}; size={w.SizeX},{w.SizeY},{w.SizeZ}; s={w.VoxelSize:R}");
        if(actual)
        {
            double error=Math.Abs(hit.Distance-expected.Distance);
            double tolerance=Math.Max(2e-6,w.VoxelSize*2e-5+expected.Distance*2e-6);
            if(!hit.Cell.Equals(expected.Cell)||error>tolerance)
                throw new Exception($"{label}: expected cell={expected.Cell} t={expected.Distance:R}; actual cell={hit.Cell} t={hit.Distance:R}; error={error:R}; {r}; size={w.SizeX},{w.SizeY},{w.SizeZ}; s={w.VoxelSize:R}");
            if(hit.Distance<0||hit.Distance>r.Reach+2e-6)throw new Exception(label+": returned distance outside reach");
            maxDistanceError=Math.Max(maxDistanceError,error);hits++;
        }
        else misses++;
        bool isOutside=r.Ox<0||r.Oy<0||r.Oz<0||r.Ox>=w.SizeX*(double)w.VoxelSize||r.Oy>=w.SizeY*(double)w.VoxelSize||r.Oz>=w.SizeZ*(double)w.VoxelSize;
        if(isOutside)outside++;
        else if(MaterialCatalog.Get(w.Get((int)Math.Floor(r.Ox/w.VoxelSize),(int)Math.Floor(r.Oy/w.VoxelSize),(int)Math.Floor(r.Oz/w.VoxelSize))).Solid)insideSolid++;
        if(r.Dx<0||r.Dy<0||r.Dz<0)negative++;
        double length=Math.Sqrt((double)r.Dx*r.Dx+(double)r.Dy*r.Dy+(double)r.Dz*r.Dz);
        if(Math.Abs(length-1)>.01&&length>0)scaledDirections++;
        cases++;return true;
    }
    static void Targeted()
    {
        var w=new VoxelWorld(5,5,5,.125f,0);w.Set(2,2,2,MaterialId.Stone);
        Check(w,new Ray(-1,.3125f,.3125f,9,0,0,4),"outside positive axis");
        Check(w,new Ray(2,.3125f,.3125f,-.00001f,0,0,4),"outside negative nonunit axis");
        Check(w,new Ray(.3125f,-1,.3125f,0,8,0,4),"positive y");
        Check(w,new Ray(.3125f,2,.3125f,0,-8,0,4),"negative y");
        Check(w,new Ray(.3125f,.3125f,-1,0,0,.00002f,4),"positive z");
        Check(w,new Ray(.3125f,.3125f,2,0,0,-19,4),"negative z");
        Check(w,new Ray(.3125f,.3125f,.3125f,1,2,3,0),"inside solid zero reach");
        Check(w,new Ray(-1,.3125f,.3125f,1,0,0,1.25f),"inclusive exact reach");
        Check(w,new Ray(-1,.3125f,.3125f,1,0,0,1.249f),"before max reach");
        Check(w,new Ray(-1,.3125f,.3125f,1,0,0,1.251f),"after max reach");
        Check(w,new Ray(-1,.3125f,.3125f,-1,0,0,4),"outside facing away");
        Check(w,new Ray(.3125f,.3125f,.3125f,0,0,0,4),"zero direction");
        Check(w,new Ray(.25f,.3125f,.3125f,-1,0,0,4),"solid boundary facing away");
        Check(w,new Ray(.25f,.3125f,.3125f,1,0,0,4),"solid boundary facing inward");
        Check(w,new Ray(.375f,.3125f,.3125f,-1,0,0,4),"negative face ownership");
        Check(w,new Ray(.375f,.3125f,.3125f,1,0,0,4),"positive face ownership");
        // A diagonal touches the side cell at an edge and enters the diagonal
        // cell. The side's zero-volume contact must not block traversal.
        w=new VoxelWorld(5,5,5,.125f,0);w.Set(1,0,2,MaterialId.Stone);w.Set(2,2,2,MaterialId.Wood);
        Check(w,new Ray(.0625f,.0625f,.3125f,1,1,0,2),"exact edge touch excludes side cell",true);
        w=new VoxelWorld(5,5,5,.125f,0);w.Set(1,0,0,MaterialId.Stone);w.Set(2,2,2,MaterialId.Wood);
        Check(w,new Ray(.0625f,.0625f,.0625f,1,1,1,2),"exact corner touch excludes side cell",true);
    }
    static void Main(string[] args)
    {
        Targeted();
        for(int worldIndex=0;worldIndex<100;worldIndex++)
        {
            float s=worldIndex%3==0?.1f:worldIndex%3==1?.125f:.03125f;
            var w=new VoxelWorld(4+(int)(Next()%9),4+(int)(Next()%9),4+(int)(Next()%9),s,worldIndex);
            var solids=new List<Int3>();
            for(int z=0;z<w.SizeZ;z++)for(int y=0;y<w.SizeY;y++)for(int x=0;x<w.SizeX;x++)
                if(worldIndex%10!=0&&(worldIndex%10==1||Unit()<.28))
                {w.Set(x,y,z,(MaterialId)(1+Next()%9));solids.Add(new Int3(x,y,z));}
            float wx=w.SizeX*s,wy=w.SizeY*s,wz=w.SizeZ*s;
            for(int rayIndex=0;rayIndex<110;rayIndex++)
            {
                float ox=Range(-wx,wx*2),oy=Range(-wy,wy*2),oz=Range(-wz,wz*2);
                float dx=Range(-1,1),dy=Range(-1,1),dz=Range(-1,1);
                switch(rayIndex%8)
                {
                    case 0: ox=Range(.01*wx,.99*wx);oy=Range(.01*wy,.99*wy);oz=Range(.01*wz,.99*wz);break;
                    case 1:
                        if(solids.Count>0){Int3 c=solids[(int)(Next()%(uint)solids.Count)];ox=(c.x+Range(.1,.9))*s;oy=(c.y+Range(.1,.9))*s;oz=(c.z+Range(.1,.9))*s;}
                        break;
                    case 2: ox=-wx;dx=wx*.5f-ox;dy=wy*.5f-oy;dz=wz*.5f-oz;break;
                    case 3: ox=wx*2;dx=wx*.5f-ox;dy=wy*.5f-oy;dz=wz*.5f-oz;break;
                    case 4: dy=0;dz=0;break;
                    case 5: dx=0;dz=0;break;
                    case 6: dx=0;dy=0;break;
                }
                float factor=(float)Math.Pow(10,Range(-5,5));dx*=factor;dy*=factor;dz*=factor;
                float reach=Range(0,Math.Sqrt(wx*wx+wy*wy+wz*wz)*4);
                var ray=new Ray(ox,oy,oz,dx,dy,dz,reach);
                Check(w,ray,$"world={worldIndex} ray={rayIndex}");
                // One in eleven random rays is checked on either side of the
                // independent oracle's first hit to validate reach clipping.
                if(rayIndex%11==0)
                {
                    Expected expected=Oracle(w,new Ray(ox,oy,oz,dx,dy,dz,100));
                    if(expected.Hit&&!expected.Ambiguous&&expected.Distance>s*.01)
                    {
                        Check(w,new Ray(ox,oy,oz,dx,dy,dz,(float)(expected.Distance-s*.001)),"near-reach miss");
                        Check(w,new Ray(ox,oy,oz,dx,dy,dz,(float)(expected.Distance+s*.001)),"near-reach hit");
                    }
                }
            }
        }
        if(cases<5000||hits<500||misses<500||outside<500||insideSolid<500||negative<500||scaledDirections<500)
            throw new Exception("Insufficient accepted coverage");
        var result=new {
            schema=1,seed=Seed,oracle="Exhaustive occupied-cell AABB slab intersections; no DDA stepping",cases,
            ambiguous_excluded=ambiguous,hits,misses,origin_outside=outside,origin_inside_solid=insideSolid,
            negative_axis_direction=negative,nonunit_direction=scaledDirections,max_distance_error_m=maxDistanceError,
            status="PASS",scope="Pure C# core, not Unity rendering or physics"
        };
        string json=JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true});
        Console.WriteLine(json);
        if(args.Length>0)File.WriteAllText(args[0],json+Environment.NewLine);
    }
}
