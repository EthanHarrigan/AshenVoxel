using System;
using System.Collections.Generic;
using System.IO;
using AshenVoxel.Core;
using AshenVoxel.Rendering;
using AshenVoxel.WorldScale;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
namespace AshenVoxel.Exploration.Editor
{
    /// <summary>Actual baked trees, fixed view, foreground-only brightness measurements.
    /// A no-sun case proves readability is independent of direct solar illumination.</summary>
    public static class ForestShadeNativeCheck
    {
        [Serializable] sealed class Result {public string status="PASS",gpu;public List<Sample> views=new List<Sample>();}
        [Serializable] sealed class Sample {public string name;public int pixels,p10,median;public double mean;}
        public static void Test()
        {
            const string folder="Logs/ForestShade";Directory.CreateDirectory(folder);
            var root=new GameObject("Bounded forest shade fixture");var meshes=new List<Mesh>();var renderers=new List<MeshRenderer>();
            var material=new Material(Shader.Find("AshenVoxel/WorldSurface"));var unlit=new Material(Shader.Find("Hidden/AshenVoxel/ForestAtlasBake"));
            var settings=AssetDatabase.LoadAssetAtPath<ExplorationSettings>(ExplorationProjectSetup.SettingsPath);
            var cameraObject=new GameObject("Forest shade camera");cameraObject.transform.SetParent(root.transform);var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.cullingMask=1<<28;camera.fieldOfView=65;camera.aspect=16f/9;camera.nearClipPlane=.1f;camera.farClipPlane=100;
            camera.transform.position=new Vector3(0,2,-9);camera.transform.LookAt(new Vector3(0,3.3f,3));camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            cameraObject.AddComponent<ExplorationVisualQuality>().Initialize(camera,GraphicsSettings.currentRenderPipeline);
            var rt=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32);camera.targetTexture=rt;var image=new Texture2D(1280,720,TextureFormat.RGB24,false);var prior=RenderTexture.active;
            var oldLights=UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);var oldIntensity=new float[oldLights.Length];for(int i=0;i<oldLights.Length;i++){oldIntensity[i]=oldLights[i].intensity;oldLights[i].intensity=0;}
            var sun=new GameObject("Forest shade sun").AddComponent<Light>();sun.transform.SetParent(root.transform);sun.type=LightType.Directional;sun.color=settings.SunColor;sun.intensity=settings.SunIntensity;sun.shadows=LightShadows.Soft;sun.shadowBias=.02f;sun.shadowNormalBias=.1f;
            bool fog=RenderSettings.fog;RenderSettings.fog=false;
            Color32[] Capture(string name){camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();if(name!=null)File.WriteAllBytes(folder+"/"+name+".png",image.EncodeToPNG());return image.GetPixels32();}
            void Add(Mesh mesh,string name,Vector3 position){meshes.Add(mesh);var go=new GameObject(name);go.layer=28;go.transform.SetParent(root.transform);go.transform.position=position;go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderers.Add(renderer);}
            try
            {
                using(var reader=new ExplorationPrototypeReader(File.ReadAllBytes("Assets/AshenVoxel/Exploration/Prototypes/ExplorationPrototypes.bytes")))while(reader.TryRead(out var p))
                {
                    if(p.Id==0||p.Id==4||p.Id==12)
                    {
                        var source=p.Mesh(0);var vertices=new Vector3[source.VertexCount];var normals=new Vector3[source.VertexCount];var colors=new Color32[source.VertexCount];
                        for(int i=0;i<vertices.Length;i++){vertices[i]=new Vector3(source.Positions[i*3]*.1f,source.Positions[i*3+1]*.1f,source.Positions[i*3+2]*.1f);int n=source.Normals[i];normals[i]=n==0?Vector3.right:n==1?Vector3.left:n==2?Vector3.up:n==3?Vector3.down:n==4?Vector3.forward:Vector3.back;uint c=source.Colors[i];colors[i]=new Color32((byte)c,(byte)(c>>8),(byte)(c>>16),(byte)(c>>24));}
                        var mesh=new Mesh{indexFormat=IndexFormat.UInt32,vertices=vertices,normals=normals,colors32=colors};mesh.SetTriangles(source.Indices,0);mesh.RecalculateBounds();
                        Add(mesh,p.Name,p.Id==0?new Vector3(-3,0,2):p.Id==4?new Vector3(0,0,4):new Vector3(4,0,5));
                    }
                    p.ReleaseMeshData();
                }
                // A mask from actual geometry excludes background sky from brightness acceptance.
                foreach(var r in renderers)r.sharedMaterial=unlit;Color32[] mask=Capture(null);foreach(var r in renderers)r.sharedMaterial=material;
                MaterialOpticsProfile.Apply(Resources.Load<MaterialOpticsProfile>("MaterialOptics"));
                MaterialOpticsProfile.SetAmbient(settings.AmbientSky,settings.AmbientEquator,settings.AmbientGround,settings.AmbientFillStrength);
                var report=new Result{gpu=SystemInfo.graphicsDeviceName};
                foreach(string name in new[]{"sun-facing","backlit","sky-fill-only"})
                {
                    sun.intensity=name=="sky-fill-only"?0:settings.SunIntensity;sun.transform.rotation=Quaternion.Euler(36,name=="sun-facing"?0:180,0);
                    var pixels=Capture(name);var luminance=new List<int>();long sum=0;
                    for(int i=0;i<pixels.Length;i++)if(mask[i].r+mask[i].g+mask[i].b>20){int value=(pixels[i].r*54+pixels[i].g*183+pixels[i].b*19)/256;luminance.Add(value);sum+=value;}
                    luminance.Sort();if(luminance.Count<10000)throw new Exception("Forest brightness fixture is missing geometry.");
                    var sample=new Sample{name=name,pixels=luminance.Count,p10=luminance[luminance.Count/10],median=luminance[luminance.Count/2],mean=(double)sum/luminance.Count};report.views.Add(sample);
                    if(sample.p10<12||sample.median<30)throw new Exception("Unacceptably dark forest: "+name+" p10="+sample.p10+" median="+sample.median);
                }
                File.WriteAllText(folder+"/results.json",JsonUtility.ToJson(report,true));Debug.Log("PASS actual-tree shade readability with front sun, back sun and no direct sun.");
            }
            finally
            {
                for(int i=0;i<oldLights.Length;i++)if(oldLights[i]!=null)oldLights[i].intensity=oldIntensity[i];RenderSettings.fog=fog;RenderTexture.active=prior;camera.targetTexture=null;rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(root);foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(unlit);MaterialOpticsProfile.ClearAmbient();
            }
        }
    }
}
