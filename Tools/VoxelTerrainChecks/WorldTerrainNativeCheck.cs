using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using AshenVoxel.WorldScale;
using AshenVoxel.WorldScaleRuntime;
using UnityEngine.Rendering;

namespace AshenVoxel.Exploration.Editor
{
    [InitializeOnLoad]
    public static class WorldVoxelTerrainNativeCheck
    {
        const string Key="AshenVoxel.WorldVoxelTerrainNative";
        static readonly string Output=Path.GetFullPath("Logs/WorldVoxelNative3");
        static WorldExplorer vista;static int stage=-1,settled;static double started;static bool positioned;static double maxBuildMs;
        [Serializable] sealed class Report
        {
            public string status,scope,unity,gpu,api,label;public bool shader_error,voxel_mode;
            public int resident_tiles,pending,voxel_tiles,frustum_tiles,total_tiles,target_limited;
            public long triangles,mesh_bytes,total_memory;public double build_max_ms,worker_last_ms,elapsed_seconds,x,y,z;
        }
        static WorldVoxelTerrainNativeCheck(){if(SessionState.GetBool(Key,false)){stage=0;started=EditorApplication.timeSinceStartup;}EditorApplication.update+=Tick;}
        public static void Run()
        {
            Directory.CreateDirectory(Output);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            SessionState.SetBool(Key,true);stage=0;started=EditorApplication.timeSinceStartup;EditorApplication.isPlaying=true;
        }
        static void Tick()
        {
            if(stage<0||EditorApplication.isCompiling||EditorApplication.isUpdating||!EditorApplication.isPlaying)return;
            try
            {
                if(EditorApplication.timeSinceStartup-started>900)throw new Exception("Native world terrain timeout at stage"+stage);
                if(vista==null)Setup();
                if(!positioned)
                {
                    double x=stage==2?86400:85760,z=15330;
                    double y=stage==0?vista.SurfaceSampler.EvaluateColumn(x,z).SurfaceHeightMeters+1.65:stage==1?450:700;
                    var rotation=Quaternion.Euler(stage==0?25:stage==1?18:20,stage==2?200:20,0);
                    vista.SetObserverPose(new WorldPoint(x,y,z),rotation);vista.ForceReplan();positioned=true;settled=0;
                }
                maxBuildMs=Math.Max(maxBuildMs,vista.LastMeshBuildMilliseconds);
                if(vista.PendingTiles!=0||vista.PlanningInProgress){settled=0;EditorApplication.QueuePlayerLoopUpdate();return;}
                if(++settled<30){EditorApplication.QueuePlayerLoopUpdate();return;}
                string label=stage==0?"ground":stage==1?"overview":"turn-rebase";
                Capture(label);
                if(++stage<3){GC.Collect();positioned=false;EditorApplication.QueuePlayerLoopUpdate();return;}
                Type.GetType("AshenVoxel.Rendering.Editor.MeshRegressionChecks, AshenVoxel.Rendering.Editor",true).GetMethod("Run",BindingFlags.Public|BindingFlags.Static).Invoke(null,null);
                File.WriteAllText(Path.Combine(Output,"completed.txt"),"PASS native actual-source terrain play, three settled voxel views and native mesh regression. Terrain-only to coexist with user main game. No full-scene/FPS acceptance.");
                SessionState.SetBool(Key,false);stage=-1;Debug.Log("WORLD_VOXEL_NATIVE_PASS");EditorApplication.Exit(0);
            }
            catch(Exception e){Directory.CreateDirectory(Output);File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());SessionState.SetBool(Key,false);stage=-1;Debug.LogException(e);EditorApplication.Exit(1);}
        }
        static void Setup()
        {
            var settings=AssetDatabase.LoadAssetAtPath<ExplorationSettings>("Assets/AshenVoxel/Settings/ExplorationSettings.asset");
            HeightAtlas macro,fine;ClimateAtlas mc,fc;ExplorationBiomeAtlas biomes;
            using(var f=new MemoryStream(settings.MacroWorld.bytes))macro=AtlasIO.Read(f);
            using(var f=new MemoryStream(settings.FineWorld.bytes))fine=AtlasIO.Read(f);
            using(var f=new MemoryStream(settings.MacroClimate.bytes))mc=ClimateArchiveIO.Read(f);
            using(var f=new MemoryStream(settings.FineClimate.bytes))fc=ClimateArchiveIO.Read(f);
            using(var f=new MemoryStream(settings.SourceBiomes.bytes))biomes=ExplorationBiomeAtlasIO.Read(f);
            var field=new ExplorationAtlasField(macro,mc,fine,fc,512,macro.ExtentX,macro.ExtentZ,1,biomes,macro.ExtentX,macro.ExtentZ);
            GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(ExplorationProjectSetup.PipelinePath);QualitySettings.renderPipeline=GraphicsSettings.defaultRenderPipeline;
            vista=new GameObject("Actual source world voxel LOD verification").AddComponent<WorldExplorer>();
            vista.UseVoxelLodTerrain=true;vista.ViewDistanceKm=30;vista.MaximumTiles=512;vista.MeshBuildsPerFrame=1;
            vista.ConfigureExternal(field,settings.Surface(),new WorldPoint(85760,138,15330),Quaternion.Euler(25,20,0));
            vista.MainCamera.fieldOfView=75;vista.MainCamera.nearClipPlane=.04f;
            var sun=new GameObject("Sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=2.1f;sun.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(36,325,0);
            RenderSettings.sun=sun;RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=settings.AmbientSky;RenderSettings.ambientEquatorColor=settings.AmbientEquator;RenderSettings.ambientGroundColor=settings.AmbientGround;RenderSettings.fog=false;
        }
        static void Capture(string label)
        {
            var camera=vista.MainCamera;var rt=new RenderTexture(1600,900,24);var image=new Texture2D(1600,900,TextureFormat.RGB24,false);var previous=RenderTexture.active;
            try
            {
                camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1600,900),0,0);image.Apply();
                File.WriteAllBytes(Path.Combine(Output,label+".png"),image.EncodeToPNG());
                var renderers=vista.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name.StartsWith("Voxel LOD")).ToArray();
                var planes=GeometryUtility.CalculateFrustumPlanes(camera);long bytes=0;foreach(var r in renderers){var m=r.GetComponent<MeshFilter>().sharedMesh;bytes+=Profiler.GetRuntimeMemorySizeLong(m);}
                var eye=vista.GlobalCamera;
                var report=new Report{status="PASS",scope="Actual bundled terrain source in native Windows Play, complete30km voxel LOD queue, without forest/props to coexist with user game. Bounds/frustum census and CPU build observations; not full Exploration/FPS acceptance.",
                    unity=Application.unityVersion,gpu=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),label=label,shader_error=ShaderUtil.ShaderHasError(Shader.Find("AshenVoxel/GroundSurface")),voxel_mode=vista.UseVoxelLodTerrain,
                    resident_tiles=vista.ResidentTiles,pending=vista.PendingTiles,voxel_tiles=vista.VoxelLodTiles,frustum_tiles=renderers.Count(r=>GeometryUtility.TestPlanesAABB(planes,r.bounds)),total_tiles=renderers.Length,
                    target_limited=vista.PixelTargetLimitedTiles,triangles=vista.TerrainTriangles,mesh_bytes=bytes,total_memory=Profiler.GetTotalAllocatedMemoryLong(),build_max_ms=maxBuildMs,worker_last_ms=vista.LastVoxelWorkerMilliseconds,
                    elapsed_seconds=EditorApplication.timeSinceStartup-started,x=eye.X,y=eye.Y,z=eye.Z};
                if(report.shader_error||!report.voxel_mode||report.voxel_tiles<1)throw new Exception("Native voxel mode/shader verification failed");
                File.WriteAllText(Path.Combine(Output,label+".json"),JsonUtility.ToJson(report,true));
            }
            finally{camera.targetTexture=null;RenderTexture.active=previous;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);}
        }
    }
}
