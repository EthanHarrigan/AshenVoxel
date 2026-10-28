#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using AshenVoxel.Core;
using AshenVoxel.Exploration.Experimental;
using AshenVoxel.Rendering;
using AshenVoxel.WorldScale;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using NV=System.Numerics.Vector3;

namespace AshenVoxel.Exploration.Editor
{
    /// <summary>Opt-in, Edit-mode-only isolated GPU fixture. Never exits Unity,
    /// opens a window, changes the user's saved pose or enters Play mode.</summary>
    [InitializeOnLoad]
    public static class NaturalMatterNativeCheck
    {
        static readonly string Root=Path.GetFullPath(Path.Combine(Application.dataPath,".."));
        static readonly string Request=Path.Combine(Root,".terrain-cache/natural-matter-native.request");
        static readonly string Latest=Path.Combine(Root,".terrain-cache/natural-matter-native.latest");
        static Runner runner;static double nextPoll;
        static NaturalMatterNativeCheck()
        {
            EditorApplication.update+=Poll;AssemblyReloadEvents.beforeAssemblyReload+=Abort;
            EditorApplication.quitting+=Abort;
        }
        static void Abort(){if(runner!=null){runner.Finish("CANCELED","Editor closing or assemblies reloading");runner=null;}}
        static void Poll()
        {
            if(runner!=null)
            {
                if(EditorApplication.isPlayingOrWillChangePlaymode){runner.Finish("CANCELED","Play mode requested during isolated fixture");runner=null;return;}
                if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
                try{if(runner.Tick())runner=null;}catch(Exception error){runner.Finish("FAIL",error.ToString());runner=null;}
                return;
            }
            if(EditorApplication.timeSinceStartup<nextPoll)return;nextPoll=EditorApplication.timeSinceStartup+1;
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||!File.Exists(Request))return;
            try
            {
                // The watcher accepts exactly the literal opt-in command.
                if(File.ReadAllText(Request).Trim()!="run")return;
                File.Delete(Request);runner=new Runner();runner.Begin();
            }
            catch(Exception error){if(runner!=null){runner.Finish("FAIL",error.ToString());runner=null;}else Debug.LogException(error);}
        }
        [Serializable] sealed class Result
        {
            public string status="RUNNING",scope="Native isolated Edit-mode explicit command-buffer draws of actual renderer cache meshes and current weighted particle leaders with the actual URP materials; not automatic gameplay MeshRenderer acceptance, full Exploration gameplay or FPS",gpu,graphics_api,unity,started_utc,error;
            public bool mesh_regression,shader_error,conserved,terrain_event_lifecycle,dormant_zero_candidates,kick_wake,terrain_wake;public int initial_cells,cuts,physics_steps,editor_ticks,maximum_build_candidates,compiled_passes;
            public string active_scene;public bool original_scene_dirty,final_scene_dirty;public int original_scene_roots,final_scene_roots;
            public List<Frame> frames=new List<Frame>();public List<string> errors=new List<string>();public List<Identity> sources=new List<Identity>();
        }
        [Serializable] sealed class Frame
        {
            public string name,png;public int source,island,individual,cargo,drawn_debris,loose_parcels,ground_impacts,awake,dormant,pending_wake;
            public float render_cell_size;public bool conserved,current;public long rgb_energy;public int changed_from_clear;
            public int[] cache_vertices;public int source_renderer_candidates,instanced_pixel_changes,tinted_instance_pixels;
            public Vector3 body_position;public Quaternion body_rotation;
        }
        [Serializable] sealed class Identity{public string path,sha256;}
        sealed class Runner
        {
            readonly Result report=new Result();readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
            readonly Dictionary<string,Vector4> globals=new Dictionary<string,Vector4>();
            Scene scene,originalScene;GameObject root;Camera camera;RenderTexture target,priorTarget;Texture2D image;
            Material material;NaturalMatterRenderer renderer;NaturalTreePhysics physics;VoxelWorld world;Int3 anchor;
            Mesh floorMesh;Material floorMaterial;
            Vector4[] priorOptics;string folder;int stage,settledStep=-1,detachedStep=-1,capturedDebris;double started;bool finished;
            static readonly string[] GlobalNames={"_VoxelAmbientSky","_VoxelAmbientEquator","_VoxelAmbientGround","_VoxelWindState","_VoxelWindPreviousState"};
            public void Begin()
            {
                started=EditorApplication.timeSinceStartup;folder=Path.Combine(Root,"Logs/NaturalMatterNative",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));Directory.CreateDirectory(folder);
                priorTarget=RenderTexture.active;originalScene=SceneManager.GetActiveScene();report.active_scene=originalScene.path;report.original_scene_dirty=originalScene.isDirty;report.original_scene_roots=originalScene.rootCount;
                report.started_utc=DateTime.UtcNow.ToString("O");report.gpu=SystemInfo.graphicsDeviceName;report.graphics_api=SystemInfo.graphicsDeviceType.ToString();report.unity=Application.unityVersion;
                foreach(string relative in new[]{"Assets/AshenVoxel/Exploration/NaturalTreePhysics.cs","Assets/AshenVoxel/Exploration/ExplorationNaturalTreeMatter.cs","Assets/AshenVoxel/Exploration/ExplorationLiveRefinement.cs","Assets/AshenVoxel/Exploration/NaturalMatterRenderer.cs","Assets/AshenVoxel/Exploration/NaturalTreeTemplate.cs","Assets/AshenVoxel/Rendering/WorldSurface.shader","Assets/AshenVoxel/Exploration/Editor/NaturalMatterNativeCheck.cs"})
                {string path=Path.Combine(Root,relative);if(File.Exists(path))using(var sha=SHA256.Create())report.sources.Add(new Identity{path=relative,sha256=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant()});}
                Application.logMessageReceived+=Log;
                foreach(string key in GlobalNames)globals[key]=Shader.GetGlobalVector(key);priorOptics=Shader.GetGlobalVectorArray("_VoxelMaterialOptics");
                MaterialOpticsProfile.Apply(Resources.Load<MaterialOpticsProfile>("MaterialOptics"));MaterialOpticsProfile.SetAmbient(Color.white,Color.white,Color.white);
                Shader.SetGlobalVector("_VoxelWindState",Vector4.zero);Shader.SetGlobalVector("_VoxelWindPreviousState",Vector4.zero);
                var regression=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("AshenVoxel.Rendering.Editor.MeshRegressionChecks",false)).FirstOrDefault(t=>t!=null);
                if(regression==null)throw new InvalidOperationException("Mesh regression Editor assembly not loaded");
                regression.GetMethod("Run",BindingFlags.Static|BindingFlags.Public).Invoke(null,null);report.mesh_regression=true;
                scene=EditorSceneManager.NewPreviewScene();root=Own(new GameObject("Natural matter native isolated fixture"));root.hideFlags=HideFlags.HideAndDontSave;root.layer=29;SceneManager.MoveGameObjectToScene(root,scene);
                CheckTerrainEventLifecycle();
                var cameraObject=Own(new GameObject("Natural matter native camera"));cameraObject.hideFlags=HideFlags.HideAndDontSave;cameraObject.layer=29;SceneManager.MoveGameObjectToScene(cameraObject,scene);
                camera=cameraObject.AddComponent<Camera>();camera.scene=scene;camera.enabled=false;camera.orthographic=true;camera.nearClipPlane=.05f;camera.farClipPlane=200;
                camera.cullingMask=1<<29;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.045f,.06f,1);camera.allowHDR=false;camera.allowMSAA=false;
                target=Own(new RenderTexture(512,512,24));target.Create();camera.targetTexture=target;image=Own(new Texture2D(512,512,TextureFormat.RGB24,false));priorTarget=RenderTexture.active;
                material=Resources.Load<Material>("VoxelSurface");if(material==null)throw new InvalidOperationException("VoxelSurface resource missing");
                var floorObject=Own(new GameObject("Natural matter fixture floor"));floorObject.hideFlags=HideFlags.HideAndDontSave;floorObject.layer=29;SceneManager.MoveGameObjectToScene(floorObject,scene);
                floorMesh=Own(new Mesh{name="Native fixture floor only"});floorMesh.vertices=new[]{new Vector3(-20,-.01f,-20),new Vector3(20,-.01f,-20),new Vector3(20,-.01f,20),new Vector3(-20,-.01f,20)};
                floorMesh.normals=new[]{Vector3.up,Vector3.up,Vector3.up,Vector3.up};var floorColor=new Color32(78,88,98,VertexAppearance.Pack(MaterialId.Stone,3));floorMesh.colors32=new[]{floorColor,floorColor,floorColor,floorColor};floorMesh.triangles=new[]{0,2,1,0,3,2};
                floorObject.AddComponent<MeshFilter>().sharedMesh=floorMesh;floorMaterial=Own(new Material(material));floorMaterial.SetFloat("_MatterInstances",0);floorMaterial.SetFloat("_VoxelWindStaticInstances",1);floorObject.AddComponent<MeshRenderer>().sharedMaterial=floorMaterial;
                world=NaturalTreeTemplate.Replay(0,out anchor,out _);physics=new NaturalTreePhysics(world,anchor,.35f){TerrainHeight=(x,z)=>0,SuctionEnabled=false,DebrisWeight=4};
                report.initial_cells=physics.Count;if(physics.Count!=3395||physics.IntactAnchoredCells()!=physics.Count)throw new InvalidOperationException("Expected complete anchored3395-cell actual birch0");
                renderer=new NaturalMatterRenderer(root.transform,material){DrawCamera=camera};renderer.SetPhysics(physics);renderer.SetVisible(false);
                // Compile the actual two cloned materials, including the instanced
                // keyword path, rather than a disconnected test shader.
                foreach(string key in new[]{"solidMaterial","debrisMaterial"})
                {
                    var value=(Material)typeof(NaturalMatterRenderer).GetField(key,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(renderer);
                    for(int pass=0;pass<value.passCount;pass++){ShaderUtil.CompilePass(value,pass,true);report.compiled_passes++;}
                    if(ShaderUtil.ShaderHasError(value.shader)){report.shader_error=true;throw new InvalidOperationException("WorldSurface shader compilation error");}
                }
                Write();
            }
            T Own<T>(T value)where T:UnityEngine.Object{owned.Add(value);return value;}
            void CheckTerrainEventLifecycle()
            {
                // Inactive host: exercise the real event/cache/coordinate adapter
                // without starting the Exploration bootstrap or changing Play mode.
                var host=Own(new GameObject("Dormant terrain event fixture"));host.hideFlags=HideFlags.HideAndDontSave;host.SetActive(false);SceneManager.MoveGameObjectToScene(host,scene);
                var boot=host.AddComponent<ExplorationBootstrap>();var live=host.AddComponent<ExplorationLiveRefinement>();var owner=host.AddComponent<ExplorationNaturalTreeMatter>();
                typeof(ExplorationBootstrap).GetProperty("LiveRefinement").SetValue(boot,live);
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=typeof(ExplorationNaturalTreeMatter);
                var w=new VoxelWorld(1,1,1,.1f,1);w.Set(0,0,0,MaterialId.Wood);var p=new NaturalTreePhysics(w,new Int3(0,0,0),0){SuctionEnabled=false,TerrainHeight=(x,z)=>0};p.Cut(new Int3(0,0,0));p.Step(.02f);
                type.GetField("boot",flags).SetValue(owner,boot);type.GetField("physics",flags).SetValue(owner,p);type.GetField("instance",flags).SetValue(owner,new WorldPoint(1000,2000,3000));
                var cache=(Dictionary<Int3,float>)type.GetField("ground",flags).GetValue(owner);cache[new Int3(0,0,0)]=0;
                type.GetMethod("GroundHeight",flags).Invoke(owner,new object[]{.05f,.05f});
                type.GetMethod("TrackTerrainChanges",flags).Invoke(owner,null);type.GetMethod("TrackTerrainChanges",flags).Invoke(owner,null);
                var eventField=typeof(ExplorationLiveRefinement).GetField("RegionChanged",flags);var callback=(Action<ExplorationRefinementBounds>)eventField.GetValue(live);
                if(callback==null||callback.GetInvocationList().Length!=1)throw new Exception("Terrain event subscription duplicated or absent");
                callback(new ExplorationRefinementBounds(900,2900,901,2901));
                if(cache.Count!=1||p.PendingWakeCandidates!=0)throw new Exception("Distant terrain event invalidated local cache or woke debris");
                callback(new ExplorationRefinementBounds(999.9,2999.9,1000.2,3000.2));
                if(cache.Count!=0||p.PendingWakeCandidates==0)throw new Exception("Global terrain event failed local cache invalidation/wake");
                owner.PublishShrubOwnership(false);
                if(eventField.GetValue(live)!=null||owner.PendingWakeCandidates!=0)throw new Exception("Retired owner retained terrain subscription or wake work");
                report.terrain_event_lifecycle=true;
            }
            void Log(string condition,string trace,LogType type)
            {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)report.errors.Add(condition+"\n"+trace);}
            public bool Tick()
            {
                if(finished)return true;report.editor_ticks++;
                if(EditorApplication.timeSinceStartup-started>120||report.physics_steps>5000)throw new TimeoutException("Bounded native fixture did not converge");
                if(stage>0)
                {
                    for(int i=0;i<4;i++)
                    {
                        physics.MovementEnabled=renderer.Current;physics.Step(.02f,8192,8192);report.physics_steps++;
                        if(!physics.Conserved)throw new InvalidOperationException("Matter conservation failed");
                    }
                }
                renderer.Tick(Vector3.zero);report.maximum_build_candidates=Math.Max(report.maximum_build_candidates,renderer.LastBuildCandidates);
                if(renderer.LastBuildCandidates>8192)throw new InvalidOperationException("Renderer candidate budget exceeded");
                if(stage==0&&renderer.Ready)
                {
                    Capture("01-intact");
                    var cells=new List<Int3>();foreach(var pair in world.Chunks)for(int i=0;i<Chunk.Volume;i++)
                    {var local=Chunk.Decode(i);var p=new Int3(pair.Key.x*16+local.x,pair.Key.y*16+local.y,pair.Key.z*16+local.z);if(p.y==anchor.y+8&&MaterialCatalog.BlocksActorBody(world.Get(p)))cells.Add(p);}
                    if(cells.Count<1||cells.Count>32)throw new InvalidOperationException("Actual birch trunk cut layer outside bounded queue");
                    foreach(var cell in cells){if(!physics.Cut(cell))throw new InvalidOperationException("Actual trunk cut refused");report.cuts++;}stage=1;
                }
                else if(stage==1&&renderer.Current&&physics.IslandCells>0)
                {
                    if(detachedStep<0)detachedStep=report.physics_steps;
                    if(report.physics_steps-detachedStep>=90){Capture("02-falling-section");stage=2;}
                }
                else if(stage==2&&physics.IslandCells==0&&!physics.StructureBusy&&physics.PendingCuts==0&&physics.GroundImpacts>0)
                {
                    if(settledStep<0)settledStep=report.physics_steps;
                    if(renderer.Current&&report.physics_steps-settledStep>=150)
                    {
                        if(physics.IndividualCells<1||physics.CargoCells!=0)throw new InvalidOperationException("Debris fixture auto-collected or produced no actual matter");
                        if(physics.AwakeDebris!=0||physics.DormantDebris!=physics.PendingDebris)throw new Exception("Settled debris not fully dormant");
                        for(int idle=0;idle<60;idle++){physics.Step(.02f);if(physics.LastBodyCandidates!=0)throw new Exception("Dormant tick visited a body");}report.dormant_zero_candidates=true;
                        Capture("03-settled-weighted-debris");if(capturedDebris<1)throw new InvalidOperationException("No instanced debris submitted");
                        int leader=physics.LooseIndex(0);var before=physics.Read(leader).Position;
                        if(physics.Kick(before-NV.UnitY*.1f,NV.UnitY)<1||physics.AwakeDebris==0)throw new Exception("Native kick failed to wake dormant parcel");
                        physics.Step(.02f);if(physics.Read(leader).Position.Y<=before.Y)throw new Exception("Native kicked parcel did not move");report.kick_wake=true;
                        physics.TerrainHeight=(x,z)=>.2f;physics.NotifyTerrainChanged(-100,-100,100,100);stage=4;
                    }
                }
                else if(stage==3&&physics.IndividualCells==0&&physics.IslandCells==0&&renderer.Current)
                {
                    if(physics.CargoCells<1)throw new InvalidOperationException("Explicit collection produced no cargo");Capture("05-explicit-collection");
                    report.conserved=physics.Conserved;if(report.errors.Count>0)throw new InvalidOperationException("Native fixture logged errors; see report");
                    Finish("PASS",null);return true;
                }
                else if(stage==4&&physics.PendingWakeCandidates==0&&physics.AwakeDebris==0)
                {
                    for(int k=0;k<physics.PendingDebris;k++)if(physics.Read(physics.LooseIndex(k)).Position.Y<.24999f)throw new Exception("Native terrain change left dormant debris below raised ground");
                    report.terrain_wake=true;Capture("04-terrain-wake-settled");
                    physics.SuctionEnabled=true;physics.Hand=new NV(0,1,0);physics.CollectionRadius=100;stage=3;
                }
                return false;
            }
            void Capture(string name)
            {
                bool any=false;Vector3 lo=Vector3.zero,hi=Vector3.zero;
                for(int i=0;i<physics.Count;i++)
                {
                    var cube=physics.Read(i);if(cube.Owner!=NaturalTreePhysics.Ownership.Source&&cube.Owner!=NaturalTreePhysics.Ownership.Island)continue;
                    var p=new Vector3(cube.Position.X,cube.Position.Y,cube.Position.Z);if(!any){lo=hi=p;any=true;}else{lo=Vector3.Min(lo,p);hi=Vector3.Max(hi,p);}
                }
                // Weighted followers retain their release coordinates; only the
                // actual loose leaders are visible and should frame this image.
                for(int i=0;i<physics.PendingDebris;i++)
                {
                    var cube=physics.ReadLocal(physics.LooseIndex(i));if(cube.Owner!=NaturalTreePhysics.Ownership.Individual)continue;
                    var p=new Vector3(cube.Position.X,cube.Position.Y,cube.Position.Z);if(!any){lo=hi=p;any=true;}else{lo=Vector3.Min(lo,p);hi=Vector3.Max(hi,p);}
                }
                var center=(lo+hi)*.5f;float span=Mathf.Max(2,Mathf.Max((hi-lo).x,Mathf.Max((hi-lo).y,(hi-lo).z)));
                camera.orthographicSize=span*.7f;camera.transform.position=center+new Vector3(-1,.55f,-1).normalized*Mathf.Max(12,span*2);camera.transform.LookAt(center);
                // Keep the ordinary renderer hidden throughout this Edit-mode
                // fixture. Never accumulate Graphics.DrawMeshInstanced requests
                // against a disabled camera over many Editor updates. URP camera
                // render initializes its real lighting state once; explicit GPU
                // commands then draw only this exact current cache/pose snapshot.
                camera.Render();DrawSnapshot(false);var withoutDebris=ReadPixels();DrawSnapshot(true);var pixels=ReadPixels();
                int instanceChanges=0,tinted=0;
                for(int i=0;i<pixels.Length;i++)
                {
                    var a=pixels[i];var b=withoutDebris[i];if(Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b)<=20)continue;
                    instanceChanges++;if(Math.Max(a.r,Math.Max(a.g,a.b))-Math.Min(a.r,Math.Min(a.g,a.b))>12)tinted++;
                }
                if(physics.IslandCells==0&&capturedDebris>32&&(instanceChanges<8||tinted<8))throw new InvalidOperationException("Instanced particles produced no distinct intrinsic-color pixels: "+name);
                long energy=0;int changed=0;Color32 clear=pixels[0];
                foreach(var pixel in pixels){energy+=pixel.r+pixel.g+pixel.b;if(Math.Abs(pixel.r-clear.r)+Math.Abs(pixel.g-clear.g)+Math.Abs(pixel.b-clear.b)>20)changed++;}
                if(changed<8)throw new InvalidOperationException("Native capture contains no visible fixture pixels: "+name);
                string png=Path.Combine(folder,name+".png");File.WriteAllBytes(png,image.EncodeToPNG());
                physics.BodyPose(0,out var bodyPosition,out var bodyRotation);var meshes=CacheMeshes();
                if(meshes[0]!=null&&meshes[0].vertexCount>physics.SourceCells*24)throw new InvalidOperationException("Source cache exceeds current exact source ownership");
                report.frames.Add(new Frame{name=name,png=png,source=physics.SourceCells,island=physics.IslandCells,individual=physics.IndividualCells,cargo=physics.CargoCells,loose_parcels=physics.PendingDebris,awake=physics.AwakeDebris,dormant=physics.DormantDebris,pending_wake=physics.PendingWakeCandidates,drawn_debris=capturedDebris,ground_impacts=physics.GroundImpacts,render_cell_size=renderer.RenderingCellSize,conserved=physics.Conserved,current=renderer.Current,rgb_energy=energy,changed_from_clear=changed,cache_vertices=meshes.Select(m=>m==null?0:m.vertexCount).ToArray(),source_renderer_candidates=renderer.LastBuildCandidates,instanced_pixel_changes=instanceChanges,tinted_instance_pixels=tinted,body_position=new Vector3(bodyPosition.X,bodyPosition.Y,bodyPosition.Z),body_rotation=new Quaternion(bodyRotation.X,bodyRotation.Y,bodyRotation.Z,bodyRotation.W)});Write();
            }
            T RendererField<T>(string name)=>(T)typeof(NaturalMatterRenderer).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(renderer);
            Mesh[] CacheMeshes()=>RendererField<Mesh[]>("published");
            Color32[] ReadPixels()
            {
                var active=RenderTexture.active;try{RenderTexture.active=target;image.ReadPixels(new Rect(0,0,512,512),0,0);image.Apply();return image.GetPixels32();}finally{RenderTexture.active=active;}
            }
            void DrawSnapshot(bool includeDebris)
            {
                var meshes=CacheMeshes();var solid=RendererField<Material>("solidMaterial");var debris=RendererField<Material>("debrisMaterial");var cubeMesh=RendererField<Mesh>("debrisMesh");
                var priorCamera=Shader.GetGlobalVector("_WorldSpaceCameraPos");var priorV=Shader.GetGlobalMatrix("unity_MatrixV");var priorP=Shader.GetGlobalMatrix("glstate_matrix_projection");
                using(var commands=new CommandBuffer{name="Exact natural matter fixture snapshot"})
                {
                    commands.SetRenderTarget(target);commands.SetViewport(new Rect(0,0,512,512));commands.ClearRenderTarget(true,true,camera.backgroundColor);
                    // Like URP ScriptableRenderer.SetCameraMatrices: this command converts the projection to GPU form itself.
                    commands.SetViewProjectionMatrices(camera.worldToCameraMatrix,camera.projectionMatrix);
                    var eye=camera.transform.position;commands.SetGlobalVector("_WorldSpaceCameraPos",new Vector4(eye.x,eye.y,eye.z,1));
                    commands.DrawMesh(floorMesh,Matrix4x4.identity,floorMaterial,0,floorMaterial.FindPass("ForwardLit"));
                    for(int i=0;i<meshes.Length;i++)
                    {
                        if(meshes[i]==null||meshes[i].vertexCount==0)continue;NV position;System.Numerics.Quaternion rotation;
                        if(i==0){position=NV.Zero;rotation=physics.SourceRotation;}else if(!physics.BodyPose(i-1,out position,out rotation))continue;
                        commands.DrawMesh(meshes[i],Matrix4x4.TRS(new Vector3(position.X,position.Y,position.Z),new Quaternion(rotation.X,rotation.Y,rotation.Z,rotation.W),Vector3.one),solid,0,solid.FindPass("ForwardLit"));
                    }
                    capturedDebris=0;
                    if(includeDebris)
                    {
                        int candidates=physics.PendingDebris,samples=Math.Min(4096,candidates),batch=0;var matrices=new Matrix4x4[1023];var tints=new Vector4[1023];var properties=new MaterialPropertyBlock();
                        for(int sample=0;sample<samples;sample++)
                        {
                            int index=physics.LooseIndex((int)((long)sample*candidates/samples));var cube=physics.ReadLocal(index);if(cube.Owner!=NaturalTreePhysics.Ownership.Individual||physics.LooseWeight(index)<1)continue;
                            uint rgb=cube.Matter.Tint==0?VoxelColor.Base(cube.Matter.Material):cube.Matter.Tint;
                            matrices[batch]=Matrix4x4.TRS(new Vector3(cube.Position.X,cube.Position.Y,cube.Position.Z),new Quaternion(cube.Rotation.X,cube.Rotation.Y,cube.Rotation.Z,cube.Rotation.W),Vector3.one*.1f);
                            tints[batch]=new Vector4(((rgb>>16)&255)/255f,((rgb>>8)&255)/255f,(rgb&255)/255f,VertexAppearance.Pack(cube.Matter.Material,3)/255f);batch++;capturedDebris++;
                            if(batch==1023){properties.SetVectorArray("_MatterTint",tints);commands.DrawMeshInstanced(cubeMesh,0,debris,debris.FindPass("ForwardLit"),matrices,batch,properties);batch=0;}
                        }
                        if(batch>0){properties.SetVectorArray("_MatterTint",tints);commands.DrawMeshInstanced(cubeMesh,0,debris,debris.FindPass("ForwardLit"),matrices,batch,properties);}
                    }
                    commands.SetGlobalVector("_WorldSpaceCameraPos",priorCamera);commands.SetViewProjectionMatrices(priorV,priorP);Graphics.ExecuteCommandBuffer(commands);
                }
            }
            void Write(){File.WriteAllText(Path.Combine(folder,"result.json"),JsonUtility.ToJson(report,true));File.WriteAllText(Latest,Path.Combine(folder,"result.json"));}
            public void Finish(string status,string error)
            {
                if(finished)return;finished=true;report.status=status;report.error=error;report.conserved=physics==null||physics.Conserved;
                Application.logMessageReceived-=Log;
                try
                {
                    if(renderer!=null){renderer.SetVisible(false);renderer.Dispose();renderer=null;}
                    if(camera!=null)camera.targetTexture=null;RenderTexture.active=priorTarget;if(target!=null)target.Release();
                    for(int i=owned.Count-1;i>=0;i--)if(owned[i]!=null)UnityEngine.Object.DestroyImmediate(owned[i]);owned.Clear();
                    if(scene.IsValid())EditorSceneManager.ClosePreviewScene(scene);
                    foreach(var entry in globals)Shader.SetGlobalVector(entry.Key,entry.Value);
                    if(priorOptics!=null)Shader.SetGlobalVectorArray("_VoxelMaterialOptics",priorOptics.Length==0?new Vector4[32]:priorOptics);
                    if(originalScene.IsValid())
                    {
                        report.final_scene_dirty=originalScene.isDirty;report.final_scene_roots=originalScene.rootCount;
                        if(report.final_scene_roots!=report.original_scene_roots||!report.original_scene_dirty&&report.final_scene_dirty)throw new InvalidOperationException("Original scene root/dirty state changed during fixture");
                    }
                    foreach(var identity in report.sources)using(var sha=SHA256.Create())
                        if(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Path.Combine(Root,identity.path)))).Replace("-","").ToLowerInvariant()!=identity.sha256)throw new InvalidOperationException("Source changed during native fixture: "+identity.path);
                }
                catch(Exception cleanup){report.status="FAIL";report.error=(report.error??"")+"\nCleanup: "+cleanup;}
                if(folder!=null)Write();Debug.Log("Natural matter isolated native fixture "+report.status+": "+folder);
            }
        }
    }
}
#endif
