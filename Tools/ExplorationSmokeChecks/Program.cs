using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using AshenVoxel.Exploration;
using AshenVoxel.WorldScale;
using AshenVoxel.WorldScaleRuntime;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
static class Program
{
 const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
 static int checks,frame;static ExplorationBootstrap boot;static bool earlyInputDisabled;static IEnumerator startup;
 static readonly HashSet<MonoBehaviour> started=new HashSet<MonoBehaviour>();
 static void Check(bool yes,string name){if(!yes)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
 static object Get(object x,string field)=>x.GetType().GetField(field,Flags).GetValue(x);
 static object Call(object x,string method,params object[] args)=>x.GetType().GetMethod(method,Flags).Invoke(x,args);
 static void Frame(bool advanceStartup=true,bool advancePlayer=true)
 {
  Time.unscaledTime=++frame/60f;Time.unscaledDeltaTime=Time.deltaTime=1f/60;
  // Preserve Unity's default-before-late-bootstrap update order. Start on a new
  // WorldExplorer occurs after ConfigureExternal and must not reset that source.
  foreach(var behaviour in UnityEngine.Object.Created.OfType<MonoBehaviour>().Where(x=>!x.WasDestroyed).ToArray())
  {
   if(!started.Add(behaviour)||behaviour==boot)continue;
   MethodInfo method=behaviour.GetType().GetMethod("Start",Flags);if(method!=null&&method.ReturnType==typeof(void))method.Invoke(behaviour,null);
  }
  if(boot.Vista!=null)Call(boot.Vista,"Update");
  if(advancePlayer&&boot.Player!=null)Call(boot.Player,"Update");
  Call(boot,"Update");
  if(advanceStartup&&startup!=null&&!startup.MoveNext())startup=null;
  if(boot.Player!=null&&!boot.Ready)earlyInputDisabled|=!boot.Player.InputEnabled;
  Call(boot,"LateUpdate");
  if(frame%32==0)Thread.Sleep(1);else Thread.Yield();
  if(boot.Failed)throw new Exception("Bootstrap failed at frame"+frame+": "+boot.Status);
 }
 static TextAsset Asset(string path)=>new TextAsset{name=Path.GetFileName(path),bytes=File.ReadAllBytes(path)};
 static ExplorationSettings LoadSettings(string assetPath,Dictionary<string,string> guidPaths)
 {
  var s=ScriptableObject.CreateInstance<ExplorationSettings>();s.name="Actual bundled exploration settings";
  foreach(string line in File.ReadAllLines(assetPath))
  {
   Match m=Regex.Match(line,@"^  ([A-Za-z][A-Za-z0-9_]*):\s*(.*)$");if(!m.Success)continue;
   FieldInfo field=typeof(ExplorationSettings).GetField(m.Groups[1].Value,BindingFlags.Instance|BindingFlags.Public);if(field==null)continue;
   string value=m.Groups[2].Value;
   if(field.FieldType==typeof(TextAsset)){Match g=Regex.Match(value,@"guid: ([0-9a-f]{32})");if(g.Success){if(!guidPaths.TryGetValue(g.Groups[1].Value,out string path))throw new Exception("Missing referenced asset "+g.Value);field.SetValue(s,Asset(path));}continue;}
   if(field.FieldType==typeof(bool))field.SetValue(s,value=="1");
   else if(field.FieldType==typeof(int))field.SetValue(s,int.Parse(value,CultureInfo.InvariantCulture));
   else if(field.FieldType==typeof(float))field.SetValue(s,float.Parse(value,CultureInfo.InvariantCulture));
   else if(field.FieldType==typeof(double))field.SetValue(s,double.Parse(value,CultureInfo.InvariantCulture));
   else if(field.FieldType==typeof(string))field.SetValue(s,value);
   else if(field.FieldType.IsEnum)field.SetValue(s,Enum.ToObject(field.FieldType,int.Parse(value,CultureInfo.InvariantCulture)));
   else if(field.FieldType==typeof(Color))
   {var numbers=Regex.Matches(value,@"[rgba]:\s*(-?[0-9.eE+-]+)").Cast<Match>().Select(x=>float.Parse(x.Groups[1].Value,CultureInfo.InvariantCulture)).ToArray();field.SetValue(s,new Color(numbers[0],numbers[1],numbers[2],numbers[3]));}
  }
  return s;
 }
 static bool PoseFree(ExplorationBootstrap b)=>!b.Population.BlocksBody(b.Player.Feet.X-.28,b.Player.Feet.Y+.002,b.Player.Feet.Z-.28,b.Player.Feet.X+.28,b.Player.Feet.Y+1.8,b.Player.Feet.Z+.28);
 static void Main(string[] args)
 {
  var input=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(args[1]));string root=input["root"];
  var guidPaths=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(args[2]));
  string scene=File.ReadAllText(input["scene"]);Match sceneScript=Regex.Match(scene,@"m_Script:.*guid: ([0-9a-f]{32})"),sceneSettings=Regex.Match(scene,@"  Settings:.*guid: ([0-9a-f]{32})");
  Check(sceneScript.Success&&sceneSettings.Success&&Path.GetFullPath(guidPaths[sceneScript.Groups[1].Value])==Path.GetFullPath(Path.Combine(root,"Assets/AshenVoxel/Exploration/ExplorationBootstrap.cs"))&&guidPaths[sceneSettings.Groups[1].Value]==input["settings"],
   "actual launch scene references the bootstrap script and the exact bundled settings asset");
  ExplorationSettings settings=LoadSettings(input["settings"],guidPaths);Application.persistentDataPath=args[3];Directory.CreateDirectory(args[3]);
  Check(settings.MacroWorld!=null&&settings.MacroClimate!=null&&settings.FineWorld!=null&&settings.FineClimate!=null&&settings.SourceBiomes!=null&&settings.PrototypePack!=null,"actual settings asset GUIDs resolve all real world/climate/biome/prototype files");
  Check(!settings.EnableLiveRefinement&&settings.UseRecommendedSpawn&&settings.UseImportedDimensions,"bundled settings choose offline learned data and recorded map spawn by default");
  var originalPipeline=new TestRenderPipeline{name="Shared pipeline",shadowDistance=77};GraphicsSettings.defaultRenderPipeline=originalPipeline;QualitySettings.renderPipeline=originalPipeline;QualitySettings.shadowDistance=77;
  var originalSky=new Material{name="Shared sky"};RenderSettings.skybox=originalSky;
  boot=new GameObject("Actual exploration boot smoke").AddComponent<ExplorationBootstrap>();boot.Settings=settings;boot.LoadSavedOptions=false;
  startup=(IEnumerator)Call(boot,"Start");started.Add(boot);
  int firstSourceFrame=0,modelsReadyFrame=0,readyFrame=0;
  var stopwatch=System.Diagnostics.Stopwatch.StartNew();
  // A background geometry worker needs wall-clock time; unthrottled shim
  // frames are not native frames or a loading-time performance measurement.
  const int frameLimit=12000;
  for(int i=0;i<frameLimit&&!boot.Ready&&stopwatch.Elapsed.TotalSeconds<90;i++)
  {
   Frame();if(boot.Source!=null&&firstSourceFrame==0)firstSourceFrame=frame;if(boot.Population!=null&&boot.Population.Ready&&modelsReadyFrame==0)modelsReadyFrame=frame;
   if(i%250==0)Console.WriteLine("FRAME "+frame+" "+boot.Status+" / vista="+(boot.Vista==null?"none":boot.Vista.Status)+" / queue="+(boot.Vista==null?0:boot.Vista.PendingTiles));
  }
  readyFrame=frame;Check(boot.Ready&&!boot.Failed&&readyFrame<frameLimit&&stopwatch.Elapsed.TotalSeconds<90,"complete async bootstrap reaches playable Ready from real assets within frame and wall-clock bounds");
  Check(earlyInputDisabled&&boot.Player.InputEnabled&&boot.Player.Motor.Grounded,"player remains gated during preparation and becomes grounded/input-enabled after publication");
  Check(!boot.LiveRefinement.Connected&&boot.LiveRefinement.PendingRequests==0&&Get(boot.LiveRefinement,"remote")==null,"offline launch never creates a model HTTP client or pending inference request");
  var metadata=JsonDocument.Parse(settings.WorldMetadata.text);string expectedWorld=metadata.RootElement.GetProperty("world_id").GetString();
  Check(boot.Source.WorldId==expectedWorld&&Math.Abs(boot.Source.WorldWidthMeters-299520)<.01&&Math.Abs(boot.Source.WorldDepthMeters-149760)<.01,"loaded whole world matches the executed diffusion identity and300by150km target extent");
  Check(boot.Source.FineWeightAt(boot.Player.Feet.X,boot.Player.Feet.Z)>.999&&boot.LiveRefinement.ResolutionMetersAt(boot.Player.Feet.X,boot.Player.Feet.Z)<=5.001,"spawn uses bundled decoded5m terrain rather than coarse preview terrain");
  Check(boot.Population.LoadedModels==99&&boot.Population.MeshBytes>250000000&&boot.Population.VisibleInstances>0&&Graphics.Calls>0,"actual99-model voxel pack decodes and submits shared instance pages");
  Check(boot.Vista.ExternalNavigation&&!boot.Vista.InspectingVoxels&&boot.Vista.IsTerrainReadyArea(boot.Player.Feet.X,boot.Player.Feet.Z,2),"scene uses external navigation with locally refined visible terrain and no editing mode");
  Keyboard.current=new Keyboard();Mouse.current=new Mouse();Keyboard.current.pKey.wasPressedThisFrame=true;Keyboard.current.rKey.wasPressedThisFrame=true;Mouse.current.leftButton.wasPressedThisFrame=true;
  Call(boot.Vista,"Update");Call(boot.Player,"Update");
  Check(!boot.Vista.InspectingVoxels&&Get(boot.Vista,"patch")==null&&boot.Source.WorldId==expectedWorld,"legacy P/rebuild and captured click inputs cannot enter terrain editing in the exploration scene");
  Keyboard.current=null;Mouse.current=null;
  Check(PoseFree(boot)&&boot.Population.IsReady(boot.Player.Feet.X,boot.Player.Feet.Z,.32),"published default spawn is clear of real wood/rock collision and its footprint is ready");
  Check(!ReferenceEquals(settings,boot.ActiveSettings)&&settings.WalkSpeed==4,"session owns a settings clone and preserves the downloaded source asset");
  Check(originalPipeline.shadowDistance==77&&GraphicsSettings.currentRenderPipeline!=originalPipeline&&((TestRenderPipeline)GraphicsSettings.currentRenderPipeline).shadowDistance==boot.ActiveSettings.ShadowDistanceMeters,"shadow settings apply to a private pipeline clone rather than the shared asset");
  WorldPoint start=boot.Player.Feet;int blockedFrames=0;double maxEyeStep=0,lastEye=boot.Player.Eye.Y;
  // Move a short actual-terrain path; hard props may legitimately block a
  // direction, so test readiness/penetration separately from requested distance.
  for(int i=0;i<120;i++)
  {boot.Player.Advance(new ExplorationMoveInput(0,1),1d/60);Frame(false,false);if(boot.Player.Motor.WaitingForWorld)blockedFrames++;maxEyeStep=Math.Max(maxEyeStep,Math.Abs(boot.Player.Eye.Y-lastEye));lastEye=boot.Player.Eye.Y;CheckPoseFinite(boot.Player.Feet);}
  double walked=Math.Sqrt(Math.Pow(boot.Player.Feet.X-start.X,2)+Math.Pow(boot.Player.Feet.Z-start.Z,2));
  Check(walked>1&&blockedFrames==0&&PoseFree(boot),"actual learned-terrain walking advances without readiness flicker or hard-prop penetration");
  Check(maxEyeStep<.35,"actual-terrain camera motion has no full-cell/stream-origin vertical jump");
  WorldPoint beforeFly=boot.Player.Feet;Check(boot.Player.Motor.SetFlying(true),"actual scene can switch its basic player into flight");
  for(int i=0;i<30;i++){boot.Player.Advance(new ExplorationMoveInput(0,0,1),1d/60);Frame(false,false);}
  Check(boot.Player.Feet.Y>beforeFly.Y+10&&PoseFree(boot),"collision-enabled flight rises through the prepared scene");
  var batches=(IList)Get(boot.Population,"batches");object firstBatch=batches.Count>0?batches[0]:null;float beforeMatrix=firstBatch==null?0:((Matrix4x4[])Get(firstBatch,"Matrices"))[0].m03;
  WorldPoint oldOrigin=boot.Vista.RenderOrigin;WorldPoint flyStart=boot.Player.Feet;
  boot.Player.ResetAt(new WorldPoint(flyStart.X+1400,flyStart.Y+500,flyStart.Z),true);boot.Player.Advance(new ExplorationMoveInput(0,0),0);Call(boot,"LateUpdate");
  WorldPoint newOrigin=boot.Vista.RenderOrigin;double deltaOrigin=oldOrigin.X-newOrigin.X;
  Check(Math.Abs(deltaOrigin)>=1024&&Math.Abs(boot.Vista.MainCamera.transform.position.x-(boot.Player.Eye.X-newOrigin.X))<.01,"camera rebase preserves global eye and submeter local position in the actual controller/vista pair");
  if(firstBatch!=null)Check(Math.Abs(((Matrix4x4[])Get(firstBatch,"Matrices"))[0].m03-(beforeMatrix+deltaOrigin))<.01,"same LateUpdate rebases existing vegetation matrices into the new camera frame");
  Keyboard.current=new Keyboard();Mouse.current=new Mouse();Keyboard.current.f1Key.wasPressedThisFrame=true;Call(boot,"Update");Keyboard.current.f1Key.wasPressedThisFrame=false;
  Check(!boot.Player.InputEnabled&&(bool)Get(boot,"settingsOpen"),"F1 opens the settings panel and pauses input through the actual bootstrap");Call(boot,"OnGUI");
  boot.ActiveSettings.WalkSpeed=99;boot.ActiveSettings.FlightSpeed=250;boot.ActiveSettings.FieldOfView=120;boot.ActiveSettings.ViewDistanceKm=50;boot.ActiveSettings.FarHorizonKm=80;boot.ActiveSettings.FogDensity=.00008f;boot.ApplyRuntimeSettings(true);
  Check(boot.Player.Motor.Settings.WalkSpeed==12&&boot.Player.Motor.Settings.FlightSpeed==200&&boot.Vista.MainCamera.fieldOfView==110&&boot.Vista.ViewDistanceKm==40&&boot.Vista.FarHorizonDistanceKm==80,"runtime settings clamp and apply move/fly/FOV/view/horizon controls to real components");
  Check(RenderSettings.fogDensity==.00008f&&settings.WalkSpeed==4&&settings.ViewDistanceKm==30,"fog is editable while source settings asset remains unchanged");
  Call(boot,"SaveOptions");string options=Path.Combine(Application.persistentDataPath,"exploration-options.json");Check(File.Exists(options),"runtime options save to the supplied isolated user-data directory");
  boot.ActiveSettings.WalkSpeed=1;Call(boot,"LoadOptions");Check(boot.ActiveSettings.WalkSpeed==12,"saved options reload through the actual options code with clamped speed");
  Keyboard.current.f1Key.wasPressedThisFrame=true;Call(boot,"Update");Keyboard.current.f1Key.wasPressedThisFrame=false;Check(boot.Player.InputEnabled&&!(bool)Get(boot,"settingsOpen"),"F1 closes settings and restores exploration input");
  var material=(Material)Get(boot,"instanceMaterial");var privatePipeline=(RenderPipelineAsset)Get(boot,"ownedPipeline");var population=boot.Population;var vista=boot.Vista;int sourceTemplateWrites=Resources.Materials["VoxelSurface"].VectorWrites;
  UnityEngine.Object.Destroy(boot.gameObject);
  Check(material.WasDestroyed&&privatePipeline.WasDestroyed&&population.MeshBytes==0&&!population.Ready&&vista.ResidentTiles==0,"destroying the scene releases owned prototype meshes, materials, terrain caches and pipeline");
  Check(GraphicsSettings.currentRenderPipeline==originalPipeline&&!originalPipeline.WasDestroyed&&RenderSettings.skybox==originalSky&&!originalSky.WasDestroyed&&QualitySettings.shadowDistance==77&&Resources.Materials["VoxelSurface"].VectorWrites==sourceTemplateWrites,"shutdown restores shared pipeline/sky/shadows without destroying or mutating shared source resources");
  Check(Debug.Exceptions.Count==0,"no runtime exception was hidden by the actual boot/update failure handlers");
  var missing=UnityEngine.Object.Instantiate(settings);missing.MacroWorld=null;
  ExplorationBootstrap missingBoot=FailingBoot(missing);
  Check(missingBoot.Failed&&!missingBoot.Ready&&missingBoot.Source==null&&missingBoot.Player==null&&missingBoot.Status.Contains("whole-world height"),"incomplete project data fails explicitly before player/source fallback is created");
  UnityEngine.Object.Destroy(missingBoot.gameObject);UnityEngine.Object.Destroy(missing);
  GraphicsSettings.defaultRenderPipeline=null;QualitySettings.renderPipeline=null;
  ExplorationBootstrap pipelineBoot=FailingBoot(settings);
  Check(pipelineBoot.Failed&&!pipelineBoot.Ready&&pipelineBoot.Vista==null&&pipelineBoot.Status.IndexOf("repair",StringComparison.OrdinalIgnoreCase)>=0,"missing render pipeline stops with the repair action instead of a pink playable scene");
  UnityEngine.Object.Destroy(pipelineBoot.gameObject);GraphicsSettings.defaultRenderPipeline=originalPipeline;QualitySettings.renderPipeline=originalPipeline;
  stopwatch.Stop();
  File.WriteAllText(args[0],JsonSerializer.Serialize(new{status="PASS",checks,scope="Actual bootstrap/settings/controller/vista/population/live-disabled logic with numerical Unity substitutes and real bundled neural/prototype assets; not native Unity/HLSL/GPU/FPS",first_source_frame=firstSourceFrame,models_ready_frame=modelsReadyFrame,playable_ready_frame=readyFrame,walked_m=walked,walk_waiting_frames=blockedFrames,maximum_eye_step_m=maxEyeStep,simulated_frames=frame,elapsed_wall_seconds=stopwatch.Elapsed.TotalSeconds,expected_boot_failures=2,world_id=expectedWorld,world_width_m=299520,world_depth_m=149760},new JsonSerializerOptions{WriteIndented=true})+"\n");
  Console.WriteLine("All "+checks+" exploration smoke checks passed.");
 }
 static void CheckPoseFinite(WorldPoint p){if(double.IsNaN(p.X)||double.IsNaN(p.Y)||double.IsNaN(p.Z)||double.IsInfinity(p.X)||double.IsInfinity(p.Y)||double.IsInfinity(p.Z))throw new Exception("Nonfinite player pose");}
 static ExplorationBootstrap FailingBoot(ExplorationSettings settings)
 {
  var b=new GameObject("Expected bootstrap failure").AddComponent<ExplorationBootstrap>();b.Settings=settings;b.LoadSavedOptions=false;
  var work=(IEnumerator)Call(b,"Start");int yields=0;while(work.MoveNext()&&yields++<20){}
  return b;
 }
}
