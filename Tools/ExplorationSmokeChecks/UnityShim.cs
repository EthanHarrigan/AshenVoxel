// Numerical/ownership substitutes for a headless bootstrap smoke test.
// No Unity engine, GPU, native input, serialized asset import or shader is run.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.Json;
namespace UnityEngine
{
 public class Object
 {
  public string name;public bool WasDestroyed;public bool Destroyed=>WasDestroyed;
  public static readonly List<Object> Created=new List<Object>();
  public Object(){Created.Add(this);}
  public static T Instantiate<T>(T value)where T:Object
  {if(value==null)return null;var clone=(T)typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(value,null);clone.WasDestroyed=false;Created.Add(clone);return clone;}
  public static void Destroy(Object x)
  {
   if(x==null||x.WasDestroyed)return;x.WasDestroyed=true;
   if(x is GameObject go)
   {
    foreach(var c in go.Components.ToArray()){Call(c,"OnDisable");Call(c,"OnDestroy");c.WasDestroyed=true;}
    foreach(var child in go.transform.Children.ToArray())Destroy(child.Owner);
   }
  }
  static void Call(object x,string method){x.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)?.Invoke(x,null);}
 }
 public class Component:Object
 {
  public GameObject gameObject;public Transform transform;
  public T GetComponent<T>()where T:Component=>gameObject.GetComponent<T>();
 }
 public class MonoBehaviour:Component
 {
  public Coroutine StartCoroutine(IEnumerator routine)=>new Coroutine(routine);
  public void StopCoroutine(Coroutine routine){if(routine!=null)routine.Stopped=true;}
 }
 public sealed class Coroutine{public readonly IEnumerator Work;public bool Stopped;public Coroutine(IEnumerator work){Work=work;}}
 public class GameObject:Object
 {
  public readonly Transform transform;public string tag;public int layer;public bool activeSelf=true;
  public readonly List<Component> Components=new List<Component>();
  public GameObject(string name=""){this.name=name;transform=new Transform(this);}
  public T AddComponent<T>()where T:Component,new(){var c=new T{gameObject=this,transform=transform};Components.Add(c);return c;}
  public T GetComponent<T>()where T:Component{foreach(var c in Components)if(c is T t)return t;return null;}
  public void SetActive(bool value){activeSelf=value;}
 }
 public sealed class Transform
 {
  public readonly GameObject Owner;public readonly List<Transform> Children=new List<Transform>();Transform parent;
  public Vector3 position;public Quaternion rotation;public Vector3 forward=>rotation*Vector3.forward;
  public Transform(GameObject owner){Owner=owner;}
  public void SetParent(Transform value,bool worldPositionStays){parent?.Children.Remove(this);parent=value;parent?.Children.Add(this);}
 }
 public class ScriptableObject:Object{public static T CreateInstance<T>()where T:ScriptableObject,new()=>new T();}
 public sealed class RangeAttribute:Attribute{public RangeAttribute(float a,float b){}}
 public sealed class HeaderAttribute:Attribute{public HeaderAttribute(string a){}}
 public sealed class TooltipAttribute:Attribute{public TooltipAttribute(string a){}}
 public sealed class CreateAssetMenuAttribute:Attribute{public string menuName,fileName;}
 public sealed class DefaultExecutionOrder:Attribute{public readonly int Order;public DefaultExecutionOrder(int order){Order=order;}}
 public class TextAsset:Object{public byte[] bytes;public string text=>Encoding.UTF8.GetString(bytes);}
 public static class JsonUtility
 {
  static readonly JsonSerializerOptions Options=new JsonSerializerOptions{IncludeFields=true};
  public static T FromJson<T>(string text)=>JsonSerializer.Deserialize<T>(text,Options);
  public static string ToJson(object value,bool pretty=false)=>JsonSerializer.Serialize(value,value.GetType(),new JsonSerializerOptions{IncludeFields=true,WriteIndented=pretty});
 }
 public struct Vector2{public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}}
 public struct Vector4{public float x,y,z,w;public Vector4(float x,float y,float z,float w){this.x=x;this.y=y;this.z=z;this.w=w;}}
 public struct Vector3
 {
  public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
  public Vector3 normalized{get{double l=Math.Sqrt(x*x+y*y+z*z);return l<1e-20?zero:new Vector3((float)(x/l),(float)(y/l),(float)(z/l));}}
  public static Vector3 one=>new Vector3(1,1,1);public static Vector3 zero=>default;public static Vector3 right=>new Vector3(1,0,0);public static Vector3 left=>new Vector3(-1,0,0);public static Vector3 up=>new Vector3(0,1,0);public static Vector3 down=>new Vector3(0,-1,0);public static Vector3 forward=>new Vector3(0,0,1);public static Vector3 back=>new Vector3(0,0,-1);
  public static Vector3 operator+(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);public static Vector3 operator-(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);public static Vector3 operator*(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);
 }
 public struct Quaternion
 {
  public float x,y,z;
  public static Quaternion Euler(float x,float y,float z)=>new Quaternion{x=x,y=y,z=z};
  public static Vector3 operator*(Quaternion q,Vector3 v)
  {double rx=q.x*Math.PI/180,ry=q.y*Math.PI/180,rz=q.z*Math.PI/180;double a=v.x*Math.Cos(rz)-v.y*Math.Sin(rz),b=v.x*Math.Sin(rz)+v.y*Math.Cos(rz),c=v.z;double by=b*Math.Cos(rx)-c*Math.Sin(rx),cz=b*Math.Sin(rx)+c*Math.Cos(rx);return new Vector3((float)(a*Math.Cos(ry)+cz*Math.Sin(ry)),(float)by,(float)(-a*Math.Sin(ry)+cz*Math.Cos(ry)));}
 }
 public struct Matrix4x4
 {
  public float m00,m01,m02,m03,m10,m11,m12,m13,m20,m21,m22,m23,m30,m31,m32,m33;
  public static Matrix4x4 TRS(Vector3 p,Quaternion q,Vector3 s){double a=q.y*Math.PI/180;return new Matrix4x4{m00=(float)Math.Cos(a)*s.x,m02=(float)Math.Sin(a)*s.z,m11=s.y,m20=-(float)Math.Sin(a)*s.x,m22=(float)Math.Cos(a)*s.z,m03=p.x,m13=p.y,m23=p.z,m33=1};}
 }
 public struct Bounds
 {
  public Vector3 center,size;public Bounds(Vector3 c,Vector3 s){center=c;size=s;}public Vector3 min=>center-size*.5f;public Vector3 max=>center+size*.5f;
  public void Encapsulate(Bounds b){Vector3 a0=min,a1=max,b0=b.min,b1=b.max;Vector3 lo=new Vector3(Math.Min(a0.x,b0.x),Math.Min(a0.y,b0.y),Math.Min(a0.z,b0.z)),hi=new Vector3(Math.Max(a1.x,b1.x),Math.Max(a1.y,b1.y),Math.Max(a1.z,b1.z));center=(lo+hi)*.5f;size=hi-lo;}
 }
 public struct Color
 {
  public float r,g,b,a;public Color(float r,float g,float b,float a=1){this.r=r;this.g=g;this.b=b;this.a=a;}
  public static implicit operator Color(Color32 c)=>new Color(c.r/255f,c.g/255f,c.b/255f,c.a/255f);
  public static implicit operator Color32(Color c)=>new Color32((byte)Math.Round(Math.Clamp(c.r,0,1)*255),(byte)Math.Round(Math.Clamp(c.g,0,1)*255),(byte)Math.Round(Math.Clamp(c.b,0,1)*255),(byte)Math.Round(Math.Clamp(c.a,0,1)*255));
 }
 public struct Color32{public byte r,g,b,a;public Color32(byte r,byte g,byte b,byte a){this.r=r;this.g=g;this.b=b;this.a=a;}}
 public static class Mathf
 {
  public static float Clamp(float v,float a,float b)=>Math.Clamp(v,a,b);public static int Clamp(int v,int a,int b)=>Math.Clamp(v,a,b);public static float Min(float a,float b)=>Math.Min(a,b);public static float Max(float a,float b)=>Math.Max(a,b);public static int Max(int a,int b)=>Math.Max(a,b);public static int CeilToInt(float v)=>(int)Math.Ceiling(v);public static int FloorToInt(float v)=>(int)Math.Floor(v);
 }
 public class Shader:Object{public static Shader Find(string name)=>new Shader{name=name};}
 public class Material:Object
 {
  public bool enableInstancing;public readonly Material Template;public readonly Dictionary<string,float> Floats=new Dictionary<string,float>();public int VectorWrites;
  public Vector4 LastVector;public Vector4[] LastVectorArray;
  public void SetVectorArray(string key,Vector4[] value){LastVectorArray=(Vector4[])value.Clone();}
  public Material(){}public Material(Material t){Template=t;enableInstancing=t!=null&&t.enableInstancing;}public Material(Shader shader){}
  public void SetFloat(string key,float v){Floats[key]=v;}public void SetVector(string key,Vector4 v){VectorWrites++;LastVector=v;}public void SetColor(string key,Color v){}
 }
 public static class Resources
 {
  public static readonly Dictionary<string,Material> Materials=new Dictionary<string,Material>{{"VoxelSurface",new Material{name="Voxel source"}},{"GroundSurface",new Material{name="Ground source"}},{"VisualWater",new Material{name="Water source"}}};
  public static T Load<T>(string path)where T:class=>Materials.TryGetValue(path,out Material m)?m as T:null;
 }
 public class Mesh:Object
 {
  public void SetVertexBufferParams(int count,params UnityEngine.Rendering.VertexAttributeDescriptor[] attributes){VertexCount=NormalCount=ColorCount=UvCount=count;}
  public void SetVertexBufferData<T>(T[] data,int source,int dest,int count) where T:struct {if(System.Runtime.InteropServices.Marshal.SizeOf<T>()!=24)throw new Exception("Packed voxel stride must be24");}
  public void SetIndexBufferParams(int count,UnityEngine.Rendering.IndexFormat format){TriangleIndices=count;indexFormat=format;}
  public void SetIndexBufferData<T>(T[] data,int source,int dest,int count) where T:struct {for(int i=0;i<count;i++){int v=(int)(object)data[source+i];if(v<0||v>=VertexCount)throw new Exception("Invalid voxel index");}}
  public void SetSubMesh(int index,UnityEngine.Rendering.SubMeshDescriptor desc,UnityEngine.Rendering.MeshUpdateFlags flags){bounds=desc.bounds;}

  public UnityEngine.Rendering.IndexFormat indexFormat;public Bounds bounds;public int VertexCount,TriangleIndices,NormalCount,ColorCount,UvCount;public bool Uploaded,CpuReadable=true;
  public Vector3[] vertices{set{VertexCount=value.Length;SetBounds(value);}}public Vector3[] normals{set{NormalCount=value.Length;}}public Color32[] colors32{set{ColorCount=value.Length;}}
  public void SetVertices(List<Vector3> value){VertexCount=value.Count;SetBounds(value);}public void SetNormals(List<Vector3> v){NormalCount=v.Count;}public void SetColors(List<Color32> v){ColorCount=v.Count;}public void SetUVs(int channel,List<Vector4> v){UvCount=v.Count;}
  public void SetTriangles(List<int> t,int submesh){TriangleIndices=t.Count;}public void SetTriangles(int[] t,int submesh,bool recalculate){TriangleIndices=t.Length;}
  public void RecalculateBounds(){}public void UploadMeshData(bool nonReadable){Uploaded=true;CpuReadable=!nonReadable;}
  void SetBounds(IList<Vector3> v){if(v.Count==0){bounds=default;return;}Vector3 lo=v[0],hi=lo;for(int i=1;i<v.Count;i++){Vector3 a=v[i];lo=new Vector3(Math.Min(lo.x,a.x),Math.Min(lo.y,a.y),Math.Min(lo.z,a.z));hi=new Vector3(Math.Max(hi.x,a.x),Math.Max(hi.y,a.y),Math.Max(hi.z,a.z));}bounds=new Bounds((lo+hi)*.5f,hi-lo);}
 }
 public class Renderer:Component{public Material sharedMaterial;}
 public class MeshRenderer:Renderer{}
 public class MeshFilter:Component{public Mesh sharedMesh;}
 public enum CameraClearFlags{SolidColor,Skybox}
 public class Camera:Component{public int pixelHeight=>Screen.height;public float aspect=>Screen.width/(float)Screen.height;public string tag;public CameraClearFlags clearFlags;public Color backgroundColor;public float nearClipPlane,farClipPlane,fieldOfView;public bool allowHDR;}
 public class AudioListener:Component{}
 public enum LightType{Directional}public enum LightShadows{Soft}
 public class Light:Component{public LightType type;public LightShadows shadows;public Color color;public float intensity,shadowBias,shadowNormalBias,shadowNearPlane;}
 public enum FogMode{Linear=1,Exponential=2,ExponentialSquared=3}
 public static class RenderSettings{public static Material skybox;public static Light sun;public static bool fog;public static FogMode fogMode;public static float fogDensity,fogStartDistance,fogEndDistance;public static Color fogColor,ambientSkyColor,ambientEquatorColor,ambientGroundColor;public static UnityEngine.Rendering.AmbientMode ambientMode;}
 public static class QualitySettings{public static float shadowDistance=100;public static UnityEngine.Rendering.RenderPipelineAsset renderPipeline;}
 public struct Plane{}
 public static class GeometryUtility{public static void CalculateFrustumPlanes(Camera c,Plane[] p){}public static bool TestPlanesAABB(Plane[] p,Bounds b)=>true;}
 public struct RenderParams
 {
  public Material material;public Camera camera;public Bounds worldBounds;public UnityEngine.Rendering.ShadowCastingMode shadowCastingMode;public bool receiveShadows;public int layer;public UnityEngine.Rendering.LightProbeUsage lightProbeUsage;
  public RenderParams(Material m){material=m;camera=null;worldBounds=default;shadowCastingMode=default;receiveShadows=false;layer=0;lightProbeUsage=default;}
 }
 public static class Graphics
 {
  public static int Calls,Instances;
  public static void RenderMeshInstanced(RenderParams p,Mesh m,int submesh,Matrix4x4[] matrices,int count)
  {if(!p.material.enableInstancing||count<1||count>1023||count>matrices.Length||m.WasDestroyed)throw new Exception("Invalid instancing contract");Calls++;Instances+=count;}
 }
 public static class Time{public static float unscaledTime,unscaledDeltaTime=1f/60,deltaTime=1f/60;public static double unscaledTimeAsDouble=>unscaledTime;}
 public static class Application{public static bool isPlaying=true;public static string persistentDataPath;}
 public static class Screen{public static int width=1440,height=900;}
 public static class Debug{public static readonly List<Exception> Exceptions=new List<Exception>();public static void Log(string text,Object c=null){}public static void LogWarning(string text,Object c=null){}public static void LogException(Exception e,Object c=null){Exceptions.Add(e);Console.WriteLine("RUNTIME_EXCEPTION "+e);}}
 public enum CursorLockMode{None,Locked}public static class Cursor{public static CursorLockMode lockState;public static bool visible;}
 public enum FontStyle{Normal,Bold}
 public class GUIStyle{public int fontSize;public bool wordWrap;public FontStyle fontStyle;public GUIStyle(){}public GUIStyle(GUIStyle s){if(s!=null){fontSize=s.fontSize;wordWrap=s.wordWrap;fontStyle=s.fontStyle;}}}
 public sealed class GUISkin{public GUIStyle label=new GUIStyle(),box=new GUIStyle();}
 public sealed class GUIContent{public static GUIContent none=new GUIContent();}
 public struct Rect{public Rect(float x,float y,float w,float h){}}
 public class Texture2D:Object{public static Texture2D whiteTexture=new Texture2D();}
 public static class GUI{public static GUISkin skin=new GUISkin();public static Color color;public static int depth;public static void Box(Rect r,GUIContent t){}public static void Label(Rect r,string t,GUIStyle s){}public static void DrawTexture(Rect r,Texture2D t){}}
 public static class GUILayout
 {
  public static void BeginArea(Rect r,GUIStyle s){}public static void EndArea(){}public static void Label(string t,GUIStyle s){}public static bool Button(string t)=>false;
  public static Vector2 BeginScrollView(Vector2 v)=>v;public static void EndScrollView(){}public static bool Toggle(bool v,string t)=>v;public static float HorizontalSlider(float v,float a,float b)=>Math.Clamp(v,a,b);
 }
}
namespace UnityEngine.Rendering
{
 public enum AmbientMode{Trilight}public enum IndexFormat{UInt16,UInt32}public enum ShadowCastingMode{Off,On,ShadowsOnly}public enum LightProbeUsage{Off,BlendProbes}
 public class RenderPipelineAsset:UnityEngine.ScriptableObject{}
 public sealed class TestRenderPipeline:RenderPipelineAsset{public float shadowDistance{get;set;}=100;}
 public static class GraphicsSettings{public static RenderPipelineAsset defaultRenderPipeline;public static RenderPipelineAsset currentRenderPipeline=>UnityEngine.QualitySettings.renderPipeline??defaultRenderPipeline;}
}
namespace UnityEngine.InputSystem
{
 public class ButtonControl{public bool wasPressedThisFrame,isPressed;}
 public sealed class VectorControl{public UnityEngine.Vector2 value;public UnityEngine.Vector2 ReadValue()=>value;}
 public sealed class Keyboard
 {
  public static Keyboard current;public ButtonControl gKey=new ButtonControl(),homeKey=new ButtonControl(),rKey=new ButtonControl(),pKey=new ButtonControl(),dKey=new ButtonControl(),aKey=new ButtonControl(),eKey=new ButtonControl(),spaceKey=new ButtonControl(),qKey=new ButtonControl(),leftCtrlKey=new ButtonControl(),wKey=new ButtonControl(),sKey=new ButtonControl(),leftShiftKey=new ButtonControl(),fKey=new ButtonControl(),nKey=new ButtonControl(),escapeKey=new ButtonControl(),f1Key=new ButtonControl(),hKey=new ButtonControl();
 }
 public sealed class Mouse{public static Mouse current;public ButtonControl rightButton=new ButtonControl(),middleButton=new ButtonControl(),leftButton=new ButtonControl();public VectorControl delta=new VectorControl(),scroll=new VectorControl();}
}
namespace AshenVoxel.Rendering
{
 public sealed class ChunkWorldRenderer:UnityEngine.MonoBehaviour{public UnityEngine.Material SurfaceMaterial;public int PendingChunks=>0;public void Initialize(AshenVoxel.Core.VoxelWorld world){}public void RebuildBudget(int count){}}
}

namespace UnityEngine.Rendering
{
 public enum VertexAttribute {Position,Normal,Color,TexCoord0}
 public enum VertexAttributeFormat {Float32,SNorm8,UNorm8}
 public enum MeshUpdateFlags {Default,DontRecalculateBounds}
 public struct VertexAttributeDescriptor {public VertexAttributeDescriptor(VertexAttribute a,VertexAttributeFormat f,int d){}}
 public struct SubMeshDescriptor {public UnityEngine.Bounds bounds;public int vertexCount;public SubMeshDescriptor(int start,int count){bounds=default;vertexCount=0;}}
}
