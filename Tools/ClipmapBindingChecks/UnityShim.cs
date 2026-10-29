using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public class Object { public string name;public HideFlags hideFlags;public static T Instantiate<T>(T value)where T:new()=>new T();public static void Destroy(Object value){}public static void DestroyImmediate(Object value){} }
    public enum HideFlags{DontSave} public class Texture:Object{}
    public class Shader:Object{public bool isSupported=true;}
    // Model the dangerous whole-property-sheet replacement contract. This shim
    // does not assert what an unobserved Unity native material currently contains.
    public class Material:Object
    {
        readonly Dictionary<string,object> values=new Dictionary<string,object>();
        public string[] shaderKeywords=Array.Empty<string>();public int renderQueue=2000;public static bool RejectTextureWrites;
        public Material(Shader shader){}
        public void SetTexture(string n,Texture value){if(!RejectTextureWrites)values[n]=value;}
        public Texture GetTexture(string n)=>values.TryGetValue(n,out var v)?v as Texture:null;
        public void SetFloat(string n,float value)=>values[n]=value;
        public float GetFloat(string n)=>values.TryGetValue(n,out var v)&&v is float f?f:0;
        public bool HasProperty(string n)=>values.ContainsKey(n);
        public void SetInt(string n,int v){}public void SetVector(string n,Vector4 v){}public void SetVectorArray(string n,Vector4[] v){}
        public void CopyPropertiesFromMaterial(Material source){values.Clear();foreach(var p in source.values)values[p.Key]=p.Value;shaderKeywords=source.shaderKeywords;renderQueue=source.renderQueue;}
    }
    public static class Resources{public static T Load<T>(string path)where T:new()=>new T();}
    public class ComputeShader:Object{public int FindKernel(string n)=>0;public void SetBuffer(int k,string n,ComputeBuffer b){}public void SetTexture(int k,string n,RenderTexture t){}public void SetInt(string n,int v){}public void Dispatch(int k,int x,int y,int z){}}
    public enum RenderTextureFormat{RFloat,ARGB32}public enum RenderTextureReadWrite{Linear}public enum TextureWrapMode{Repeat}public enum FilterMode{Bilinear}
    public class RenderTexture:Texture{public RenderTexture(int w,int h,int d,RenderTextureFormat f,RenderTextureReadWrite r){}public Rendering.TextureDimension dimension;public int volumeDepth;public bool enableRandomWrite,useMipMap,autoGenerateMips;public TextureWrapMode wrapMode;public FilterMode filterMode;public bool Create()=>true;public void Release(){}}
    public enum ComputeBufferType{Structured}public sealed class ComputeBuffer:IDisposable{public ComputeBuffer(int n,int stride,ComputeBufferType t){}public void SetData(Array a,int start,int to,int n){}public void Dispose(){}}
    public class MaterialPropertyBlock{public void Clear(){}public void SetVector(string n,Vector4 v){}}
    public struct Vector4{public float x,y,z,w;public Vector4(float x,float y,float z,float w){this.x=x;this.y=y;this.z=z;this.w=w;}public static Vector4 zero=>new Vector4();}
    public struct Vector3{public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public static Vector3 one=>new Vector3(1,1,1);public static Vector3 operator*(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);}
    public struct Bounds{public Bounds(Vector3 c,Vector3 s){}}
    public struct Rect{public Rect(float x,float y,float w,float h){}}
    public class Transform{public Vector3 position;}
    public class Camera:Object{public Transform transform=new Transform();public float farClipPlane=1000;public enum MonoOrStereoscopicEye{Mono}public void CalculateFrustumCorners(Rect r,float f,MonoOrStereoscopicEye e,Vector3[] c){}}
    public struct RenderParams{public RenderParams(Material m){camera=null;layer=0;matProps=null;worldBounds=default;shadowCastingMode=default;receiveShadows=false;lightProbeUsage=default;}public Camera camera;public int layer;public MaterialPropertyBlock matProps;public Bounds worldBounds;public Rendering.ShadowCastingMode shadowCastingMode;public bool receiveShadows;public Rendering.LightProbeUsage lightProbeUsage;}
    public enum MeshTopology{Triangles}
    public static class Graphics{public static void CopyTexture(Texture a,int a0,int a1,Texture b,int b0,int b1){}public static void RenderPrimitives(RenderParams r,MeshTopology t,int n,int instances){}}
    public static class QualitySettings{public static float shadowDistance=100;}
    public static class Application{public static bool isPlaying=false;}
    public static class SystemInfo{public static bool supportsComputeShaders=true,supports2DArrayTextures=true;public static bool SupportsRenderTextureFormat(RenderTextureFormat f)=>true;public static bool SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat f)=>true;public static Rendering.CopyTextureSupport copyTextureSupport=Rendering.CopyTextureSupport.Basic;}
}
namespace UnityEngine.Rendering
{
    public enum TextureDimension{Tex2DArray}public enum CopyTextureSupport{None,Basic}public enum ShadowCastingMode{On,Off,ShadowsOnly}public enum LightProbeUsage{Off}
}
namespace AshenVoxel.WorldScale
{
    public struct WorldPoint{public double X,Y,Z;}
    public struct ClipmapSample{public float Height;public uint Tint,Appearance;}
    public struct ClipmapRect{public long X,Z;public int Width,Height;}
    public struct ClipmapLevelInfo{public int Level;public double Spacing;public long OriginX,OriginZ;public bool Published;}
    public static class TerrainClipmapMath{public static ClipmapRect[] GetRingRects(ClipmapLevelInfo p,ClipmapLevelInfo? c,int s)=>Array.Empty<ClipmapRect>();}
    public class SurfaceMaterialSampler{public SurfaceMaterialSampler(object a,object b){}}
}
namespace AshenVoxel.WorldScaleRuntime
{
    // The thin forwarding wrapper is the only omitted production component.
    public class TerrainClipmap
    {readonly TerrainClipmapResources resources;public TerrainClipmap(TerrainClipmapResources r){resources=r;}public bool Failed;public UnityEngine.Material Material=>resources.Material;internal void CopySurfaceStyle(UnityEngine.Material m)=>resources.CopySurfaceStyle(m);}
    public sealed partial class WorldExplorer
    {object field=new object(),CurrentSurfaceProfile=new object(),planJob;UnityEngine.Material ownedMaterial;int revision;bool coarseStartup;public WorldExplorer(UnityEngine.Material m){ownedMaterial=m;}object CaptureField(object o)=>o;object BareField(object o)=>o;void ClearTerrain(){}void ForceReplan(){}void RequestPlan(){}}
}
