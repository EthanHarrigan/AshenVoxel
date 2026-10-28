using System;
using System.Collections.Generic;
using System.IO;
using AshenVoxel.Rendering;
using AshenVoxel.WorldScale;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace AshenVoxel.Exploration.Editor
{
    // Run only in the idle validation project. No Exploration scene or Play boot.
    public static class ForestElevationNativeCheck
    {
        const string Folder="Logs/ForestElevation";
        [Serializable] sealed class Result
        {
            public string status="PASS",gpu,api,error;
            public string scope="Bounded actual-atlas forward CPU/GPU views; explicit CPU depth/normal coverage; shader pass compilation. No FPS or full-world acceptance.";
            public int compiledPasses;
            public List<View> views=new List<View>();
        }
        [Serializable] sealed class View {public int prototype;public float elevation;public int cpuPixels,gpuPixels,depthPixels,normalPixels,coverageMismatch;}
        static void Check(bool pass,string text){if(!pass)throw new InvalidOperationException(text);}
        public static void Run()
        {
            AssetDatabase.DesiredWorkerCount=0;AssetDatabase.ForceToDesiredWorkerCount();
            Directory.CreateDirectory(Folder);var report=new Result{gpu=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString()};
            int code=0;
            try{Test(report);File.WriteAllText(Folder+"/results.json",JsonUtility.ToJson(report,true));Debug.Log("PASS forest elevation native fixture. Inspect image sweep before visual acceptance.");}
            catch(Exception e){code=1;report.status="FAIL";report.error=e.ToString();File.WriteAllText(Folder+"/results.json",JsonUtility.ToJson(report,true));Debug.LogException(e);}
            EditorApplication.Exit(code);
        }
        static void Test(Result report)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=new GameObject("Bounded elevation fixture");var viewObject=new GameObject("Elevation camera");viewObject.transform.SetParent(root.transform);
            var camera=viewObject.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.aspect=1;camera.cullingMask=1<<29;camera.nearClipPlane=.1f;camera.farClipPlane=1000;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;camera.allowHDR=false;camera.allowMSAA=false;
            var target=new RenderTexture(256,256,24,RenderTextureFormat.ARGB32);camera.targetTexture=target;var image=new Texture2D(256,256,TextureFormat.RGBA32,false);
            var prior=RenderTexture.active;bool fog=RenderSettings.fog;RenderSettings.fog=false;
            var template=Resources.Load<Material>("ForestImpostor");Check(template!=null,"Missing existing atlas material.");
            var material=new Material(template);material.SetFloat("_HorizonNear",0);material.SetFloat("_HorizonFar",100000);material.enableInstancing=true;
            var gpuMaterial=new Material(Resources.Load<Shader>("ForestHorizonGpu"));
            MaterialOpticsProfile.Apply(Resources.Load<MaterialOpticsProfile>("MaterialOptics"));MaterialOpticsProfile.SetAmbient(Color.white,Color.white,Color.white);
            var modelObject=new GameObject("Actual tree quad");modelObject.layer=29;modelObject.transform.SetParent(root.transform);
            var filter=modelObject.AddComponent<MeshFilter>();var renderer=modelObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
            var meshes=new List<Mesh>();
            Color32[] Read(string file=null)
            {
                RenderTexture.active=target;image.ReadPixels(new Rect(0,0,256,256),0,0);image.Apply();
                if(file!=null)File.WriteAllBytes(Folder+"/"+file+".png",image.EncodeToPNG());return image.GetPixels32();
            }
            bool[] PassMask(Mesh mesh,string pass)
            {
                var commands=new CommandBuffer{name="Elevation explicit "+pass};
                try
                {
                    commands.SetRenderTarget(target);commands.ClearRenderTarget(true,true,Color.white);
                    commands.SetViewProjectionMatrices(camera.worldToCameraMatrix,GL.GetGPUProjectionMatrix(camera.projectionMatrix,true));
                    commands.SetGlobalVector("_WorldSpaceCameraPos",camera.transform.position);
                    commands.DrawMesh(mesh,Matrix4x4.identity,material,0,material.FindPass(pass));Graphics.ExecuteCommandBuffer(commands);
                    var colors=Read();var mask=new bool[colors.Length];for(int i=0;i<mask.Length;i++)mask[i]=pass=="DepthOnly"?colors[i].r<128:colors[i].a<128;return mask;
                }
                finally{commands.Release();}
            }
            try
            {
                foreach(var m in new[]{material,gpuMaterial})
                {
                    for(int i=0;i<m.passCount;i++){ShaderUtil.CompilePass(m,i,true);report.compiledPasses++;}
                    Check(!ShaderUtil.ShaderHasError(m.shader),"Native shader compile failed: "+m.shader.name);
                }
                using(var gpu=ForestGpuRenderer.TryCreate(material))
                using(var reader=new ExplorationPrototypeReader(File.ReadAllBytes("Assets/AshenVoxel/Exploration/Prototypes/ExplorationPrototypes.bytes")))
                {
                    Check(gpu!=null,"GPU indirect fixture unsupported.");
                    while(reader.TryRead(out var prototype))
                    {
                        if(prototype.Id!=0&&prototype.Id!=4&&prototype.Id!=8&&prototype.Id!=12){prototype.ReleaseMeshData();continue;}
                        Mesh mesh=ForestBillboard.Create(prototype);meshes.Add(mesh);filter.sharedMesh=mesh;
                        float span=ForestBillboard.Span(prototype);Vector3 center=new Vector3(0,(prototype.MinY+prototype.MaxY)*.5f,0);camera.orthographicSize=span*.55f;
                        gpu.Publish(new[]{new ForestGpuRenderer.Instance{PositionSpan=new Vector4(0,center.y,0,span),Style=new Vector4(1,prototype.Id,0,0)}},new WorldPoint(0,0,0));
                        foreach(float angle in new[]{0f,30f,45.9f,46.2f,60f,75.9f,76.1f,78f,80f,82f,84f,86f,87.9f,88.1f,89.9f,90f})
                        {
                            float radians=angle*Mathf.Deg2Rad;var direction=new Vector3(0,Mathf.Sin(radians),Mathf.Cos(radians));
                            camera.transform.position=center+direction*span*3;camera.transform.rotation=Quaternion.LookRotation(-direction,new Vector3(0,Mathf.Cos(radians),-Mathf.Sin(radians)));
                            string label="tree-"+prototype.Id+"-elevation-"+angle.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture);
                            renderer.enabled=true;camera.Render();var cpu=Read(label+"-cpu");
                            var depth=PassMask(mesh,"DepthOnly");var normals=PassMask(mesh,"DepthNormals");
                            renderer.enabled=false;var eye=camera.transform.position;gpu.Draw(camera,new WorldPoint(eye.x,eye.y,eye.z),new WorldPoint(0,0,0),0,100000,29);camera.Render();var indirect=Read(label+"-gpu");
                            var sample=new View{prototype=prototype.Id,elevation=angle};
                            for(int i=0;i<cpu.Length;i++){if(cpu[i].r+cpu[i].g+cpu[i].b>10)sample.cpuPixels++;if(indirect[i].r+indirect[i].g+indirect[i].b>10)sample.gpuPixels++;if(depth[i])sample.depthPixels++;if(normals[i])sample.normalPixels++;if(depth[i]!=normals[i])sample.coverageMismatch++;}
                            report.views.Add(sample);Check(sample.cpuPixels>20&&sample.gpuPixels>20,"Missing forward tree: "+label);Check(sample.depthPixels>20&&sample.coverageMismatch==0,"Depth/normal coverage mismatch: "+label+" mismatch="+sample.coverageMismatch);
                            Check(Math.Abs(sample.cpuPixels-sample.gpuPixels)<Math.Max(16,sample.cpuPixels*.03),"CPU/GPU silhouette mismatch: "+label);
                        }
                        prototype.ReleaseMeshData();
                    }
                }
                Check(report.views.Count==64,"Expected four prototypes and sixteen elevations.");
            }
            finally
            {
                RenderSettings.fog=fog;RenderTexture.active=prior;camera.targetTexture=null;target.Release();
                UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);
                foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(gpuMaterial);MaterialOpticsProfile.ClearAmbient();
            }
        }
    }
}
