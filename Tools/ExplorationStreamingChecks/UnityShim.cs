using System;
using System.Collections.Generic;
namespace UnityEngine
{
 public sealed class RangeAttribute:Attribute{public RangeAttribute(float a,float b){}}
 public class Object{public bool Destroyed;public static bool operator==(Object a,Object b){bool an=ReferenceEquals(a,null)||a.Destroyed,bn=ReferenceEquals(b,null)||b.Destroyed;return an||bn?an==bn:ReferenceEquals(a,b);}public static bool operator!=(Object a,Object b)=>!(a==b);public override bool Equals(object x)=>ReferenceEquals(this,x);public override int GetHashCode()=>base.GetHashCode();public static void Destroy(Object x){if(x!=null)x.Destroyed=true;}}
 public class GameObject:Object{public int layer;}
 public class MonoBehaviour:Object{public GameObject gameObject=new GameObject();}
 public class Material:Object{public bool enableInstancing;public void SetFloat(string key,float value){}public Material(){}public Material(Material x){enableInstancing=x.enableInstancing;}}
 public class TextAsset:Object{public byte[] bytes;}
 public class Camera:Object{}
 public static class Resources{public static Dictionary<string,byte[]> RootFiles=new Dictionary<string,byte[]>();public static bool UseImpostors;public static Material Template=new Material();public static T Load<T>(string path)where T:class=>RootFiles.TryGetValue(path,out var data)?new TextAsset{bytes=data} as T:path=="ForestAtlasVersion"?(UseImpostors?new TextAsset() as T:null):Template as T;}
 public struct Color32{public byte r,g,b,a;public Color32(byte r,byte g,byte b,byte a){this.r=r;this.g=g;this.b=b;this.a=a;}}
 public struct Vector4{public float x,y,z,w;public Vector4(float x,float y,float z,float w){this.x=x;this.y=y;this.z=z;this.w=w;}}
 public struct Vector3
 {
  public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
  public static Vector3 one=>new Vector3(1,1,1);public static Vector3 zero=>new Vector3();public static Vector3 right=>new Vector3(1,0,0);public static Vector3 left=>new Vector3(-1,0,0);public static Vector3 up=>new Vector3(0,1,0);public static Vector3 down=>new Vector3(0,-1,0);public static Vector3 forward=>new Vector3(0,0,1);public static Vector3 back=>new Vector3(0,0,-1);
  public static Vector3 operator+(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);public static Vector3 operator-(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);public static Vector3 operator*(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);
 }
 public struct Quaternion{public float Yaw;public static Quaternion Euler(float x,float y,float z)=>new Quaternion{Yaw=y};}
 public struct Matrix4x4
 {
  public float m00,m01,m02,m03,m10,m11,m12,m13,m20,m21,m22,m23,m30,m31,m32,m33;
  public static Matrix4x4 TRS(Vector3 p,Quaternion q,Vector3 s){double a=q.Yaw*Math.PI/180;return new Matrix4x4{m00=(float)Math.Cos(a)*s.x,m02=(float)Math.Sin(a)*s.z,m11=s.y,m20=-(float)Math.Sin(a)*s.x,m22=(float)Math.Cos(a)*s.z,m03=p.x,m13=p.y,m23=p.z,m33=1};}
 }
 public struct Bounds
 {
  public Vector3 center,size;public Bounds(Vector3 c,Vector3 s){center=c;size=s;}public Vector3 min=>center-size*.5f;public Vector3 max=>center+size*.5f;
  public void Expand(float amount){size=size+Vector3.one*amount;}public void Encapsulate(Bounds b){Vector3 a0=min,a1=max,b0=b.min,b1=b.max;Vector3 low=new Vector3(Math.Min(a0.x,b0.x),Math.Min(a0.y,b0.y),Math.Min(a0.z,b0.z)),high=new Vector3(Math.Max(a1.x,b1.x),Math.Max(a1.y,b1.y),Math.Max(a1.z,b1.z));center=(low+high)*.5f;size=high-low;}
 }
 public static class QualitySettings{public static float shadowDistance=180;}
 public struct Plane{}
 public static class GeometryUtility{public static bool CullAll;public static void CalculateFrustumPlanes(Camera c,Plane[] p){}public static bool TestPlanesAABB(Plane[] p,Bounds b)=>!CullAll;}
 public class Mesh:Object
 {
  public int subMeshCount=1;public string name;public UnityEngine.Rendering.IndexFormat indexFormat;public Vector3[] vertices,normals;public Color32[] colors32;public int[] triangles;public Bounds bounds;
  public void SetUVs(int channel,List<Vector4> uv){}
  public void SetTriangles(int[] t,int submesh,bool recalculate){triangles=t;if(recalculate)RecalculateBounds();}
  public void RecalculateBounds(){Vector3 low=vertices[0],high=low;foreach(Vector3 v in vertices){low=new Vector3(Math.Min(low.x,v.x),Math.Min(low.y,v.y),Math.Min(low.z,v.z));high=new Vector3(Math.Max(high.x,v.x),Math.Max(high.y,v.y),Math.Max(high.z,v.z));}bounds=new Bounds((low+high)*.5f,high-low);}
  public void UploadMeshData(bool markNonReadable){if(markNonReadable){vertices=null;normals=null;colors32=null;triangles=null;}}
 }
 public struct RenderParams
 {
  public Material material;public Camera camera;public Bounds worldBounds;public UnityEngine.Rendering.ShadowCastingMode shadowCastingMode;public bool receiveShadows;public int layer;public UnityEngine.Rendering.LightProbeUsage lightProbeUsage;
  public RenderParams(Material m){material=m;camera=null;worldBounds=default;shadowCastingMode=default;receiveShadows=false;layer=0;lightProbeUsage=default;}
 }
 public static class Graphics
 {
  public static int Calls,Instances;
  public static void RenderMeshInstanced(RenderParams p,Mesh m,int submesh,Matrix4x4[] matrices,int count)
  {if(!p.material.enableInstancing||count<1||count>1023||count>matrices.Length)throw new Exception("Bad instancing contract");Calls++;Instances+=count;}
 }
}
namespace UnityEngine.Rendering{public enum IndexFormat{UInt16,UInt32}public enum ShadowCastingMode{Off,On,ShadowsOnly}public enum LightProbeUsage{Off,BlendProbes}}

namespace AshenVoxel.Exploration{public static class ExplorationWind{public const float MaximumDisplacementMeters=2;}}
