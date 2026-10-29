using System;
using AshenVoxel.WorldScale;
namespace UnityEngine
{
    public class MonoBehaviour { public bool enabled=true;public GameObject gameObject=new GameObject();protected static void Destroy(object value){} }
    public class GameObject { public int layer; }
    public class Camera
    {public float farClipPlane=100;public enum MonoOrStereoscopicEye{Mono}public void CalculateFrustumCorners(Rect r,float f,MonoOrStereoscopicEye eye,Vector3[] corners){for(int i=0;i<4;i++)corners[i]=new Vector3(f,f,f);}}
    public struct Rect{public Rect(float x,float y,float w,float h){}}
    public struct Vector3{public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public float magnitude=>(float)Math.Sqrt(x*x+y*y+z*z);}
    public struct Vector4{public Vector4(float x,float y,float z,float w){}public static Vector4 zero=>default;}
    public static class Time{static readonly System.Diagnostics.Stopwatch clock=System.Diagnostics.Stopwatch.StartNew();public static double realtimeSinceStartupAsDouble=>clock.Elapsed.TotalSeconds;public static float unscaledDeltaTime=>1f/60;}
    public static class Application{public static bool isEditor=false;public static string dataPath=>"unused";public static string persistentDataPath=>"unused";}
    public class RangeAttribute:Attribute{public RangeAttribute(float a,float b){}}
    public enum TextureFormat{RGBA32}public enum FilterMode{Bilinear}public enum TextureWrapMode{Clamp}public enum HideFlags{DontSave}
    public class Texture2D{public string name;public FilterMode filterMode;public TextureWrapMode wrapMode;public HideFlags hideFlags;public Texture2D(int x,int y,TextureFormat f,bool mip,bool linear){}public void SetPixelData(byte[] data,int mip){}public void Apply(bool mip,bool unreadable){}}
    public class TextAsset{public byte[] bytes;}
    public static class Resources{public static T Load<T>(string name)where T:class=>null;public static void UnloadAsset(object value){}}
    public static class Shader{public static int PropertyToID(string name)=>0;public static void SetGlobalVector(int id,Vector4 value){}public static void SetGlobalTexture(int id,Texture2D value){}}
    public class Material { }
    public class DefaultExecutionOrderAttribute:Attribute { public DefaultExecutionOrderAttribute(int value){} }
    public static class Debug
    { public static void Log(object message,object context=null){} public static void LogWarning(object message,object context=null){Console.WriteLine(message);} }
}
namespace AshenVoxel.WorldScaleRuntime
{
    public sealed class WorldExplorer
    {
        public WorldPoint GlobalCamera,RenderOrigin;
        public UnityEngine.Camera MainCamera=new UnityEngine.Camera();
        public TerrainClipmap Clipmap;
        public bool UsingClipmap=>Clipmap!=null;
        public bool LegacyLocalReady,LegacyDistantReady;
        public bool HasActiveCoverage=>UsingClipmap?Clipmap.HasCoverage:LegacyDistantReady;
        public bool IsTerrainReadyArea(double x,double z,double radius)=>UsingClipmap?Clipmap.IsReadyArea(x,z,radius):LegacyLocalReady;
        public readonly SurfaceMaterialSampler Sampler=new SurfaceMaterialSampler(new FlatField());
        public string ClipmapProfileIdentity=>Sampler.Profile.Identity;
        public bool FailCapture;
        public SurfaceMaterialSampler CaptureClipmapSampler()=>FailCapture?throw new InvalidOperationException("Injected optional sampler failure"):Sampler;
        public void AttachClipmap(TerrainClipmap value){Clipmap=value;}
        public void DetachClipmap(TerrainClipmap value){if(Clipmap==value)Clipmap=null;}
        sealed class FlatField:IWorldField
        {public string SourceIdentity=>"loading-lifecycle-flat-v1";public SurfaceSample Sample(double x,double z)=>new SurfaceSample(10,6,6,true);}
    }
    // Modeled graphics boundary only. Production worker, state, sampler and
    // publication lifecycle are compiled unchanged by run_checks.py.
    public sealed class TerrainClipmapResources:IDisposable
    {
        public static TerrainClipmapResources Last;
        public UnityEngine.Material Material=new UnityEngine.Material();
        public long TrianglesLastDraw,GpuBytes,CpuStagingBytes;
        public int DrawCallsLastFrame,Publications,DrawInvocations;
        public bool ThrowOnDraw;
        int expected,uploaded;bool staging;
        public TerrainClipmapResources(int size,int levels){Last=this;}
        public void CopySurfaceStyle(UnityEngine.Material source){}
        public void BeginUpdate(int level,bool copyPublished,int expectedSamples)
        {if(staging)throw new Exception("duplicate staging");staging=true;expected=expectedSamples;uploaded=0;}
        public void Upload(int level,long x,long z,ClipmapRect rect,ClipmapSample[] samples)
        {if(!staging)throw new Exception("upload without staging");uploaded+=rect.Count;}
        public void Publish(int level)
        {if(!staging||uploaded!=expected)throw new Exception("incomplete publication");staging=false;Publications++;}
        public void CancelUpdate(){staging=false;}
        public void ResetDrawCounters(){DrawCallsLastFrame=0;TrianglesLastDraw=0;}
        public void Draw(UnityEngine.Camera camera,int layer,WorldPoint origin,ClipmapLevelInfo[] visible)
        {if(ThrowOnDraw)throw new InvalidOperationException("Injected first draw failure");DrawInvocations++;ResetDrawCounters();if(visible.Length>0){DrawCallsLastFrame=1;TrianglesLastDraw=12;}}
        public void Dispose(){ResetDrawCounters();}
        public bool CanPublishFarAppearance=>false;
        public bool FarAppearancePublished=>false;
        public void AdvanceFarAppearance(double dt){}
        public void BeginFarAppearance(FarAppearanceWindow w,bool copy,int count){}
        public void UploadFarAppearance(FarAppearanceWindow w,ClipmapRect r,FarAppearanceSample[] samples){}
        public void PublishFarAppearance(FarAppearanceWindow w){}
        public void CancelFarAppearance(){}
        public void DisableFarAppearance(){}
    }
}
