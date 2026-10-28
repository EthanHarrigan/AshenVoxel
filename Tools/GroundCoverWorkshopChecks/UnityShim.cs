// Compile-only authoring API substitutes. These do not test Unity behavior or renderer integration.
using System;
using System.Collections;
using AshenVoxel.Core;
namespace UnityEngine
{
 public static class JsonUtility { public static T FromJson<T>(string text)=>System.Text.Json.JsonSerializer.Deserialize<T>(text,new System.Text.Json.JsonSerializerOptions { IncludeFields=true }); }
    public class Object { public string name; public bool WasDestroyed; public static int Destroyed; public static void Destroy(Object value) { Destroyed++; if(value!=null)value.WasDestroyed=true; } }
    public class Component : Object
    {
        public Transform transform = new Transform(); public GameObject gameObject;
    }
    public class MonoBehaviour : Component
    {
        public int CoroutineStarts;
        public Coroutine StartCoroutine(IEnumerator routine) { CoroutineStarts++; return new Coroutine(); }
        public void StopCoroutine(Coroutine routine) { }
    }
    public sealed class Coroutine { }
    public class GameObject : Object
    {
        public Transform transform = new Transform(); public string tag;
        public GameObject(string name) { }
        public T AddComponent<T>() where T:Component,new(){var c=new T();c.gameObject=this;c.transform=transform;return c;}
        public bool activeSelf=true; public void SetActive(bool value){activeSelf=value;}
    }
    public class Transform
    {
        public Vector3 position; public Quaternion rotation; public Vector3 forward=>rotation*Vector3.forward;
        public void SetParent(Transform parent,bool worldPositionStays) { } public Vector3 TransformPoint(Vector3 point)=>position+point;
    }
    public struct Vector2 { public float x,y; }
    public struct Vector4 { public float x,y,z,w; public Vector4(float x,float y,float z,float w){this.x=x;this.y=y;this.z=z;this.w=w;} }
    public struct Vector3
    {
        public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public Vector3 normalized {get {float l=(float)Math.Sqrt(x*x+y*y+z*z);return l<1e-20?new Vector3():new Vector3(x/l,y/l,z/l);}}
        public static Vector3 down=>new Vector3(0,-1,0);
        public static Vector3 forward=>new Vector3(0,0,1);
        public static Vector3 right=>new Vector3(1,0,0);
        public static Vector3 up=>new Vector3(0,1,0);
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);
    }
    public struct Quaternion
    {
        float x,y,z;
        public static Quaternion Euler(float x,float y,float z)=>new Quaternion{x=x,y=y,z=z};
        public static Vector3 operator *(Quaternion q,Vector3 v)
        {
            // Test substitute for Unity's Z-X-Y Euler convention; this is not an engine API test.
            double rx=q.x*Math.PI/180,ry=q.y*Math.PI/180,rz=q.z*Math.PI/180;
            double x=v.x*Math.Cos(rz)-v.y*Math.Sin(rz),y=v.x*Math.Sin(rz)+v.y*Math.Cos(rz),z=v.z;
            double yy=y*Math.Cos(rx)-z*Math.Sin(rx),zz=y*Math.Sin(rx)+z*Math.Cos(rx);
            return new Vector3((float)(x*Math.Cos(ry)+zz*Math.Sin(ry)),(float)yy,(float)(-x*Math.Sin(ry)+zz*Math.Cos(ry)));
        }
    }
    public enum CameraClearFlags { SolidColor }
    public class Camera:Component
    {
        public string tag;public CameraClearFlags clearFlags;public Color backgroundColor;
        public float nearClipPlane,farClipPlane,fieldOfView; public Vector3 WorldToScreenPoint(Vector3 point)=>point;
    }
    public static class Application { public static bool isPlaying=true; }
    public static class Screen { public static int height=1000,width=1400; }
    public static class Debug
    {
        public static void Log(string value,Object context=null) { }
        public static void LogWarning(string value,Object context=null) { }
        public static void LogException(Exception value,Object context=null) { }
    }
    public class GUIStyle { public int fontSize;public bool wordWrap;public GUIStyle(GUIStyle style) { } }
    public class GUISkin { public GUIStyle label; }
    public class GUIContent { public static GUIContent none; }
    public struct Rect { public Rect(float x,float y,float width,float height) { } }
    public static class GUI
    {
        public static GUISkin skin=new GUISkin();
        public static Color color; public static int depth;
        public static void Box(Rect area,GUIContent text) { }
        public static void Label(Rect area,string text,GUIStyle style) { }
        public static void DrawTexture(Rect area,Texture2D texture) { }
    }
    public class Texture2D:Object { public static Texture2D whiteTexture=new Texture2D(); }
}
namespace UnityEngine.InputSystem
{
    public sealed class ButtonControl { public bool wasPressedThisFrame,isPressed; }
    public sealed class VectorControl { public UnityEngine.Vector2 value; public UnityEngine.Vector2 ReadValue()=>value; }
    public sealed class Keyboard { public static Keyboard current;public ButtonControl tabKey=new ButtonControl(),fKey=new ButtonControl(),oKey=new ButtonControl(); public ButtonControl gKey=new ButtonControl(),homeKey=new ButtonControl(),rKey=new ButtonControl(),pKey=new ButtonControl(),dKey=new ButtonControl(),aKey=new ButtonControl(),eKey=new ButtonControl(),spaceKey=new ButtonControl(),qKey=new ButtonControl(),leftCtrlKey=new ButtonControl(),wKey=new ButtonControl(),sKey=new ButtonControl(),leftShiftKey=new ButtonControl(); }
    public sealed class Mouse
    {
        public static Mouse current;public ButtonControl rightButton=new ButtonControl(),middleButton=new ButtonControl(),leftButton=new ButtonControl();
        public VectorControl delta=new VectorControl(),scroll=new VectorControl();
    }
}
namespace AshenVoxel.Rendering
{
    public sealed class ChunkWorldRenderer:UnityEngine.MonoBehaviour
    {
        public UnityEngine.Material SurfaceMaterial;
        public int PendingChunks=>0;
        public void Initialize(VoxelWorld world) { }
        public void RebuildBudget(int count) { }
    }
}

namespace UnityEngine
{
 public class TextAsset:Object { public byte[] bytes; }
 public class Material:Object { public readonly Material Template; public int VectorWrites; public Material(Material value) {Template=value;} public void SetFloat(string key,float v) {} public void SetVector(string key,Vector4 value) {VectorWrites++;} }
 public class Renderer:Component { public Material sharedMaterial; }
 public sealed class MeshRenderer:Renderer {}
 public sealed class MeshFilter:Component { public Mesh sharedMesh; }
 public sealed class Mesh:Object
 {
  public System.Collections.Generic.List<Vector3> Vertices,Normals;
  public System.Collections.Generic.List<Color32> Colors;
  public System.Collections.Generic.List<int> Triangles;
  public System.Collections.Generic.List<Vector4> SurfaceParameters;
  public void SetVertices(System.Collections.Generic.List<Vector3> v){Vertices=v;}
  public void SetNormals(System.Collections.Generic.List<Vector3> v){Normals=v;}
  public void SetColors(System.Collections.Generic.List<Color32> v){Colors=v;}
  public void SetUVs(int channel,System.Collections.Generic.List<Vector4> v){SurfaceParameters=v;}
  public void SetTriangles(System.Collections.Generic.List<int> v,int submesh){Triangles=v;}
  public void RecalculateBounds(){}
 }
 public static class Resources { public static T Load<T>(string key) where T:class=>new Material(null) as T; }
 public static class Time { public static float unscaledTime,unscaledDeltaTime=1f/60; }
}
