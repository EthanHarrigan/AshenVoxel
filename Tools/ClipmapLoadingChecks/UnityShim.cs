using System;
using AshenVoxel.WorldScale;
namespace UnityEngine
{
    public class MonoBehaviour { public bool enabled=true;public GameObject gameObject=new GameObject(); }
    public class GameObject { public int layer; }
    public class Camera { }
    public class Material { }
    public class DefaultExecutionOrderAttribute:Attribute { public DefaultExecutionOrderAttribute(int value){} }
    public static class Debug
    { public static void Log(object message,object context){} public static void LogWarning(object message,object context){Console.WriteLine(message);} }
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
        public SurfaceMaterialSampler CaptureClipmapSampler()=>Sampler;
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
    }
}
