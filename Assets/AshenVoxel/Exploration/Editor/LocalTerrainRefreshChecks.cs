#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using AshenVoxel.WorldScale;
using AshenVoxel.WorldScaleRuntime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AshenVoxel.Exploration.Editor
{
    // Native lifecycle/queue regression. Synthetic payload sizes exercise admission
    // without allocating hundreds of MiB; this is not a geometry/performance test.
    public static class LocalTerrainRefreshChecks
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        const long MiB=1024*1024;
        static readonly Type Explorer=typeof(WorldExplorer),SurfaceType=Explorer.GetNestedType("Surface",BindingFlags.NonPublic);
        [Serializable] sealed class Report
        {
            public string status="FAIL",unity,error,scope="Native Edit-mode local invalidation, retained iterator/queue and synthetic contact admission. No Play, geometry, rendering or frame-time claim.";
            public List<string> checks=new List<string>(),source_sha256=new List<string>(),errors=new List<string>();
        }
        sealed class Field:IWorldFieldBounds,IWorldFieldSnapshotProvider
        {
            public string SourceIdentity=>"local-refresh-native-fixture";
            public SurfaceSample Sample(double x,double z)=>new SurfaceSample(10,6,6,true);
            public HeightBounds QueryBounds(double a,double b,double c,double d)=>new HeightBounds(10,10);
            public IWorldField CaptureSnapshot()=>this;
        }
        interface IHeld { bool Disposed { get; } }
        sealed class Held<T>:IEnumerator<T>,IHeld
        {
            public bool Disposed {get;private set;}
            public T Current=>default;object IEnumerator.Current=>Current;
            public bool MoveNext()=>true;public void Reset(){}public void Dispose(){Disposed=true;}
        }
        static object Get(object o,string name)=>o.GetType().GetField(name,Flags).GetValue(o);
        static void Set(object o,string name,object value)=>o.GetType().GetField(name,Flags).SetValue(o,value);
        static object Call(WorldExplorer w,string name,params object[] args)=>Explorer.GetMethod(name,Flags).Invoke(w,args);
        static LodTile Tile(long x)=>new LodTile(new TileKey(x,0,0),x*25.6,0,25.6,10,10);
        static object Work(WorldExplorer w,LodTile tile,bool contact=false,int quads=0,bool progressive=false)=>Activator.CreateInstance(Explorer.GetNestedType("Work",BindingFlags.NonPublic),new object[]{tile,(int)Get(w,"revision"),true,contact,quads,progressive});
        static object[] Pending(WorldExplorer w)=>((IEnumerable)Get(w,"pending")).Cast<object>().ToArray();
        static bool IsTile(object work,TileKey key)=>((LodTile)Get(work,"Tile")).Key.Equals(key);
        static void Enqueue(WorldExplorer w,object work){object queue=Get(w,"pending");queue.GetType().GetMethod("Enqueue").Invoke(queue,new[]{work});}
        static void Apply(WorldExplorer w,params LodTile[] tiles)
        {
            var plan=new LodPlan();typeof(LodPlan).GetProperty("Tiles").SetValue(plan,tiles);
            Call(w,"ApplyPlan",plan,25.6);
        }
        static IHeld Hold(WorldExplorer w,LodTile tile,bool contact=false)
        {
            object steps=Activator.CreateInstance(typeof(Held<>).MakeGenericType(SurfaceType),true);
            object build=Activator.CreateInstance(Explorer.GetNestedType("SurfaceBuild",BindingFlags.NonPublic),true);
            Set(build,"Work",Work(w,tile,contact));Set(build,"Steps",steps);Set(w,"currentBuild",build);return (IHeld)steps;
        }
        sealed class Fixture:IDisposable
        {
            readonly Scene scene=EditorSceneManager.NewPreviewScene();
            readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
            public readonly WorldExplorer World;
            public Fixture(bool voxel)
            {
                var root=Own(new GameObject("Local terrain refresh native fixture"));World=root.AddComponent<WorldExplorer>();World.enabled=false;
                World.ExternalNavigation=true;World.UseVoxelLodTerrain=voxel;World.AsyncPlanning=false;World.ConnectedTerrainSurface=false;
                World.VoxelFullDetailRadius=32;World.VoxelContactBudgetMB=128;
                Set(World,"field",new Field());Set(World,"surfaceProfile",new SurfaceProfile(enableSnow:false));
                Set(World,"cameraX",1d);Set(World,"cameraY",12d);Set(World,"cameraZ",1d);
            }
            T Own<T>(T value)where T:UnityEngine.Object
            {owned.Add(value);if(value is GameObject go)SceneManager.MoveGameObjectToScene(go,scene);return value;}
            public void Publish(TileKey key,long bytes,double height,object work=null)
            {
                var tile=new LodTile(key,key.X*25.6,key.Z*25.6,25.6,10,10);
                object surface=Activator.CreateInstance(SurfaceType,true);
                // A rejected payload does not need a native object. Accepted objects
                // are fixture-owned and detached from cache disposal in Edit mode.
                var go=Own(new GameObject("Synthetic contact payload"));Set(surface,"Object",go);
                Set(surface,"Tile",tile);Set(surface,"MeshBytes",bytes);Set(surface,"Quads",256);
                Set(surface,"VoxelColumns",true);Set(surface,"AnchorY",height);Set(surface,"LocalHeights",new float[256*256]);
                bool admitted=(bool)Call(World,"AdmitContactMesh",key,bytes);
                if(!admitted)Set(surface,"Object",null);
                Call(World,"PublishContact",surface,work??Work(World,tile,true));Set(surface,"Object",null);
            }
            public void Dispose()
            {
                Call(World,"CancelBuild");((IDictionary)Get(World,"contactSurfaces")).Clear();((IDictionary)Get(World,"surfaces")).Clear();
                for(int i=owned.Count-1;i>=0;i--)if(owned[i]!=null)UnityEngine.Object.DestroyImmediate(owned[i]);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
        static void Check(Report r,bool condition,string label)
        {if(!condition)throw new InvalidOperationException(label);r.checks.Add(label);}
        static void Terrain(Report r,bool voxel)
        {
            using(var f=new Fixture(voxel))
            {
                var w=f.World;var a=Tile(0);var b=Tile(8);var c=Tile(16);var d=Tile(24);
                var desired=(HashSet<TileKey>)Get(w,"desired");var tiles=(IDictionary)Get(w,"desiredTiles");
                foreach(var tile in new[]{a,b,c,d}){desired.Add(tile.Key);tiles.Add(tile.Key,tile);}
                var held=Hold(w,c);object build=Get(w,"currentBuild");int revision=(int)Get(w,"revision");
                Enqueue(w,Work(w,d,false,17,true));((HashSet<TileKey>)Get(w,"refining")).Add(d.Key);
                w.RefreshExternalRegion(1,1,2,2);w.RefreshExternalRegion(b.OriginXMeters+1,1,b.OriginXMeters+2,2);
                Check(r,(int)Get(w,"revision")==revision&&ReferenceEquals(build,Get(w,"currentBuild"))&&!held.Disposed,"two distant publications preserve unrelated iterator/revision (voxel="+voxel+")");
                Check(r,Pending(w).Count(p=>IsTile(p,d.Key))==1&&Pending(w).Any(p=>IsTile(p,a.Key))&&Pending(w).Any(p=>IsTile(p,b.Key)),"both affected requests coexist with unrelated queued work (voxel="+voxel+")");
                Apply(w,a,b,c,d);
                object retained=Pending(w).Single(p=>IsTile(p,d.Key));
                Check(r,ReferenceEquals(build,Get(w,"currentBuild"))&&!held.Disposed&&(int)Get(retained,"Quads")==17&&(bool)Get(retained,"Progressive")&&((HashSet<TileKey>)Get(w,"refining")).Contains(d.Key),"subsequent ApplyPlan preserves iterator and queued refinement identity (voxel="+voxel+")");
                double edge=c.OriginXMeters+c.SizeMeters+SurfaceProfile.SampleRadiusMax*.5;
                w.RefreshExternalRegion(edge,1,edge+.01,2);
                Check(r,held.Disposed&&Get(w,"currentBuild")==null&&Pending(w).Any(p=>IsTile(p,c.Key)),"sampling-halo publication cancels/requeues affected snapshot (voxel="+voxel+")");
                held=Hold(w,c);Set(w,"revision",revision+1);Apply(w,a,b,c,d);
                Check(r,held.Disposed,"global reset revision still rejects stale work (voxel="+voxel+")");
            }
        }
        static IList Contacts(WorldExplorer w)=>(IList)Get(w,"contactPending");
        static object Take(WorldExplorer w,TileKey key)
        {var queue=Contacts(w);for(int i=0;i<queue.Count;i++)if(IsTile(queue[i],key)){object result=queue[i];queue.RemoveAt(i);return result;}return null;}
        static void Contact(Report r)
        {
            using(var f=new Fixture(true))
            {
                var w=f.World;Call(w,"RefreshContactPlan");var a=Tile(0).Key;var b=Tile(1).Key;
                foreach(var key in ((HashSet<TileKey>)Get(w,"contactDesired")).ToArray())f.Publish(key,key.Equals(a)?100*MiB:key.Equals(b)?20*MiB:120,10);
                Contacts(w).Clear();w.RefreshExternalRegion(1,1,2,2);var replacement=Take(w,a);
                Check(r,replacement!=null,"local contact edit queues an existing same-revision surface");
                f.Publish(a,120*MiB,20,replacement);
                var dirty=(HashSet<TileKey>)Get(w,"contactExternallyDirty");var deferred=(IDictionary)Get(w,"contactDeferred");
                Check(r,dirty.Contains(a)&&deferred.Contains(a)&&w.TrySampleRenderedHeight(1,1,out double h)&&h==10,"denied contact replacement retains old support and dirty obligation");
                Set(w,"cameraX",26.6d);Call(w,"RefreshContactPlan");replacement=Take(w,a);
                Check(r,replacement!=null&&dirty.Contains(a),"overlap movement requeues locally dirty contact despite equal global revision");
                f.Publish(a,120*MiB,20,replacement);Contacts(w).Clear();f.Publish(b,120,20);replacement=Take(w,a);
                Check(r,replacement!=null,"released capacity wakes the retained contact replacement");
                f.Publish(a,120*MiB,20,replacement);
                Check(r,!dirty.Contains(a)&&!deferred.Contains(a)&&w.TrySampleRenderedHeight(1,1,out h)&&h==20,"successful contact publication alone clears its freshness obligation");
                var held=Hold(w,Tile(0),true);w.RefreshExternalRegion(1,1,2,2);
                Check(r,held.Disposed&&Take(w,a)!=null&&dirty.Contains(a),"affected contact iterator is canceled and requeued on another local update");
                Set(w,"cameraX",2000d);Call(w,"RefreshContactPlan");
                Check(r,!dirty.Contains(a),"eviction prunes contact dirty metadata outside residency and demand");
            }
        }
        [MenuItem("Ashen Voxel/Validation/Check local terrain refresh")]
        public static void Run()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Run in Edit mode.");
            var report=new Report{unity=Application.unityVersion};string root=Path.GetFullPath(Path.Combine(Application.dataPath,".."));
            string folder=Path.Combine(root,"Logs/LocalTerrainRefresh",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));Directory.CreateDirectory(folder);
            Application.LogCallback log=(message,stack,type)=>{if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)report.errors.Add(message);};
            Application.logMessageReceived+=log;
            try
            {
                foreach(string file in new[]{"Assets/AshenVoxel/WorldScaleRuntime/WorldExplorer.cs","Assets/AshenVoxel/WorldScaleRuntime/WorldExplorerIntegration.cs","Assets/AshenVoxel/WorldScaleRuntime/WorldExplorerVoxels.cs","Assets/AshenVoxel/Exploration/Editor/LocalTerrainRefreshChecks.cs"})
                    using(var sha=SHA256.Create())report.source_sha256.Add(file+" "+BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Path.Combine(root,file)))).Replace("-","").ToLowerInvariant());
                Terrain(report,false);Terrain(report,true);Contact(report);if(report.errors.Count>0)throw new InvalidOperationException("Native fixture logged errors");report.status="PASS";
            }
            catch(Exception e){report.error=e.ToString();}
            finally{Application.logMessageReceived-=log;}
            File.WriteAllText(Path.Combine(folder,"report.json"),JsonUtility.ToJson(report,true));
            if(report.status=="PASS")Debug.Log("Local terrain refresh PASS: "+report.checks.Count+" checks; "+folder);
            else Debug.LogError("Local terrain refresh FAIL: "+report.error+"; "+folder);
        }
    }
}
#endif
