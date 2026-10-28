using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Diagnostics;
using AshenVoxel.Exploration;
using AshenVoxel.WorldScale;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AshenVoxel.Validation
{
    // Inert unless explicitly configured in the copied guarded smoke scene.
    // Uses the EXISTING deterministic controller bridge; no keyboard injection.
    // Streaming trial uses noclip during elevated flight. This is not a body-
    // collision or ordinary player-route acceptance test.
    [DefaultExecutionOrder(-1000)]
    public sealed class BoundedFlightReplay:MonoBehaviour
    {
        const string ScenePath="Assets/_Validation/CoordinatorSmoke/ExplorationSmoke_20261027.unity";
        [Serializable] public struct Sample
        {
            public double seconds,frameSeconds,x,y,z,originX,originY,originZ;
            public bool gate,waiting,terrainReady,flying,noClip;
            public double simulatedSeconds,deferredSeconds,skippedLongFrameSeconds;
            public int phase,farPending,contactPending;
            public long detailBytes,contactBytes;
        }
        [Serializable] sealed class Report
        {
            public string scope="Native controller bridge replay; elevated noclip flight, not collision/FPS acceptance. Input precedes default world Update; samples occur in LateUpdate after all Updates. Legs are time windows, not guaranteed equal distances.",status,stopReason,cleanupError="";
            public int count,gateEntries,gateExits,rebaseCount;
            public double elapsed,maxFrameSeconds;
            public double finalDistanceFromSpawn,finalHeightFromSpawn,deferredSeconds,skippedLongFrameSeconds;
            public bool walkingAttempted,walkingAccepted,finalFlying,finalNoClip;
            public Sample[] samples;
        }
        [Serializable] sealed class GuardArm
        {public string marker="",run="";public int pid=0;public long startTicks=0;public bool began=false,finished=false;}
        readonly Sample[] samples=new Sample[6000];
        ExplorationBootstrap boot;ExplorationController player;
        string run;bool configured,finished,oldEnabled,oldInput,oldNoClip,oldFlying,oldCapture,walkingAttempted,walkingAccepted;
        float oldFlightSpeed,oldFlightBoost;WorldPoint spawn;
        int count,phase,lastPhase=-1,entries,exits,rebases;
        bool lastGate,haveOrigin;WorldPoint lastOrigin;
        double elapsed,maxFrame,frameSeconds;
        public void Configure(ExplorationBootstrap target,string identifier)
        {
            if(configured||target==null||!target.Ready||target.Player==null||SceneManager.GetActiveScene().path!=ScenePath||
                target.gameObject.scene!=SceneManager.GetActiveScene()||target.Player.gameObject.scene!=target.gameObject.scene||
                !Application.isPlaying||!Regex.IsMatch(identifier??"","^[A-Za-z0-9_-]{1,60}$"))
                throw new InvalidOperationException("Replay requires one ready, explicitly guarded copied scene and bounded run identity.");
            string armPath=Path.Combine(Path.GetDirectoryName(Application.dataPath),".terrain-cache/coordinator-smoke/arm.json");
            var arm=File.Exists(armPath)?JsonUtility.FromJson<GuardArm>(File.ReadAllText(armPath)):null;
            using(var process=Process.GetCurrentProcess())
                if(arm==null||arm.marker!="coordinator-smoke-v1"||arm.run!=identifier||arm.pid!=process.Id||
                    arm.startTicks!=process.StartTime.ToUniversalTime().Ticks||!arm.began||arm.finished)
                    throw new InvalidOperationException("Current native guard identity not armed for this replay.");
            if(target.LoadSavedOptions||target.Vista.TerrainDetailLimitBytes!=64L*1024*1024||target.Vista.ContactMeshLimitBytes!=128L*1024*1024)
                throw new InvalidOperationException("Replay requires copied saved-options-disabled64/128MiB profile.");
            boot=target;player=boot.Player;run=identifier;spawn=player.Feet;
            oldEnabled=player.enabled;oldInput=player.InputEnabled;oldNoClip=player.Motor.NoClip;
            oldFlying=player.Motor.Flying;
            oldCapture=player.CapturePointerOnEnable;
            oldFlightSpeed=boot.ActiveSettings.FlightSpeed;oldFlightBoost=boot.ActiveSettings.FlightBoost;
            // Runtime clone only. Original scene/settings/options are never saved.
            configured=true;
            try
            {
                boot.ActiveSettings.FlightSpeed=40;boot.ActiveSettings.FlightBoost=4;
                player.ApplyMovementSettings(boot.ActiveSettings.Movement());
                player.CapturePointerOnEnable=false;player.enabled=false;player.SetInputEnabled(true);
                player.ResetAt(new WorldPoint(spawn.X,spawn.Y+220,spawn.Z),true);player.Motor.NoClip=true;
            }
            catch{Finish("Configuration failed; owned state restored");throw;}
        }
        void Update()
        {
            if(!configured||finished)return;
            try{AdvanceFrame();}
            catch(Exception e){Finish("Replay exception: "+e.Message);UnityEngine.Debug.LogException(e);}
        }
        void AdvanceFrame()
        {
            if(!Application.isPlaying||boot==null||player==null||SceneManager.GetActiveScene().path!=ScenePath){Finish("Scene/owner unavailable");return;}
            double dt=Time.unscaledDeltaTime;
            if(double.IsNaN(dt)||double.IsInfinity(dt)||dt<0){Finish("Invalid frame time");return;}
            frameSeconds=dt;elapsed+=dt;maxFrame=Math.Max(maxFrame,dt);
            phase=elapsed<4?0:elapsed<6?1:elapsed<10?2:elapsed<12?3:elapsed<17?4:5;
            var input=phase==0?new ExplorationMoveInput(1,0,sprint:true):phase==2?new ExplorationMoveInput(-1,0,sprint:true):
                phase==4&&player.Feet.Y>spawn.Y+5?new ExplorationMoveInput(0,0,vertical:-1):default;
            // Do not double-step through Player.Update: its component is disabled.
            // Advance retains its actual250ms cap/solver-debt behavior.
            player.Advance(input,dt);
            if(phase==5&&lastPhase!=5)
            {player.Motor.NoClip=false;walkingAttempted=true;walkingAccepted=player.Motor.SetFlying(false);}
            lastPhase=phase;
        }
        void LateUpdate()
        {
            if(!configured||finished)return;
            try{CaptureFrame();}
            catch(Exception e){Finish("Sample exception: "+e.Message);UnityEngine.Debug.LogException(e);}
        }
        void CaptureFrame()
        {
            if(boot==null||player==null){Finish("Owner unavailable during sample");return;}
            bool gate=boot.Vista.VoxelTravelDetailDeferred;
            if(gate&&!lastGate)entries++;if(!gate&&lastGate)exits++;lastGate=gate;
            WorldPoint origin=boot.Vista.RenderOrigin;
            if(haveOrigin&&(origin.X!=lastOrigin.X||origin.Y!=lastOrigin.Y||origin.Z!=lastOrigin.Z))rebases++;
            lastOrigin=origin;haveOrigin=true;
            if(count==samples.Length){Finish("Bounded sample capacity reached");return;}
            var feet=player.Feet;
            samples[count++]=new Sample{seconds=elapsed,frameSeconds=frameSeconds,x=feet.X,y=feet.Y,z=feet.Z,
                originX=origin.X,originY=origin.Y,originZ=origin.Z,gate=gate,waiting=player.Motor.WaitingForWorld,
                flying=player.Motor.Flying,noClip=player.Motor.NoClip,simulatedSeconds=player.Motor.SimulatedSeconds,
                deferredSeconds=player.DeferredMovementSeconds,skippedLongFrameSeconds=player.SkippedLongFrameInputSeconds,
                terrainReady=boot.Vista.IsTerrainReadyArea(feet.X,feet.Z,2),phase=phase,farPending=boot.Vista.PendingTiles,
                contactPending=boot.Vista.ContactPendingTiles,detailBytes=boot.Vista.TerrainDetailMeshBytes,contactBytes=boot.Vista.ContactMeshBytes};
            if(elapsed>=20)Finish("20second bounded controller replay complete");
        }
        public void Finish(string reason)
        {
            if(!configured||finished)return;finished=true;
            var exact=new Sample[count];Array.Copy(samples,exact,count);
            var report=new Report{status="REPLAY_RECORDED_NOT_PERFORMANCE_ACCEPTANCE",stopReason=reason,count=count,
                gateEntries=entries,gateExits=exits,rebaseCount=rebases,elapsed=elapsed,maxFrameSeconds=maxFrame,samples=exact,
                walkingAttempted=walkingAttempted,walkingAccepted=walkingAccepted};
            if(player!=null)
            {
                var feet=player.Feet;double dx=feet.X-spawn.X,dz=feet.Z-spawn.Z;
                report.finalDistanceFromSpawn=Math.Sqrt(dx*dx+dz*dz);report.finalHeightFromSpawn=feet.Y-spawn.Y;
                report.finalFlying=player.Motor.Flying;report.finalNoClip=player.Motor.NoClip;
                report.deferredSeconds=player.DeferredMovementSeconds;report.skippedLongFrameSeconds=player.SkippedLongFrameInputSeconds;
            }
            void Restore(Action action){try{action();}catch(Exception e){report.cleanupError+=e.Message+"; ";}}
            Restore(()=>{if(boot!=null&&boot.ActiveSettings!=null){boot.ActiveSettings.FlightSpeed=oldFlightSpeed;boot.ActiveSettings.FlightBoost=oldFlightBoost;}});
            if(Application.isPlaying&&player!=null)
            {
                Restore(()=>{if(boot!=null&&boot.ActiveSettings!=null)player.ApplyMovementSettings(boot.ActiveSettings.Movement());});
                Restore(()=>{if(boot!=null&&boot.Vista!=null)player.ResetAt(spawn,oldFlying);});
                Restore(()=>player.Motor.NoClip=oldNoClip);Restore(()=>player.SetInputEnabled(oldInput));
                Restore(()=>player.enabled=oldEnabled);Restore(()=>player.CapturePointerOnEnable=oldCapture);
            }
            try{string folder=Path.Combine(Path.GetDirectoryName(Application.dataPath),".terrain-cache/native-flight-replay",run);
                Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"report.json"),JsonUtility.ToJson(report,true));}
            catch(Exception e){UnityEngine.Debug.LogException(e);}
        }
        void OnDisable(){Finish("Replay disabled/owned Play ended");}
    }
}
