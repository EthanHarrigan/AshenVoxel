using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using AshenVoxel.Core;
using AshenVoxel.Rendering;
using AshenVoxel.WorldScale;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace AshenVoxel.Exploration.Editor
{
    // Isolated Editor fixture only. No startup hook, Play operation, asset save,
    // automatic process exit or scene persistence. Installation/run remains gated.
    public static class RootContactNativePreview
    {
        const string Data="C:/Users/ethan/Downloads/AshenVoxel-WorldVoxels/.terrain-cache";
        const int Layer=27,Width=960,Height=640;
        static readonly (int,int)[] Directions={(1,0),(-1,0),(0,1),(0,-1)};
        [Serializable] sealed class Identity {public string path,sha256;}
        [Serializable] sealed class Inputs {public Identity[] files;}
        [StructLayout(LayoutKind.Sequential)] struct PerformanceInfo
        {public uint cb;public UIntPtr total,limit,peak,physicalTotal,physicalAvailable,systemCache,kernelTotal,kernelPaged,kernelNonpaged,pageSize;public uint handles,processes,threads;}
        [StructLayout(LayoutKind.Sequential)] struct ProcessMemory
        {public uint cb,pageFaults;public UIntPtr peakWorking,working,peakPaged,paged,peakNonpaged,nonpaged,pagefile,peakPagefile,privateBytes;}
        [DllImport("psapi.dll",SetLastError=true)] static extern bool GetPerformanceInfo(out PerformanceInfo value,uint size);
        [DllImport("psapi.dll",SetLastError=true)] static extern bool GetProcessMemoryInfo(IntPtr process,out ProcessMemory value,uint size);
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        static long PrivateBytes(){if(!GetProcessMemoryInfo(GetCurrentProcess(),out var m,(uint)Marshal.SizeOf<ProcessMemory>())||m.privateBytes.ToUInt64()==0)throw new Exception("Cannot read valid native process memory");return checked((long)m.privateBytes.ToUInt64());}
        static long AvailableCommit(){if(!GetPerformanceInfo(out var p,(uint)Marshal.SizeOf<PerformanceInfo>()))throw new Exception("Cannot read system commit");return checked((long)((p.limit.ToUInt64()-p.total.ToUInt64())*p.pageSize.ToUInt64()));}
        [Serializable] sealed class Site {public int id;public double x,z,original_y,yaw;}
        [Serializable] sealed class Sites {public Site[] sites;}
        [Serializable] sealed class View {public int id,base_vertices,shared_vertices,appended_vertices,changed_pixels,pink_pixels;public string root_sha256,terrain_sha256;}
        [Serializable] sealed class Report
        {
            public string status="RUNNING",gpu,pack_sha256,error;
            public string scope="Native shared-submesh/root fixture only, not scene integration or Play/FPS. Exact recorded10cm terrain heights/top pigments; representative subsurface pigment with neutral opaque Dirt material/AO. Actual root Wood/material/RGB and shared original upper tree. Static wind, two fixed side views.";
            public bool original_scene_dirty_unchanged,original_scene_roots_unchanged;public List<View> views=new List<View>();
            public long initial_private_bytes,max_private_bytes,minimum_available_commit;public double seconds;public string last_stage;
            public bool original_active_scene_restored,original_lighting_unchanged,shader_state_restored,render_target_restored;
        }
        sealed class Terrain
        {
            public long X,Z;public int Side;public long[] Top,Bare;public uint[] Tint,Deep;public double Anchor;
            public Terrain(string path)
            {
                using(var r=new BinaryReader(File.OpenRead(path)))
                {
                    X=r.ReadInt64();Z=r.ReadInt64();Side=r.ReadInt32();if(Side!=256)throw new Exception("Expected bounded256-cell terrain");int n=Side*Side;
                    Top=new long[n];Bare=new long[n];Tint=new uint[n];Deep=new uint[n];
                    for(int i=0;i<n;i++){Top[i]=r.ReadInt64();Bare[i]=r.ReadInt64();Tint[i]=r.ReadUInt32();Deep[i]=r.ReadUInt32();}
                    if(r.BaseStream.Position!=r.BaseStream.Length)throw new Exception("Trailing terrain bytes");
                }
                Anchor=Top.Min()*.1;
            }
        }
        static string Sha(string path){using(var s=SHA256.Create())using(var f=File.OpenRead(path))return BitConverter.ToString(s.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
        static Vector3 Axis(int i)=>i==0?Vector3.right:i==1?Vector3.up:Vector3.forward;
        static Mesh SharedMesh(SharedUpperSplit.Result split)
        {
            var p=new Vector3[split.VertexCount];var n=new Vector3[p.Length];var c=new Color32[p.Length];
            for(int i=0;i<p.Length;i++)
            {
                p[i]=new Vector3(split.Position(i,0)*.1f,split.Position(i,1)*.1f,split.Position(i,2)*.1f);int normal=split.Normal(i);n[i]=Axis(normal/2)*((normal&1)==0?1:-1);
                uint color=split.Color(i);c[i]=new Color32((byte)color,(byte)(color>>8),(byte)(color>>16),(byte)(color>>24));
            }
            var mesh=new Mesh();try
            {
                mesh.name="Root preview shared upper/authored base";mesh.indexFormat=IndexFormat.UInt32;mesh.vertices=p;mesh.normals=n;mesh.colors32=c;mesh.subMeshCount=2;
                mesh.SetTriangles(split.UpperIndices,0,false);mesh.SetTriangles(split.BaseIndices,1,false);mesh.RecalculateBounds();return mesh;
            }
            catch{UnityEngine.Object.DestroyImmediate(mesh);throw;}
        }
        static Mesh RootMesh(string path,out Vector3 origin)
        {
            var cells=new List<(short x,short y,short z,uint tint)>();using(var r=new BinaryReader(File.OpenRead(path)))
            {
                int count=r.ReadInt32();if(count<1||count>65536)throw new Exception("Root cell cap");
                for(int i=0;i<count;i++)cells.Add((r.ReadInt16(),r.ReadInt16(),r.ReadInt16(),r.ReadUInt32()));
                if(r.BaseStream.Position!=r.BaseStream.Length)throw new Exception("Trailing root bytes");
            }
            int x0=cells.Min(v=>v.x),y0=cells.Min(v=>v.y),z0=cells.Min(v=>v.z);origin=new Vector3(x0*.1f,y0*.1f,z0*.1f);
            var world=new VoxelWorld(cells.Max(v=>v.x)-x0+1,31-y0,cells.Max(v=>v.z)-z0+1,.1f,714103);
            foreach(var p in cells)world.Set(p.x-x0,p.y-y0,p.z-z0,MaterialId.Wood,p.tint);
            if(world.Chunks.Count>128)throw new Exception("Root chunk cap");
            var parts=new List<Mesh>();var combine=new List<CombineInstance>();var mesher=new RootBaseChunkMesher();
            try
            {
                foreach(var chunk in world.Chunks){var part=new Mesh();parts.Add(part);mesher.Build(world,chunk.Key,part,30-y0);combine.Add(new CombineInstance{mesh=part,transform=Matrix4x4.identity});}
                var result=new Mesh();try{result.name="Bounded root owned geometry";result.indexFormat=IndexFormat.UInt32;result.CombineMeshes(combine.ToArray(),true,true);result.RecalculateBounds();return result;}
                catch{UnityEngine.Object.DestroyImmediate(result);throw;}
            }
            finally{foreach(var part in parts)UnityEngine.Object.DestroyImmediate(part);}
        }
        static Mesh TerrainMesh(Terrain t)
        {
            var positions=new List<Vector3>();var normals=new List<Vector3>();var colors=new List<Color32>();var indices=new List<int>();int faces=0;
            void Face(Vector3 p,Vector3 u,Vector3 v,Vector3 normal,uint tint)
            {
                if(++faces>300000)throw new Exception("Terrain face cap");int start=positions.Count;
                positions.Add(p);positions.Add(p+u);positions.Add(p+u+v);positions.Add(p+v);
                var color=new Color32((byte)(tint>>16),(byte)(tint>>8),(byte)tint,VertexAppearance.Pack(MaterialId.Dirt,3));
                for(int i=0;i<4;i++){normals.Add(normal);colors.Add(color);}bool positive=Vector3.Dot(Vector3.Cross(u,v),normal)>0;
                indices.Add(start);indices.Add(start+(positive?1:2));indices.Add(start+(positive?2:1));indices.Add(start);indices.Add(start+(positive?2:3));indices.Add(start+(positive?3:2));
            }
            for(int z=0;z<t.Side;z++)for(int x=0;x<t.Side;x++)
            {
                int i=x+z*t.Side;double top=(t.Top[i]+1)*.1-t.Anchor;Face(new Vector3(x*.1f,(float)top,z*.1f),Vector3.right*.1f,Vector3.forward*.1f,Vector3.up,t.Tint[i]);
                foreach(var d in Directions)
                {
                    int nx=x+d.Item1,nz=z+d.Item2;double other=nx<0||nz<0||nx>=t.Side||nz>=t.Side?-.1:(t.Top[nx+nz*t.Side]+1)*.1-t.Anchor;
                    if(other>=top)continue;Vector3 normal=new Vector3(d.Item1,0,d.Item2);
                    var p=new Vector3((x+(d.Item1>0?1:0))*.1f,(float)other,(z+(d.Item2>0?1:0))*.1f);
                    Face(p,Vector3.up*(float)(top-other),(d.Item1!=0?Vector3.forward:Vector3.right)*.1f,normal,t.Deep[i]);
                }
            }
            var mesh=new Mesh();try{mesh.name="Recorded actual10cm support surface";mesh.indexFormat=IndexFormat.UInt32;mesh.SetVertices(positions);mesh.SetNormals(normals);mesh.SetColors(colors);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();return mesh;}
            catch{UnityEngine.Object.DestroyImmediate(mesh);throw;}
        }
        static GameObject RenderObject(string name,Transform parent,Mesh mesh,params Material[] materials)
        {var go=new GameObject(name);go.layer=Layer;go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterials=materials;return go;}
        [MenuItem("Ashen Voxel/Review/Isolated root-contact native preview")]
        public static void Run()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)throw new InvalidOperationException("Native fixture requires idle Editor");
            if(!String.Equals(Path.GetFullPath(Application.dataPath).TrimEnd('/','\\'),Path.GetFullPath("C:/Users/ethan/Downloads/AshenVoxel/Assets").TrimEnd('/','\\'),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Native fixture is scoped to the existing main project Editor");
            string output=Path.Combine(Data,"root-contact-native",DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ"));Directory.CreateDirectory(output);
            const string pack="Assets/AshenVoxel/Exploration/Prototypes/ExplorationPrototypes.bytes";
            var report=new Report{gpu=SystemInfo.graphicsDeviceName,pack_sha256=Sha(pack)};
            var watch=System.Diagnostics.Stopwatch.StartNew();
            void Guard(string stage,bool initial=false)
            {
                report.last_stage=stage;long memory=PrivateBytes(),available=AvailableCommit();
                if(initial){report.initial_private_bytes=memory;report.minimum_available_commit=available;}
                report.max_private_bytes=Math.Max(report.max_private_bytes,memory);report.minimum_available_commit=Math.Min(report.minimum_available_commit,available);report.seconds=watch.Elapsed.TotalSeconds;
                if(available<(initial?3584L:2048L)*1024*1024||memory-report.initial_private_bytes>1024L*1024*1024||report.seconds>90)throw new Exception("Native preview resource/time checkpoint exceeded at "+stage);
            }
            Scene previous=SceneManager.GetActiveScene(),owned=default;bool dirty=previous.isDirty;int roots=previous.rootCount;
            bool previousFog=RenderSettings.fog;Light previousSun=RenderSettings.sun;
            Vector4 sky=Shader.GetGlobalVector("_VoxelAmbientSky"),equator=Shader.GetGlobalVector("_VoxelAmbientEquator"),ground=Shader.GetGlobalVector("_VoxelAmbientGround"),wind=Shader.GetGlobalVector("_VoxelWindState"),oldWind=Shader.GetGlobalVector("_VoxelWindPreviousState");
            Vector4[] optics=Shader.GetGlobalVectorArray("_VoxelMaterialOptics");var oldRT=RenderTexture.active;var objects=new List<UnityEngine.Object>();
            try
            {
                Guard("preflight",true);
                var inputs=JsonUtility.FromJson<Inputs>(File.ReadAllText(Path.Combine(Data,"root-contact-mesh-prototype/native-inputs.json")));
                if(inputs.files==null||inputs.files.Length<7)throw new Exception("Missing frozen input manifest");
                foreach(var input in inputs.files)if(Sha(input.path)!=input.sha256)throw new Exception("Frozen input changed: "+input.path);
                var packIdentity=inputs.files.Single(v=>String.Equals(Path.GetFullPath(v.path),Path.GetFullPath(pack),StringComparison.OrdinalIgnoreCase));
                if(report.pack_sha256!=packIdentity.sha256)throw new Exception("Opened pack differs from frozen main pack");
                // An empty global array cannot be restored to absent in Unity. Require the
                // existing initialized state rather than mutate that irreversible edge case.
                if(optics.Length!=32)throw new Exception("Expected initialized32-entry material optics; no shader globals changed");
                if(GraphicsSettings.currentRenderPipeline==null)throw new Exception("Active URP pipeline required");
                owned=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);if(!SceneManager.SetActiveScene(owned))throw new Exception("Cannot isolate native fixture scene");
                var root=new GameObject("Owned root-contact validation scene");
                var material=new Material(Shader.Find("AshenVoxel/WorldSurface"));objects.Add(material);material.SetFloat("_GrainStrength",0);
                MaterialOpticsProfile.Apply(Resources.Load<MaterialOpticsProfile>("MaterialOptics"));MaterialOpticsProfile.SetAmbient(new Color(.72f,.78f,.85f),new Color(.58f,.66f,.73f),new Color(.36f,.39f,.32f),1.5f);
                Shader.SetGlobalVector("_VoxelWindState",Vector4.zero);Shader.SetGlobalVector("_VoxelWindPreviousState",Vector4.zero);RenderSettings.fog=false;
                var lightObject=new GameObject("Fixture sun");lightObject.transform.SetParent(root.transform);var sun=lightObject.AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.5f;sun.color=new Color(1,.92f,.81f);sun.shadows=LightShadows.Soft;sun.cullingMask=1<<Layer;sun.transform.rotation=Quaternion.Euler(45,225,0);RenderSettings.sun=sun;
                var cameraObject=new GameObject("Fixture camera");cameraObject.transform.SetParent(root.transform);var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.scene=owned;
                camera.cullingMask=1<<Layer;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.14f,.2f,.24f);camera.nearClipPlane=.05f;camera.farClipPlane=150;camera.fieldOfView=49;camera.aspect=(float)Width/Height;camera.allowHDR=false;camera.allowMSAA=false;
                var rt=new RenderTexture(Width,Height,24,RenderTextureFormat.ARGB32);objects.Add(rt);camera.targetTexture=rt;var image=new Texture2D(Width,Height,TextureFormat.RGB24,false);objects.Add(image);
                Color32[] Capture(string name){Guard("before capture "+name);camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,Width,Height),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());Guard("after capture "+name);return image.GetPixels32();}
                var sites=JsonUtility.FromJson<Sites>("{\"sites\":"+File.ReadAllText(Path.Combine(Data,"grounding-root-visual/sites.json"))+"}").sites;
                using(var reader=new ExplorationPrototypeReader(File.ReadAllBytes(pack)))while(true)
                {
                    Guard("before prototype decode");if(!reader.TryRead(out var prototype))break;
                    Guard("decoded prototype "+prototype.Id);Site site=sites.FirstOrDefault(v=>v.id==prototype.Id);if(site==null){prototype.ReleaseMeshData();GC.Collect();continue;}
                    string rootPath=Path.Combine(Data,$"root-contact-runtime-prototype/v2-expected-{site.id}.bin"),terrainPath=Path.Combine(Data,$"grounding-root-visual/terrain-{site.id}.bin");
                    var terrain=new Terrain(terrainPath);var split=SharedUpperSplit.Build(prototype.Mesh(0));Mesh shared=null,baseMesh=null,support=null;GameObject specimen=null,supportObject=null;
                    try
                    {
                        Guard("before shared mesh");shared=SharedMesh(split);Guard("after shared mesh");baseMesh=RootMesh(rootPath,out Vector3 origin);Guard("after root mesh");support=TerrainMesh(terrain);Guard("after terrain mesh");supportObject=RenderObject("Exact support",root.transform,support,material);
                        specimen=RenderObject("Shared prototype",root.transform,shared,material,material);specimen.transform.position=new Vector3((float)(site.x-terrain.X*.1),(float)(site.original_y-terrain.Anchor),(float)(site.z-terrain.Z*.1));specimen.transform.rotation=Quaternion.Euler(0,(float)site.yaw,0);
                        var replacement=RenderObject("Bounded replacement base",specimen.transform,baseMesh,material);replacement.transform.localPosition=origin;replacement.SetActive(false);
                        double dx=(terrain.Bare[128*256+148]-terrain.Bare[128*256+108])/40d,dz=(terrain.Bare[148*256+128]-terrain.Bare[108*256+128])/40d;
                        Vector3 side=new Vector3((float)dz,0,(float)-dx).normalized,target=specimen.transform.position+Vector3.up*1.8f;camera.transform.position=target+side*12+Vector3.up*1.2f;
                        int cx=Mathf.FloorToInt(camera.transform.position.x*10),cz=Mathf.FloorToInt(camera.transform.position.z*10);if(cx<0||cz<0||cx>=256||cz>=256)throw new Exception("Camera outside recorded patch");
                        var cp=camera.transform.position;cp.y=Math.Max(cp.y,(float)((terrain.Top[cx+cz*256]+1)*.1-terrain.Anchor+1.7));camera.transform.position=cp;camera.transform.LookAt(target);
                        var before=Capture(site.id+"-authored");var authored=shared.GetSubMesh(1);shared.SetSubMesh(1,new SubMeshDescriptor(authored.indexStart,0,MeshTopology.Triangles));replacement.SetActive(true);var after=Capture(site.id+"-grounded");
                        int changed=0,pink=0;for(int i=0;i<before.Length;i++){if(Math.Abs(before[i].r-after[i].r)+Math.Abs(before[i].g-after[i].g)+Math.Abs(before[i].b-after[i].b)>24)changed++;if(after[i].r>210&&after[i].b>210&&after[i].g<60)pink++;}
                        if(changed<100||pink>8)throw new Exception("Missing root change or shader error pixels");
                        report.views.Add(new View{id=site.id,base_vertices=baseMesh.vertexCount,shared_vertices=shared.vertexCount,appended_vertices=split.ExtraColors.Length,changed_pixels=changed,pink_pixels=pink,root_sha256=Sha(rootPath),terrain_sha256=Sha(terrainPath)});
                    }
                    finally{if(specimen)UnityEngine.Object.DestroyImmediate(specimen);if(supportObject)UnityEngine.Object.DestroyImmediate(supportObject);foreach(var mesh in new[]{shared,baseMesh,support})if(mesh)UnityEngine.Object.DestroyImmediate(mesh);prototype.ReleaseMeshData();}
                    GC.Collect();if(report.views.Count==2)break;
                }
                if(report.views.Count!=2)throw new Exception("Expected two native root comparisons");camera.targetTexture=null;rt.Release();report.status="PASS";
            }
            catch(Exception e){report.status="FAIL";report.error=e.ToString();Debug.LogException(e);}
            finally
            {
                void Cleanup(Action action){try{action();}catch(Exception e){report.status="FAIL";report.error+=" Cleanup: "+e;}}
                Cleanup(()=>{RenderTexture.active=oldRT;report.render_target_restored=RenderTexture.active==oldRT;});
                Cleanup(()=>Shader.SetGlobalVector("_VoxelAmbientSky",sky));Cleanup(()=>Shader.SetGlobalVector("_VoxelAmbientEquator",equator));Cleanup(()=>Shader.SetGlobalVector("_VoxelAmbientGround",ground));
                Cleanup(()=>Shader.SetGlobalVector("_VoxelWindState",wind));Cleanup(()=>Shader.SetGlobalVector("_VoxelWindPreviousState",oldWind));Cleanup(()=>{if(optics.Length>0)Shader.SetGlobalVectorArray("_VoxelMaterialOptics",optics);});
                Cleanup(()=>{SceneManager.SetActiveScene(previous);report.original_active_scene_restored=SceneManager.GetActiveScene()==previous;});
                Cleanup(()=>{if(owned.IsValid()&&!EditorSceneManager.CloseScene(owned,true))throw new Exception("Owned scene did not close");});
                foreach(var obj in objects)if(obj){if(obj is RenderTexture target)Cleanup(()=>target.Release());Cleanup(()=>UnityEngine.Object.DestroyImmediate(obj));}
                Cleanup(()=>
                {
                    report.original_scene_dirty_unchanged=previous.isDirty==dirty;report.original_scene_roots_unchanged=previous.rootCount==roots;
                    report.original_lighting_unchanged=RenderSettings.fog==previousFog&&RenderSettings.sun==previousSun;
                    report.shader_state_restored=Shader.GetGlobalVector("_VoxelAmbientSky")==sky&&Shader.GetGlobalVector("_VoxelAmbientEquator")==equator&&Shader.GetGlobalVector("_VoxelAmbientGround")==ground&&Shader.GetGlobalVector("_VoxelWindState")==wind&&Shader.GetGlobalVector("_VoxelWindPreviousState")==oldWind&&Shader.GetGlobalVectorArray("_VoxelMaterialOptics").SequenceEqual(optics);
                    if(!report.original_scene_dirty_unchanged||!report.original_scene_roots_unchanged||!report.original_active_scene_restored||!report.original_lighting_unchanged||!report.shader_state_restored||!report.render_target_restored)throw new Exception("Original scene/shader/target state changed unexpectedly");
                });
                report.seconds=watch.Elapsed.TotalSeconds;File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));
            }
            Debug.Log("Root-contact native preview "+report.status+": "+output);
        }
    }
}
