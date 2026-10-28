using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AshenVoxel.WorldScale;
using AshenVoxel.WorldScaleRuntime;
using UnityEngine;
using UnityEngine.Rendering;

namespace AshenVoxel.Exploration
{
    /// <summary>One background page builder, at most49 resident 3.2m pages and
    /// one mesh upload per Tick. Complete proxies remain until a page is ready.</summary>
    public sealed class ExplorationVoxelTerrain:MonoBehaviour
    {
        sealed class Resident
        {
            public VoxelTerrainPage Data;
            public GameObject Object;
            public Mesh Mesh;
        }
        readonly Dictionary<WorldCell,Resident> pages=new Dictionary<WorldCell,Resident>();
        readonly HashSet<WorldCell> desired=new HashSet<WorldCell>(),rejected=new HashSet<WorldCell>();
        readonly List<WorldCell> remove=new List<WorldCell>();
        readonly Vector4[] mask=new Vector4[16];
        IWorldField source;
        SurfaceProfile profile;
        WorldExplorer vista;
        Material material;
        Task<VoxelTerrainPage> job;
        CancellationTokenSource cancellation;
        WorldCell jobKey;
        int generation,jobGeneration;
        long gridX,gridZ;
        public int ResidentPages=>pages.Count;
        public int PendingPages {get;private set;}
        public int ResidentCells {get;private set;}
        public int Triangles {get;private set;}
        public string Status {get;private set;}="Waiting for voxel terrain";

        public void Initialize(WorldExplorer renderer,IWorldField bareSource,SurfaceProfile surfaceProfile,Material sharedMaterial)
        {vista=renderer;source=bareSource;profile=surfaceProfile;material=sharedMaterial;}

        public void InvalidateRegion(double x0,double z0,double x1,double z1)
        {
            generation++;cancellation?.Cancel();remove.Clear();
            foreach(var entry in pages)
            {
                double x=entry.Key.X*VoxelTerrainPage.WidthMeters,z=entry.Key.Z*VoxelTerrainPage.WidthMeters;
                // Include material-classification height-query halos.
                double halo=SurfaceProfile.SampleRadiusMax;
                if(x+VoxelTerrainPage.WidthMeters+halo>=x0&&x-halo<=x1&&z+VoxelTerrainPage.WidthMeters+halo>=z0&&z-halo<=z1)remove.Add(entry.Key);
            }
            foreach(var key in remove)Release(key);
            rejected.Clear();
        }

        public void Tick(WorldPoint observer,WorldPoint origin)
        {
            if(source==null||vista==null)return;
            long cx=VoxelTerrainPage.PageAt(observer.X),cz=VoxelTerrainPage.PageAt(observer.Z);
            gridX=cx-3;gridZ=cz-3;desired.Clear();
            for(int z=0;z<7;z++)for(int x=0;x<7;x++)desired.Add(new WorldCell(gridX+x,0,gridZ+z));
            remove.Clear();foreach(var key in pages.Keys)if(!desired.Contains(key))remove.Add(key);
            foreach(var key in remove)Release(key);
            rejected.RemoveWhere(key=>!desired.Contains(key));
            if(job!=null&&!desired.Contains(jobKey))cancellation.Cancel();
            if(job!=null&&job.IsCompleted)
            {
                if(job.IsFaulted)
                {Status=job.Exception.GetBaseException().Message;if(jobGeneration==generation&&desired.Contains(jobKey))rejected.Add(jobKey);}
                else if(!job.IsCanceled&&jobGeneration==generation&&desired.Contains(jobKey))Publish(job.Result,jobKey);
                job=null;cancellation.Dispose();cancellation=null;
            }
            PendingPages=0;bool found=false;WorldCell next=default;double nearest=double.MaxValue;
            foreach(var key in desired)
            {
                if(pages.ContainsKey(key)||rejected.Contains(key))continue;
                PendingPages++;double dx=(key.X+.5)*VoxelTerrainPage.WidthMeters-observer.X,dz=(key.Z+.5)*VoxelTerrainPage.WidthMeters-observer.Z;
                double distance=dx*dx+dz*dz;if(distance<nearest){next=key;nearest=distance;found=true;}
            }
            if(job==null&&found)
            {
                jobKey=next;jobGeneration=generation;cancellation=new CancellationTokenSource();CancellationToken token=cancellation.Token;
                IWorldField snapshot=source is IWorldFieldSnapshotProvider provider?provider.CaptureSnapshot():source;
                var sampler=new SurfaceMaterialSampler(snapshot,profile);
                job=Task.Run(()=>VoxelTerrainPage.Build(sampler,next.X,next.Z,token),token);
            }
            Array.Clear(mask,0,mask.Length);
            foreach(var entry in pages)
            {
                Resident page=entry.Value;page.Object.transform.position=new Vector3(
                    (float)(page.Data.PageX*VoxelTerrainPage.WidthMeters-origin.X),
                    (float)(page.Data.OriginCellY*.1-origin.Y),
                    (float)(page.Data.PageZ*VoxelTerrainPage.WidthMeters-origin.Z));
                int index=(int)(entry.Key.X-gridX)+(int)(entry.Key.Z-gridZ)*8;
                Vector4 vector=mask[index/4];int lane=index%4;
                if(lane==0)vector.x=1;else if(lane==1)vector.y=1;else if(lane==2)vector.z=1;else vector.w=1;
                mask[index/4]=vector;
            }
            vista.SetVoxelTerrainMask(new Vector4((float)(gridX*VoxelTerrainPage.WidthMeters-origin.X),
                (float)(gridZ*VoxelTerrainPage.WidthMeters-origin.Z),(float)VoxelTerrainPage.WidthMeters,1),mask);
            if(rejected.Count==0)Status="10cm terrain: "+pages.Count+" pages · "+PendingPages+" queued · "+ResidentCells+" cells";
        }

