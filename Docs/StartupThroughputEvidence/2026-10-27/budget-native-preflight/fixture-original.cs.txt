using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using AshenVoxel.Rendering;
using AshenVoxel.WorldScale;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using Debug=UnityEngine.Debug;

namespace AshenVoxel.Exploration.Editor
{
    // Separate invocations keep only one population/native mesh set in a process.
    // No WorldExplorer, terrain renderer, Play session or production setting writes.
    public static class VegetationBudgetNativeTrial
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        const long MiB=1024L*1024,MaxMeshBytes=512*MiB,MaxMatrixBytes=64*MiB,MaxProcessGrowth=2048*MiB,MaxPrivateBytes=4096*MiB;
        const double MaximumSeconds=60;
        static object Get(object value,string name)=>value.GetType().GetField(name,Flags).GetValue(value);
        static void Set(object value,string name,object data)=>value.GetType().GetField(name,Flags).SetValue(value,data);
        static object Call(object value,string name,params object[] args)=>value.GetType().GetMethod(name,Flags).Invoke(value,args);
        [Serializable] sealed class Metadata {public string world_id;public Spawn recommended_spawn;}
        [Serializable] sealed class Spawn {public double x_m,y_m,z_m;public float yaw_degrees;}
        [Serializable] sealed class Identity {public string path,sha256;}
        [Serializable] sealed class Memory
        {
            public string phase;public double wallSeconds;public long processPrivateBytes,processWorkingSetBytes,gcManagedBytes,unityAllocatedBytes,unityReservedBytes,meshPayloadBytes,matrixPayloadBytes;
        }
        [Serializable] sealed class Lod
        {
            public int kind,lod,batches,instances,visibleInstances,shadowOnlyInstances,shadowEligibleInstances;public long triangles,visibleTriangles,shadowOnlyTriangles,shadowEligibleTriangles;
        }
        [Serializable] sealed class Phase
        {
            public string name,loadedPlacementSha256;public bool settled;public int ticks,modelsReadyTick,firstPublicationTick,nearReadyTick;
            public double wallMilliseconds,tickCpuTotalMilliseconds,tickCpuP50Milliseconds,tickCpuP95Milliseconds,tickCpuMaxMilliseconds,workCpuP95Milliseconds,modelsReadyMilliseconds,firstPublicationMilliseconds,nearReadyMilliseconds;
            public int loadedModels,residentCells,residentInstances,loadedTrees,publishedCells,queuedCells,deferredCells,publishedBatches,publishedInstances,publishedTrees,drawCalls,visibleInstances,shadowOnlyDrawCalls,shadowOnlyInstances,budgetDemotions,budgetOmittedSoftInstances;
            public long publishedTriangles,visibleTriangles,shadowOnlyTriangles,submittedTriangles,shadowEligibleTriangles,meshPayloadBytes,matrixPayloadBytes;
            public List<Lod> lodDistribution=new List<Lod>();
        }
        [Serializable] sealed class Report
        {
            public string status="RUNNING",error,unity,gpu,api,worldId,sourceIdentity,startedUtc;
            public string scope="Native Unity vegetation only. Editor-update ticks are scheduler iterations, not60Hz frames. Startup excludes camera rendering. Tick CPU includes draw submission. Camera.Render CPU elapsed may include driver waits; no GPU timer or whole-game FPS claim. One population per process; no terrain or distant stand horizon. Triangle counts describe submitted instances; shadow-eligible counts do not multiply cascades or prove GPU rasterized triangles. Memory guard is cooperative, sampled every30ticks; single uploads may overshoot time targets.";
            public int triangleBudget,seed,width=1440,height=900,cellsPerTick=2,matricesPerTick=5000,maxResidentCells=2200,maxInstances;
            public float workMilliseconds=2,fieldOfView,pitch,yaw,treeDistance,treeDetailDistance,redwoodDetailDistance,treeMidDistance,groundCoverDistance,rockDistance,treeDensity,plantDensity,shadowDistance;
            public double eyeX,eyeY,eyeZ,originX,originY,originZ,maximumSeconds=MaximumSeconds,finalReadbackCpuMilliseconds;
            public string renderTimingScope="Three unmeasured warmup views and eight measured fixed-view Camera.Render calls; final ReadPixels fences submitted GPU work, but its CPU wait is not GPU render duration.";
            public double renderCpuP50Milliseconds,renderCpuP95Milliseconds,renderCpuMaxMilliseconds;
            public List<double> renderCpuMilliseconds=new List<double>();
            public List<Identity> sources=new List<Identity>();public List<Memory> memory=new List<Memory>();public List<Phase> phases=new List<Phase>();
        }
        static Report report;static Phase phase;static VegetationStreaming population;static Camera camera;static GameObject root;
        static RenderTexture target;static ExplorationSettings settings;static SurfaceMaterialSampler sampler;static WorldPoint eye,origin;
        static readonly Stopwatch whole=new Stopwatch(),phaseClock=new Stopwatch();static readonly List<double> tickTimes=new List<double>(),workTimes=new List<double>();
        static Process process;static long baselinePrivate;static string folder;static int state,settledTicks,renderFrames,totalTicks;static bool ending;
        public static void Run32()=>Begin(32000000);
        public static void Run40()=>Begin(40000000);
        static void Begin(int budget)
        {
            folder=Path.GetFullPath("Logs/VegetationBudget/"+budget+"-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(folder);
            report=new Report{triangleBudget=budget,startedUtc=DateTime.UtcNow.ToString("o"),unity=Application.unityVersion,gpu=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString()};
            try
            {
                AssetDatabase.DesiredWorkerCount=0;AssetDatabase.ForceToDesiredWorkerCount();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                process=Process.GetCurrentProcess();process.Refresh();baselinePrivate=process.PrivateMemorySize64;whole.Restart();RecordMemory("before-source-load");
                foreach(string path in new[]{"Assets/AshenVoxel/Exploration/VegetationStreaming.cs","Assets/AshenVoxel/Exploration/ForestBillboard.cs","Assets/AshenVoxel/Exploration/ForestImpostor.shader","Assets/AshenVoxel/Exploration/ForestImpostorLighting.hlsl","Assets/AshenVoxel/Rendering/WorldSurface.shader","Assets/AshenVoxel/Rendering/VoxelWind.hlsl","Assets/AshenVoxel/Settings/ExplorationSettings.asset","Assets/AshenVoxel/Exploration/Prototypes/ExplorationPrototypes.bytes","Assets/AshenVoxel/Exploration/Samples/ExplorationMacro.bytes","Assets/AshenVoxel/Exploration/Samples/ExplorationMacro.climate.bytes","Assets/AshenVoxel/Exploration/Samples/ExplorationFine.bytes","Assets/AshenVoxel/Exploration/Samples/ExplorationFine.climate.bytes","Assets/AshenVoxel/Exploration/Samples/ExplorationBiomes.bytes","Assets/AshenVoxel/Exploration/Samples/exploration-world.json","Assets/AshenVoxel/Exploration/Resources/ForestAtlas.png","Assets/AshenVoxel/Exploration/Resources/ForestAtlasNormals.png","Assets/AshenVoxel/Exploration/Resources/ForestImpostor.mat","Assets/AshenVoxel/Rendering/Resources/MaterialOptics.asset","Assets/AshenVoxel/Exploration/Editor/VegetationBudgetNativeTrial.cs"})
                    if(File.Exists(path))report.sources.Add(new Identity{path=path,sha256=HashFile(path)});
                foreach(string path in Directory.GetFiles("Assets/AshenVoxel/WorldScaleData","*.cs"))if(!report.sources.Any(p=>p.path==path))report.sources.Add(new Identity{path=path,sha256=HashFile(path)});
                string pipeline=AssetDatabase.GetAssetPath(GraphicsSettings.currentRenderPipeline);if(!string.IsNullOrEmpty(pipeline)&&File.Exists(pipeline))report.sources.Add(new Identity{path=pipeline,sha256=HashFile(pipeline)});
                settings=AssetDatabase.LoadAssetAtPath<ExplorationSettings>("Assets/AshenVoxel/Settings/ExplorationSettings.asset");Check(settings!=null,"Missing settings asset.");
                var metadata=JsonUtility.FromJson<Metadata>(File.ReadAllText("Assets/AshenVoxel/Exploration/Samples/exploration-world.json"));
                Check(metadata.world_id=="14fb32f26ff725abaf9f1e04668a1f46c5b4783a66f91d23253676f595f613e4","This trial requires the actual14fb32 world.");Check(settings.Seed==714103,"Unexpected population seed.");
                sampler=LoadField();report.worldId=metadata.world_id;report.sourceIdentity=sampler.SourceIdentity;report.seed=settings.Seed;
                eye=new WorldPoint(metadata.recommended_spawn.x_m,sampler.EvaluateColumn(metadata.recommended_spawn.x_m,metadata.recommended_spawn.z_m).SurfaceHeightMeters+settings.EyeHeight,metadata.recommended_spawn.z_m);
                origin=new WorldPoint(Math.Floor(eye.X/256)*256,Math.Floor(eye.Y/256)*256,Math.Floor(eye.Z/256)*256);
                report.eyeX=eye.X;report.eyeY=eye.Y;report.eyeZ=eye.Z;report.originX=origin.X;report.originY=origin.Y;report.originZ=origin.Z;
                root=new GameObject("Controlled vegetation budget fixture");var cameraObject=new GameObject("Fixed spawn camera");cameraObject.transform.SetParent(root.transform);
                camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.nearClipPlane=.04f;camera.farClipPlane=3000;camera.fieldOfView=settings.FieldOfView;camera.aspect=1440f/900;
                camera.transform.position=new Vector3((float)(eye.X-origin.X),(float)(eye.Y-origin.Y),(float)(eye.Z-origin.Z));camera.transform.rotation=Quaternion.Euler(settings.SpawnPitch,metadata.recommended_spawn.yaw_degrees,0);
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.allowHDR=false;camera.allowMSAA=false;camera.cullingMask=1<<29;
                target=new RenderTexture(1440,900,24,RenderTextureFormat.ARGB32);camera.targetTexture=target;RenderSettings.fog=false;QualitySettings.shadowDistance=settings.ShadowDistanceMeters;
                var sunObject=new GameObject("Fixed trial sun");sunObject.transform.SetParent(root.transform);sunObject.transform.rotation=Quaternion.Euler(settings.SunElevation,settings.SunAzimuth,0);
                var sun=sunObject.AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=settings.SunIntensity;sun.color=settings.SunColor;sun.shadows=LightShadows.Soft;sun.shadowBias=.02f;sun.shadowNormalBias=.1f;
                MaterialOpticsProfile.Apply(Resources.Load<MaterialOpticsProfile>("MaterialOptics"));MaterialOpticsProfile.SetAmbient(settings.AmbientSky,settings.AmbientEquator,settings.AmbientGround,settings.AmbientFillStrength);
                Shader.SetGlobalVector("_VoxelWindState",Vector4.zero);Shader.SetGlobalVector("_VoxelWindPreviousState",Vector4.zero);
                var objectPopulation=new GameObject("Only population instance");objectPopulation.transform.SetParent(root.transform);objectPopulation.layer=29;population=objectPopulation.AddComponent<VegetationStreaming>();
                population.TreeDistance=settings.TreeDistanceMeters;population.TreeDetailDistance=settings.FullTreeDetailMeters;population.TreeMidDistance=Mathf.Min(settings.MediumTreeDetailMeters,500);population.TreeCoarseDistance=500;population.TreeFarDistance=1000;
                population.RedwoodDetailDistance=settings.RedwoodFullDetailMeters;population.GroundCoverDistance=settings.GroundCoverDistanceMeters;population.RockDistance=settings.RockDistanceMeters;
                population.TreeDensity=settings.TreeDensity;population.PlantDensity=settings.GroundCoverDensity;population.RockDensity=1;population.MaxInstances=Mathf.Clamp(settings.MaximumResidentInstances,2000,60000);
                population.TriangleBudget=budget;population.WorkMilliseconds=2;population.CellsPerFrame=2;population.MatricesPerFrame=5000;population.MaxResidentCells=2200;
                population.EnableForestHorizon=false;population.UseGpuForestHorizon=false;population.UseTreeImpostors=true;
                report.fieldOfView=camera.fieldOfView;report.pitch=settings.SpawnPitch;report.yaw=metadata.recommended_spawn.yaw_degrees;report.treeDistance=population.TreeDistance;report.treeDetailDistance=population.TreeDetailDistance;report.redwoodDetailDistance=population.RedwoodDetailDistance;report.treeMidDistance=population.TreeMidDistance;report.groundCoverDistance=population.GroundCoverDistance;report.rockDistance=population.RockDistance;report.treeDensity=population.TreeDensity;report.plantDensity=population.PlantDensity;report.maxInstances=population.MaxInstances;report.shadowDistance=QualitySettings.shadowDistance;
                population.Initialize(sampler.BaseField,sampler.Profile,settings.PrototypePack,Resources.Load<Material>("VoxelSurface"),settings.Seed);
                ((Material)Get(population,"material")).SetFloat("_VoxelWindStaticInstances",1);
                BeginPhase("cold-prototype-upload-and-publication");Save();EditorApplication.update+=Tick;
            }
            catch(Exception e){End("FAIL",e);}
        }
        static SurfaceMaterialSampler LoadField()
        {
            const string prefix="Assets/AshenVoxel/Exploration/Samples/";HeightAtlas macro,fine;ClimateAtlas mc,fc;ExplorationBiomeAtlas biome;
            using(var f=File.OpenRead(prefix+"ExplorationMacro.bytes"))macro=AtlasIO.Read(f);using(var f=File.OpenRead(prefix+"ExplorationFine.bytes"))fine=AtlasIO.Read(f);
            using(var f=File.OpenRead(prefix+"ExplorationMacro.climate.bytes"))mc=ClimateArchiveIO.Read(f);using(var f=File.OpenRead(prefix+"ExplorationFine.climate.bytes"))fc=ClimateArchiveIO.Read(f);
            using(var f=File.OpenRead(prefix+"ExplorationBiomes.bytes"))biome=ExplorationBiomeAtlasIO.Read(f);
            var atlas=new ExplorationAtlasField(macro,mc,fine,fc,settings.FineBlendMeters,macro.ExtentX,macro.ExtentZ,settings.VerticalScale,biome,macro.ExtentX,macro.ExtentZ);
            IWorldField source=settings.EnableSurfaceRelief?(IWorldField)new TerrainDetailField(atlas,settings.Seed):atlas;return new SurfaceMaterialSampler(source,settings.Surface());
        }
        static void BeginPhase(string name)
        {phase=new Phase{name=name};report.phases.Add(phase);tickTimes.Clear();workTimes.Clear();settledTicks=0;phaseClock.Restart();}
        static void Tick()
        {
            if(ending)return;
            try
            {
                Check(whole.Elapsed.TotalSeconds<MaximumSeconds,"60s cooperative fixture watchdog expired; retain partial measurements.");
                var sw=Stopwatch.StartNew();population.SetObserver(eye,origin);population.Tick(camera);sw.Stop();totalTicks++;
                Check(population.MeshBytes<=MaxMeshBytes&&population.MatrixBufferBytes<=MaxMatrixBytes,"Controlled mesh/matrix payload guard exceeded.");
                if(state<2)
                {
                    phase.ticks++;tickTimes.Add(sw.Elapsed.TotalMilliseconds);workTimes.Add(population.LastWorkMilliseconds);
                    if(population.Ready&&phase.modelsReadyTick==0){phase.modelsReadyTick=phase.ticks;phase.modelsReadyMilliseconds=phaseClock.Elapsed.TotalMilliseconds;}
                    if(((IList)Get(population,"batches")).Count>0&&phase.firstPublicationTick==0){phase.firstPublicationTick=phase.ticks;phase.firstPublicationMilliseconds=phaseClock.Elapsed.TotalMilliseconds;}
                    if(population.Ready&&phase.nearReadyTick==0&&population.IsReady(eye.X,eye.Z,16)){phase.nearReadyTick=phase.ticks;phase.nearReadyMilliseconds=phaseClock.Elapsed.TotalMilliseconds;}
                    bool settled=population.Ready&&population.PendingCells==0&&!(bool)Get(population,"buildingMatrices")&&!(bool)Get(population,"matricesDirty");settledTicks=settled?settledTicks+1:0;
                    if(settledTicks>=2)
                    {
                        FinishPhase(true);Check(phase.loadedModels==99&&phase.publishedCells==phase.residentCells,"Settled publication is incomplete.");
                        if(state==0){state=1;population.RefreshPopulation();BeginPhase("warm-population-replay-retaining-prototype-meshes");}
                        else{Check(report.phases[0].loadedPlacementSha256==phase.loadedPlacementSha256,"Cold/warm resident placements differ.");state=2;}
                        Save();
                    }
                }
                else
                {
                    sw.Restart();camera.Render();sw.Stop();
                    if(renderFrames>=3)report.renderCpuMilliseconds.Add(sw.Elapsed.TotalMilliseconds);renderFrames++;
                    if(renderFrames==11)
                    {
                        var old=RenderTexture.active;var image=new Texture2D(1440,900,TextureFormat.RGB24,false);
                        try{RenderTexture.active=target;sw.Restart();image.ReadPixels(new Rect(0,0,1440,900),0,0);image.Apply();sw.Stop();report.finalReadbackCpuMilliseconds=sw.Elapsed.TotalMilliseconds;File.WriteAllBytes(folder+"/fixed-spawn.png",image.EncodeToPNG());}
                        finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(image);}
                        report.renderCpuP50Milliseconds=Percentile(report.renderCpuMilliseconds,.5);report.renderCpuP95Milliseconds=Percentile(report.renderCpuMilliseconds,.95);report.renderCpuMaxMilliseconds=report.renderCpuMilliseconds.Max();End("PASS",null);return;
                    }
                }
                if(totalTicks%30==0){RecordMemory(state<2?phase.name:"fixed-view-render");Save();}
                // Request another Editor update; tick counts remain explicitly scheduler iterations.
                EditorApplication.QueuePlayerLoopUpdate();
            }
            catch(Exception e){End("FAIL",e);}
        }
        static void FinishPhase(bool settled)
        {
            phase.settled=settled;phase.wallMilliseconds=phaseClock.Elapsed.TotalMilliseconds;phase.tickCpuTotalMilliseconds=tickTimes.Sum();phase.tickCpuP50Milliseconds=Percentile(tickTimes,.5);phase.tickCpuP95Milliseconds=Percentile(tickTimes,.95);phase.tickCpuMaxMilliseconds=tickTimes.Count>0?tickTimes.Max():0;phase.workCpuP95Milliseconds=Percentile(workTimes,.95);
            phase.loadedModels=population.LoadedModels;phase.residentCells=population.ResidentCells;phase.residentInstances=population.ResidentInstances;phase.queuedCells=population.PendingCells;phase.deferredCells=((HashSet<PopulationKey>)Get(population,"deferred")).Count;
            phase.drawCalls=population.DrawCalls;phase.visibleInstances=population.VisibleInstances;phase.visibleTriangles=population.VisibleTriangles;phase.shadowOnlyDrawCalls=population.ShadowOnlyDrawCalls;phase.shadowOnlyInstances=population.ShadowOnlyInstances;
            phase.budgetDemotions=population.BudgetDemotions;phase.budgetOmittedSoftInstances=population.BudgetOmittedSoftInstances;phase.meshPayloadBytes=population.MeshBytes;phase.matrixPayloadBytes=population.MatrixBufferBytes;
            var placements=new List<PopulationInstance>();foreach(object cell in ((IDictionary)Get(population,"cells")).Values){if((bool)Get(cell,"Published"))phase.publishedCells++;var data=(PopulationCell)Get(cell,"Data");placements.AddRange(data.Instances);phase.loadedTrees+=data.Instances.Count(p=>p.PrototypeId<100);}
            placements.Sort((a,b)=>{int c=a.PrototypeId.CompareTo(b.PrototypeId);if(c!=0)return c;c=a.StableKey.CompareTo(b.StableKey);if(c!=0)return c;c=a.Position.X.CompareTo(b.Position.X);return c!=0?c:a.Position.Z.CompareTo(b.Position.Z);});
            using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)){foreach(var p in placements){writer.Write(p.PrototypeId);writer.Write(p.StableKey);writer.Write(p.Position.X);writer.Write(p.Position.Y);writer.Write(p.Position.Z);writer.Write(p.YawDegrees);writer.Write(p.Scale);}writer.Flush();phase.loadedPlacementSha256=HashBytes(stream.ToArray());}
            var planes=GeometryUtility.CalculateFrustumPlanes(camera);var groups=new Dictionary<int,Lod>();
            foreach(object batch in (IList)Get(population,"batches"))
            {
                int model=(int)Get(batch,"ModelId"),lod=(int)Get(batch,"Lod"),kind=model<100?0:model<200?1:2,count=(int)Get(batch,"Count");long triangles=(long)count*(int)Get(batch,"TriangleCount");
                int key=kind*10+lod;if(!groups.TryGetValue(key,out Lod row)){row=new Lod{kind=kind,lod=lod};groups.Add(key,row);}row.batches++;row.instances+=count;row.triangles+=triangles;
                phase.publishedBatches++;phase.publishedInstances+=count;phase.publishedTriangles+=triangles;if(kind==0)phase.publishedTrees+=count;
                Bounds bounds=(Bounds)Get(batch,"Bounds");bool visible=GeometryUtility.TestPlanesAABB(planes,bounds);
                bool shadow=!visible&&(bool)Get(batch,"Shadows")&&(double)Call(population,"BoundsDistance",bounds)<=QualitySettings.shadowDistance&&(double)Call(population,"MinimumLodDistance",batch)<=QualitySettings.shadowDistance&&!(kind==0&&lod>=3);
                if(visible){row.visibleInstances+=count;row.visibleTriangles+=triangles;}else if(shadow){row.shadowOnlyInstances+=count;row.shadowOnlyTriangles+=triangles;phase.shadowOnlyTriangles+=triangles;}
                if((visible||shadow)&&(bool)Get(batch,"Shadows")&&!(kind==0&&lod>=3)){row.shadowEligibleInstances+=count;row.shadowEligibleTriangles+=triangles;phase.shadowEligibleTriangles+=triangles;}
            }
            phase.submittedTriangles=phase.visibleTriangles+phase.shadowOnlyTriangles;
            phase.lodDistribution=groups.OrderBy(p=>p.Key).Select(p=>p.Value).ToList();
            Check(phase.lodDistribution.Sum(p=>p.visibleInstances)==population.VisibleInstances&&phase.lodDistribution.Sum(p=>p.shadowOnlyInstances)==population.ShadowOnlyInstances,"Reflected submission classification differs from installed streamer counters.");
            RecordMemory(phase.name+"-complete");
        }
        static void RecordMemory(string label)
        {
            process.Refresh();var m=new Memory{phase=label,wallSeconds=whole.Elapsed.TotalSeconds,processPrivateBytes=process.PrivateMemorySize64,processWorkingSetBytes=process.WorkingSet64,gcManagedBytes=GC.GetTotalMemory(false),unityAllocatedBytes=Profiler.GetTotalAllocatedMemoryLong(),unityReservedBytes=Profiler.GetTotalReservedMemoryLong(),meshPayloadBytes=population==null?0:population.MeshBytes,matrixPayloadBytes=population==null?0:population.MatrixBufferBytes};report.memory.Add(m);
            Check(m.processPrivateBytes<=MaxPrivateBytes&&m.processPrivateBytes-baselinePrivate<=MaxProcessGrowth,"Cooperative native process memory guard exceeded.");
        }
        static double Percentile(List<double> values,double fraction){if(values.Count==0)return 0;var sorted=values.OrderBy(v=>v).ToArray();return sorted[Math.Min(sorted.Length-1,(int)Math.Ceiling(fraction*sorted.Length)-1)];}
        static string HashFile(string path){using(var sha=SHA256.Create())using(var stream=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
        static string HashBytes(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        static void Save()=>File.WriteAllText(folder+"/results.json",JsonUtility.ToJson(report,true));
        static void End(string status,Exception error)
        {
            if(ending)return;ending=true;EditorApplication.update-=Tick;report.status=status;report.error=error?.ToString();
            try{if(state<2&&phase!=null&&!phase.settled&&population!=null)FinishPhase(false);Save();}catch(Exception capture){report.error=(report.error??"")+"\nPartial capture: "+capture;Save();}
            // The production lifecycle uses deferred Play-mode Destroy. Explicitly
            // release this fixture's transient objects before OnDestroy in Edit mode.
            try
            {
                if(population!=null)
                {
                    if(Get(population,"loading") is IDisposable loading)loading.Dispose();Set(population,"loading",null);
                    foreach(object model in ((IDictionary)Get(population,"models")).Values)foreach(Mesh mesh in (Mesh[])Get(model,"Meshes"))if(mesh!=null)UnityEngine.Object.DestroyImmediate(mesh);
                    foreach(string name in new[]{"material","impostorMaterial"}){var material=(Material)Get(population,name);if(material!=null)UnityEngine.Object.DestroyImmediate(material);Set(population,name,null);}
                    if(population.Horizon!=null){var material=(Material)Get(population.Horizon,"material");if(material!=null)UnityEngine.Object.DestroyImmediate(material);Set(population,"<Horizon>k__BackingField",null);}
                }
                if(camera!=null)camera.targetTexture=null;if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}if(root!=null)UnityEngine.Object.DestroyImmediate(root);MaterialOpticsProfile.ClearAmbient();process?.Dispose();
            }
            catch(Exception cleanup){Debug.LogWarning("Fixture cleanup: "+cleanup);}
            Debug.Log("VEGETATION_BUDGET_"+report.status+" "+folder);if(error!=null)Debug.LogException(error);EditorApplication.Exit(status=="PASS"?0:1);
        }
    }
}
