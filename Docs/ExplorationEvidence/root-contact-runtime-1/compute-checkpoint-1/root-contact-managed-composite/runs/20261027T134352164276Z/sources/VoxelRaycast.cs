using System;
namespace AshenVoxel.Core
{
    public readonly struct VoxelHit
    {
        public readonly Int3 Cell, Previous;
        public readonly float Distance;
        public VoxelHit(Int3 cell,Int3 previous,float distance){Cell=cell;Previous=previous;Distance=distance;}
    }
    public static class VoxelRaycast
    {
        /// <summary>Amanatides-Woo traversal. Direction need not be normalized; distance is meters. Zero direction is a miss.</summary>
        public static bool TryCast(VoxelWorld w,float ox,float oy,float oz,float dx,float dy,float dz,float maxDistance,out VoxelHit hit)
        {
            hit=default;
            if(w==null)throw new ArgumentNullException(nameof(w));
            if(!WorldEdits.Finite(ox)||!WorldEdits.Finite(oy)||!WorldEdits.Finite(oz)||!WorldEdits.Finite(dx)||!WorldEdits.Finite(dy)||!WorldEdits.Finite(dz)||!WorldEdits.Finite(maxDistance)||maxDistance<0)return false;
            double length=Math.Sqrt((double)dx*dx+(double)dy*dy+(double)dz*dz);if(length<1e-12)return false;
            Span<double> o=stackalloc double[3]{ox,oy,oz};
            Span<double> d=stackalloc double[3]{dx/length,dy/length,dz/length};
            Span<double> bounds=stackalloc double[3]{w.SizeX*(double)w.VoxelSize,w.SizeY*(double)w.VoxelSize,w.SizeZ*(double)w.VoxelSize};
            double enter=0,exit=maxDistance;
            for(int a=0;a<3;a++)
            {
                if(Math.Abs(d[a])<1e-15){if(o[a]<0||o[a]>=bounds[a])return false;continue;}
                double t0=-o[a]/d[a],t1=(bounds[a]-o[a])/d[a];if(t0>t1){double t=t0;t0=t1;t1=t;}
                enter=Math.Max(enter,t0);exit=Math.Min(exit,t1);if(enter>exit)return false;
            }
            double s=w.VoxelSize;
            // Tiny positive inset ensures rays entering from the maximum boundary select the interior cell.
            double start=enter>0?enter+Math.Min(s*1e-7,Math.Max(0,(exit-enter)*.5)):enter;
            int x=(int)Math.Floor((o[0]+d[0]*start)/s),y=(int)Math.Floor((o[1]+d[1]*start)/s),z=(int)Math.Floor((o[2]+d[2]*start)/s);
            if(d[0]<0 && (o[0]+d[0]*start)/s==x)x--;
            if(d[1]<0 && (o[1]+d[1]*start)/s==y)y--;
            if(d[2]<0 && (o[2]+d[2]*start)/s==z)z--;
            int sx=Math.Sign(d[0]),sy=Math.Sign(d[1]),sz=Math.Sign(d[2]);
            double tx=sx==0?double.PositiveInfinity:((sx>0?x+1:x)*s-o[0])/d[0];
            double ty=sy==0?double.PositiveInfinity:((sy>0?y+1:y)*s-o[1])/d[1];
            double tz=sz==0?double.PositiveInfinity:((sz>0?z+1:z)*s-o[2])/d[2];
            double stepx=sx==0?double.PositiveInfinity:s/Math.Abs(d[0]),stepy=sy==0?double.PositiveInfinity:s/Math.Abs(d[1]),stepz=sz==0?double.PositiveInfinity:s/Math.Abs(d[2]);
            bool outsideOrigin=o[0]<0||o[1]<0||o[2]<0||o[0]>=bounds[0]||o[1]>=bounds[1]||o[2]>=bounds[2];
            Int3 previous=outsideOrigin ? new Int3((int)Math.Floor((o[0]+d[0]*(enter-s*1e-7))/s),(int)Math.Floor((o[1]+d[1]*(enter-s*1e-7))/s),(int)Math.Floor((o[2]+d[2]*(enter-s*1e-7))/s)) : new Int3(x,y,z);double distance=enter;
            // A finite box admits at most SizeX+SizeY+SizeZ boundary events.
            for(int n=0;n<=w.SizeX+w.SizeY+w.SizeZ+3 && distance<=exit;n++)
            {
                if(!w.InBounds(x,y,z))break;
                if(MaterialCatalog.Get(w.Get(x,y,z)).Solid){hit=new VoxelHit(new Int3(x,y,z),previous,(float)distance);return true;}
                previous=new Int3(x,y,z);
                double next=Math.Min(tx,Math.Min(ty,tz));
                // Advance all tied axes; a point/edge contact does not enter the side cells.
                if(tx<=next+1e-10){x+=sx;tx+=stepx;}
                if(ty<=next+1e-10){y+=sy;ty+=stepy;}
                if(tz<=next+1e-10){z+=sz;tz+=stepz;}
                distance=next;
            }
            return false;
        }
    }
}
