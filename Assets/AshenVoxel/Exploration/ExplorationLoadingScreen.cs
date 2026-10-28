using System;
using System.Collections;
using System.IO;
using UnityEngine;
using AshenVoxel.WorldScaleRuntime;

namespace AshenVoxel.Exploration
{
    public sealed partial class ExplorationBootstrap
    {
        readonly WorldLoadingProgress loadingProgress=new WorldLoadingProgress();
        WorldLoadingSnapshot loadingSnapshot=new WorldLoadingSnapshot();
        bool lighterLoadingSession,restartingLoading,loadingDetails;
        double nextLoadingSample;
        string loadingError="",loadingNotice="";
        GUIStyle loadingTitle,loadingBody,loadingSmall,loadingStep,loadingButton;
        public WorldLoadingSnapshot LoadingSnapshot=>loadingSnapshot;
        void OnEnable(){Application.logMessageReceived+=ObserveLoadingError;}
        void OnDisable(){Application.logMessageReceived-=ObserveLoadingError;}
        void ObserveLoadingError(string condition,string trace,LogType type)
        {if(!Ready&&(type==LogType.Error||type==LogType.Exception||type==LogType.Assert))loadingError=condition;}
        void BeginLoading()
        {
            loadingProgress.Reset(Time.realtimeSinceStartupAsDouble);nextLoadingSample=0;
            loadingError=loadingNotice="";Ready=Failed=false;
            Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
        }
        void SetLoadingPhase(WorldLoadingPhase phase)
        {
            loadingProgress.SetPhase(phase,Time.realtimeSinceStartupAsDouble);
            loadingSnapshot.phase=phase.ToString();loadingSnapshot.ready=phase==WorldLoadingPhase.Complete;
            loadingSnapshot.failed=phase==WorldLoadingPhase.Failed;nextLoadingSample=0;
        }
        void UpdateLoadingProgress()
        {
            double now=Time.realtimeSinceStartupAsDouble;
            if(now<nextLoadingSample)return;nextLoadingSample=now+(Ready?1:.25);
            var s=loadingSnapshot;s.phase=loadingProgress.Phase.ToString();s.utc=DateTime.UtcNow.ToString("O");
            s.ready=Ready;s.failed=Failed;s.status=Status;s.quality=terrainQualityCaptured?appliedTerrainQuality.Label:"Reading settings";
            s.lighterSession=lighterLoadingSession;s.error=Failed?Status:loadingError;
            s.sourceReady=LiveRefinement?.NearFrontReadyCells??0;s.sourceRequired=LiveRefinement?.NearFrontRequiredCells??0;
            s.sourceStatus=LiveRefinement?.Status??"Reading bundled world data";
            s.sourceFallback=loadingProgress.Phase>WorldLoadingPhase.Source&&ActiveSettings!=null&&ActiveSettings.EnableLiveRefinement&&LiveRefinement!=null&&!LiveRefinement.NearFrontSourceReady;
            s.terrainResident=Vista?.ResidentTiles??0;s.terrainPlanned=Vista?.TerrainPlannedTiles??0;s.terrainPending=Vista?.PendingTiles??0;
            s.distantCoverageReady=Vista!=null&&Vista.HasActiveCoverage;
            s.terrainVisible=Vista?.ActiveTerrainTiles??0;s.terrainCoverageGroupsPending=Vista?.PendingCoverageGroups??0;
            s.distantPlanComplete=Vista!=null&&Vista.TerrainPlanCoverageComplete;
            s.distantSweepQuads=Vista?.DistantSweepQuads??0;s.distantSweepCompletedTiles=Vista?.DistantSweepCompletedTiles??0;s.distantSweepTiles=Vista?.DistantSweepTiles??0;
            s.distantSweepDeferredTiles=Vista?.DistantSweepDeferredTiles??0;
            s.distantSweepSpacingMeters=Vista?.DistantSweepSpacingMeters??0;s.distantSweepLargestSpacingMeters=Vista?.DistantSweepLargestSpacingMeters??0;
            s.distantSweepSpacingLimitedTiles=Vista?.DistantSweepSpacingLimitedTiles??0;
            s.overviewSourceStatus=OverviewRefinement?.Status??"Waiting for baked source";
            s.overviewSpacingMeters=OverviewRefinement?.PassSpacingMeters??0;s.overviewCompletedCells=OverviewRefinement?.CompletedCells??0;s.overviewDesiredCells=OverviewRefinement?.DesiredCells??0;
            s.terrainSeamPending=Vista?.TerrainSeamPending??0;s.terrainSeamBytes=Vista?.TerrainSeamBytes??0;
            s.contactReady=Vista?.ContactTiles??0;s.contactPending=Vista?.ContactPendingTiles??0;
            s.buildFailures=Vista?.VoxelBuildFailures??0;s.terrainStatus=Vista?.TerrainQualityLimitReason??"Waiting for source";
            s.memoryBlocked=Vista!=null&&Vista.VoxelMemoryPressureBlocked;s.availableCommitBytes=Vista?.VoxelAvailableCommitBytes??-1;
            s.requiredCommitBytes=Vista?.VoxelRequiredCommitBytes??WorldExplorer.VoxelCommitReserveBytes+64L*1048576;
            if(Vista!=null&&!string.IsNullOrEmpty(Vista.LastVoxelBuildError))s.error=Vista.LastVoxelBuildError;
            s.modelsReady=Population?.CompletedModels??0;s.modelsTotal=Population?.TotalModels??0;
            s.populationCells=Population?.ResidentCells??0;s.populationPending=Population?.PendingCells??0;
            s.objectStatus=Population?.Status??"Objects wait until starting ground is ready";
            var probe=Ready&&Player!=null?Player.Feet:spawn;
            s.terrainReady=Vista!=null&&Vista.IsTerrainReadyArea(probe.X,probe.Z,2);
            s.objectsReady=Population!=null&&Population.Ready;s.spawnReady=Population!=null&&Population.IsReady(probe.X,probe.Z,16);
            // The startup map is hidden after Ready. Preserve its final sample;
            // live terrain/memory counters above continue without90 hidden probes.
            if(!Ready&&Vista!=null)
            {
                s.groundSampleUtc=s.utc;
                s.groundReadySamples=0;
                for(int z=0;z<9;z++)for(int x=0;x<9;x++)s.ground[z*9+x]=Vista.IsTerrainReady(probe.X+(x-4)*16,probe.Z+(4-z)*16);
                for(int z=-1;z<=1;z++)for(int x=-1;x<=1;x++)if(Vista.IsTerrainReady(probe.X+x*2,probe.Z+z*2))s.groundReadySamples++;
            }
            switch(loadingProgress.Phase)
            {
                case WorldLoadingPhase.Source:loadingProgress.Observe(s.sourceReady,now);break;
                case WorldLoadingPhase.Terrain:loadingProgress.Observe(s.terrainResident+s.contactReady+s.groundReadySamples*100000,now);break;
                case WorldLoadingPhase.Objects:loadingProgress.Observe(s.modelsReady,now);break;
                case WorldLoadingPhase.Spawn:loadingProgress.Observe(s.populationCells+(s.spawnReady?1000000:0)+(s.terrainReady?1000000:0),now);break;
            }
            s.elapsedSeconds=loadingProgress.Elapsed(now);s.stageSeconds=loadingProgress.StageElapsed(now);s.secondsWithoutProgress=loadingProgress.Idle(now);s.stalled=loadingProgress.Stalled(now);
        }
        public string SaveLoadingDiagnostics()
        {
            UpdateLoadingProgress();string folder=Path.Combine(Application.persistentDataPath,"LoadingReports");Directory.CreateDirectory(folder);
            string path=Path.Combine(folder,"loading-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".json");
            File.WriteAllText(path,JsonUtility.ToJson(loadingSnapshot,true));return path;
        }
        public void RestartLoading(bool lighter)
        {
            if(Ready||restartingLoading)return;
            restartingLoading=true;StopAllCoroutines();StartCoroutine(RestartLoadingRoutine(lighter));
        }
        IEnumerator RestartLoadingRoutine(bool lighter)
        {
            Status="Releasing the previous loading attempt…";Player?.SetInputEnabled(false);
            // Release the same session resources as scene teardown. No saved
            // options or source data are written by this recovery action.
            OnDestroy();if(worldObject!=null)Destroy(worldObject);
            worldObject=null;Vista=null;Population=null;LiveRefinement=null;OverviewRefinement=null;Player=null;VoxelTerrain=null;WorldField=null;Source=null;
            ownedPipeline=null;sky=null;instanceMaterial=null;ActiveSettings=null;sun=null;distantRocks=null;bees=null;betaMatter=null;
#if UNITY_5_3_OR_NEWER
            Upscaler=null;Wind=null;
#endif
            terrainQualityCaptured=false;settingsOpen=false;loadingSnapshot=new WorldLoadingSnapshot();
            yield return null;yield return Resources.UnloadUnusedAssets();GC.Collect();
            lighterLoadingSession=lighter;restartingLoading=false;yield return Start();
        }
        static readonly string[] LoadingNames={"Read world data","Prepare terrain source","Build ground and distant view","Load trees and rocks","Verify safe spawn"};
        static readonly Color LoadingInk=new Color(.9f,.93f,.88f),LoadingMuted=new Color(.58f,.66f,.65f),LoadingGreen=new Color(.43f,.72f,.43f),LoadingAmber=new Color(.94f,.69f,.30f);
        void LoadingStyles()
        {
            if(loadingTitle!=null)return;
            loadingTitle=new GUIStyle(GUI.skin.label){fontSize=30,fontStyle=FontStyle.Bold,normal={textColor=LoadingInk}};
            loadingBody=new GUIStyle(GUI.skin.label){fontSize=16,wordWrap=true,normal={textColor=LoadingInk}};
            loadingSmall=new GUIStyle(loadingBody){fontSize=13,normal={textColor=LoadingMuted}};
            loadingStep=new GUIStyle(loadingBody){fontSize=17,fontStyle=FontStyle.Bold};
            loadingButton=new GUIStyle(GUI.skin.button){fontSize=14,padding=new RectOffset(12,12,8,8)};
        }
        static void LoadingRect(Rect r,Color c){Color prior=GUI.color;GUI.color=c;GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=prior;}
        void DrawLoadingScreen()
        {
            LoadingStyles();var s=loadingSnapshot;double now=Time.realtimeSinceStartupAsDouble;
            Matrix4x4 old=GUI.matrix;Color color=GUI.color;int depth=GUI.depth;GUI.depth=-1000;
            try
            {
                GUI.matrix=Matrix4x4.identity;LoadingRect(new Rect(0,0,Screen.width,Screen.height),new Color(.035f,.055f,.062f));
                float scale=Mathf.Min((Screen.width-24)/920f,(Screen.height-24)/700f);
                GUI.matrix=Matrix4x4.TRS(new Vector3((Screen.width-920*scale)/2,(Screen.height-700*scale)/2,0),Quaternion.identity,Vector3.one*scale);
                LoadingRect(new Rect(0,0,920,700),new Color(.065f,.092f,.10f));
                GUI.Label(new Rect(36,22,600,24),"ASHEN VOXEL   /   WORLD GENERATION",loadingSmall);
                GUI.Label(new Rect(36,54,850,48),Failed?"World loading needs attention":"Preparing your world",loadingTitle);
                GUI.Label(new Rect(38,104,840,30),"Elapsed "+WorldLoadingProgress.Duration(loadingProgress.Elapsed(now))+"   ·   Current step "+WorldLoadingProgress.Duration(loadingProgress.StageElapsed(now))+"   ·   "+(lighterLoadingSession?"Lighter settings for this run":s.quality),loadingSmall);
                int phase=(int)loadingProgress.Phase;
                for(int i=0;i<5;i++)LoadingRect(new Rect(36+i*173,142,165,5),i<phase&&!Failed?LoadingGreen:i==phase?LoadingAmber:new Color(.16f,.21f,.22f));
                // These squares are actual terrain readiness samples, not an animation.
                GUI.Label(new Rect(36,170,260,26),"Starting area",loadingStep);
                for(int z=0;z<9;z++)for(int x=0;x<9;x++)
                {
                    bool center=x==4&&z==4;var rect=new Rect(38+x*26,206+z*26,23,23);
                    LoadingRect(rect,s.ground[z*9+x]?LoadingGreen:new Color(.16f,.22f,.23f));
                    if(center){LoadingRect(new Rect(rect.x+8,rect.y+8,7,7),LoadingInk);}
                }
                GUI.Label(new Rect(36,448,270,38),"128 m across · north ↑\nGreen: ground ready   •   White: you",loadingSmall);
                for(int i=0;i<5;i++)
                {
                    float y=176+i*59;bool complete=i<phase&&phase<6;bool current=i==phase;
                    GUI.Label(new Rect(330,y,535,25),(complete?"✓  ":current?"›  ":"·  ")+LoadingNames[i],loadingStep);
                    string detail=i==0?(phase>0?"Bundled height, climate and biome data read":"Checking bundled world files"):
                        i==1?(phase==1?s.sourceReady+" / "+s.sourceRequired+" nearby source cells ready":s.sourceFallback?"Bundled terrain active; finer source will continue later":phase>1?"Terrain source prepared":"Waiting"):
                        i==2?s.groundReadySamples+" / 9 spawn checks · "+s.terrainVisible+" visible / "+s.terrainResident+" cached distant tiles":
                        i==3?s.modelsReady+" / "+s.modelsTotal+" complete models":s.spawnReady&&s.terrainReady?"Ground and nearby objects ready":s.populationCells+" population cells · "+s.populationPending+" queued";
                    GUI.Label(new Rect(352,y+27,525,28),detail,loadingSmall);
                }
                bool attention=Failed||s.memoryBlocked||s.stalled||!string.IsNullOrEmpty(s.error);
                LoadingRect(new Rect(36,490,848,92),attention?new Color(.19f,.15f,.095f):new Color(.09f,.14f,.15f));
                string message=Failed?s.error:s.memoryBlocked?"Waiting for memory — terrain work is paused safely":s.stalled?"No completed work for "+WorldLoadingProgress.Duration(loadingProgress.Idle(now))+" — this step may be stuck":!string.IsNullOrEmpty(s.error)?"A loading error was reported":Status;
                GUI.Label(new Rect(50,500,820,43),message,loadingBody);
                string hint=s.memoryBlocked?"Available commit: "+s.availableCommitBytes/1048576+" MiB · resume needs at least "+s.requiredCommitBytes/1048576+" MiB. Close other apps or restart lighter.":
                    s.stalled?"You can save a diagnostic report or retry. Movement stays locked until solid ground and a safe spawn are ready.":"Last completed work "+WorldLoadingProgress.Duration(loadingProgress.Idle(now))+" ago · distant detail continues after loading.";
                GUI.Label(new Rect(50,546,820,33),hint,loadingSmall);
                // The small heartbeat only signals responsive UI, never generation progress.
                LoadingRect(new Rect(868,31,7,7),((int)(now*2)&1)==0?LoadingGreen:LoadingMuted);
                if(GUI.Button(new Rect(36,598,184,38),loadingDetails?"Hide details":"Show details",loadingButton))loadingDetails=!loadingDetails;
                if(GUI.Button(new Rect(230,598,190,38),"Save diagnostics",loadingButton))
                {try{loadingNotice="Saved: "+SaveLoadingDiagnostics();}catch(Exception e){loadingNotice="Could not save report: "+e.Message;}}
                GUI.enabled=!restartingLoading;
                if(GUI.Button(new Rect(430,598,184,38),"Retry loading",loadingButton))RestartLoading(lighterLoadingSession);
                if(GUI.Button(new Rect(624,598,260,38),"Restart with lighter settings",loadingButton))RestartLoading(true);
                GUI.enabled=true;
                GUI.Label(new Rect(36,646,848,43),string.IsNullOrEmpty(loadingNotice)?"Lighter: Normal terrain, 32 m ground detail, fewer distant objects. Saved preferences are unchanged.":loadingNotice,loadingSmall);
                if(loadingDetails)DrawLoadingDetails(s);
            }
            finally{GUI.matrix=old;GUI.color=color;GUI.depth=depth;GUI.enabled=true;}
        }
        void DrawLoadingDetails(WorldLoadingSnapshot s)
        {
            LoadingRect(new Rect(28,162,864,320),new Color(.045f,.07f,.08f));
            GUI.Label(new Rect(44,174,830,29),"Live loading details",loadingStep);
            GUI.Label(new Rect(44,211,830,255),"Source: "+s.sourceStatus+"\n\nTerrain: "+s.terrainStatus+" · "+s.terrainPending+" queued · "+s.contactPending+" ground pending · "+s.buildFailures+" build failures\n\nObjects: "+s.objectStatus+"\n\nLast error: "+(string.IsNullOrEmpty(s.error)?"None":s.error)+"\n\nUpdated "+s.utc,loadingSmall);
        }
        void DrawTerrainStreamingStatus()
        {
            if(Vista==null)return;
            var s=loadingSnapshot;
            if(!s.memoryBlocked&&s.distantPlanComplete&&string.IsNullOrEmpty(s.error))return;
            GUI.Box(new Rect(14,90,720,64),GUIContent.none);
            string state=s.memoryBlocked?"Terrain paused for memory: "+s.availableCommitBytes/1048576+" MiB available; needs at least "+s.requiredCommitBytes/1048576+" MiB":
                !string.IsNullOrEmpty(s.error)?"Terrain build error: "+s.error:s.distantCoverageReady?"Expanding distant terrain coverage…":"Preparing distant terrain coverage…";
            GUI.Label(new Rect(28,96,690,27),state,text);
            GUI.Label(new Rect(28,123,690,25),s.terrainVisible+" visible · "+s.terrainResident+" cached / "+s.terrainPlanned+" planned · "+s.terrainCoverageGroupsPending+" coverage groups pending · F1 details",text);
        }
    }
}
