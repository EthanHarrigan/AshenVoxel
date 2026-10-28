using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using AshenVoxel.WorldScale;

namespace AshenVoxel.Exploration.Editor
{
    // Local, opt-in capture requests. No scene or authoring assets are changed.
    [InitializeOnLoad]
    public static class ExplorationCapture
    {
        [Serializable] sealed class Request { public string label="view"; public double x=85760,y=350,z=15330; public float yaw=20,pitch=18,horizonKm; public int settleFrames=240; public bool stop,requireGpu; }
        [Serializable] sealed class Report
        {
            public string unity,gpu,api,horizon_error;
            public int width=1600,height=900,tiles,instances,horizon_stands,horizon_pending,horizon_draws;
            public long triangles,mesh_bytes;
            public double population_ms,horizon_ms;
            public bool impostor_shader_errors;
            public bool horizon_gpu,gpu_shader_errors;
            public uint gpu_visible;
            public double forest_requested_m,forest_published_m,forest_farthest_stand_m;
            public long gpu_buffer_bytes;
        }
        static Request request; static int frames;
        static readonly string Folder=Path.GetFullPath("Logs/ExplorationCapture");
        static ExplorationCapture(){string saved=SessionState.GetString("AshenVoxel.Capture","");if(saved.Length>0)request=JsonUtility.FromJson<Request>(saved);EditorApplication.update+=Tick;}
        public static void RunBatch()
        {
            Directory.CreateDirectory(Folder);
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ExplorationProjectSetup.ScenePath);
            File.WriteAllText(Path.Combine(Folder,"request.json"),"{\"label\":\"overview\",\"x\":85760,\"y\":550,\"z\":15330,\"yaw\":20,\"pitch\":22,\"settleFrames\":1800}");
        }
        public static void RunForestGpuBatch()
        {
            Directory.CreateDirectory(Folder);
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ExplorationProjectSetup.ScenePath);
            File.WriteAllText(Path.Combine(Folder,"request.json"),"{\"label\":\"forest-gpu-100km\",\"x\":90560,\"y\":1500,\"z\":12480,\"yaw\":20,\"pitch\":12,\"horizonKm\":100,\"requireGpu\":true,\"settleFrames\":1800}");
        }
        static void Tick()
        {
            if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            string path=Path.Combine(Folder,"request.json");
            if(request==null&&File.Exists(path))
            {
                request=JsonUtility.FromJson<Request>(File.ReadAllText(path));File.Move(path,path+".consumed-"+DateTime.UtcNow.Ticks);
                frames=0;
                if(request.stop){EditorApplication.isPlaying=false;request=null;return;}
                if(!EditorApplication.isPlaying){SessionState.SetString("AshenVoxel.Capture",JsonUtility.ToJson(request));EditorApplication.isPlaying=true;return;}
            }
            if(request==null||!EditorApplication.isPlaying)return;
            var root=UnityEngine.Object.FindFirstObjectByType<ExplorationBootstrap>();
            if(root==null||!root.Ready){EditorApplication.QueuePlayerLoopUpdate();return;}
            root.Player.SetInputEnabled(false);
            if(request.horizonKm>0)root.ActiveSettings.FarHorizonKm=request.horizonKm;
            if(frames++==0){root.Player.ResetAt(new WorldPoint(request.x,request.y,request.z),true);root.Player.Motor.NoClip=true;}
            root.Vista.SetObserverPose(new WorldPoint(request.x,request.y,request.z),Quaternion.Euler(request.pitch,request.yaw,0));
            root.Vista.MainCamera.transform.rotation=Quaternion.Euler(request.pitch,request.yaw,0);
            if(frames<request.settleFrames){EditorApplication.QueuePlayerLoopUpdate();return;}
            if(request.requireGpu)
            {
                if(root.Population.Horizon==null||!root.Population.Horizon.UsingGpu){Debug.LogError("Required GPU forest path was not active.");EditorApplication.Exit(1);return;}
                if(root.Population.Horizon.Error!=null){Debug.LogError(root.Population.Horizon.Error);EditorApplication.Exit(1);return;}
                if(root.Population.Horizon.PublishedFarDistance<request.horizonKm*1000||root.Population.Horizon.PendingSamples!=0){EditorApplication.QueuePlayerLoopUpdate();return;}
            }
            Directory.CreateDirectory(Folder);
            var camera=root.Vista.MainCamera;var previous=camera.targetTexture;var rt=new RenderTexture(1600,900,24);var image=new Texture2D(1600,900,TextureFormat.RGB24,false);
            var active=RenderTexture.active;
            string captured=request.label;
            try
            {
                camera.targetTexture=rt;root.Population.Tick(camera);camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1600,900),0,0);image.Apply();
                File.WriteAllBytes(Path.Combine(Folder,request.label+".png"),image.EncodeToPNG());
                var horizon=root.Population.Horizon;
                var report=new Report{unity=Application.unityVersion,gpu=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),tiles=root.Vista.ResidentTiles,
                    instances=root.Population.VisibleInstances,triangles=root.Population.VisibleTriangles,mesh_bytes=root.Population.MeshBytes,population_ms=root.Population.LastWorkMilliseconds,
                    horizon_stands=horizon==null?0:horizon.ResidentStands,horizon_pending=horizon==null?0:horizon.PendingSamples,horizon_draws=horizon==null?0:horizon.DrawCalls,
                    horizon_ms=horizon==null?0:horizon.LastWorkMilliseconds,horizon_error=horizon==null?null:horizon.Error,
                    impostor_shader_errors=ShaderUtil.ShaderHasError(Shader.Find("AshenVoxel/ForestImpostor")),
                    horizon_gpu=horizon!=null&&horizon.UsingGpu,gpu_shader_errors=ShaderUtil.ShaderHasError(Resources.Load<Shader>("ForestHorizonGpu")),
                    gpu_visible=horizon==null?0:horizon.ReadGpuVisibleCountForValidation(),forest_requested_m=root.Population.HorizonDistance,
                    forest_published_m=horizon==null?0:horizon.PublishedFarDistance,forest_farthest_stand_m=horizon==null?0:horizon.FarthestStandDistance,
                    gpu_buffer_bytes=horizon==null?0:horizon.GpuBufferBytes};
                File.WriteAllText(Path.Combine(Folder,request.label+".json"),JsonUtility.ToJson(report,true));
            }
            finally{camera.targetTexture=previous;RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(image);rt.Release();UnityEngine.Object.DestroyImmediate(rt);request=null;SessionState.EraseString("AshenVoxel.Capture");root.Player.ReleasePointer();}
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-ashenCaptureExit")>=0)
            {
                if(captured=="overview")File.WriteAllText(Path.Combine(Folder,"request.json"),"{\"label\":\"ground\",\"x\":85760,\"y\":160,\"z\":15330,\"yaw\":20,\"pitch\":0,\"settleFrames\":1200}");
                else if(captured=="ground")File.WriteAllText(Path.Combine(Folder,"request.json"),"{\"label\":\"redwood\",\"x\":90560,\"y\":800,\"z\":12480,\"yaw\":20,\"pitch\":12,\"settleFrames\":1800}");
                else EditorApplication.Exit(0);
            }
        }
    }
}
