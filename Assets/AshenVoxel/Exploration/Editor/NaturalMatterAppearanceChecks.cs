#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using AshenVoxel.Core;
using AshenVoxel.Rendering;
using AshenVoxel.Exploration.Experimental;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using NV=System.Numerics.Vector3;
namespace AshenVoxel.Exploration.Editor
{
    [InitializeOnLoad] public static class NaturalMatterAppearanceChecks
    {
        static readonly string Root=Path.GetFullPath(Path.Combine(Application.dataPath,".."));
        static readonly string Request=Path.Combine(Root,".terrain-cache/natural-matter-appearance.request");
        static Probe probe;static double nextPoll;
        static NaturalMatterAppearanceChecks(){EditorApplication.update+=Poll;AssemblyReloadEvents.beforeAssemblyReload+=Abort;EditorApplication.quitting+=Abort;}
        static void Abort(){if(probe!=null){probe.End("CANCELED","Editor closing/reloading");probe=null;}}
        static void Poll()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode){if(probe!=null)Abort();return;} // Never consume a request or toggle Play.
            if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            try
            {
                if(probe!=null){if(probe.Tick())probe=null;return;}
                if(EditorApplication.timeSinceStartup<nextPoll)return;nextPoll=EditorApplication.timeSinceStartup+.5;
                if(!File.Exists(Request)||File.ReadAllText(Request).Trim()!="run")return;File.Delete(Request);probe=new Probe();probe.Begin();
            }
            catch(Exception error){if(probe!=null){probe.End("FAIL",error.ToString());probe=null;}else Debug.LogException(error);}
        }
        [Serializable] sealed class Report
        {
            public string status="RUNNING",error,unity,gpu,scope="Native Edit-mode two-camera Graphics.DrawMesh appearance comparison; actual borrowed prototype/mask/greedy patch path; not full-scene gameplay/FPS";
            public int initial_pixels,initial_different,cut_pixels,cut_different,changed_after_cut,removed,cargo,patch_vertices,original_parts;
            public int feedback_pixels,feedback_pulse_pixels;public bool feedback_pending_after_second;
            public int source_cells,island_cells,individual_cells,source_world_nonair;public int[] expected_mesh_vertices,original_mesh_vertices;public bool renderer_current;
            public bool conserved,mask_enabled,shared_mesh_survived,shader_error;public List<string> errors=new List<string>(),source_sha256=new List<string>();
        }
        sealed class Probe
        {
            readonly Report report=new Report();readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();readonly Dictionary<string,Vector4> globals=new Dictionary<string,Vector4>();
            Scene scene;Camera baseline,candidate;RenderTexture a,b,prior;Texture2D image;Material material;Mesh[] original,expected;NaturalTreePhysics physics;NaturalMatterRenderer renderer;VoxelWorld world;
            Vector4[] optics;Color32[] initial,feedback;string folder;double started,warmAt;int stage;bool ended;
            T Own<T>(T value)where T:UnityEngine.Object{owned.Add(value);return value;}
            public void Begin()
            {
                started=EditorApplication.timeSinceStartup;prior=RenderTexture.active;folder=Path.Combine(Root,"Logs/NaturalMatterAppearance",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));Directory.CreateDirectory(folder);
                report.unity=Application.unityVersion;report.gpu=SystemInfo.graphicsDeviceName;
                foreach(var name in new[]{"_VoxelAmbientSky","_VoxelAmbientEquator","_VoxelAmbientGround","_VoxelWindState","_VoxelWindPreviousState"})globals[name]=Shader.GetGlobalVector(name);
                optics=Shader.GetGlobalVectorArray("_VoxelMaterialOptics");MaterialOpticsProfile.Apply(Resources.Load<MaterialOpticsProfile>("MaterialOptics"));MaterialOpticsProfile.SetAmbient(Color.white,Color.white,Color.white);
                Shader.SetGlobalVector("_VoxelWindState",Vector4.zero);Shader.SetGlobalVector("_VoxelWindPreviousState",Vector4.zero);Application.logMessageReceived+=Log;
                foreach(var path in new[]{"Assets/AshenVoxel/Rendering/WorldSurface.shader","Assets/AshenVoxel/Rendering/GreedyChunkMesher.cs","Assets/AshenVoxel/Exploration/NaturalMatterRenderer.cs","Assets/AshenVoxel/Exploration/NaturalTreePhysics.cs","Assets/AshenVoxel/Exploration/Editor/NaturalMatterAppearanceChecks.cs"})
                    using(var sha=SHA256.Create())report.source_sha256.Add(path+" "+BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Path.Combine(Root,path)))).Replace("-","").ToLowerInvariant());
                scene=EditorSceneManager.NewPreviewScene();baseline=Camera("Appearance baseline",29,out a);candidate=Camera("Appearance candidate",30,out b);image=Own(new Texture2D(256,256,TextureFormat.RGB24,false));
                material=Own(new Material(Resources.Load<Material>("VoxelSurface")));material.SetFloat("_MatterChunkMaskEnabled",0);material.SetFloat("_VoxelWindStaticInstances",0);
                world=new VoxelWorld(48,16,16,.1f,714103);for(int z=0;z<8;z++)for(int y=0;y<8;y++)for(int x=0;x<48;x++)world.Set(x,y,z,MaterialId.Wood,VoxelColor.Pack(140,110,80));
                original=BuildMeshes();expected=original;var root=Own(new GameObject("Appearance candidate matter"));root.hideFlags=HideFlags.HideAndDontSave;root.layer=30;SceneManager.MoveGameObjectToScene(root,scene);
                physics=new NaturalTreePhysics(world,new Int3(0,0,0),0){MovementEnabled=false,ExternalRigidBodies=true,SuctionEnabled=true,CollectionRadius=100,Hand=NV.Zero};
                renderer=new NaturalMatterRenderer(root.transform,material){DrawCamera=candidate};renderer.SetPhysics(physics,new NaturalMatterRenderSource(original.Select(mesh=>new NaturalMatterRenderSource.Part(mesh,0,false)).ToArray(),material));renderer.SetVisible(false);report.original_parts=original.Length;Write();
            }
            Camera Camera(string name,int layer,out RenderTexture target)
            {
                var go=Own(new GameObject(name));go.hideFlags=HideFlags.HideAndDontSave;SceneManager.MoveGameObjectToScene(go,scene);var camera=go.AddComponent<Camera>();camera.scene=scene;camera.enabled=false;
                camera.orthographic=true;camera.orthographicSize=2.8f;camera.transform.position=new Vector3(2.4f,1.8f,-5);camera.transform.LookAt(new Vector3(2.4f,.4f,.4f));camera.nearClipPlane=.05f;camera.farClipPlane=30;camera.cullingMask=1<<layer;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.allowHDR=false;camera.allowMSAA=false;target=Own(new RenderTexture(256,256,24));target.Create();camera.targetTexture=target;return camera;
            }
            Mesh[] BuildMeshes(){var meshes=new Mesh[3];for(int x=0;x<3;x++){meshes[x]=Own(new Mesh());new GreedyChunkMesher().Build(world,new Int3(x,0,0),meshes[x]);}return meshes;}
            void Log(string text,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)report.errors.Add(text+"\n"+trace);}
            public bool Tick()
            {
                if(EditorApplication.timeSinceStartup-started>15)throw new TimeoutException("Appearance fixture exceeded15 seconds");
                if(renderer.Error!=null)throw new InvalidOperationException(renderer.Error);
                if(stage==5||stage==6||stage==7)
                {
                    // Deliberately leave support unresolved. Acknowledgement must
                    // render on the old surface immediately, including a long wait.
                    renderer.SetVisible(true);renderer.UpdateBreakFeedback(stage==7?1.08f:1f);renderer.Tick(Vector3.zero);candidate.Render();
                    if(stage==5){stage=6;warmAt=EditorApplication.timeSinceStartup+.05;return false;}
                    if(EditorApplication.timeSinceStartup<warmAt)return false;
                    var pixels=Read(b);int changed=0;var reference=stage==6?initial:feedback;
                    for(int i=0;i<pixels.Length;i++)if(Math.Abs(reference[i].r-pixels[i].r)+Math.Abs(reference[i].g-pixels[i].g)+Math.Abs(reference[i].b-pixels[i].b)>10)changed++;
                    if(stage==6)
                    {
                        feedback=pixels;report.feedback_pixels=changed;report.feedback_pending_after_second=!renderer.Current;
                        Save(pixels,"02a-pending-break-feedback");
                        if(changed<5||changed>report.initial_pixels/3||!report.feedback_pending_after_second)throw new Exception("Pending break feedback absent, unbounded or prematurely cleared");
                        stage=7;warmAt=EditorApplication.timeSinceStartup+.05;return false;
                    }
                    report.feedback_pulse_pixels=changed;Save(pixels,"02b-pending-break-pulse");
                    if(changed<3)throw new Exception("Pending break feedback did not animate");
                    renderer.SetVisible(false);stage=2;return false;
                }
                if(stage==2){physics.Step(.1f,8192,8192);renderer.Tick(Vector3.zero);Diagnostics();if(!renderer.Current||physics.CargoCells!=report.removed)return false;expected=BuildMeshes();stage=3;}
                if(stage==0||stage==3)
                {
                    renderer.SetVisible(true);renderer.Tick(Vector3.zero);if(!renderer.Current){renderer.SetVisible(false);return false;}
                    // New edited baseline meshes have never been submitted by the
                    // candidate. Warm BOTH camera-specific upload/render paths,
                    // discard those frames, then compare on the following update.
                    DrawBaseline();baseline.Render();candidate.Render();Diagnostics();
                    warmAt=EditorApplication.timeSinceStartup+.05;stage++;EditorApplication.QueuePlayerLoopUpdate();return false;
                }
                if(EditorApplication.timeSinceStartup<warmAt)return false;
                DrawBaseline();
                renderer.Tick(Vector3.zero);baseline.Render();var pixelsA=Read(a);candidate.Render();var pixelsB=Read(b);renderer.SetVisible(false);
                int different=0,visible=0;for(int i=0;i<pixelsA.Length;i++){var p=pixelsA[i];var q=pixelsB[i];if(p.r+p.g+p.b>20)visible++;if(Math.Abs(p.r-q.r)>2||Math.Abs(p.g-q.g)>2||Math.Abs(p.b-q.b)>2)different++;if(q.r>200&&q.b>200&&q.g<80)throw new Exception("Error-shader magenta pixels");}
                Save(pixelsA,stage==1?"01-baseline":"03-edited-baseline");Save(pixelsB,stage==1?"02-borrowed":"04-mask-and-patches");
                if(stage==1){report.initial_pixels=visible;report.initial_different=different;}else{report.cut_pixels=visible;report.cut_different=different;}
                Diagnostics();if(visible<50||different>8)throw new Exception("Appearance mismatch "+different+" pixels / "+visible+" visible at stage "+stage);
                if(stage==1)
                {
                    report.initial_pixels=visible;report.initial_different=different;initial=pixelsB;report.removed=physics.CutSphere(new Int3(4,7,0));if(report.removed<1)throw new Exception("Fixture cut removed no matter");
                    renderer.SetVisible(true);renderer.BeginBreakFeedback(new Vector3(.45f,.75f,.05f),0);stage=5;return false;
                }
                report.cut_pixels=visible;report.cut_different=different;for(int i=0;i<initial.Length;i++)if(Math.Abs(initial[i].r-pixelsB[i].r)+Math.Abs(initial[i].g-pixelsB[i].g)+Math.Abs(initial[i].b-pixelsB[i].b)>10)report.changed_after_cut++;
                report.mask_enabled=(bool)typeof(NaturalMatterRenderer).GetField("maskEnabled",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(renderer);var patches=(Mesh[])typeof(NaturalMatterRenderer).GetField("published",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(renderer);report.patch_vertices=patches.Sum(mesh=>mesh==null?0:mesh.vertexCount);
                report.cargo=physics.CargoCells;report.conserved=physics.Conserved;report.shader_error=ShaderUtil.ShaderHasError(material.shader);renderer.Dispose();renderer=null;report.shared_mesh_survived=original.All(mesh=>mesh!=null&&mesh.vertexCount>0);
                if(report.changed_after_cut<10||!report.mask_enabled||report.patch_vertices==0||!report.conserved||!report.shared_mesh_survived||report.shader_error||report.errors.Count>0)throw new Exception("Cut/mask/patch/conservation/shared-mesh appearance gate failed");End("PASS",null);return true;
            }
            void DrawBaseline(){foreach(var mesh in expected)Graphics.DrawMesh(mesh,Matrix4x4.identity,material,29,baseline,0,null,ShadowCastingMode.Off,true,null,LightProbeUsage.Off);}
            void Diagnostics()
            {
                report.source_cells=physics?.SourceCells??0;report.island_cells=physics?.IslandCells??0;report.individual_cells=physics?.IndividualCells??0;report.cargo=physics?.CargoCells??0;report.conserved=physics==null||physics.Conserved;
                report.source_world_nonair=world==null?0:world.Chunks.Values.Sum(chunk=>chunk.NonAirCount);report.renderer_current=renderer!=null&&renderer.Current;
                report.expected_mesh_vertices=expected?.Select(mesh=>mesh==null?0:mesh.vertexCount).ToArray();report.original_mesh_vertices=original?.Select(mesh=>mesh==null?0:mesh.vertexCount).ToArray();
                if(renderer!=null){report.mask_enabled=(bool)typeof(NaturalMatterRenderer).GetField("maskEnabled",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(renderer);var patches=(Mesh[])typeof(NaturalMatterRenderer).GetField("published",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(renderer);report.patch_vertices=patches.Sum(mesh=>mesh==null?0:mesh.vertexCount);}
            }
            Color32[] Read(RenderTexture target){var old=RenderTexture.active;try{RenderTexture.active=target;image.ReadPixels(new Rect(0,0,256,256),0,0);image.Apply();return image.GetPixels32();}finally{RenderTexture.active=old;}}
            void Save(Color32[] pixels,string name){image.SetPixels32(pixels);image.Apply();File.WriteAllBytes(Path.Combine(folder,name+".png"),image.EncodeToPNG());}
            void Write(){File.WriteAllText(Path.Combine(folder,"result.json"),JsonUtility.ToJson(report,true));File.WriteAllText(Path.Combine(Root,".terrain-cache/natural-matter-appearance.latest"),Path.Combine(folder,"result.json"));}
            public void End(string status,string error)
            {
                if(ended)return;ended=true;report.status=status;report.error=error;Diagnostics();Application.logMessageReceived-=Log;
                try{renderer?.Dispose();if(baseline!=null)baseline.targetTexture=null;if(candidate!=null)candidate.targetTexture=null;RenderTexture.active=prior;if(a!=null)a.Release();if(b!=null)b.Release();for(int i=owned.Count-1;i>=0;i--)if(owned[i]!=null)UnityEngine.Object.DestroyImmediate(owned[i]);if(scene.IsValid())EditorSceneManager.ClosePreviewScene(scene);foreach(var entry in globals)Shader.SetGlobalVector(entry.Key,entry.Value);if(optics!=null)Shader.SetGlobalVectorArray("_VoxelMaterialOptics",optics.Length==0?new Vector4[32]:optics);}
                catch(Exception cleanup){report.status="FAIL";report.error=(report.error??"")+"\nCleanup "+cleanup;}
                if(folder!=null)Write();Debug.Log("Natural matter appearance "+report.status+" "+folder);
            }
        }
    }
}
#endif
