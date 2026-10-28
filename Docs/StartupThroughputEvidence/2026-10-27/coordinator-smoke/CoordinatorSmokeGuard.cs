// Temporary development guard for ONE explicitly armed copied test scene.
// It never starts Play, alters settings, saves scenes, or kills a process.
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using AshenVoxel.Exploration;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace AshenVoxel.Validation
{
    [InitializeOnLoad] public static class CoordinatorSmokeGuard
    {
        const long GiB=1024L*1024*1024,MiB=1024L*1024;
        const string ScenePath="Assets/_Validation/CoordinatorSmoke/ExplorationSmoke_20261027.unity";
        static readonly string Root=Path.GetDirectoryName(Application.dataPath);
        static readonly string Folder=Path.Combine(Root,".terrain-cache/coordinator-smoke");
        static readonly string ArmPath=Path.Combine(Folder,"arm.json");
        static double next;
        [Serializable] sealed class Arm
        {
            public string marker="",run="",stopReason="";
            public int pid=0;
            public long startTicks=0,baselinePrivate,minimumHeadroom,peakPrivate,minHeadroomSeen;
            public double started,readyAt=-1;
            public bool began,finished,originalPaused;
        }
        [Serializable] sealed class Row
        {
            public string utc,status,phase;
            public double elapsed,frameDelta,x,y,z;
            public bool ready,failed;
            public long privateBytes,headroomBytes,graphicsDriverBytes,farBytes,detailBytes,contactBytes,prototypeBytes,scratchBytes;
            public int contactTiles,contactPending,farPending,residentInstances;
        }
        [StructLayout(LayoutKind.Sequential)] struct Performance
        {
            public uint cb;public UIntPtr commit,limit,peak,physical,available,cache,kernel, paged,nonpaged,pageSize;
            public uint handles,processes,threads;
        }
        [DllImport("psapi.dll",SetLastError=true)] static extern bool GetPerformanceInfo(out Performance value,uint size);
        [StructLayout(LayoutKind.Sequential)] struct ProcessCounters
        {
            public uint cb,pageFaults;
            public UIntPtr peakWorkingSet,workingSet,peakPaged,paged,peakNonPaged,nonPaged,pagefile,peakPagefile,privateUsage;
        }
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("psapi.dll",SetLastError=true)] static extern bool GetProcessMemoryInfo(IntPtr process,out ProcessCounters value,uint size);
        static long PrivateBytes()
        {
            if(!GetProcessMemoryInfo(GetCurrentProcess(),out ProcessCounters p,(uint)Marshal.SizeOf(typeof(ProcessCounters)))||p.privateUsage.ToUInt64()==0)
                throw new InvalidOperationException("Native private-memory telemetry unavailable");
            return checked((long)p.privateUsage.ToUInt64());
        }
        static long Headroom()
        {
            if(!GetPerformanceInfo(out Performance p,(uint)Marshal.SizeOf(typeof(Performance))))throw new InvalidOperationException("Commit telemetry unavailable");
            return checked((long)((p.limit.ToUInt64()-p.commit.ToUInt64())*p.pageSize.ToUInt64()));
        }
        static CoordinatorSmokeGuard(){EditorApplication.update+=Poll;EditorApplication.playModeStateChanged+=State;}
        static Arm Load()
        {
            if(!File.Exists(ArmPath))return null;
            var a=JsonUtility.FromJson<Arm>(File.ReadAllText(ArmPath));
            if(a==null||a.marker!="coordinator-smoke-v1"||!Regex.IsMatch(a.run??"","^[a-zA-Z0-9_-]{1,80}$"))return null;
            using(var process=Process.GetCurrentProcess())
                if(a.pid!=process.Id||a.startTicks!=process.StartTime.ToUniversalTime().Ticks)return null;
            return a;
        }
        static bool Target()=>SceneManager.GetActiveScene().path==ScenePath;
        static string Output(Arm a)=>Path.Combine(Folder,a.run);
        static void Save(Arm a){File.WriteAllText(ArmPath,JsonUtility.ToJson(a,true));}
        static Row Sample(Arm a)
        {
            using(var process=Process.GetCurrentProcess())
            {
                process.Refresh();long free=Headroom();
                long ownPrivate=PrivateBytes();a.peakPrivate=Math.Max(a.peakPrivate,ownPrivate);
                if(a.minHeadroomSeen==0||free<a.minHeadroomSeen)a.minHeadroomSeen=free;
                var row=new Row{utc=DateTime.UtcNow.ToString("o"),phase=EditorApplication.isPlaying?"Play":"Editor",elapsed=a.began?EditorApplication.timeSinceStartup-a.started:0,
                    frameDelta=EditorApplication.isPlaying?Time.unscaledDeltaTime:0,privateBytes=ownPrivate,headroomBytes=free,graphicsDriverBytes=Profiler.GetAllocatedMemoryForGraphicsDriver()};
                var boot=UnityEngine.Object.FindFirstObjectByType<ExplorationBootstrap>();
                if(boot!=null)
                {
                    row.status=boot.Status;row.ready=boot.Ready;row.failed=boot.Failed;
                    if(boot.Ready&&a.readyAt<0)a.readyAt=row.elapsed;
                    if(boot.Vista!=null)
                    {
                        row.farBytes=boot.Vista.TerrainResidentMeshBytes;row.detailBytes=boot.Vista.TerrainDetailMeshBytes;row.contactBytes=boot.Vista.ContactMeshBytes;row.scratchBytes=boot.Vista.VoxelScratchBytes;
                        row.contactTiles=boot.Vista.ContactTiles;row.contactPending=boot.Vista.ContactPendingTiles;row.farPending=boot.Vista.PendingTiles;
                    }
                    if(boot.Population!=null){row.prototypeBytes=boot.Population.MeshBytes;row.residentInstances=boot.Population.ResidentInstances;}
                    if(boot.Player!=null){var p=boot.Player.Feet;row.x=p.X;row.y=p.Y;row.z=p.Z;}
                }
                return row;
            }
        }
        static void Record(Arm a,Row row)
        {Directory.CreateDirectory(Output(a));File.AppendAllText(Path.Combine(Output(a),"samples.jsonl"),JsonUtility.ToJson(row)+"\n");Save(a);}
        static void Stop(Arm a,string reason)
        {
            a.stopReason=reason;Save(a);
            // Scope is exact copied test scene plus exact current PID/start time.
            // Stop the owned Play test, never the user's main Editor process.
            if(Target()&&EditorApplication.isPlayingOrWillChangePlaymode){if(EditorApplication.isPlaying)EditorApplication.isPaused=true;EditorApplication.isPlaying=false;}
        }
        static void State(PlayModeStateChange state)
        {
            var a=Load();if(a==null||!Target())return;
            try
            {
                if(state==PlayModeStateChange.ExitingEditMode)
                {
                    a.baselinePrivate=PrivateBytes();
                    a.minimumHeadroom=Headroom();a.started=EditorApplication.timeSinceStartup;a.began=true;a.originalPaused=EditorApplication.isPaused;
                    Record(a,Sample(a));
                    if(a.minimumHeadroom<7*GiB/2)Stop(a,"Preflight below3.5GiB:1.5growth+1.5reserve+.5overshoot allowance");
                }
                else if(state==PlayModeStateChange.ExitingPlayMode){if(string.IsNullOrEmpty(a.stopReason))a.stopReason="UI ended owned smoke";Record(a,Sample(a));}
                else if(state==PlayModeStateChange.EnteredEditMode&&a.began)
                {
                    a.finished=true;Save(a);File.WriteAllText(Path.Combine(Output(a),"result.json"),JsonUtility.ToJson(a,true));
                    EditorApplication.isPaused=a.originalPaused;File.Move(ArmPath,ArmPath+".completed-"+a.run);
                }
            }
            catch(Exception e){Stop(a,"Guard exception: "+e.Message);UnityEngine.Debug.LogException(e);}
        }
        static void Poll()
        {
            if(EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+.2;
            var a=Load();if(a==null||!a.began||a.finished||!Target()||!EditorApplication.isPlaying)return;
            try
            {
                Row row=Sample(a);Record(a,row);
                string reason=row.headroomBytes<2*GiB?"Commit headroom below2GiB":row.privateBytes-a.baselinePrivate>=3*GiB/2?"Main private growth reached1.5GiB":
                    row.farBytes>=448*MiB?"Far payload safety observation threshold":row.contactBytes>128*MiB?"Contact payload exceeded128MiB":
                    row.prototypeBytes>512*MiB?"Prototype payload exceeded512MiB":row.failed?"Bootstrap failed":row.elapsed>60?"60second smoke complete":null;
                var boot=UnityEngine.Object.FindFirstObjectByType<ExplorationBootstrap>();
                if(boot?.Vista!=null&&(boot.Vista.TerrainDetailLimitBytes!=64*MiB||boot.Vista.ContactMeshLimitBytes!=128*MiB))reason="Native initial payload quota mismatch";
                if(boot!=null&&boot.LoadSavedOptions)reason="Copied smoke must not load saved options";
                if(boot?.ActiveSettings!=null)
                {
                    var s=boot.ActiveSettings;
                    if(s.ViewDistanceKm!=20||s.MaximumTerrainTiles!=192||s.VoxelFullDetailRadius!=16||s.EnableLiveRefinement||s.EnableForestHorizon||s.VegetationTriangleBudget!=1000000)
                        reason="Temporary smoke profile mismatch";
                }
                if(reason!=null)Stop(a,reason);
            }
            catch(Exception e){Stop(a,"Guard exception: "+e.Message);UnityEngine.Debug.LogException(e);}
        }
    }
}
