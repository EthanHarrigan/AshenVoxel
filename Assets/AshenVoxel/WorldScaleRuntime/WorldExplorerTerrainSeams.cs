using System;
using System.Collections.Generic;
using AshenVoxel.WorldScale;
using UnityEngine;
using UnityEngine.Rendering;

namespace AshenVoxel.WorldScaleRuntime
{
    public sealed partial class WorldExplorer
    {
        // Replacement seams retain exact visible triangle profiles, even while
        // neighboring source revisions or mesh resolutions differ.
        sealed class TerrainSeam
        {public GameObject Object;public Mesh Mesh;public long Bytes;public readonly HashSet<Surface> Neighbors=new HashSet<Surface>();}
        readonly Dictionary<Surface,TerrainSeam> terrainSeams=new Dictionary<Surface,TerrainSeam>();
        readonly Dictionary<Surface,HashSet<Surface>> terrainSeamDependents=new Dictionary<Surface,HashSet<Surface>>();
        readonly Queue<Surface> terrainSeamQueue=new Queue<Surface>();
        readonly HashSet<Surface> terrainSeamQueued=new HashSet<Surface>();
        public long TerrainSeamBytes {get;private set;}
        public int TerrainSeamPending=>terrainSeamQueue.Count;
        const long TerrainSeamLimit=32L*1024*1024;
        void QueueTerrainSeam(Surface s){if(s!=null&&terrainSeamQueued.Add(s))terrainSeamQueue.Enqueue(s);}
        void TerrainSeamChanged(Surface s)
        {
            QueueTerrainSeam(s);
            if(terrainSeamDependents.TryGetValue(s,out var dependent))foreach(var owner in dependent)QueueTerrainSeam(owner);
            // New contact coverage can cut the interior of an existing far tile.
            double e=.0001,x=s.Tile.OriginXMeters,z=s.Tile.OriginZMeters,d=s.Tile.SizeMeters;
            for(int edge=0;edge<4;edge++)
            {
                double t=0;
                while(t<d)
                {
                    double px=edge==1?x+d+e:edge==3?x-e:x+t+e;
                    double pz=edge==0?z-e:edge==2?z+d+e:z+t+e;
                    Surface other=TerrainSeamSurface(px,pz);QueueTerrainSeam(other);
                    double end=other==null?t+d*.5:(edge%2==0?other.Tile.OriginXMeters+other.Tile.SizeMeters-x:other.Tile.OriginZMeters+other.Tile.SizeMeters-z);
                    if(end<=t+.000001)end=t+25.6;
                    t=Math.Min(d,end);
                }
            }
        }
        void DropTerrainSeam(Surface owner)
        {
            if(!terrainSeams.TryGetValue(owner,out var seam))return;
            foreach(var neighbor in seam.Neighbors)if(terrainSeamDependents.TryGetValue(neighbor,out var set)){set.Remove(owner);if(set.Count==0)terrainSeamDependents.Remove(neighbor);}
            TerrainSeamBytes-=seam.Bytes;Destroy(seam.Object);Destroy(seam.Mesh);terrainSeams.Remove(owner);
        }
        void TerrainSeamRemoved(Surface s)
        {
            if(terrainSeamDependents.TryGetValue(s,out var set))
            {var owners=new List<Surface>(set);foreach(var owner in owners){DropTerrainSeam(owner);QueueTerrainSeam(owner);}}
            DropTerrainSeam(s);terrainSeamQueued.Remove(s);
        }
        Surface TerrainSeamSurface(double x,double z)
        {
            double size=VoxelLodSurface.BaseTileMeters;
            if(contactSurfaces.TryGetValue(new TileKey((long)Math.Floor(x/size),(long)Math.Floor(z/size),0),out var contact))return contact;
            for(int level=0;level<=14;level++)
            {double d=size*(1<<level);var key=new TileKey((long)Math.Floor(x/d),(long)Math.Floor(z/d),level);if(active.Contains(key)&&surfaces.TryGetValue(key,out var s))return s;}
            return null;
        }
        bool TerrainSeamVisible(Surface s)=>s!=null&&s.HeightProxy&&s.Object!=null&&((contactSurfaces.TryGetValue(s.Tile.Key,out var c)&&ReferenceEquals(c,s))||(active.Contains(s.Tile.Key)&&surfaces.TryGetValue(s.Tile.Key,out var a)&&ReferenceEquals(a,s)));
        static double SeamHeight(Surface s,double x,double z)=>HeightProxySurface.TriangleHeight(s.LocalHeights,s.Quads,s.AnchorY,(x-s.Tile.OriginXMeters)/s.Tile.SizeMeters*s.Quads,(z-s.Tile.OriginZMeters)/s.Tile.SizeMeters*s.Quads);
        static void SeamBreakpoints(SortedSet<double> points,Surface s,bool horizontal,double fixedCoordinate,double start,double end)
        {
            double spacing=s.Tile.SizeMeters/s.Quads,origin=horizontal?s.Tile.OriginXMeters:s.Tile.OriginZMeters;
            double crossOrigin=horizontal?s.Tile.OriginZMeters:s.Tile.OriginXMeters;
            double fraction=(fixedCoordinate-crossOrigin)/spacing;fraction-=Math.Floor(fraction);
            int first=(int)Math.Floor((start-origin)/spacing),last=(int)Math.Ceiling((end-origin)/spacing);
            for(int i=first;i<=last;i++)
            {
                double grid=origin+i*spacing,diagonal=grid+(1-fraction)*spacing;
                if(grid>start&&grid<end)points.Add(grid);
                if(diagonal>start&&diagonal<end)points.Add(diagonal);
            }
        }
        static void SeamCrossings(SortedSet<double> points,Surface a,Surface b,bool horizontal,double fixedCoordinate)
        {
            var original=new List<double>(points);
            double Difference(double p)=>SeamHeight(a,horizontal?p:fixedCoordinate,horizontal?fixedCoordinate:p)-SeamHeight(b,horizontal?p:fixedCoordinate,horizontal?fixedCoordinate:p);
            for(int i=0;i<original.Count-1;i++)
            {
                double p=original[i],q=original[i+1],dp=Difference(p),dq=Difference(q);
                if(dp*dq<0)points.Add(p+(q-p)*dp/(dp-dq));
            }
        }
        TerrainSeam BuildTerrainSeam(Surface owner)
        {
            var seam=new TerrainSeam();var vertices=new List<PackedVoxelVertex>();var indices=new List<int>();
            double x=owner.Tile.OriginXMeters,z=owner.Tile.OriginZMeters,d=owner.Tile.SizeMeters,e=.0001;
            var sampler=new SurfaceMaterialSampler(BareField(field),CurrentSurfaceProfile);
            for(int edge=0;edge<4;edge++)
            {
                bool horizontal=edge%2==0;double fixedCoordinate=horizontal?(edge==0?z:z+d):(edge==3?x:x+d),start=horizontal?x:z,end=start+d,t=start;
                while(t<end-.000001)
                {
                    double px=horizontal?t+e:fixedCoordinate,pz=horizontal?fixedCoordinate:t+e;
                    double nx=edge==1?e:edge==3?-e:0,nz=edge==2?e:edge==0?-e:0;
                    Surface inside=TerrainSeamSurface(px-nx,pz-nz),other=TerrainSeamSurface(px+nx,pz+nz);
                    double stop=end;
                    foreach(var s in new[]{inside,other})if(s!=null)stop=Math.Min(stop,horizontal?s.Tile.OriginXMeters+s.Tile.SizeMeters:s.Tile.OriginZMeters+s.Tile.SizeMeters);
                    if(other==null)stop=Math.Min(stop,t+d*.5);
                    if(stop<=t+.000001)stop=Math.Min(end,t+25.6);
                    if(ReferenceEquals(inside,owner)&&other!=null&&other.HeightProxy&&!ReferenceEquals(owner,other))
                    {
                        seam.Neighbors.Add(other);var points=new SortedSet<double>{t,stop};
                        SeamBreakpoints(points,owner,horizontal,fixedCoordinate,t,stop);SeamBreakpoints(points,other,horizontal,fixedCoordinate,t,stop);
                        SeamCrossings(points,owner,other,horizontal,fixedCoordinate);
                        if(points.Count>4096)throw new InvalidOperationException("Terrain seam exceeds bounded edge profile");
                        bool different=false;
                        foreach(double p in points)
                        {double wx=horizontal?p:fixedCoordinate,wz=horizontal?fixedCoordinate:p;if(Math.Abs(SeamHeight(owner,wx,wz)-SeamHeight(other,wx,wz))>.000001){different=true;break;}}
                        if(!different){t=stop;continue;}
                        var column=sampler.EvaluateColumn(horizontal?(t+stop)*.5:fixedCoordinate,horizontal?fixedCoordinate:(t+stop)*.5);
                        uint tint=column.SurfaceTint;int offset=vertices.Count;
                        foreach(double p in points)
                        {
                            double wx=horizontal?p:fixedCoordinate,wz=horizontal?fixedCoordinate:p;
                            foreach(var s in new[]{owner,other})vertices.Add(new PackedVoxelVertex{Position=new Vector3((float)(wx-x),(float)(SeamHeight(s,wx,wz)-owner.AnchorY),(float)(wz-z)),NX=(sbyte)(horizontal?0:127),NY=0,NZ=(sbyte)(horizontal?127:0),Color=new Color32((byte)(tint>>16),(byte)(tint>>8),(byte)tint,255),Surface=new Color32(240,0,column.SurfaceMaterial==Core.MaterialId.Snow?(byte)255:(byte)0,(byte)(128+(int)column.SurfaceMaterial))});
                        }
                        foreach(int i in HeightProxySurface.SeamRibbonIndices(points.Count))indices.Add(offset+i);
                    }
                    t=stop;
                }
            }
            seam.Bytes=vertices.Count*24L+indices.Count*4L;
            if(vertices.Count==0)return seam;
            long previous=terrainSeams.TryGetValue(owner,out var old)?old.Bytes:0;
            if(TerrainSeamBytes-previous+seam.Bytes>TerrainSeamLimit||!HasVoxelUploadHeadroom(seam.Bytes))return null;
            var mesh=new Mesh{name="Exact terrain edge bridges",indexFormat=IndexFormat.UInt32};
            mesh.SetVertexBufferParams(vertices.Count,VoxelVertexLayout);mesh.SetVertexBufferData(vertices.ToArray(),0,0,vertices.Count);
            mesh.SetIndexBufferParams(indices.Count,IndexFormat.UInt32);mesh.SetIndexBufferData(indices.ToArray(),0,0,indices.Count);
            var lo=vertices[0].Position;var hi=lo;foreach(var v in vertices){lo=new Vector3(Math.Min(lo.x,v.Position.x),Math.Min(lo.y,v.Position.y),Math.Min(lo.z,v.Position.z));hi=new Vector3(Math.Max(hi.x,v.Position.x),Math.Max(hi.y,v.Position.y),Math.Max(hi.z,v.Position.z));}
            var bounds=new Bounds((lo+hi)*.5f,hi-lo);mesh.SetSubMesh(0,new SubMeshDescriptor(0,indices.Count){bounds=bounds,vertexCount=vertices.Count},MeshUpdateFlags.DontRecalculateBounds);mesh.bounds=bounds;mesh.UploadMeshData(true);
            seam.Mesh=mesh;seam.Object=new GameObject(mesh.name);seam.Object.transform.SetParent(transform,false);seam.Object.AddComponent<MeshFilter>().sharedMesh=mesh;
            if(contactMaterial==null){contactMaterial=new Material(EffectiveMaterial);contactMaterial.SetVector("_VoxelTerrainGrid",new Vector4(0,0,0,0));}
            seam.Object.AddComponent<MeshRenderer>().sharedMaterial=contactMaterial;
            seam.Object.transform.position=new Vector3((float)(x-originX),(float)(owner.AnchorY-originY),(float)(z-originZ));seam.Object.SetActive(!inspection);return seam;
        }
        void TickTerrainSeams()
        {
            if(!ConnectedTerrainSurface||field==null)return;
            var clock=System.Diagnostics.Stopwatch.StartNew();int count=0;
            while(terrainSeamQueue.Count>0&&count++<4)
            {
                var owner=terrainSeamQueue.Dequeue();terrainSeamQueued.Remove(owner);
                if(!TerrainSeamVisible(owner)){DropTerrainSeam(owner);continue;}
                var seam=BuildTerrainSeam(owner);if(seam==null){QueueTerrainSeam(owner);break;}
                DropTerrainSeam(owner);terrainSeams[owner]=seam;TerrainSeamBytes+=seam.Bytes;
                foreach(var neighbor in seam.Neighbors){if(!terrainSeamDependents.TryGetValue(neighbor,out var set)){set=new HashSet<Surface>();terrainSeamDependents.Add(neighbor,set);}set.Add(owner);}
                if(clock.Elapsed.TotalMilliseconds>=1)break;
            }
        }
        void RebaseTerrainSeams(){foreach(var pair in terrainSeams)if(pair.Value.Object!=null){var s=pair.Key;pair.Value.Object.transform.position=new Vector3((float)(s.Tile.OriginXMeters-originX),(float)(s.AnchorY-originY),(float)(s.Tile.OriginZMeters-originZ));pair.Value.Object.SetActive(!inspection&&TerrainSeamVisible(s));}}
        void ClearTerrainSeams(){foreach(var seam in terrainSeams.Values){if(seam.Object!=null)Destroy(seam.Object);if(seam.Mesh!=null)Destroy(seam.Mesh);}terrainSeams.Clear();terrainSeamDependents.Clear();terrainSeamQueue.Clear();terrainSeamQueued.Clear();TerrainSeamBytes=0;}
    }
}

