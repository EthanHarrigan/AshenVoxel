using System;
using System.IO;
using System.Reflection;
using AshenVoxel.WorldScale;
using AshenVoxel.WorldScaleRuntime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AshenVoxel.Exploration.Editor
{
    // Used only in an isolated copied project, never automatically in the user's scene.
    public static class VoxelNativeCheck
    {
        sealed class Fixture:IWorldField
        {public string SourceIdentity=>"explicit-native-voxel-fixture";public SurfaceSample Sample(double x,double z)=>new SurfaceSample(10+.15*x,6,6,true);}
        [Serializable] sealed class Report
        {public string status,scope,unity,gpu,api;public int pages,cells,triangles,passes;public bool ground_shader_error,voxel_shader_error,mesh_regression;}
        static ExplorationVoxelTerrain terrain;static WorldExplorer vista;static Material ground,voxel;static Camera camera;static double started;
        static readonly string Output=Path.GetFullPath("Logs/VoxelNative");
        public static void Run()
        {
            try
            {
                Directory.CreateDirectory(Output);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(ExplorationProjectSetup.PipelinePath);
                QualitySettings.renderPipeline=GraphicsSettings.defaultRenderPipeline;
                ground=new Material(Shader.Find("AshenVoxel/GroundSurface"));voxel=new Material(Resources.Load<Material>("VoxelSurface"));
                var root=new GameObject("Isolated voxel verification");vista=root.AddComponent<WorldExplorer>();vista.TerrainMaterial=ground;
                terrain=root.AddComponent<ExplorationVoxelTerrain>();terrain.Initialize(vista,new Fixture(),new SurfaceProfile(enableSnow:false),voxel);
                // A continuous proxy beneath the exact same fixture used by voxel conversion.
                var mesh=new Mesh();const int side=65;var vertices=new Vector3[side*side];var colors=new Color32[vertices.Length];var normals=new Vector3[vertices.Length];var indices=new int[64*64*6];int at=0;
                for(int z=0;z<side;z++)for(int x=0;x<side;x++)
                {float px=x*.5f-16,pz=z*.5f-16;int i=x+z*side;vertices[i]=new Vector3(px,10+px*.15f,pz);colors[i]=new Color32(72,99,49,255);normals[i]=new Vector3(-.15f,1,0).normalized;}
                for(int z=0;z<64;z++)for(int x=0;x<64;x++)
                {int a=x+z*side,b=a+side;indices[at++]=a;indices[at++]=b;indices[at++]=a+1;indices[at++]=a+1;indices[at++]=b;indices[at++]=b+1;}
                mesh.vertices=vertices;mesh.normals=normals;mesh.colors32=colors;mesh.triangles=indices;mesh.RecalculateBounds();
                var proxy=new GameObject("Continuous proxy comparison");proxy.AddComponent<MeshFilter>().sharedMesh=mesh;proxy.AddComponent<MeshRenderer>().sharedMaterial=ground;
                var sunlight=new GameObject("Sun").AddComponent<Light>();sunlight.type=LightType.Directional;sunlight.intensity=1.2f;sunlight.shadows=LightShadows.Soft;sunlight.transform.rotation=Quaternion.Euler(45,25,0);
                RenderSettings.sun=sunlight;RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(.55f,.6f,.65f);RenderSettings.ambientEquatorColor=new Color(.35f,.4f,.45f);RenderSettings.ambientGroundColor=new Color(.2f,.2f,.2f);RenderSettings.fog=false;
                camera=new GameObject("Native verification camera").AddComponent<Camera>();camera.nearClipPlane=.04f;camera.farClipPlane=100;camera.fieldOfView=60;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.16f,.2f);
                camera.GetUniversalAdditionalCameraData().requiresDepthTexture=true;
                started=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;
            }
            catch(Exception e){Fail(e);}
        }
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup-started>180)throw new Exception("Timed out waiting for49 native voxel pages: "+terrain.Status);
                terrain.Tick(new WorldPoint(0,12,0),new WorldPoint(0,0,0));
                if(terrain.ResidentPages<49)return;
                EditorApplication.update-=Tick;
                Capture("close",new Vector3(3,12,-4),new Vector3(0,10.2f,0));
                Capture("transition",new Vector3(16,15,-18),new Vector3(8,11,0));
                foreach(Material m in new[]{ground,voxel})for(int pass=0;pass<m.passCount;pass++)m.SetPass(pass);
                bool groundError=ShaderUtil.ShaderHasError(ground.shader),voxelError=ShaderUtil.ShaderHasError(voxel.shader);
                Type.GetType("AshenVoxel.Rendering.Editor.MeshRegressionChecks, AshenVoxel.Rendering.Editor",true).GetMethod("Run",BindingFlags.Public|BindingFlags.Static).Invoke(null,null);
                var report=new Report{status=groundError||voxelError?"FAIL":"PASS",scope="Isolated native Windows Editor D3D11 controlled fixture: real voxel worker/page mesh uploads, URP camera renders and pass checks plus native mesh regressions. Not the full Exploration scene, interactive movement, decoded inference, standalone or FPS benchmark.",unity=Application.unityVersion,gpu=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),pages=terrain.ResidentPages,cells=terrain.ResidentCells,triangles=terrain.Triangles,passes=ground.passCount+voxel.passCount,ground_shader_error=groundError,voxel_shader_error=voxelError,mesh_regression=true};
                File.WriteAllText(Path.Combine(Output,"validation.json"),JsonUtility.ToJson(report,true));Debug.Log("VOXEL_NATIVE_"+report.status);EditorApplication.Exit(report.status=="PASS"?0:1);
            }
            catch(Exception e){Fail(e);}
        }
        static void Capture(string label,Vector3 position,Vector3 target)
        {
            camera.transform.position=position;camera.transform.LookAt(target);
            var rt=new RenderTexture(1200,800,24);var image=new Texture2D(1200,800,TextureFormat.RGB24,false);var previous=RenderTexture.active;
            try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1200,800),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Output,label+".png"),image.EncodeToPNG());}
            finally{camera.targetTexture=null;RenderTexture.active=previous;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);}
        }
        static void Fail(Exception error)
        {EditorApplication.update-=Tick;Directory.CreateDirectory(Output);File.WriteAllText(Path.Combine(Output,"failure.txt"),error.ToString());Debug.LogException(error);EditorApplication.Exit(1);}
    }
}
