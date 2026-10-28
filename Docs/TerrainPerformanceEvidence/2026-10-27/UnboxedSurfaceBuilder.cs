using System; using System.Collections.Generic; using System.Threading; using AshenVoxel.Core;
namespace AshenVoxel.WorldScale { public static class UnboxedSurfaceBuilder { const int Version=1,MaximumQuads=256,MaximumFaces=1048576;
        public static VoxelLodSurface Build(SurfaceMaterialSampler sampler,LodTile tile,int n,double seamFloor,CancellationToken cancel=default)
        {
            if(sampler==null||tile==null)throw new ArgumentNullException();
            if(n<8||n>MaximumQuads||(n&(n-1))!=0||tile.Key.Level<0||tile.Key.Level>14)throw new ArgumentOutOfRangeException(nameof(n));
            long cells=256L<<tile.Key.Level;
            if(Math.Abs(tile.SizeMeters-cells*.1)>1e-5||Math.Abs(tile.OriginXMeters-tile.Key.X*cells*.1)>1e-4||Math.Abs(tile.OriginZMeters-tile.Key.Z*cells*.1)>1e-4)
                throw new ArgumentException("Voxel LOD tiles must share the global25.6m dyadic grid.");
            Numeric.Finite(seamFloor,nameof(seamFloor));
            int step=checked((int)(cells/n)),pitch=n+2;long ox=checked(tile.Key.X*cells),oz=checked(tile.Key.Z*cells);
            double spacing=step*.1;
            TerrainProbe.Begin();
            var columns=new SurfaceColumn[pitch*pitch];var tops=new long[columns.Length];var samples=new MatterSample[columns.Length];var wet=new byte[columns.Length];
            int At(int x,int z)=>x+1+(z+1)*pitch;
            WorldCell Cell(int x,long y,int z)=>new WorldCell(ox+x*step+step/2,y,oz+z*step+step/2);
            for(int z=-1;z<=n;z++)for(int x=-1;x<=n;x++)
            {
                cancel.ThrowIfCancellationRequested();int i=At(x,z);WorldPoint p=Cell(x,0,z).Center;
                columns[i]=sampler.EvaluateColumn(p.X,p.Z);tops[i]=VoxelTerrainPage.TopCell(columns[i].SurfaceHeightMeters);
                samples[i]=sampler.SampleMatter(Cell(x,tops[i],z),columns[i]);wet[i]=(byte)Math.Round(columns[i].Wetness*255);
                if(samples[i].Material==MaterialId.Air)throw new InvalidOperationException("Surface occupancy and material disagree.");
            }
            TerrainProbe.Mark(0);
            long anchorCell=tops[At(0,0)]+1;double anchor=anchorCell*.1;
            var heights=new float[n*n];var faces=new List<VoxelLodFace>(n*n*2);var used=new bool[n*n];
            void Add(VoxelLodFace f){if(faces.Count>=MaximumFaces)throw new VoxelLodCapacityException();faces.Add(f);}
            byte AO(int x,int z,int dx,int dz,long h)
            {
                bool a=tops[At(x+dx,z)]>h,b=tops[At(x,z+dz)]>h,c=tops[At(x+dx,z+dz)]>h;
                int light=a&&b?0:3-(a?1:0)-(b?1:0)-(c?1:0);int raw=64+light*191/3;return (byte)(255-(255-raw)/step);
            }
            bool Open(int x,int z,long h)=>tops[At(x-1,z)]<=h&&tops[At(x+1,z)]<=h&&tops[At(x,z-1)]<=h&&tops[At(x,z+1)]<=h&&
                tops[At(x-1,z-1)]<=h&&tops[At(x+1,z-1)]<=h&&tops[At(x-1,z+1)]<=h&&tops[At(x+1,z+1)]<=h;
            bool Compatible(int x,int z,int reference)=>!used[x+z*n]&&tops[At(x,z)]==tops[reference]&&samples[At(x,z)].Material==samples[reference].Material&&
                samples[At(x,z)].Tint==samples[reference].Tint&&wet[At(x,z)]==wet[reference]&&Open(x,z,tops[reference]);
            for(int z=0;z<n;z++)for(int x=0;x<n;x++)
            {
                cancel.ThrowIfCancellationRequested();int i=At(x,z);long top=tops[i];heights[x+z*n]=(float)((top+1-anchorCell)*.1);
                if(used[x+z*n])continue;
                int w=1,d=1;bool open=Open(x,z,top);
                if(open)
                {
                    while(x+w<n&&Compatible(x+w,z,i))w++;
                    bool extend=true;
                    while(z+d<n&&extend){for(int xx=x;xx<x+w;xx++)if(!Compatible(xx,z+d,i)){extend=false;break;}if(extend)d++;}
                }
                for(int zz=z;zz<z+d;zz++)for(int xx=x;xx<x+w;xx++)used[xx+zz*n]=true;
                // For axisY, U runs +Z and V runs +X; no smoothed normals.
                Add(new VoxelLodFace((float)(x*spacing),heights[x+z*n],(float)(z*spacing),(float)(d*spacing),(float)(w*spacing),1,true,samples[i],wet[i],
                    open?(byte)255:AO(x,z,-1,-1,top),open?(byte)255:AO(x,z,-1,1,top),open?(byte)255:AO(x,z,1,1,top),open?(byte)255:AO(x,z,1,-1,top)));
            }
            // All risers are exposed; opposite/internal faces are omitted. The
            // boundary closure extends beneath every possible neighboring LOD.
            // Height/material transitions are snapped to the same global cells.
            TerrainProbe.Mark(1);
            long floorCell=VoxelTerrainPage.TopCell(seamFloor)-1;
            var cuts=new List<long>(40);
            for(int z=0;z<n;z++)for(int x=0;x<n;x++)
            {
                cancel.ThrowIfCancellationRequested();int i=At(x,z);long high=tops[i]+1;
                for(int side=0;side<4;side++)
                {
                    bool alongX=side<2,positive=(side&1)!=0;int nx=x+(alongX?(positive?1:-1):0),nz=z+(!alongX?(positive?1:-1):0);
                    bool boundary=nx<0||nz<0||nx>=n||nz>=n;
                    long low=boundary?Math.Min(floorCell,tops[At(nx,nz)]+1):tops[At(nx,nz)]+1;
                    if(low>=high)continue;
                    float px=(float)((x+(alongX&&positive?1:0))*spacing),pz=(float)((z+(!alongX&&positive?1:0))*spacing);
                    if(boundary)
                    {
                        long apronTop=Math.Max(low,high-Math.Max(20,step*2));
                        if(apronTop>low)
                        {
                            MatterSample rock=sampler.SampleMatter(Cell(x,apronTop-1,z),columns[i]);
                            float y=(float)((low-anchorCell)*.1),height=(float)((apronTop-low)*.1);
                            Add(new VoxelLodFace(px,y,pz,alongX?height:(float)spacing,alongX?(float)spacing:height,alongX?0:2,positive,rock,wet[i]));
                            low=apronTop;
                        }
                    }
                    cuts.Clear();cuts.Add(low);cuts.Add(high);
                    // Sample pigment at the LOD's vertical footprint, capped for
                    // pathological cliffs. Physical layer boundaries remain exact.
                    long regular=Math.Max(step,(long)Math.Ceiling((high-low)/32.0));
                    for(long y=high-regular;y>low;y-=regular)cuts.Add(y);
                    SurfaceColumn col=columns[i];
                    void Cut(double depth){long y=VoxelTerrainPage.TopCell(col.GroundHeightMeters-depth)+1;if(y>low&&y<high)cuts.Add(y);}
                    Cut(0);Cut(col.IceDepthMeters);Cut(col.GroundCoverDepthMeters);Cut(col.GroundCoverDepthMeters+col.OrganicDepthMeters);
                    Cut(col.GroundCoverDepthMeters+col.OrganicDepthMeters+col.SoilDepthMeters);Cut(col.GroundCoverDepthMeters+col.OrganicDepthMeters+col.SoilDepthMeters+col.SedimentDepthMeters);
                    cuts.Sort();
                    for(int k=1;k<cuts.Count;k++)
                    {
                        long a=cuts[k-1],b=cuts[k];if(a==b)continue;
                        MatterSample material=sampler.SampleMatter(Cell(x,a+(b-a-1)/2,z),col);
                        float y=(float)((a-anchorCell)*.1),height=(float)((b-a)*.1);
                        Add(new VoxelLodFace(px,y,pz,alongX?height:(float)spacing,alongX?(float)spacing:height,alongX?0:2,positive,material,wet[i]));
                    }
                }
            }
            TerrainProbe.Mark(2);
            int unmerged=faces.Count;
            MergeSides(faces,true,cancel);MergeSides(faces,false,cancel);
            TerrainProbe.Mark(3);
            return new VoxelLodSurface(n,step,anchor,heights,faces,sampler.SourceIdentity+"/voxel-lod-v"+Version,unmerged);
        }
        static float Width(VoxelLodFace f)=>f.Axis==0?f.V:f.U;
        static float Height(VoxelLodFace f)=>f.Axis==0?f.U:f.V;
        static float Along(VoxelLodFace f)=>f.Axis==0?f.Z:f.X;
        static float Plane(VoxelLodFace f)=>f.Axis==0?f.X:f.Z;
        // Two exact-pigment passes collapse coplanar side rectangles, including
        // strata and aprons. Geometry and material colors are preserved.
        static void MergeSides(List<VoxelLodFace> faces,bool vertical,CancellationToken cancel)
        {
            faces.Sort((a,b)=>
            {
                int c=a.Axis.CompareTo(b.Axis);if(c!=0)return c;
                c=a.Positive.CompareTo(b.Positive);if(c!=0)return c;
                c=((byte)a.Material).CompareTo((byte)b.Material);if(c!=0)return c;
                c=a.Tint.CompareTo(b.Tint);if(c!=0)return c;
                c=a.Wetness.CompareTo(b.Wetness);if(c!=0)return c;
                c=Plane(a).CompareTo(Plane(b));if(c!=0)return c;
                c=(vertical?Along(a):a.Y).CompareTo(vertical?Along(b):b.Y);if(c!=0)return c;
                c=(vertical?Width(a):Height(a)).CompareTo(vertical?Width(b):Height(b));if(c!=0)return c;
                return (vertical?a.Y:Along(a)).CompareTo(vertical?b.Y:Along(b));
            });
            int write=0;
            for(int read=0;read<faces.Count;read++)
            {
                if((read&1023)==0)cancel.ThrowIfCancellationRequested();var b=faces[read];
                if(write>0&&b.Axis!=1)
                {
                    var a=faces[write-1];
                    bool same=a.Axis==b.Axis&&a.Positive==b.Positive&&a.Material==b.Material&&a.Tint==b.Tint&&a.Wetness==b.Wetness&&Plane(a)==Plane(b);
                    bool touches=vertical?Along(a)==Along(b)&&Width(a)==Width(b)&&Math.Abs(a.Y+Height(a)-b.Y)<.00001f:
                        a.Y==b.Y&&Height(a)==Height(b)&&Math.Abs(Along(a)+Width(a)-Along(b))<.00001f;
                    if(same&&touches)
                    {
                        float height=Height(a)+(vertical?Height(b):0),width=Width(a)+(vertical?0:Width(b));
                        faces[write-1]=new VoxelLodFace(a.X,a.Y,a.Z,a.Axis==0?height:width,a.Axis==0?width:height,a.Axis,a.Positive,new MatterSample(a.Material,a.Tint),a.Wetness);
                        continue;
                    }
                }
                faces[write++]=b;
            }
            if(write<faces.Count)faces.RemoveRange(write,faces.Count-write);
        }
}}