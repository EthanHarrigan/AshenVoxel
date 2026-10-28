#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using AshenVoxel.WorldScale;
using AshenVoxel.WorldScaleRuntime;
using AshenVoxel.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
namespace AshenVoxel.Exploration.Editor
{
 [InitializeOnLoad] public static class TerrainSeamAppearanceChecks
 {
  static readonly string Root=Path.GetFullPath(Path.Combine(Application.dataPath,".."));
  static readonly string Request=Path.Combine(Root,".terrain-cache/terrain-seam-appearance.request");
  static Probe probe;static double nextPoll;
  static TerrainSeamAppearanceChecks(){EditorApplication.update+=Poll;AssemblyReloadEvents.beforeAssemblyReload+=Abort;EditorApplication.quitting+=Abort;}
  static void Abort(){if(probe!=null){probe.End("CANCELED","Editor reloading/closing");probe=null;}}
  static void Poll()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode){if(probe!=null)Abort();return;}
   if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
   try{if(probe!=null){if(probe.Tick())probe=null;return;}if(EditorApplication.timeSinceStartup<nextPoll)return;nextPoll=EditorApplication.timeSinceStartup+.5;
    if(!File.Exists(Request)||File.ReadAllText(Request).Trim()!="run")return;File.Delete(Request);probe=new Probe();probe.Begin();}
   catch(Exception e){if(probe!=null){probe.End("FAIL",e.ToString());probe=null;}else Debug.LogException(e);}
  }
  [Serializable] sealed class Report
  {
   public string status="RUNNING",error,unity,gpu,scope="Native Edit-mode production exact seam meshes and URP GroundSurface; synthetic curved unequal-resolution/contact-cut fixtures with top-only production terrain triangles (skirts omitted diagnostically). Oblique geometry/depth occlusion coverage, not full-world/FPS acceptance.";
   public int[] seam_pixels=new int[2],filled_gap_pixels=new int[2],unfilled_gap_pixels=new int[2],candidate_foreground=new int[2];public bool shader_error,restored;public long seam_bytes;
   public List<string> errors=new List<string>(),source_sha256=new List<string>();
  }
  sealed class Field:IWorldFieldBounds
  {public double Offset;public string SourceIdentity=>"native-seam-synthetic-"+Offset;public SurfaceSample Sample(double x,double z)=>new SurfaceSample(40+Offset+Math.Sin(x*.16)*3+Math.Cos(z*.19)*2,6,6,true);public HeightBounds QueryBounds(double a,double b,double c,double d)=>new HeightBounds(35+Offset,45+Offset);}
  sealed class Probe
  {
   const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
   readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();readonly Report report=new Report();readonly List<(Mesh mesh,Matrix4x4 matrix,Material material)>[] terrain={new List<(Mesh,Matrix4x4,Material)>(),new List<(Mesh,Matrix4x4,Material)>()};
   readonly List<(Mesh mesh,Matrix4x4 matrix,Material material)>[] bridges={new List<(Mesh,Matrix4x4,Material)>(),new List<(Mesh,Matrix4x4,Material)>()};
   readonly Dictionary<string,Vector4> globals=new Dictionary<string,Vector4>();Vector4[] optics;Scene scene;Camera camera;RenderTexture target,prior;Texture2D texture;Material material;string folder;int fixture,stage;double started,warmAt;bool ended;
   T Own<T>(T v)where T:UnityEngine.Object{owned.Add(v);return v;}
   static object Get(object o,string n)=>o.GetType().GetField(n,Flags).GetValue(o);static void Set(object o,string n,object v)=>o.GetType().GetField(n,Flags).SetValue(o,v);static object Call(object o,string n,params object[] a)=>o.GetType().GetMethod(n,Flags).Invoke(o,a);
   public void Begin()
   {
    started=EditorApplication.timeSinceStartup;prior=RenderTexture.active;folder=Path.Combine(Root,"Logs/TerrainSeamAppearance",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));Directory.CreateDirectory(folder);
    report.unity=Application.unityVersion;report.gpu=SystemInfo.graphicsDeviceName;
    foreach(string name in new[]{"_VoxelAmbientSky","_VoxelAmbientEquator","_VoxelAmbientGround"})globals[name]=Shader.GetGlobalVector(name);optics=Shader.GetGlobalVectorArray("_VoxelMaterialOptics");MaterialOpticsProfile.Apply(Resources.Load<MaterialOpticsProfile>("MaterialOptics"));MaterialOpticsProfile.SetAmbient(Color.white,Color.white,Color.white);
    Application.logMessageReceived+=Log;scene=EditorSceneManager.NewPreviewScene();
    var cameraObject=Own(new GameObject("Terrain seam offscreen camera"));SceneManager.MoveGameObjectToScene(cameraObject,scene);camera=cameraObject.AddComponent<Camera>();camera.scene=scene;camera.enabled=false;camera.cullingMask=1<<29;camera.orthographic=true;camera.orthographicSize=20;camera.nearClipPlane=.05f;camera.farClipPlane=300;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.allowHDR=false;camera.allowMSAA=false;camera.depthTextureMode=DepthTextureMode.Depth;
    target=Own(new RenderTexture(512,512,24));target.Create();camera.targetTexture=target;texture=Own(new Texture2D(512,512,TextureFormat.RGB24,false));
    material=Own(new Material(Shader.Find("AshenVoxel/GroundSurface")));material.SetVector("_VoxelTerrainGrid",Vector4.zero);
    foreach(string file in new[]{"Assets/AshenVoxel/WorldScaleData/HeightProxySurface.cs","Assets/AshenVoxel/WorldScaleRuntime/WorldExplorerTerrainSeams.cs","Assets/AshenVoxel/WorldScaleRuntime/GroundSurface.shader","Assets/AshenVoxel/Exploration/Editor/TerrainSeamAppearanceChecks.cs"})using(var sha=SHA256.Create())report.source_sha256.Add(file+" "+BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Path.Combine(Root,file)))).Replace("-","").ToLowerInvariant());
    Build(0);Build(1);Write();
   }
   LodTile Tile(long x,long z,int level){double d=25.6*(1<<level);return new LodTile(new TileKey(x,z,level),x*d,z*d,d,0,100);}
   object Add(WorldExplorer w,LodTile tile,int n,Field field,bool contact,int index,Material mat)
   {
    var data=HeightProxySurface.Build(new SurfaceMaterialSampler(field,new SurfaceProfile(enableSnow:false)),tile,n,1);var mesh=Own(new Mesh{name="Production top triangles; diagnostic no skirts",indexFormat=IndexFormat.UInt32});
    var layout=(VertexAttributeDescriptor[])typeof(WorldExplorer).GetField("VoxelVertexLayout",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
    int[] indices=data.Indices.Take(n*n*6).ToArray();mesh.SetVertexBufferParams(data.Vertices.Length,layout);mesh.SetVertexBufferData(data.Vertices,0,0,data.Vertices.Length);mesh.SetIndexBufferParams(indices.Length,IndexFormat.UInt32);mesh.SetIndexBufferData(indices,0,0,indices.Length);var bounds=new Bounds(new Vector3((float)tile.SizeMeters*.5f,(data.MinimumLocalY+data.MaximumLocalY)*.5f,(float)tile.SizeMeters*.5f),new Vector3((float)tile.SizeMeters,data.MaximumLocalY-data.MinimumLocalY+1,(float)tile.SizeMeters));mesh.SetSubMesh(0,new SubMeshDescriptor(0,indices.Length){bounds=bounds,vertexCount=data.Vertices.Length},MeshUpdateFlags.DontRecalculateBounds);mesh.bounds=bounds;mesh.UploadMeshData(true);
    var go=Own(new GameObject("Native seam surface"));SceneManager.MoveGameObjectToScene(go,scene);go.transform.position=new Vector3((float)tile.OriginXMeters,(float)data.AnchorY,(float)tile.OriginZMeters);
    var s=Activator.CreateInstance(typeof(WorldExplorer).GetNestedType("Surface",BindingFlags.NonPublic),true);Set(s,"Object",go);Set(s,"Mesh",mesh);Set(s,"Tile",tile);Set(s,"Quads",n);Set(s,"AnchorY",data.AnchorY);Set(s,"LocalHeights",data.Heights);Set(s,"HeightProxy",true);
    ((IDictionary)Get(w,contact?"contactSurfaces":"surfaces")).Add(tile.Key,s);if(!contact)((HashSet<TileKey>)Get(w,"active")).Add(tile.Key);terrain[index].Add((mesh,go.transform.localToWorldMatrix,mat));return s;
   }
   void Build(int index)
   {
    var root=Own(new GameObject("Native production seam builder"));SceneManager.MoveGameObjectToScene(root,scene);var w=root.AddComponent<WorldExplorer>();w.enabled=false;w.UseVoxelLodTerrain=true;w.ConnectedTerrainSurface=true;w.TerrainMaterial=material;Set(w,"field",new Field());Set(w,"surfaceProfile",new SurfaceProfile(enableSnow:false));var surfaces=new List<object>();
    if(index==0){surfaces.Add(Add(w,Tile(0,0,1),8,new Field(),false,index,material));surfaces.Add(Add(w,Tile(2,0,0),64,new Field{Offset=2},false,index,material));surfaces.Add(Add(w,Tile(2,1,0),16,new Field{Offset=2},false,index,material));}
    else
    {
     var masked=Own(new Material(material));var mask=new Vector4[64];mask[5].y=1;masked.SetVector("_VoxelTerrainGrid",new Vector4(0,0,25.6f,16));masked.SetVectorArray("_VoxelTerrainMask",mask);
     surfaces.Add(Add(w,Tile(1,0,2),8,new Field(),false,index,masked));surfaces.Add(Add(w,Tile(5,1,0),64,new Field{Offset=3},true,index,material));
    }
    foreach(var s in surfaces)Call(w,"TerrainSeamChanged",s);for(int i=0;i<64&&w.TerrainSeamPending>0;i++)Call(w,"TickTerrainSeams");
    if(w.TerrainSeamPending>0)throw new Exception("Native seam publication blocked; pending demand retained");report.seam_bytes+=w.TerrainSeamBytes;
    foreach(DictionaryEntry entry in (IDictionary)Get(w,"terrainSeams")){var seam=entry.Value;var go=(GameObject)Get(seam,"Object");var mesh=(Mesh)Get(seam,"Mesh");if(go!=null&&mesh!=null){var m=go.GetComponent<MeshRenderer>().sharedMaterial;bridges[index].Add((mesh,go.transform.localToWorldMatrix,m));}}
    if(bridges[index].Count==0)throw new Exception("Production seam fixture emitted no bridge meshes");
   }
   void Pose(){if(fixture==0){camera.orthographicSize=20;camera.transform.position=new Vector3(85,48,-30);camera.transform.LookAt(new Vector3(51.2f,40,24));}else{camera.orthographicSize=21;camera.transform.position=new Vector3(105,49,0);camera.transform.LookAt(new Vector3(140,40,38));}}
   void Draw(bool ground,bool seam){if(ground)foreach(var p in terrain[fixture])Graphics.DrawMesh(p.mesh,p.matrix,p.material,29,camera,0,null,ShadowCastingMode.On,true,null,LightProbeUsage.Off);if(seam)foreach(var p in bridges[fixture])Graphics.DrawMesh(p.mesh,p.matrix,p.material,29,camera,0,null,ShadowCastingMode.On,true,null,LightProbeUsage.Off);}
   Color32[] Capture(bool ground,bool seam,string name){Draw(ground,seam);camera.Render();var old=RenderTexture.active;try{RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,512,512),0,0);texture.Apply();var pixels=texture.GetPixels32();File.WriteAllBytes(Path.Combine(folder,fixture+"-"+name+".png"),texture.EncodeToPNG());return pixels;}finally{RenderTexture.active=old;}}
   static bool Visible(Color32 p)=>p.r+p.g+p.b>20;
   public bool Tick()
   {
    if(EditorApplication.timeSinceStartup-started>20)throw new TimeoutException("Terrain seam fixture exceeded20 seconds");Pose();
    if(stage==0){Draw(true,true);camera.Render();warmAt=EditorApplication.timeSinceStartup+.05;stage=1;EditorApplication.QueuePlayerLoopUpdate();return false;}if(EditorApplication.timeSinceStartup<warmAt)return false;
    var before=Capture(true,false,"baseline");var after=Capture(true,true,"stitched");var seam=Capture(false,true,"bridge-only");
    int footprint=0,filled=0,unfilled=0,foreground=0;for(int i=0;i<before.Length;i++){if(Visible(seam[i])){footprint++;if(!Visible(before[i])){if(Visible(after[i]))filled++;else unfilled++;}}if(Visible(after[i]))foreground++;if(after[i].r>200&&after[i].b>200&&after[i].g<80)throw new Exception("Magenta error shader pixel");}
    report.seam_pixels[fixture]=footprint;report.filled_gap_pixels[fixture]=filled;report.unfilled_gap_pixels[fixture]=unfilled;report.candidate_foreground[fixture]=foreground;Write();
    if(footprint<20||filled<8||unfilled>2||foreground<500)throw new Exception("Oblique gap/depth coverage gate failed: fixture"+fixture+" footprint="+footprint+" filled="+filled+" unfilled="+unfilled+" foreground="+foreground);
    if(++fixture<2){stage=0;return false;}report.shader_error=ShaderUtil.ShaderHasError(material.shader);if(report.shader_error||report.errors.Count>0)throw new Exception("Native shader/error gate failed");End("PASS",null);return true;
   }
   void Log(string text,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)report.errors.Add(text+"\n"+stack);}
   void Write(){File.WriteAllText(Path.Combine(folder,"result.json"),JsonUtility.ToJson(report,true));File.WriteAllText(Path.Combine(Root,".terrain-cache/terrain-seam-appearance.latest"),Path.Combine(folder,"result.json"));}
   public void End(string status,string error)
   {
    if(ended)return;ended=true;report.status=status;report.error=error;Application.logMessageReceived-=Log;
    try{if(camera!=null)camera.targetTexture=null;RenderTexture.active=prior;if(target!=null)target.Release();for(int i=owned.Count-1;i>=0;i--)if(owned[i]!=null)UnityEngine.Object.DestroyImmediate(owned[i]);if(scene.IsValid())EditorSceneManager.ClosePreviewScene(scene);foreach(var p in globals)Shader.SetGlobalVector(p.Key,p.Value);if(optics!=null)Shader.SetGlobalVectorArray("_VoxelMaterialOptics",optics.Length==0?new Vector4[32]:optics);report.restored=true;}
    catch(Exception e){report.status="FAIL";report.error=(report.error??"")+"\nCleanup "+e;}
    if(folder!=null)Write();Debug.Log("Terrain seam appearance "+report.status+" "+folder);
   }
  }
 }
}
#endif
