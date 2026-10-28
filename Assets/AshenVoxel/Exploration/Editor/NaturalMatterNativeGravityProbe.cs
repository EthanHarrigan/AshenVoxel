using System;
using System.IO;
using System.Security.Cryptography;
using AshenVoxel.Core;
using AshenVoxel.Exploration.Experimental;
using AshenVoxel.WorldScale;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace AshenVoxel.Exploration.Editor
{
    // Exact opt-in only. Never enters/exits Play or changes the user's active scene.
    [InitializeOnLoad] public static class NaturalMatterNativeGravityProbe
    {
        const string Token="ASHEN_NATIVE_MATTER_GRAVITY_V1";
        static string Root=>Path.GetDirectoryName(Application.dataPath);
        static string Request=>Path.Combine(Root,".terrain-cache/native-matter-gravity.request");
        static readonly string[] Sources={"Assets/AshenVoxel/Exploration/NaturalTreePhysics.cs","Assets/AshenVoxel/Exploration/NaturalMatterRigidBodies.cs","Assets/AshenVoxel/Exploration/Editor/NaturalMatterNativeGravityProbe.cs"};
        [Serializable] sealed class Report
        {
            public string status,error,utc,fixture;public string[] sources,sha256;public int steps,total,source,island,individual,cargo,impacts,expectedSource,expectedIsland,expectedReleased;
            public float initialY,minimumAirY,maximumAirSpinDegrees,contactTime,contactY,releaseTime;public bool conserved,allObjectsIsolated,activeScenePreserved,playPreserved,cleanupComplete;
        }
        sealed class FlatField:IWorldField {public string SourceIdentity=>"native-gravity-flat-v1";public SurfaceSample Sample(double x,double z)=>new SurfaceSample(0,6,6,true);}
        static Scene scene;static PhysicsScene physicsScene;static GameObject root;static NaturalTreePhysics matter;static NaturalMatterRigidBodies bridge;
        static Report report;static string output;static int activeHandle,sceneCount;static double began;static bool running,cleaning;static AsyncOperation unload;
        static NaturalMatterNativeGravityProbe(){EditorApplication.update+=Poll;}
        static string Hash(string path){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Path.Combine(Root,path)))).Replace("-","").ToLowerInvariant();}
        static void Poll()
        {
            if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            if(cleaning)
            {
                if(unload!=null&&!unload.isDone)return;report.cleanupComplete=SceneManager.sceneCount==sceneCount;
                report.activeScenePreserved=SceneManager.GetActiveScene().handle==activeHandle;report.playPreserved=EditorApplication.isPlaying;
                if(!report.cleanupComplete||!report.activeScenePreserved||!report.playPreserved)report.status="FAIL";
                File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));cleaning=false;return;
            }
            if(!running)
            {
                if(!File.Exists(Request))return;string token=File.ReadAllText(Request).Trim();File.Delete(Request);if(token!=Token&&token!=Token+"-STUMP")return;
                if(!EditorApplication.isPlaying){Debug.LogWarning("Native gravity probe requires existing Play; no Play transition performed");return;}
                try{Begin(token==Token+"-STUMP");}catch(Exception e){Finish(e.ToString());}return;
            }
            try
            {
                if(!EditorApplication.isPlaying)throw new InvalidOperationException("Play ended externally");
                if(EditorApplication.timeSinceStartup-began>15)throw new TimeoutException("15-second native probe wall limit");
                bridge.Tick(Vector3.zero,true);if(bridge.Error!=null)throw new InvalidOperationException(bridge.Error);if(!bridge.Ready)return;
                for(int batch=0;batch<8;batch++)
                {
                    foreach(var child in root.GetComponentsInChildren<Transform>(true))if(child.gameObject.scene!=scene){report.allObjectsIsolated=false;throw new InvalidOperationException("Bridge object escaped isolated PhysicsScene");}
                    var body=root.GetComponentInChildren<Rigidbody>();
                    if(body!=null&&matter.GroundImpacts==0)
                    {
                        if(report.initialY==0)report.initialY=report.minimumAirY=body.position.y;
                        report.minimumAirY=Math.Min(report.minimumAirY,body.position.y);
                        report.maximumAirSpinDegrees=Math.Max(report.maximumAirSpinDegrees,Quaternion.Angle(Quaternion.identity,body.rotation));
                    }
                    physicsScene.Simulate(.02f);bridge.ReadBack(.02f);matter.Step(.02f,512,512);bridge.Tick(Vector3.zero,true);report.steps++;
                    if(!matter.Conserved)throw new InvalidOperationException("Native ownership conservation failed");
                    if(matter.GroundImpacts>0&&report.contactTime==0){report.contactTime=report.steps*.02f;report.contactY=body==null?float.NaN:body.position.y;}
                    if(matter.IslandCells==0&&matter.IndividualCells==report.expectedReleased)
                    {
                        report.releaseTime=report.steps*.02f;
                        bool contactValid=report.fixture=="stump-contact"?report.contactY>1.9f:report.initialY-report.minimumAirY>=.2f;
                        if(!contactValid||report.maximumAirSpinDegrees>.01f||report.contactTime<=0||report.releaseTime-report.contactTime<.58f||matter.SourceCells!=report.expectedSource)
                            throw new InvalidOperationException("Fixture contact/drop, airborne-spin, retention or source-count acceptance failed");
                        Finish(null);return;
                    }
                    if(report.steps>=400)throw new TimeoutException("Eight simulated seconds without conserved release");
                }
            }
            catch(Exception e){Finish(e.ToString());}
        }
        static void Begin(bool stump)
        {
            activeHandle=SceneManager.GetActiveScene().handle;sceneCount=SceneManager.sceneCount;began=EditorApplication.timeSinceStartup;
            output=Path.Combine(Root,".terrain-cache/native-matter-gravity-"+(stump?"stump-":"freefall-")+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));Directory.CreateDirectory(output);
            report=new Report{status="RUNNING",fixture=stump?"stump-contact":"free-fall",utc=DateTime.UtcNow.ToString("O"),sources=Sources,sha256=Array.ConvertAll(Sources,Hash),allObjectsIsolated=true};running=true;
            scene=SceneManager.CreateScene("Ashen isolated matter gravity",new CreateSceneParameters(LocalPhysicsMode.Physics3D));physicsScene=scene.GetPhysicsScene();
            root=new GameObject("Isolated gravity probe owner");SceneManager.MoveGameObjectToScene(root,scene);
            const int stemCells=20,crownWidth=2,crownDepth=2,crownHeight=4;
            var world=new VoxelWorld(8,26,4,.1f,714103);for(int y=0;y<stemCells;y++)world.Set(1,y,1,MaterialId.Wood);
            if(!stump)for(int x=2;x<=4;x++)world.Set(x,19,1,MaterialId.Wood);
            int crownX=stump?1:4;
            for(int y=20;y<20+crownHeight;y++)for(int z=1;z<1+crownDepth;z++)for(int x=crownX;x<crownX+crownWidth;x++)world.Set(x,y,z,MaterialId.Wood,0xff845731);
            report.expectedIsland=crownWidth*crownDepth*crownHeight+(stump?0:1);report.expectedReleased=report.expectedIsland+1;
            matter=new NaturalTreePhysics(world,new Int3(1,0,1),0){SuctionEnabled=false,TerrainHeight=(x,z)=>0};
            bridge=new NaturalMatterRigidBodies(root.transform,matter,world,new WorldPoint(0,0,0),new SurfaceMaterialSampler(new FlatField(),new SurfaceProfile(seaLevelMeters:-100,enableSnow:false)));
            report.expectedSource=matter.Count-report.expectedReleased;
            if(!matter.Cut(new Int3(stump?1:3,19,1)))throw new InvalidOperationException("Native fixture stem cut refused");
            for(int i=0;i<8&&matter.PendingSupport>0;i++)matter.StepSupport(512);
            if(matter.IslandCells!=report.expectedIsland||matter.SourceCells!=report.expectedSource)throw new InvalidOperationException("Native fixture did not form expected exact detached/source partition");
        }
        static void Finish(string error)
        {
            if(report==null){Debug.LogError(error);return;}report.status=error==null?"PASS":"FAIL";report.error=error;
            if(matter!=null){report.total=matter.Count;report.source=matter.SourceCells;report.island=matter.IslandCells;report.individual=matter.IndividualCells;report.cargo=matter.CargoCells;report.impacts=matter.GroundImpacts;report.conserved=matter.Conserved;}
            for(int i=0;i<Sources.Length;i++)if(Hash(Sources[i])!=report.sha256[i]){report.status="FAIL";report.error="Source changed during native run";}
            bridge?.Dispose();bridge=null;if(root!=null)UnityEngine.Object.DestroyImmediate(root);root=null;running=false;
            unload=scene.IsValid()?SceneManager.UnloadSceneAsync(scene):null;cleaning=true;
        }
    }
}
