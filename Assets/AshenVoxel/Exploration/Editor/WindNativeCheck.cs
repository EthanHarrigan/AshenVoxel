using System;
using System.IO;
using AshenVoxel.Rendering;
using UnityEditor;
using UnityEngine;

namespace AshenVoxel.Exploration.Editor
{
    /// <summary>Isolated native shader/pixel fixture; not a full-scene FPS or
    /// temporal upscaler quality test. Invoke only in the idle validation copy.</summary>
    public static class WindNativeCheck
    {
        [Serializable] sealed class Result
        {
            public string status="PASS",gpu;
            public int leaf_changed_pixels,wood_changed_pixels,stone_changed_pixels,root_changed_pixels,frozen_changed_pixels;
            public int compiled_passes;
            public string limitation="Motion pass compiled; actual DLAA/DLSS history/velocity pixels and play feel require scene acceptance.";
        }
        public static void Test()
        {
            const string folder="Logs/Wind";Directory.CreateDirectory(folder);
            var report=new Result{gpu=SystemInfo.graphicsDeviceName};
            string[] globals={"_VoxelWindTime","_VoxelWindPreviousTime","_VoxelWindState","_VoxelWindPreviousState","_VoxelWindOriginDelta"};
            var saved=new Vector4[globals.Length];for(int i=0;i<globals.Length;i++)saved[i]=Shader.GetGlobalVector(globals[i]);
            var go=new GameObject("Wind native fixture");go.layer=29;
            var mesh=new Mesh();go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var material=new Material(Shader.Find("AshenVoxel/WorldSurface"));go.AddComponent<MeshRenderer>().sharedMaterial=material;
            var cameraObject=new GameObject("Wind fixture camera");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;
            camera.orthographic=true;camera.orthographicSize=1.6f;camera.nearClipPlane=.1f;camera.farClipPlane=50;camera.cullingMask=1<<29;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.allowHDR=false;camera.allowMSAA=false;
            var rt=new RenderTexture(256,256,24);camera.targetTexture=rt;
            var image=new Texture2D(256,256,TextureFormat.RGB24,false);var previousTarget=RenderTexture.active;
            var lights=UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);var intensities=new float[lights.Length];
            for(int i=0;i<lights.Length;i++){intensities[i]=lights[i].intensity;lights[i].intensity=0;}
            bool fog=RenderSettings.fog;RenderSettings.fog=false;MaterialOpticsProfile.SetAmbient(Color.white,Color.white,Color.white);
            var optics=ScriptableObject.CreateInstance<MaterialOpticsProfile>();optics.Materials=new MaterialOpticsProfile.Entry[0];MaterialOpticsProfile.Apply(optics);
            Color32[] Capture(string name=null)
            {
                camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,256,256),0,0);image.Apply();
                if(name!=null)File.WriteAllBytes(folder+"/"+name+".png",image.EncodeToPNG());return image.GetPixels32();
            }
            void Pose(uint id,bool root=false)
            {
                mesh.Clear();
                if(root)
                {
                    mesh.vertices=new[]{new Vector3(-.7f,0,-.7f),new Vector3(-.7f,0,.7f),new Vector3(.7f,0,.7f),new Vector3(.7f,0,-.7f)};
                    mesh.normals=new[]{Vector3.up,Vector3.up,Vector3.up,Vector3.up};camera.transform.position=new Vector3(0,4,-8);camera.transform.LookAt(Vector3.zero);
                }
                else
                {
                    mesh.vertices=new[]{new Vector3(-.5f,8,0),new Vector3(-.5f,9,0),new Vector3(.5f,9,0),new Vector3(.5f,8,0)};
                    mesh.normals=new[]{Vector3.back,Vector3.back,Vector3.back,Vector3.back};camera.transform.position=new Vector3(0,8.5f,-20);camera.transform.rotation=Quaternion.identity;
                }
                var color=new Color32(150,200,125,(byte)((id<<2)|3u));mesh.colors32=new[]{color,color,color,color};
                mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateBounds();var bounds=mesh.bounds;bounds.Expand(4);mesh.bounds=bounds;
            }
            void Wind(bool enabled)
            {
                Shader.SetGlobalVector("_VoxelWindState",new Vector4(enabled?1:0,.4f,0,0));
                Shader.SetGlobalVector("_VoxelWindTime",new Vector4(1.1f,.7f,2.3f,0));
            }
            try
            {
                Pose(7);Wind(false);var still=Capture("leaf-still");Wind(true);var moving=Capture("leaf-wind");
                report.leaf_changed_pixels=Difference(still,moving);report.frozen_changed_pixels=Difference(moving,Capture());
                Pose(6);Wind(false);still=Capture();Wind(true);report.wood_changed_pixels=Difference(still,Capture("wood-wind"));
                Pose(1);Wind(false);still=Capture();Wind(true);report.stone_changed_pixels=Difference(still,Capture());
                Pose(7,true);Wind(false);still=Capture();Wind(true);report.root_changed_pixels=Difference(still,Capture("fixed-root"));
                if(report.leaf_changed_pixels<400||report.wood_changed_pixels<400)throw new Exception("Canopy/branch displacement was not visible in native pixels.");
                if(report.frozen_changed_pixels!=0||report.stone_changed_pixels!=0||report.root_changed_pixels!=0)throw new Exception("Wind violated fixed-time, material, or root invariance.");
                if(material.FindPass("MotionVectors")<0)throw new Exception("Missing motion vector pass.");
                for(int i=0;i<material.passCount;i++){ShaderUtil.CompilePass(material,i,true);report.compiled_passes++;}
                if(ShaderUtil.ShaderHasError(material.shader))throw new Exception("Native wind shader compilation failed.");
                File.WriteAllText(folder+"/results.json",JsonUtility.ToJson(report,true));
                Debug.Log("PASS native wind pixels: visible leaves/wood, fixed roots/stone/time and all five shader passes compiled. DLAA/DLSS motion history acceptance remains separate.");
            }
            finally
            {
                for(int i=0;i<globals.Length;i++)Shader.SetGlobalVector(globals[i],saved[i]);
                for(int i=0;i<lights.Length;i++)if(lights[i]!=null)lights[i].intensity=intensities[i];RenderSettings.fog=fog;
                RenderTexture.active=previousTarget;camera.targetTexture=null;rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(optics);
                UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(material);
                MaterialOpticsProfile.Apply(Resources.Load<MaterialOpticsProfile>("MaterialOptics"));MaterialOpticsProfile.ClearAmbient();
            }
        }
        static int Difference(Color32[] a,Color32[] b)
        {
            int changed=0;for(int i=0;i<a.Length;i++)if(Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b)>6)changed++;return changed;
        }
    }
}
