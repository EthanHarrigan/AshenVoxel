using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using AshenVoxel.WorldScale;

namespace AshenVoxel.Exploration.Editor
{
    [InitializeOnLoad]
    public static class WorldVoxelNativeCheck
    {
        const string Key="AshenVoxel.WorldVoxelNative";
        static readonly string Output=Path.GetFullPath("Logs/WorldVoxelNative4");
        static int stage=-1,settled;static double started;static bool positioned;static double maxBuildMs;
        [Serializable] sealed class Report
        {
            public string status,scope,unity,gpu,api,label;public bool shader_error,voxel_mode,mesh_regression;
            public int resident_tiles,pending,voxel_tiles,frustum_tiles,total_tiles,target_limited;
            public long triangles,mesh_bytes,total_memory,vertex_index_bytes;public int vertex_stride;public double build_max_ms,worker_last_ms,elapsed_seconds,x,y,z;
        }
        static WorldVoxelNativeCheck(){if(SessionState.GetBool(Key,false)){stage=0;started=EditorApplication.timeSinceStartup;}EditorApplication.update+=Tick;}
        public static void Run()
        {
            Directory.CreateDirectory(Output);EditorSceneManager.OpenScene(ExplorationProjectSetup.ScenePath);
            var root=UnityEngine.Object.FindFirstObjectByType<ExplorationBootstrap>();root.LoadSavedOptions=false;
            SessionState.SetBool(Key,true);stage=0;started=EditorApplication.timeSinceStartup;EditorApplication.isPlaying=true;
        }
        static void Tick()
        {
            if(stage<0||EditorApplication.isCompiling||EditorApplication.isUpdating||!EditorApplication.isPlaying)return;
            try
            {
                if(EditorApplication.timeSinceStartup-started>900)throw new Exception("Native world terrain timeout at stage"+stage);
                var root=UnityEngine.Object.FindFirstObjectByType<ExplorationBootstrap>();if(root==null)return;
                if(root.Failed)throw new Exception(root.Status);
                if(!root.Ready){EditorApplication.QueuePlayerLoopUpdate();return;}
                root.Player.SetInputEnabled(false);root.Player.Motor.NoClip=true;
                if(!positioned)
                {
                    double x=stage==2?86400:85760,z=15330;
                    double y=stage==0?root.Vista.SurfaceSampler.EvaluateColumn(x,z).SurfaceHeightMeters+1.65:stage==1?450:700;
                    root.Player.ResetAt(new WorldPoint(x,y-1.65,z),true);
                    var rotation=Quaternion.Euler(stage==0?25:stage==1?18:20,stage==2?200:20,0);
                    root.Vista.SetObserverPose(new WorldPoint(x,y,z),rotation);root.Vista.ForceReplan();positioned=true;settled=0;
                }
                maxBuildMs=Math.Max(maxBuildMs,root.Vista.LastMeshBuildMilliseconds);
                if(root.Vista.PendingTiles!=0||root.Vista.PlanningInProgress){settled=0;EditorApplication.QueuePlayerLoopUpdate();return;}
                if(++settled<30){EditorApplication.QueuePlayerLoopUpdate();return;}
                string label=stage==0?"ground":stage==1?"overview":"turn-rebase";
                Capture(root,label);
                if(++stage<3){positioned=false;EditorApplication.QueuePlayerLoopUpdate();return;}
                Type.GetType("AshenVoxel.Rendering.Editor.MeshRegressionChecks, AshenVoxel.Rendering.Editor",true).GetMethod("Run",BindingFlags.Public|BindingFlags.Static).Invoke(null,null);
                File.WriteAllText(Path.Combine(Output,"completed.txt"),"PASS native full Exploration play, three settled voxel views and native mesh regression. No interactive/FPS acceptance.");
                SessionState.SetBool(Key,false);stage=-1;Debug.Log("WORLD_VOXEL_NATIVE_PASS");EditorApplication.Exit(0);
            }
            catch(Exception e){Directory.CreateDirectory(Output);File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());SessionState.SetBool(Key,false);stage=-1;Debug.LogException(e);EditorApplication.Exit(1);}
        }
        static void Capture(ExplorationBootstrap root,string label)
        {
            var camera=root.Vista.MainCamera;var rt=new RenderTexture(1600,900,24);var image=new Texture2D(1600,900,TextureFormat.RGB24,false);var previous=RenderTexture.active;
            try
            {
                camera.targetTexture=rt;root.Population.Tick(camera);camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1600,900),0,0);image.Apply();
                File.WriteAllBytes(Path.Combine(Output,label+".png"),image.EncodeToPNG());
                var renderers=root.Vista.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name.StartsWith("Voxel LOD")).ToArray();
                var planes=GeometryUtility.CalculateFrustumPlanes(camera);long bytes=0;foreach(var r in renderers){var m=r.GetComponent<MeshFilter>().sharedMesh;bytes+=Profiler.GetRuntimeMemorySizeLong(m);}
                var eye=root.Vista.GlobalCamera;int stride=renderers[0].GetComponent<MeshFilter>().sharedMesh.GetVertexBufferStride(0);if(stride!=24)throw new Exception("Packed vertex stride is not24");
                var report=new Report{status="PASS",scope="Actual bundled Exploration scene in native Windows Play, completed terrain queue, fixed camera capture. Render bounds/frustum census and CPU build observations; no stable FPS or GPU budget claim.",
                    unity=Application.unityVersion,gpu=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),label=label,shader_error=ShaderUtil.ShaderHasError(Shader.Find("AshenVoxel/GroundSurface")),voxel_mode=root.Vista.UseVoxelLodTerrain,
                    resident_tiles=root.Vista.ResidentTiles,pending=root.Vista.PendingTiles,voxel_tiles=root.Vista.VoxelLodTiles,frustum_tiles=renderers.Count(r=>GeometryUtility.TestPlanesAABB(planes,r.bounds)),total_tiles=renderers.Length,
                    target_limited=root.Vista.PixelTargetLimitedTiles,vertex_stride=stride,vertex_index_bytes=renderers.Sum(r=>{var m=r.GetComponent<MeshFilter>().sharedMesh;return (long)m.vertexCount*24+(long)m.GetIndexCount(0)*4;}),triangles=root.Vista.TerrainTriangles,mesh_bytes=bytes,total_memory=Profiler.GetTotalAllocatedMemoryLong(),build_max_ms=maxBuildMs,worker_last_ms=root.Vista.LastVoxelWorkerMilliseconds,
                    elapsed_seconds=EditorApplication.timeSinceStartup-started,x=eye.X,y=eye.Y,z=eye.Z};
                if(report.shader_error||!report.voxel_mode||report.voxel_tiles<1)throw new Exception("Native voxel mode/shader verification failed");
                File.WriteAllText(Path.Combine(Output,label+".json"),JsonUtility.ToJson(report,true));
            }
            finally{camera.targetTexture=null;RenderTexture.active=previous;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);}
        }
    }
}
