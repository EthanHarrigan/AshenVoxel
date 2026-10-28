using System;
using System.Collections.Generic;
using System.Threading;
namespace UnityEngine
{
    static class OwnerThread
    {
        static readonly int Main=Thread.CurrentThread.ManagedThreadId;
        public static void Require(){if(Thread.CurrentThread.ManagedThreadId!=Main)throw new InvalidOperationException("Unity object API called on worker");}
    }
    // Numerical API substitutes only; never evidence of native Unity rendering.
    public class Object
    {
        public string name;public bool WasDestroyed;public static readonly List<Object> Created=new List<Object>();
        public Object(){OwnerThread.Require();Created.Add(this);}public static void Destroy(Object value){OwnerThread.Require();if(value!=null)value.WasDestroyed=true;}public static void DestroyImmediate(Object value)=>Destroy(value);
    }
    public class Transform
    {
        public GameObject gameObject;
        public Vector3 position;public Quaternion rotation;
        public void SetParent(Transform value,bool worldPositionStays){}
        public void SetPositionAndRotation(Vector3 p,Quaternion q){position=p;rotation=q;}
    }
    public class GameObject:Object
    {
        public readonly Transform transform=new Transform();public int layer;public GameObject(string label){name=label;transform.gameObject=this;}
        public T AddComponent<T>()where T:new()=>new T();
    }
    public struct Vector3
    {
        public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public float this[int i]{get=>i==0?x:i==1?y:z;set{if(i==0)x=value;else if(i==1)y=value;else z=value;}}
        public static Vector3 zero=>new Vector3();public static Vector3 one=>new Vector3(1,1,1);
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator *(Vector3 a,float s)=>new Vector3(a.x*s,a.y*s,a.z*s);
    }
    public struct Quaternion{public float x,y,z,w;public Quaternion(float x,float y,float z,float w){this.x=x;this.y=y;this.z=z;this.w=w;}}
    public struct Vector4{public float x,y,z,w;public Vector4(float x,float y,float z,float w){this.x=x;this.y=y;this.z=z;this.w=w;}}
    public struct Color32{public byte r,g,b,a;public Color32(byte r,byte g,byte b,byte a){this.r=r;this.g=g;this.b=b;this.a=a;}}
    public struct Matrix4x4
    {
        public Vector3 Position,Scale;public Quaternion Rotation;
        public static Matrix4x4 TRS(Vector3 p,Quaternion q,Vector3 s)=>new Matrix4x4{Position=p,Rotation=q,Scale=s};
    }
    public class Material:Object
    {
        public bool enableInstancing;public readonly Dictionary<string,float> Floats=new Dictionary<string,float>();
        public readonly Dictionary<string,Vector4> Vectors=new Dictionary<string,Vector4>();
        public Material(Material value){if(value!=null){foreach(var p in value.Floats)Floats[p.Key]=p.Value;foreach(var p in value.Vectors)Vectors[p.Key]=p.Value;}}public void SetFloat(string name,float value){Floats[name]=value;}
        public void SetVector(string name,Vector4 value){Vectors[name]=value;}
    }
    public class MaterialPropertyBlock
    {
        public Vector4[] Tints;public readonly Dictionary<string,object> Values=new Dictionary<string,object>();
        public void SetFloat(string name,float value){Values[name]=value;}public void SetVector(string name,Vector4 value){Values[name]=value;}public void SetTexture(string name,Object value){if(value==null)throw new ArgumentNullException(nameof(value));Values[name]=value;}
        public void SetVectorArray(string name,Vector4[] value){Tints=(Vector4[])value.Clone();}
    }
    public class Mesh:Object
    {
        public static int Uploads;public Rendering.IndexFormat indexFormat;public Vector3[] Vertices,Normals;public Color32[] Colors;public int[] Indices;
        public int vertexCount=>Vertices?.Length??0;
        public void Clear(){Vertices=Normals=null;Colors=null;Indices=null;}
        public void SetVertices(List<Vector3> v){OwnerThread.Require();Vertices=v.ToArray();Uploads++;}public void SetNormals(List<Vector3> v){Normals=v.ToArray();}
        public void SetColors(List<Color32> v){Colors=v.ToArray();}public void SetTriangles(List<int> v,int submesh,bool bounds=true){Indices=v.ToArray();}
        public void RecalculateBounds(){}
        public void UploadMeshData(bool nonReadable){}
    }
    public class MeshFilter{public Mesh sharedMesh;}
    public class MeshRenderer
    {
        public Material sharedMaterial;public bool enabled,receiveShadows;public Rendering.ShadowCastingMode shadowCastingMode;public Rendering.LightProbeUsage lightProbeUsage;
        public MaterialPropertyBlock Properties;public void SetPropertyBlock(MaterialPropertyBlock value){Properties=value;}
    }
    public static class Graphics
    {
        public static int Draws,Instances,MaximumBatch,SharedDraws;public static Mesh LastShared;public static int LastSubmesh;
        public static void DrawMesh(Mesh mesh,Matrix4x4 matrix,Material material,int layer,Camera camera,int submesh,MaterialPropertyBlock block,Rendering.ShadowCastingMode shadows,bool receive,Transform anchor,Rendering.LightProbeUsage probes){OwnerThread.Require();if(probes!=Rendering.LightProbeUsage.Off)throw new Exception("Borrowed rendering changed forest light-probe policy");SharedDraws++;LastShared=mesh;LastSubmesh=submesh;}
        public static void DrawMeshInstanced(Mesh mesh,int submesh,Material material,Matrix4x4[] matrices,int count,MaterialPropertyBlock properties,Rendering.ShadowCastingMode cast,bool receive,int layer,Camera camera)
        {
            OwnerThread.Require();if(!material.enableInstancing||count<1||count>1023||mesh.WasDestroyed||properties.Tints.Length<count)throw new Exception("Invalid instance draw");
            if(!material.Floats.TryGetValue("_MatterInstances",out float mode)||mode!=1)throw new Exception("Instance material not selected");
            for(int i=0;i<count;i++)if(matrices[i].Scale.x<=0||properties.Tints[i].w<0||properties.Tints[i].w>1)throw new Exception("Invalid instance data");
            Draws++;Instances+=count;MaximumBatch=Math.Max(MaximumBatch,count);
        }
    }
    public enum TextureFormat{R8}public enum FilterMode{Point}public enum TextureWrapMode{Clamp}
    public class Texture3D:Object
    {
        public readonly int Width,Height,Depth;public byte[] Data;public FilterMode filterMode;public TextureWrapMode wrapMode;
        public Texture3D(int x,int y,int z,TextureFormat format,bool mip){Width=x;Height=y;Depth=z;}
        public void SetPixelData(byte[] bytes,int mip){OwnerThread.Require();Data=(byte[])bytes.Clone();}public void Apply(bool mip,bool read){OwnerThread.Require();}
    }
    public class Camera{}
    public static class Application{public static bool isPlaying=true;}
}
namespace UnityEngine.Rendering{public enum IndexFormat{UInt32}public enum ShadowCastingMode{Off,On}public enum LightProbeUsage{Off,BlendProbes}}
