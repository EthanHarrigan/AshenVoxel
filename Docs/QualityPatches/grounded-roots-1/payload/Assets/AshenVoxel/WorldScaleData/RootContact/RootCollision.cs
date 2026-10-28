using System;
using System.Collections.Generic;
using System.Threading;
using AshenVoxel.Core;
using AshenVoxel.WorldScale;
using RootContactCache;
using RootContactInteraction;
using RootContactPrototype;
using RootContactTemplateCache;

namespace RootContactCollision
{
    public sealed class BuildLimits
    {
        public readonly int MaxCells,MaxColumns,MaxSpans;
        public readonly long MaxElementBytes;
        public BuildLimits(int cells=65536,int columns=8192,int spans=65536,long elementBytes=2*1024*1024)
        {
            if(cells<1||cells>65536||columns<1||columns>8192||spans<1||spans>65536||elementBytes<1||elementBytes>4*1024*1024)throw new ArgumentException("collision-limits");
            MaxCells=cells;MaxColumns=columns;MaxSpans=spans;MaxElementBytes=elementBytes;
        }
    }

    // Immutable collision snapshot. Canonical prototype collision columns are immutable;
    // ReleaseMeshData is harmless. Freshness and atomic publication remain caller-owned.
    public sealed class RootCollision
    {
        public const string PackIdentity="41556b474ce7a26342e4dd7bcff2e4fe658ab0333e8360058262ee21c0f011aa";
        public const string Model10Companion="2215e06929167d2ccb50957a2a22cfc11dbc3c66b1aee8f67d6250a71d9a0549";
        public const string Model11Companion="492c37ab98e15ca83ff3657f380cdc7ce69e21e8bdaade2b3a553076d6727b8a";
        sealed class Column
        {
            internal readonly short[] Spans;
            internal readonly bool SeamContinues;
            internal Column(short[] spans,bool seam){Spans=spans;SeamContinues=seam;}
        }
        readonly Dictionary<int,Column> hard,sticks;
        readonly ExplorationPrototype canonical;
        public readonly string ArtifactIdentity,CompanionIdentity;
        public readonly double MinX,MinY,MinZ,MaxX,MaxY,MaxZ;
        public readonly int OwnedCells,ContextCells,HardColumns,StickColumns,SpanCount;
        // Exact short-array payload; sort-key temporary payload is separate. Dictionary,
        // column objects, references, identity strings, input and GC are not included.
        public readonly long SpanElementBytes,SortElementBytes;
        public bool HasCollision=>HardColumns>0||canonical.HasCollision;
        public bool HasResistance=>StickColumns>0||canonical.HasResistance;
        public double Radius=>Math.Max(Math.Max(Math.Abs(MinX),Math.Abs(MaxX)),Math.Max(Math.Abs(MinZ),Math.Abs(MaxZ)));

