// Data-only substitutes for water logic tests, not native Unity API validation.
using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public class Object { public string name; public bool Destroyed; public static int DestroyCount; public static void Destroy(Object value) { if(value!=null&&!value.Destroyed){value.Destroyed=true;DestroyCount++;} } }
    public class Component : Object { public Transform transform=new Transform(); }
    public class MonoBehaviour : Component { }
    public class Transform { public Vector3 position; public void SetParent(Transform parent,bool stays) { } }
    public class GameObject:Object
    {
        public Transform transform=new Transform(); public bool activeSelf=true; public readonly List<Component> Components=new List<Component>();
        public GameObject(string n){name=n;}
        public T AddComponent<T>() where T:Component,new(){var v=new T();v.transform=transform;Components.Add(v);return v;}
        public void SetActive(bool value){activeSelf=value;}
    }
    public sealed class Camera:Component { }
    public struct Vector3 { public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;} public static Vector3 up=>new Vector3(0,1,0); }
    public struct Color { public float r,g,b,a;public Color(float r,float g,float b,float a=1){this.r=r;this.g=g;this.b=b;this.a=a;} }
    public struct Color32 { public byte r,g,b,a;public Color32(byte r,byte g,byte b,byte a){this.r=r;this.g=g;this.b=b;this.a=a;} }
    public struct Rect { public float x,y,width,height;public Rect(float x,float y,float width,float height){this.x=x;this.y=y;this.width=width;this.height=height;} }
    public class Material:Object { public readonly Dictionary<string,float> Properties=new Dictionary<string,float>();public Material(Material source) { }public void SetFloat(string n,float value){Properties[n]=value;} }
    public class Renderer:Component { public Material sharedMaterial; }
    public sealed class MeshRenderer:Renderer { }
    public sealed class MeshFilter:Component { public Mesh sharedMesh; }
    public class Mesh:Object
    {
        public List<Vector3> Vertices,Normals;public List<Color32> Colors;public List<int> Triangles;
        public void SetVertices(List<Vector3> v){Vertices=v;}public void SetNormals(List<Vector3> v){Normals=v;}
        public void SetColors(List<Color32> v){Colors=v;}public void SetTriangles(List<int> v,int submesh){Triangles=v;}public void RecalculateBounds() { }
    }
    public static class Resources { public static Material Template=new Material(null); public static T Load<T>(string key) where T:class=>Template as T; }
    public static class Time { public static float unscaledTime; }
    public static class Screen { public static int width=1920,height=1080; }
    public sealed class Texture2D:Object { public static readonly Texture2D whiteTexture=new Texture2D(); }
    public static class GUI
    {
        public static Color color=new Color(1,1,1,1);public static int depth;public static int DrawCount;public static Rect LastRect;public static Color LastColor;
        public static void DrawTexture(Rect r,Texture2D texture){DrawCount++;LastRect=r;LastColor=color;}
    }
    public sealed class TooltipAttribute:Attribute { public TooltipAttribute(string t) { } }
    public sealed class RangeAttribute:Attribute { public RangeAttribute(float a,float b) { } }
}
