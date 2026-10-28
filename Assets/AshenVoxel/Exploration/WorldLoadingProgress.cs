using System;

namespace AshenVoxel.Exploration
{
    public enum WorldLoadingPhase { Data, Source, Terrain, Objects, Spawn, Complete, Failed }

    /// <summary>Progress is observed completed work, never elapsed-time interpolation.
    /// A changing heartbeat or status message does not conceal a stalled stage.</summary>
    public sealed class WorldLoadingProgress
    {
        public WorldLoadingPhase Phase {get;private set;}
        double started,stageStarted,progressAt,highWater;
        public void Reset(double now){Phase=WorldLoadingPhase.Data;started=stageStarted=progressAt=now;highWater=0;}
        public void SetPhase(WorldLoadingPhase phase,double now)
        {if(Phase==phase)return;Phase=phase;stageStarted=progressAt=now;highWater=0;}
        public void Observe(double completed,double now)
        {if(double.IsNaN(completed)||double.IsInfinity(completed)||completed<=highWater)return;highWater=completed;progressAt=now;}
        public double Elapsed(double now)=>Math.Max(0,now-started);
        public double StageElapsed(double now)=>Math.Max(0,now-stageStarted);
        public double Idle(double now)=>Math.Max(0,now-progressAt);
        public bool Stalled(double now)=>Phase!=WorldLoadingPhase.Complete&&Phase!=WorldLoadingPhase.Failed&&Idle(now)>=20;
        public static string Duration(double seconds)
        {int value=(int)Math.Max(0,Math.Min(int.MaxValue,seconds));return value>=60?(value/60)+"m "+(value%60).ToString("00")+"s":value+"s";}
        public static float Fraction(int completed,int total)=>total<=0?0:(float)Math.Max(0,Math.Min(1,completed/(double)total));
    }

    [Serializable] public sealed class WorldLoadingSnapshot
    {
        public string phase,status,sourceStatus,terrainStatus,objectStatus,error,quality,utc,groundSampleUtc;
        public double elapsedSeconds,stageSeconds,secondsWithoutProgress;
        public bool failed,stalled,memoryBlocked,ready,sourceFallback,terrainReady,objectsReady,spawnReady,lighterSession,distantCoverageReady,distantPlanComplete;
        public long availableCommitBytes=-1,requiredCommitBytes;
        public int sourceReady,sourceRequired,terrainResident,terrainPlanned,terrainPending,contactReady,contactPending,buildFailures,modelsReady,modelsTotal,populationCells,populationPending,groundReadySamples;
        public int terrainVisible,terrainCoverageGroupsPending;
        public int distantSweepQuads,distantSweepCompletedTiles,distantSweepTiles,distantSweepDeferredTiles;
        public double distantSweepSpacingMeters,distantSweepLargestSpacingMeters;
        public int distantSweepSpacingLimitedTiles;
        public string overviewSourceStatus;
        public int overviewSpacingMeters,overviewCompletedCells,overviewDesiredCells,terrainSeamPending;
        public long terrainSeamBytes;
        // A fixed 9x9 local ground readiness map:128m across, north at the top.
        public bool[] ground=new bool[81];
    }
}