        void Publish(VoxelTerrainPage page,WorldCell key)
        {
            var vertices=new List<Vector3>(page.Faces.Count*4);var normals=new List<Vector3>(vertices.Capacity);
            var colors=new List<Color32>(vertices.Capacity);var indices=new List<int>(page.Faces.Count*6);
            foreach(TerrainVoxelFace face in page.Faces)
            {
                int start=vertices.Count;var p=new Vector3(face.X,face.Y,face.Z);Vector3 axis=Axis(face.Axis);if(face.Positive)p+=axis;
                Vector3 u=Axis((face.Axis+1)%3),v=Axis((face.Axis+2)%3),n=axis*(face.Positive?1:-1);
                vertices.Add(p*.1f);vertices.Add((p+u)*.1f);vertices.Add((p+u+v)*.1f);vertices.Add((p+v)*.1f);
                byte r=(byte)(face.Tint>>16),g=(byte)(face.Tint>>8),b=(byte)face.Tint;
                colors.Add(new Color32(r,g,b,face.AO0));colors.Add(new Color32(r,g,b,face.AO1));colors.Add(new Color32(r,g,b,face.AO2));colors.Add(new Color32(r,g,b,face.AO3));
                for(int i=0;i<4;i++)normals.Add(n);
                void Triangle(int a,int bb,int c){indices.Add(start+a);indices.Add(start+(face.Positive?bb:c));indices.Add(start+(face.Positive?c:bb));}
                if(face.AO0+face.AO2>face.AO1+face.AO3){Triangle(0,1,3);Triangle(1,2,3);}
                else{Triangle(0,1,2);Triangle(0,2,3);}
            }
            var mesh=new Mesh{name="10cm terrain page",indexFormat=IndexFormat.UInt32};
            mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetColors(colors);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();mesh.UploadMeshData(true);
            var go=new GameObject("Voxel terrain "+key.X+","+key.Z);go.transform.SetParent(transform,false);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;
            pages.Add(key,new Resident{Data=page,Object=go,Mesh=mesh});ResidentCells+=page.ResidentCells;Triangles+=page.Faces.Count*2;
        }
        void Release(WorldCell key)
        {Resident page=pages[key];ResidentCells-=page.Data.ResidentCells;Triangles-=page.Data.Faces.Count*2;page.Object.SetActive(false);Destroy(page.Mesh);Destroy(page.Object);pages.Remove(key);}
        static Vector3 Axis(int axis)=>axis==0?new Vector3(1,0,0):axis==1?new Vector3(0,1,0):new Vector3(0,0,1);
        void OnDestroy()
        {
            cancellation?.Cancel();cancellation?.Dispose();
            if(job!=null)job.ContinueWith(task=>{var observed=task.Exception;},TaskContinuationOptions.OnlyOnFaulted);
            remove.Clear();remove.AddRange(pages.Keys);foreach(var key in remove)Release(key);
            if(vista!=null)vista.SetVoxelTerrainMask(default,new Vector4[16]);
        }
    }
}
