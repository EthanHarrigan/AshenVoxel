using System;
using System.IO;
using System.Collections.Generic;
using AshenVoxel.WorldScale;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AshenVoxel.Exploration.Editor
{
    public static class ForestAtlasBaker
    {
        [MenuItem("Ashen Voxel/Exploration/Bake distant forest atlas")]
        public static void Bake()
        {
            const int tile=256,side=tile*12;
            var atlas=new Texture2D(side,side,TextureFormat.RGBA32,false);
            var empty=new Color32[side*side];atlas.SetPixels32(empty);
            var normalAtlas=new Texture2D(side,side,TextureFormat.RGBA32,false,true);normalAtlas.SetPixels32(empty);
            var root=new GameObject("Temporary forest atlas bake");var cameraObject=new GameObject("Atlas camera");
            var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;
            camera.nearClipPlane=.1f;camera.farClipPlane=1000;camera.cullingMask=1<<30;camera.allowHDR=false;camera.allowMSAA=false;
            var material=new Material(Shader.Find("Hidden/AshenVoxel/ForestAtlasBake"));
            var rt=new RenderTexture(tile,tile,24,RenderTextureFormat.ARGB32);camera.targetTexture=rt;
            var normalRt=new RenderTexture(tile,tile,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            var previous=RenderTexture.active;
            try
            {
                using(var reader=new ExplorationPrototypeReader(File.ReadAllBytes("Assets/AshenVoxel/Exploration/Prototypes/ExplorationPrototypes.bytes")))
                while(reader.TryRead(out ExplorationPrototype data))
                {
                    if(data.Kind!=0){data.ReleaseMeshData();continue;}
                    var source=data.Mesh(0);var vertices=new Vector3[source.VertexCount];var normals=new Vector3[source.VertexCount];var colors=new Color32[source.VertexCount];
                    for(int i=0;i<vertices.Length;i++)
                    {
                        vertices[i]=new Vector3(source.Positions[i*3]*.1f,source.Positions[i*3+1]*.1f,source.Positions[i*3+2]*.1f);
                        int n=source.Normals[i];normals[i]=n==0?Vector3.right:n==1?Vector3.left:n==2?Vector3.up:n==3?Vector3.down:n==4?Vector3.forward:Vector3.back;
                        uint c=source.Colors[i];colors[i]=new Color32((byte)c,(byte)(c>>8),(byte)(c>>16),(byte)(c>>24));
                    }
                    var mesh=new Mesh{indexFormat=IndexFormat.UInt32,vertices=vertices,normals=normals,colors32=colors};mesh.SetTriangles(source.Indices,0);mesh.RecalculateBounds();
                    var instance=new GameObject(data.Name);instance.layer=30;instance.transform.SetParent(root.transform);instance.AddComponent<MeshFilter>().sharedMesh=mesh;instance.AddComponent<MeshRenderer>().sharedMaterial=material;
                    float span=ForestBillboard.Span(data),center=(data.MinY+data.MaxY)*.5f;camera.orthographicSize=span*.5f;
                    for(int view=0;view<9;view++)
                    {
                        Vector3 target=new Vector3(0,center,0);float angle=view*Mathf.PI/4;
                        camera.transform.position=target+(view==8?Vector3.up:new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle)))*span*2;
                        camera.transform.LookAt(target,view==8?Vector3.forward:Vector3.up);
                        material.SetFloat("_BakeNormals",0);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;int index=data.Id*9+view;
                        atlas.ReadPixels(new Rect(0,0,tile,tile),index%12*tile,index/12*tile,false);
                        material.SetFloat("_BakeNormals",1);camera.targetTexture=normalRt;camera.Render();RenderTexture.active=normalRt;
                        normalAtlas.ReadPixels(new Rect(0,0,tile,tile),index%12*tile,index/12*tile,false);
                    }
                    UnityEngine.Object.DestroyImmediate(instance);UnityEngine.Object.DestroyImmediate(mesh);data.ReleaseMeshData();Debug.Log("Captured forest prototype "+data.Id);
                }
                atlas.Apply();normalAtlas.Apply();
                int visible=0;foreach(var pixel in atlas.GetPixels32())if(pixel.a>128)visible++;if(visible<10000)throw new InvalidOperationException("Atlas capture is blank; verify the active URP pipeline before baking.");
                string folder="Assets/AshenVoxel/Exploration/Resources";Directory.CreateDirectory(folder);
                string path=folder+"/ForestAtlas.png";File.WriteAllBytes(path,atlas.EncodeToPNG());AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Default;importer.sRGBTexture=true;importer.alphaSource=TextureImporterAlphaSource.FromInput;importer.alphaIsTransparency=true;importer.mipmapEnabled=true;importer.mipMapsPreserveCoverage=true;importer.alphaTestReferenceValue=.38f;importer.wrapMode=TextureWrapMode.Clamp;importer.maxTextureSize=4096;importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.SaveAndReimport();
                string normalPath=folder+"/ForestAtlasNormals.png";File.WriteAllBytes(normalPath,normalAtlas.EncodeToPNG());AssetDatabase.ImportAsset(normalPath,ImportAssetOptions.ForceSynchronousImport);
                var normalImporter=(TextureImporter)AssetImporter.GetAtPath(normalPath);normalImporter.textureType=TextureImporterType.Default;normalImporter.sRGBTexture=false;normalImporter.alphaSource=TextureImporterAlphaSource.FromInput;normalImporter.alphaIsTransparency=false;normalImporter.mipmapEnabled=true;normalImporter.wrapMode=TextureWrapMode.Clamp;normalImporter.maxTextureSize=4096;normalImporter.textureCompression=TextureImporterCompression.Uncompressed;normalImporter.SaveAndReimport();
                var forest=new Material(Shader.Find("AshenVoxel/ForestImpostor")){enableInstancing=true};forest.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);forest.SetTexture("_NormalMap",AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));forest.SetFloat("_AtlasLightingVersion",3);
                string mat=folder+"/ForestImpostor.mat";var existing=AssetDatabase.LoadAssetAtPath<Material>(mat);
                if(existing==null)AssetDatabase.CreateAsset(forest,mat);else{EditorUtility.CopySerialized(forest,existing);UnityEngine.Object.DestroyImmediate(forest);EditorUtility.SetDirty(existing);}
                File.WriteAllText(folder+"/ForestAtlasVersion.txt","forest-impostor-v3: sixteen original 10cm prototypes; eight azimuths plus overhead; unlit sRGB albedo + linear object normals + point-sampled material/AO byte; live URP lighting\n");
                AssetDatabase.ImportAsset(folder+"/ForestAtlasVersion.txt");
                AssetDatabase.SaveAssets();Debug.Log("Forest atlas baked: 16 actual 10cm prototypes x 9 views; 3072 square.");
            }
            finally{RenderTexture.active=previous;camera.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(atlas);normalRt.Release();UnityEngine.Object.DestroyImmediate(normalRt);UnityEngine.Object.DestroyImmediate(normalAtlas);UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(cameraObject);}
        }
    }
}
