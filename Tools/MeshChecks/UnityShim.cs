using System;
using System.Collections.Generic;
namespace UnityEngine {
 // Presence catches System.Random/UnityEngine.Random name ambiguity in editor sources.
 public static class Random {}
 public class Object { public static void DestroyImmediate(Object x){} }
 public class Mesh:Object {
 public Vector3[] vertices=Array.Empty<Vector3>(),normals=Array.Empty<Vector3>();
 public Color32[] colors32=Array.Empty<Color32>(); public int[] triangles=Array.Empty<int>();
 public UnityEngine.Rendering.IndexFormat indexFormat; public int vertexCount=>vertices.Length;
 public void Clear(){vertices=Array.Empty<Vector3>(); normals=Array.Empty<Vector3>(); colors32=Array.Empty<Color32>();triangles=Array.Empty<int>();}
 public void SetVertices(List<Vector3> v){vertices=v.ToArray();} public void SetNormals(List<Vector3> v){normals=v.ToArray();}
 public void SetColors(List<Color32> c){colors32=c.ToArray();} public void SetTriangles(List<int> t,int s,bool recalc){triangles=t.ToArray();}
 }
 public struct Color32 {public byte r,g,b,a; public Color32(byte r,byte g,byte b,byte a){this.r=r;this.g=g;this.b=b;this.a=a;}}
 public struct Vector3 {
 public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
 public static Vector3 zero=>new Vector3(); public float magnitude=>(float)Math.Sqrt(x*x+y*y+z*z);
 public float this[int i]{get=>i==0?x:i==1?y:z;set{if(i==0)x=value;else if(i==1)y=value;else z=value;}}
 public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
 public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
 public static Vector3 operator *(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);
 public static Vector3 Cross(Vector3 a,Vector3 b)=>new Vector3(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
 public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
 }
 public static class Mathf{public static float Abs(float x)=>Math.Abs(x);}
 public static class Debug{public static void Log(string x)=>Console.WriteLine(x);}
}
namespace UnityEngine.Rendering{public enum IndexFormat{UInt16,UInt32}}
namespace UnityEditor{public class MenuItem:Attribute{public MenuItem(string name){}}}
class Program{static void Main(){AshenVoxel.Rendering.Editor.MeshRegressionChecks.Run();}}