        RootCollision(OwnedArtifact input,ExplorationPrototype original,string companion,Dictionary<int,Column> h,Dictionary<int,Column> s,
            int count,int spans,double minX,double minY,double minZ,double maxX,double maxZ)
        {
            canonical=original;hard=h;sticks=s;ArtifactIdentity=input.Identity.Hash;CompanionIdentity=companion;
            OwnedCells=count;ContextCells=input.CellCount-count;HardColumns=h.Count;StickColumns=s.Count;SpanCount=spans;
            SpanElementBytes=4L*spans;SortElementBytes=8L*input.CellCount;
            MinX=Math.Min(original.MinX,minX);MinY=Math.Min(original.MinY,minY);MinZ=Math.Min(original.MinZ,minZ);
            MaxX=Math.Max(original.MaxX,maxX);MaxY=Math.Max(original.MaxY,3);MaxZ=Math.Max(original.MaxZ,maxZ);
        }
        static void Current(Func<bool> current,CancellationToken cancel)
        {cancel.ThrowIfCancellationRequested();if(!current())throw new InvalidOperationException("stale-collision-input");cancel.ThrowIfCancellationRequested();}
        // This certificate is deliberately restricted to the two reviewed pinned models.
        // Caller must associate canonical with the SAME verified opened AVPM pack; Id alone
        // cannot prove where a pre-existing ExplorationPrototype object came from.
        public static RootCollision Create(OwnedArtifact input,ExplorationPrototype canonical,TemplateBundle bundle,
            string openedPackIdentity,BuildLimits limits,Func<bool> isCurrent,CancellationToken cancel=default)
        {
            if(input==null||canonical==null||bundle==null||limits==null||isCurrent==null)throw new ArgumentException("collision-input");
            string pin=bundle.ModelId==10?Model10Companion:bundle.ModelId==11?Model11Companion:null;
            if(pin==null||canonical.Id!=bundle.ModelId||openedPackIdentity!=PackIdentity||bundle.PackIdentity!=PackIdentity||bundle.FileIdentity!=pin||
                input.Identity.Template!=bundle.Root.Identity||input.Identity.Context!=bundle.Context.Identity||
                input.Identity.RootPolicy!=Policy.Identity+";seam="+bundle.Root.DeformSeam||input.Identity.InteractionPolicy!=RootContactInteraction.Builder.InteractionPolicy)
                throw new ArgumentException("unsupported-or-mismatched-zero-lower-nonwood-certificate");
            return Build(input,canonical,pin,limits,isCurrent,cancel);
        }
        // Same assembly only: synthetic query tests cannot mint public certificates.
        internal static RootCollision Build(OwnedArtifact input,ExplorationPrototype canonical,string companion,BuildLimits limits,Func<bool> current,CancellationToken cancel)
        {
            Current(current,cancel);
            if(input.CellCount<1||input.CellCount>limits.MaxCells||8L*input.CellCount>limits.MaxElementBytes)throw new ArgumentException("collision-cell-cap");
            var keys=new ulong[input.CellCount];int owned=0,context=0;
            double minX=double.PositiveInfinity,minY=minX,minZ=minX,maxX=double.NegativeInfinity,maxZ=maxX;
            for(int i=0;i<keys.Length;i++)
            {
                if((i&255)==0)Current(current,cancel);
                Int3 p=input.Position(i);Channel role=input.Role(i);uint color=input.Color(i);
                if(p.x< -128||p.x>127||p.z< -128||p.z>127||p.y< -84||p.y>30||(color>>24)!=255||
                    (p.y==30?role!=Channel.None:role!=Channel.Hard&&role!=Channel.Stick))throw new ArgumentException("collision-cell-domain");
                keys[i]=((ulong)((p.x+128)*256+p.z+128)*128+(uint)(p.y+84))*4+(byte)role;
                if(p.y==30){context++;continue;}owned++;
                minX=Math.Min(minX,p.x*.1);minY=Math.Min(minY,p.y*.1);minZ=Math.Min(minZ,p.z*.1);maxX=Math.Max(maxX,(p.x+1)*.1);maxZ=Math.Max(maxZ,(p.z+1)*.1);
            }
            if(owned==0||owned!=input.RenderCellCount||context!=input.ContextCellCount)throw new ArgumentException("collision-partition-count");
            // One bounded sort, not a cooperative time-sliced stage. Cancellation brackets it.
            Current(current,cancel);Array.Sort(keys);Current(current,cancel);
            var hard=new Dictionary<int,Column>();var sticks=new Dictionary<int,Column>();int totalSpans=0,totalColumns=0;
            for(int begin=0;begin<keys.Length;)
            {
                Current(current,cancel);int end=begin+1;ulong column=keys[begin]/512;
                while(end<keys.Length&&keys[end]/512==column)end++;
                int x=(int)(column/256)-128,z=(int)(column%256)-128;
                for(int i=begin+1;i<end;i++)if(keys[i]/4==keys[i-1]/4)throw new ArgumentException("duplicate-collision-cell");
                for(byte role=1;role<=2;role++)
                {
                    int n=0,last=-999;
                    for(int i=begin;i<end;i++)if((keys[i]&3)==role){int y=(int)(keys[i]/4%128)-84;if(y!=last+1)n++;last=y;}
                    if(n==0)continue;
                    totalSpans+=n;totalColumns++;
                    if(totalSpans>limits.MaxSpans||totalColumns>limits.MaxColumns||8L*keys.Length+4L*totalSpans>limits.MaxElementBytes)throw new ArgumentException("collision-span-cap");
                    var spans=new short[n*2];int next=0;last=-999;
                    for(int i=begin;i<end;i++)if((keys[i]&3)==role)
                    {
                        int y=(int)(keys[i]/4%128)-84;
                        if(y!=last+1){spans[next++]=(short)y;spans[next++]=(short)(y+1);}else spans[next-1]=(short)(y+1);last=y;
                    }
                    bool seam=role==1&&spans[spans.Length-1]==30&&canonical.BlocksLocal(x*.1+.025,3,z*.1+.025,x*.1+.075,3.1,z*.1+.075);
                    (role==1?hard:sticks).Add(Key(x,z),new Column(spans,seam));
                }
                begin=end;
            }
            Current(current,cancel);var result=new RootCollision(input,canonical,companion,hard,sticks,owned,totalSpans,minX,minY,minZ,maxX,maxZ);Current(current,cancel);return result;
        }
        static int Key(int x,int z)=>unchecked((x&65535)|z<<16);
        static void Query(double value){if(double.IsNaN(value)||double.IsInfinity(value)||Math.Abs(value)>1000)throw new ArgumentOutOfRangeException("prototype local coordinates");}
        static void Body(double x0,double y0,double z0,double x1,double y1,double z1,bool resistance)
        {
            Query(x0);Query(y0);Query(z0);Query(x1);Query(y1);Query(z1);
            if(resistance){if(x0>=x1||y0>=y1||z0>=z1||x1-x0>20||y1-y0>20||z1-z0>20)throw new ArgumentOutOfRangeException("resistance body bounds");}
            else
            {
                if(x0>=x1||y0>=y1||z0>=z1)throw new ArgumentException("Prototype body bounds must be positive.");
                if((int)Math.Ceiling(x1*10)-1-(int)Math.Floor(x0*10)>1024||(int)Math.Ceiling(z1*10)-1-(int)Math.Floor(z0*10)>1024)throw new ArgumentOutOfRangeException("prototype query extent");
            }
        }
        public bool BlocksLocal(double x0,double y0,double z0,double x1,double y1,double z1)
        {
            Body(x0,y0,z0,x1,y1,z1,false);
            // The clipped upper band's new lower endpoint is3, even if the original
            // canonical span began below it. Preserve endpoint tolerance explicitly.
            if(y1>3+1e-8&&canonical.BlocksLocal(x0,Math.Max(3,y0),z0,x1,y1,z1))return true;
            if(y0>=3+1e-8)return false;double top=Math.Min(y1,3);
            for(int z=(int)Math.Floor(z0*10);z<=(int)Math.Ceiling(z1*10)-1;z++)for(int x=(int)Math.Floor(x0*10);x<=(int)Math.Ceiling(x1*10)-1;x++)if(hard.TryGetValue(Key(x,z),out Column c))
            {
                // A merged hard run has no endpoint at the seam and no epsilon hole.
                if(c.SeamContinues&&y1>3-1e-8&&y0<3+1e-8)return true;
                for(int i=0;i<c.Spans.Length;i+=2)if(top>c.Spans[i]*.1+1e-8&&y0<c.Spans[i+1]*.1-1e-8)return true;
            }
            return false;
        }
        public double ResistanceLocal(double x0,double y0,double z0,double x1,double y1,double z1)
        {
            Body(x0,y0,z0,x1,y1,z1,true);
            double result=y1>3?canonical.ResistanceLocal(x0,Math.Max(3,y0),z0,x1,y1,z1):0;
            if(y0>=3)return result;double top=Math.Min(y1,3),volume=0;
            for(int z=(int)Math.Floor(z0*10);z<=(int)Math.Ceiling(z1*10)-1;z++)for(int x=(int)Math.Floor(x0*10);x<=(int)Math.Ceiling(x1*10)-1;x++)if(sticks.TryGetValue(Key(x,z),out Column c))
            {
                double area=(Math.Min(x1,(x+1)*.1)-Math.Max(x0,x*.1))*(Math.Min(z1,(z+1)*.1)-Math.Max(z0,z*.1));
                for(int i=0;i<c.Spans.Length;i+=2)volume+=area*Math.Max(0,Math.Min(top,c.Spans[i+1]*.1)-Math.Max(y0,c.Spans[i]*.1));
            }
            return result+volume*4000;
        }
        public bool SupportLocal(double x,double z,double radius,double minY,double maxY,out double height)
        {
            Query(x);Query(z);Query(minY);Query(maxY);if(double.IsNaN(radius)||double.IsInfinity(radius)||radius<0||radius>20||minY>maxY)throw new ArgumentOutOfRangeException("prototype support query");
            int x0=(int)Math.Floor((x-radius)*10),x1=(int)Math.Floor((x+radius)*10),z0=(int)Math.Floor((z-radius)*10),z1=(int)Math.Floor((z+radius)*10);
            if(x1-x0>1024||z1-z0>1024)throw new ArgumentOutOfRangeException("prototype query extent");
            height=double.NegativeInfinity;
            // Canonical column ends are integer decimeters. Exclude old lower top=3,
            // while retaining the exact canonical tolerance for every upper top>=3.1.
            if(maxY>=3.0000002&&canonical.SupportLocal(x,z,radius,Math.Max(minY,3.0000002),maxY,out double upper))height=upper;
            foreachQuery(x,z,radius,minY,maxY,x0,x1,z0,z1,ref height);
            return !double.IsNegativeInfinity(height);
        }
        void foreachQuery(double x,double z,double radius,double minY,double maxY,int x0,int x1,int z0,int z1,ref double height)
        {
            for(int iz=z0;iz<=z1;iz++)for(int ix=x0;ix<=x1;ix++)if(hard.TryGetValue(Key(ix,iz),out Column c))
            {
                double dx=x-Math.Max(ix*.1,Math.Min((ix+1)*.1,x)),dz=z-Math.Max(iz*.1,Math.Min((iz+1)*.1,z));
                if(dx*dx+dz*dz>radius*radius+1e-10)continue;
                for(int i=1;i<c.Spans.Length;i+=2){double top=c.Spans[i]*.1;if(c.Spans[i]==30&&c.SeamContinues)continue;if(top>=minY-1e-7&&top<=maxY+1e-7&&top>height)height=top;}
            }
        }
    }
}
