// Compile-only authoring API substitutes. These do not test Unity behavior or renderer integration.
using System;
using System.Collections;
using AshenVoxel.Core;
namespace UnityEngine
{
    public class Object { public static void Destroy(Object value) { } }
    public class Component : Object
    {
        public Transform transform = new Transform(); public GameObject gameObject;
    }
    public class MonoBehaviour : Component
    {
        public Coroutine StartCoroutine(IEnumerator routine)=>new Coroutine();
        public void StopCoroutine(Coroutine routine) { }
    }
    public sealed class Coroutine { }
    public class GameObject : Object
    {
        public Transform transform = new Transform(); public string tag;
        public GameObject(string name) { }
        public T AddComponent<T>() where T:Component,new()=>new T();
    }
    public class Transform
    {
        public Vector3 position; public Quaternion rotation;
        public void SetParent(Transform parent,bool worldPositionStays) { }
    }
    public struct Vector2 { public float x,y; }
    public struct Vector3
    {
        public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
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
        public float nearClipPlane,farClipPlane,fieldOfView;
    }
    public static class Application { public static bool isPlaying=true; }
    public static class Screen { public static int height=1000; }
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
        public static void Box(Rect area,GUIContent text) { }
        public static void Label(Rect area,string text,GUIStyle style) { }
    }
}
namespace UnityEngine.InputSystem
{
    public sealed class ButtonControl { public bool wasPressedThisFrame,isPressed; }
    public sealed class VectorControl { public UnityEngine.Vector2 value; public UnityEngine.Vector2 ReadValue()=>value; }
    public sealed class Keyboard { public static Keyboard current;public ButtonControl gKey=new ButtonControl(),homeKey=new ButtonControl(); }
    public sealed class Mouse
    {
        public static Mouse current;public ButtonControl rightButton=new ButtonControl(),middleButton=new ButtonControl();
        public VectorControl delta=new VectorControl(),scroll=new VectorControl();
    }
}
namespace AshenVoxel.Rendering
{
    public sealed class ChunkWorldRenderer:UnityEngine.MonoBehaviour
    {
        public int PendingChunks=>0;
        public void Initialize(VoxelWorld world) { }
        public void RebuildBudget(int count) { }
    }
}
