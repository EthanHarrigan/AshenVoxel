using System;
using System.Collections.Generic;
using System.IO;
using AshenVoxel.Core;
using AshenVoxel.Rendering;
using UnityEditor;
using UnityEngine;
namespace AshenVoxel.Exploration.Editor
{
    public static class MaterialOpticsNativeCheck
    {
        [Serializable] sealed class Result {public string status="PASS",gpu;public List<Sample> samples=new List<Sample>();public List<DefaultSample> defaults=new List<DefaultSample>();public long backlight_dark,backlight_transmitted,leaf_distant,leaf_near;public int stationary_pixel_changes,translated_pixel_changes,translated_pixels;}
        [Serializable] sealed class Sample {public string material;public long opaque,half,clear;}
        [Serializable] sealed class DefaultSample {public string material;public float coverage;}
        public static void Test()
        {
            const string folder="Logs/MaterialOptics";Directory.CreateDirectory(folder);
            var report=new Result{gpu=SystemInfo.graphicsDeviceName};
            var go=new GameObject("Material optics fixture");go.layer=29;
            var mesh=new Mesh();go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var material=new Material(Shader.Find("AshenVoxel/WorldSurface"));go.AddComponent<MeshRenderer>().sharedMaterial=material;
            var cameraObject=new GameObject("Material optics camera");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=.6f;
            camera.transform.position=new Vector3(.5f,.5f,-3);camera.transform.rotation=Quaternion.identity;camera.nearClipPlane=.1f;camera.farClipPlane=10;camera.cullingMask=1<<29;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.allowHDR=false;camera.allowMSAA=false;
            var rt=new RenderTexture(128,128,24);camera.targetTexture=rt;var image=new Texture2D(128,128,TextureFormat.RGB24,false);var prior=RenderTexture.active;
            var profile=ScriptableObject.CreateInstance<MaterialOpticsProfile>();var entry=new MaterialOpticsProfile.Entry();profile.Materials=new[]{entry};var mesher=new GreedyChunkMesher();
            var lights=UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);var intensities=new float[lights.Length];for(int i=0;i<lights.Length;i++){intensities[i]=lights[i].intensity;lights[i].intensity=0;}
            bool fog=RenderSettings.fog;RenderSettings.fog=false;MaterialOpticsProfile.SetAmbient(Color.white,Color.white,Color.white);
            var wind=Shader.GetGlobalVector("_VoxelWindState");var priorWind=Shader.GetGlobalVector("_VoxelWindPreviousState");Shader.SetGlobalVector("_VoxelWindState",Vector4.zero);Shader.SetGlobalVector("_VoxelWindPreviousState",Vector4.zero);
            long Capture(string path=null){camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,128,128),0,0);image.Apply();long energy=0;foreach(var c in image.GetPixels32())energy+=c.r+c.g+c.b;if(path!=null)File.WriteAllBytes(folder+"/"+path+".png",image.EncodeToPNG());return energy;}
            try
            {
                for(int id=1;id<MaterialCatalog.Count;id++)
                {
                    var w=new VoxelWorld(16,16,16,1,1);w.Set(0,0,0,(MaterialId)id,VoxelColor.Pack(160,180,150));mesher.Build(w,new Int3(0,0,0),mesh);entry.Material=(MaterialId)id;entry.Transmission=0;
                    entry.Opacity=1;MaterialOpticsProfile.Apply(profile);long opaque=Capture(id==1?"opaque-control":null);
                    entry.Opacity=.5f;MaterialOpticsProfile.Apply(profile);long half=Capture();
                    entry.Opacity=0;MaterialOpticsProfile.Apply(profile);long clear=Capture();
                    if(opaque<100000||half<opaque*.4||half>opaque*.6||clear>100)throw new Exception("Unexpected opacity coverage: "+id+" "+opaque+"/"+half+"/"+clear);
                    report.samples.Add(new Sample{material=((MaterialId)id).ToString(),opaque=opaque,half=half,clear=clear});
                }
                var leaf=new VoxelWorld(16,16,16,1,1);leaf.Set(0,0,0,MaterialId.Leaves);mesher.Build(leaf,new Int3(0,0,0),mesh);entry.Material=MaterialId.Leaves;entry.Opacity=1;entry.Transmission=0;MaterialOpticsProfile.SetAmbient(Color.black,Color.black,Color.black);
                var sun=new GameObject("Material backlight").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1;sun.transform.rotation=Quaternion.Euler(0,180,0);sun.cullingMask=1<<29;sun.shadows=LightShadows.None;
                MaterialOpticsProfile.Apply(profile);report.backlight_dark=Capture("leaf-transmission-off");entry.Transmission=1;MaterialOpticsProfile.Apply(profile);report.backlight_transmitted=Capture("leaf-transmission-on");UnityEngine.Object.DestroyImmediate(sun.gameObject);
                if(report.backlight_transmitted<report.backlight_dark*1.3)throw new Exception("Leaf backlighting did not respond to transmission.");
                var defaults=Resources.Load<MaterialOpticsProfile>("MaterialOptics");if(defaults==null)throw new Exception("Missing default optics profile.");MaterialOpticsProfile.SetAmbient(Color.white,Color.white,Color.white);
                foreach(var id in new[]{MaterialId.Leaves,MaterialId.Ice,MaterialId.Petal,MaterialId.Web})
                {
                    var w=new VoxelWorld(16,16,16,1,1);w.Set(0,0,0,id);mesher.Build(w,new Int3(0,0,0),mesh);
                    entry.Material=id;entry.Opacity=1;entry.Transmission=0;MaterialOpticsProfile.Apply(profile);long opaque=Capture();
                    MaterialOpticsProfile.Apply(defaults);long energy=Capture(id+"-defaults");float ratio=(float)energy/opaque;
                    float expected=id==MaterialId.Leaves?1:id==MaterialId.Ice?.98f:id==MaterialId.Petal?.99f:.65f;
                    if(Mathf.Abs(ratio-expected)>.025f)throw new Exception("Unexpected default coverage: "+id+" "+ratio);
                    if(id==MaterialId.Leaves)
                    {
                        report.leaf_distant=energy;if(energy!=opaque)throw new Exception("Distant default leaves must be completely opaque.");
                        camera.transform.position=new Vector3(.5f,.5f,-.15f);report.leaf_near=Capture("Leaves-near-eye");camera.transform.position=new Vector3(.5f,.5f,-3);
                        if(report.leaf_near<opaque*.92||report.leaf_near>=opaque*.999)throw new Exception("Near-eye leaf fade must be present but no greater than 8%.");
                    }
                    report.defaults.Add(new DefaultSample{material=id.ToString(),coverage=ratio});
                }
                // Use a half-covered front face to detect screen-fixed or animated
                // patterns. With an orthographic camera, one-pixel camera travel
                // must translate surface coverage by exactly one pixel.
                entry.Material=MaterialId.Web;entry.Opacity=.5f;entry.Transmission=0;MaterialOpticsProfile.Apply(profile);
                Capture();var stationary=image.GetPixels32();Capture();var repeated=image.GetPixels32();
                for(int i=0;i<stationary.Length;i++)if(stationary[i].r!=repeated[i].r||stationary[i].g!=repeated[i].g||stationary[i].b!=repeated[i].b)report.stationary_pixel_changes++;
                if(report.stationary_pixel_changes!=0)throw new Exception("Stationary material coverage changed between renders.");
                float pixelMetres=camera.orthographicSize*2/128;camera.transform.position+=new Vector3(pixelMetres,0,0);Capture();var translated=image.GetPixels32();camera.transform.position=new Vector3(.5f,.5f,-3);
                for(int y=25;y<103;y++)for(int x=25;x<103;x++)
                {
                    var a=stationary[y*128+x];var b=translated[y*128+x-1];report.translated_pixels++;
                    if(Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b)>12)report.translated_pixel_changes++;
                }
                if(report.translated_pixel_changes>report.translated_pixels*.02)throw new Exception("Coverage did not stay attached to the surface under camera translation: "+report.translated_pixel_changes+"/"+report.translated_pixels);
                for(int pass=0;pass<material.passCount;pass++)ShaderUtil.CompilePass(material,pass,true);
                if(ShaderUtil.ShaderHasError(material.shader))throw new Exception("Material shader compilation error.");
                File.WriteAllText(folder+"/results.json",JsonUtility.ToJson(report,true));Debug.Log("PASS material optics: 24 materials, manual/default/near-eye GPU coverage, stationary and camera-translated surface stability, directional backlight response.");
            }
            finally
            {
                for(int i=0;i<lights.Length;i++)if(lights[i]!=null)lights[i].intensity=intensities[i];RenderSettings.fog=fog;RenderTexture.active=prior;camera.targetTexture=null;rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(profile);UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(material);
                MaterialOpticsProfile.Apply(Resources.Load<MaterialOpticsProfile>("MaterialOptics"));MaterialOpticsProfile.ClearAmbient();
                Shader.SetGlobalVector("_VoxelWindState",wind);Shader.SetGlobalVector("_VoxelWindPreviousState",priorWind);
            }
        }
    }
}
