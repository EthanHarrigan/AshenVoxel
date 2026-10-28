using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using AshenVoxel.WorldScale;
using AshenVoxel.WorldScaleRuntime;
using UnityEngine;

namespace AshenVoxel.Exploration.Editor
{
    /// <summary>Explicit main-thread observation only. No Tick/replan, source sampling,
    /// GPU readback, queue mutation, camera movement or scene changes.</summary>
    public static class ExplorationTerrainDiagnostic
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        const int MaximumRows=8192;
        static readonly int[] Levels={0,8,16,32,64,128,256};
        static readonly double[] Edges={0,250,1000,3000,6000,12000,double.PositiveInfinity};
        static object Field(object o,string name)=>o?.GetType().GetField(name,Flags)?.GetValue(o);
        static bool Bool(object o,string name)=>Field(o,name) is bool value&&value;
        static int Int(object o,string name)=>Field(o,name) is int value?value:0;
        [Serializable] sealed class TileRow
        {
            public string key,reason;
            public int level,currentColumns,effectiveTargetColumns,supportedRequestColumns,faceCapColumns;
            public double tileMeters,nearest3dMeters,centerHorizontalMeters,currentSpacingMeters,effectiveTargetSpacingMeters,supportedRequestSpacingMeters;
            public bool desired,active,resident,eligible,dirty,queued,building,budgetDeferred,faceLimited,pixelTargetUnmet;
            public long deferredDetailBytes;
        }
        [Serializable] sealed class DistanceBucket
        {
            public string interval;
            public int desired,active,activeOnly,resident,missing,eligible,eligibleAtEffectiveTarget,eligibleAtSupportedRequest,dirty,queued,building,budgetDeferred,faceLimited,pixelTargetUnmet;
            public long currentSampleColumns,targetSampleColumns,supportedRequestSampleColumns;
            public int[] currentColumnsTileCounts=new int[Levels.Length],targetColumnsTileCounts=new int[Levels.Length];
        }
        [Serializable] sealed class CaptureData
        {
            public string diagnosticVersion="terrain-convergence-2-memory",utc,unity,sourceIdentity,bootStatus,terrainStatus,limitReason,lastBuildError;
            public string appliedPreset,selectedPreset,notes;
            public bool playing,ready,failed,restartPending,validationProfile,voxelTerrain,greedyVisible,coarseStartup,planActivationPending,planning,travelDeferred,refinementScanPending;
            public bool workerPresent,workerCompleted,buildPresent,buildContact,truncated,eligibleEffectiveTargetsReached,eligibleSupportedRequestsReached;
            public int frame,width,height,configuredPlanLimit,appliedPlanLimit,residentTiles,activeTiles,desiredTiles,activeOnlyTiles,missingDesired,eligibleDesired,eligibleBelowEffective,eligibleBelowSupported;
            public int pendingTotal,pendingQueued,qualityPending,qualityDeferred,qualityBuilds,planSplitsPending,faceLimited,pixelLimited,contactReady,contactPending,contactDeferred,buildFailures,uploadsThisFrame;
            public float targetPixels,splitFactor,fieldOfView;
            public double x,y,z,originX,originY,originZ,forwardX,forwardY,forwardZ,focalPixels,workerMilliseconds,uploadWorkMilliseconds,collectionMilliseconds;
            public long farMeshBytes,farDetailBytes,farDetailLimitBytes,coarsePlanReserveBytes,transitionLimitBytes,contactBytes,contactLimitBytes,uploadScratchBytes;
            public long processPrivateBytes,processWorkingSetBytes,managedHeapEstimateBytes,monoUsedBytes,monoReservedBytes,unityAllocatedBytes,unityReservedBytes,retainedHeightBytes;
            public long availableCommitBytes,commitReserveBytes;public bool memoryPressureBlocked,contactAltitudeDeferred,contactAltitudeBoundsKnown;public int altitudeHeldContactTiles,altitudeRetiredContactTiles;public double contactAltitudeCeilingMeters;
            public int gpuMemoryCapacityMiB;
            public bool processCountersAvailable,broadLithologyApplied,distantRocksEnabled;public int distantRockResident,distantRockDrawn;public long distantRockTriangles;public string distantRockStatus;
            public int[] columnLevels=Levels;
            public DistanceBucket[] distanceBuckets;
            public TileRow[] tiles;
        }
        static int LevelIndex(int n){for(int i=0;i<Levels.Length;i++)if(Levels[i]==n)return i;return 0;}
        static int Bucket(double distance){for(int i=0;i<Edges.Length-1;i++)if(distance<Edges[i+1])return i;return Edges.Length-2;}
        static string Interval(int i)=>Edges[i]+".."+(double.IsPositiveInfinity(Edges[i+1])?"infinity":Edges[i+1].ToString())+"m";
        /// <summary>Writes one timestamped JSON. Invoke from the existing Editor menu
        /// on Unity's main thread. It also works before Ready, to expose coverage stalls.</summary>
        public static void Capture(ExplorationBootstrap boot,string outputDirectory)
        {
            if(boot==null||boot.Vista==null)throw new ArgumentException("An existing ExplorationBootstrap with Vista is required.");
            if(string.IsNullOrWhiteSpace(outputDirectory))throw new ArgumentException(nameof(outputDirectory));
            var clock=Stopwatch.StartNew();var w=boot.Vista;var camera=w.MainCamera;var eye=w.GlobalCamera;var origin=w.RenderOrigin;
            var applied=boot.AppliedTerrainQuality;
            var d=new CaptureData{utc=DateTime.UtcNow.ToString("o"),unity=Application.unityVersion,frame=Time.frameCount,playing=Application.isPlaying,
                ready=boot.Ready,failed=boot.Failed,bootStatus=boot.Status,terrainStatus=w.Status,sourceIdentity=w.TerrainField?.SourceIdentity,
                appliedPreset=applied.Label,selectedPreset=boot.ActiveSettings==null?"unavailable":boot.ActiveSettings.TerrainQuality.ToString(),restartPending=boot.TerrainQualityRestartPending,
                validationProfile=boot.TerrainQualityValidationProfile,appliedPlanLimit=applied.MaximumTiles,configuredPlanLimit=w.MaximumTiles,
                voxelTerrain=w.UseVoxelLodTerrain,greedyVisible=w.VoxelGreedyVisibleDetail,targetPixels=w.VoxelTargetPixels,splitFactor=w.VoxelPlanSplitFactor,
                x=eye.X,y=eye.Y,z=eye.Z,originX=origin.X,originY=origin.Y,originZ=origin.Z,
                width=camera==null?Screen.width:camera.pixelWidth,height=camera==null?Screen.height:camera.pixelHeight,fieldOfView=camera==null?0:camera.fieldOfView,
                residentTiles=w.ResidentTiles,desiredTiles=w.TerrainPlannedTiles,pendingTotal=w.PendingTiles,qualityPending=w.VoxelRefinementPendingTiles,
                qualityDeferred=w.VoxelRefinementDeferredTiles,qualityBuilds=w.VoxelRefinementBuilds,planSplitsPending=w.TerrainPlanPendingSplits,
                faceLimited=w.VoxelFaceCapacityLimitedTiles,pixelLimited=w.PixelTargetLimitedTiles,contactReady=w.ContactTiles,contactPending=w.ContactPendingTiles,
                contactDeferred=w.ContactDeferredTiles,buildFailures=w.VoxelBuildFailures,lastBuildError=w.LastVoxelBuildError,uploadsThisFrame=w.MeshUploadsLastFrame,
                workerMilliseconds=w.LastVoxelWorkerMilliseconds,uploadWorkMilliseconds=w.LastMeshBuildMilliseconds,limitReason=w.TerrainQualityLimitReason,
                farMeshBytes=w.TerrainResidentMeshBytes,farDetailBytes=w.TerrainDetailMeshBytes,farDetailLimitBytes=w.TerrainDetailLimitBytes,
                coarsePlanReserveBytes=w.TerrainCoveragePlanReserveBytes,transitionLimitBytes=w.TerrainTransitionMeshLimitBytes,
                contactBytes=w.ContactMeshBytes,contactLimitBytes=w.ContactMeshLimitBytes,uploadScratchBytes=w.VoxelScratchBytes,
                coarseStartup=Bool(w,"coarseStartup"),planActivationPending=Bool(w,"planActivationPending"),planning=w.PlanningInProgress,
                travelDeferred=w.VoxelTravelDetailDeferred,refinementScanPending=Bool(w,"voxelRefinementScanPending")};
            if(camera!=null){var f=camera.transform.forward;d.forwardX=f.x;d.forwardY=f.y;d.forwardZ=f.z;}
            var type=typeof(WorldExplorer);var quads=type.GetMethod("VoxelQuadsFor",Flags);var visible=type.GetMethod("InVoxelView",Flags);var distance=type.GetMethod("DistanceSquared",Flags);
            var focalProperty=type.GetProperty("FocalPixels",Flags);
            if(quads==null||visible==null||distance==null||focalProperty==null)throw new InvalidOperationException("Terrain diagnostic runtime contract changed.");
            d.focalPixels=(double)focalProperty.GetValue(w);
            var desired=Field(w,"desiredTiles") as IDictionary;var surfaces=Field(w,"surfaces") as IDictionary;
            var active=Field(w,"active") as HashSet<TileKey>;var dirty=Field(w,"externallyDirty") as HashSet<TileKey>;
            var deferred=Field(w,"voxelDeferredQuads") as IDictionary;var faceCaps=Field(w,"voxelCapacityQuads") as IDictionary;
            if(desired==null||surfaces==null||active==null||dirty==null||deferred==null||faceCaps==null)throw new InvalidOperationException("Terrain diagnostic dictionary contract changed.");
            using(var process=Process.GetCurrentProcess()){process.Refresh();d.processPrivateBytes=process.PrivateMemorySize64;d.processWorkingSetBytes=process.WorkingSet64;}
            d.processCountersAvailable=d.processPrivateBytes>0;d.broadLithologyApplied=boot.AppliedBroadLithology;
            var rocks=boot.DistantRockPatch;if(rocks!=null){d.distantRocksEnabled=rocks.ShowDistantRocks;d.distantRockResident=rocks.ResidentRepresentatives;d.distantRockDrawn=rocks.DrawnRepresentatives;d.distantRockTriangles=rocks.DrawnTriangles;d.distantRockStatus=rocks.Status;}
            d.managedHeapEstimateBytes=GC.GetTotalMemory(false);d.monoUsedBytes=UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();d.monoReservedBytes=UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong();
            d.unityAllocatedBytes=UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();d.unityReservedBytes=UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong();d.gpuMemoryCapacityMiB=SystemInfo.graphicsMemorySize;
            d.availableCommitBytes=w.VoxelAvailableCommitBytes;d.commitReserveBytes=WorldExplorer.VoxelCommitReserveBytes;d.memoryPressureBlocked=w.VoxelMemoryPressureBlocked;
            d.contactAltitudeDeferred=w.ContactAltitudeDeferred;d.contactAltitudeBoundsKnown=w.ContactAltitudeBoundsKnown;d.contactAltitudeCeilingMeters=w.ContactAltitudeCeilingMeters;d.altitudeHeldContactTiles=w.ContactAltitudeHeldTiles;d.altitudeRetiredContactTiles=w.ContactAltitudeRetiredTiles;
            foreach(DictionaryEntry e in surfaces)if(Field(e.Value,"LocalHeights") is float[] heights)d.retainedHeightBytes+=heights.LongLength*4;
            if(Field(w,"contactSurfaces") is IDictionary contacts)foreach(DictionaryEntry e in contacts)if(Field(e.Value,"LocalHeights") is float[] heights)d.retainedHeightBytes+=heights.LongLength*4;
            d.activeTiles=active.Count;
            var queued=new HashSet<TileKey>();var pending=Field(w,"pending") as IEnumerable;
            if(pending!=null)foreach(object work in pending)if(!Bool(work,"Contact")&&Field(work,"Tile") is LodTile tile){queued.Add(tile.Key);d.pendingQueued++;}
            object current=Field(w,"currentBuild"),currentWork=Field(current,"Work");var buildingTile=Field(currentWork,"Tile") as LodTile;
            d.buildPresent=current!=null;d.buildContact=Bool(currentWork,"Contact");
            var worker=Field(w,"voxelBuildJob") as Task;d.workerPresent=worker!=null;d.workerCompleted=worker!=null&&worker.IsCompleted;
            var all=new Dictionary<TileKey,LodTile>();foreach(DictionaryEntry e in desired)all[(TileKey)e.Key]=(LodTile)e.Value;
            foreach(TileKey key in active)if(!all.ContainsKey(key)&&surfaces.Contains(key)&&Field(surfaces[key],"Tile") is LodTile tile)all[key]=tile;
            var buckets=new DistanceBucket[Edges.Length-1];for(int i=0;i<buckets.Length;i++)buckets[i]=new DistanceBucket{interval=Interval(i)};
            var rows=new List<TileRow>(Math.Min(MaximumRows,all.Count));
            foreach(var item in all)
            {
                if(rows.Count>=MaximumRows){d.truncated=true;break;}
                var tile=item.Value;object surface=surfaces.Contains(item.Key)?surfaces[item.Key]:null;
                double closest=Math.Sqrt((double)distance.Invoke(w,new object[]{tile}));bool eligible=(bool)visible.Invoke(w,new object[]{tile});
                int target=(int)quads.Invoke(w,new object[]{tile}),currentColumns=surface==null?0:Int(surface,"Quads");
                // Recompute the existing selector BEFORE its face-cap clamp, without
                // changing its dictionaries. effectiveTarget always calls real code.
                int requested=VoxelLodQuality.Select(tile.SizeMeters,closest,d.focalPixels,Mathf.Clamp(w.VoxelTargetPixels,.5f,8),32,eligible,d.coarseStartup);
                if(!d.coarseStartup&&eligible)
                {if(w.VoxelGreedyVisibleDetail)requested=VoxelLodSurface.MaximumQuads;while(requested<256&&tile.SizeMeters/requested*d.focalPixels/Math.Max(.1,closest)>Mathf.Clamp(w.VoxelTargetPixels,.5f,8))requested*=2;}
                double dx=tile.OriginXMeters+tile.SizeMeters*.5-eye.X,dz=tile.OriginZMeters+tile.SizeMeters*.5-eye.Z;
                var r=new TileRow{key=item.Key.ToString(),level=item.Key.Level,tileMeters=tile.SizeMeters,nearest3dMeters=closest,centerHorizontalMeters=Math.Sqrt(dx*dx+dz*dz),
                    desired=desired.Contains(item.Key),active=active.Contains(item.Key),resident=surface!=null,currentColumns=currentColumns,effectiveTargetColumns=target,supportedRequestColumns=requested,
                    currentSpacingMeters=currentColumns==0?0:tile.SizeMeters/currentColumns,effectiveTargetSpacingMeters=tile.SizeMeters/target,supportedRequestSpacingMeters=tile.SizeMeters/requested,
                    eligible=eligible,dirty=dirty.Contains(item.Key),queued=queued.Contains(item.Key),building=!d.buildContact&&buildingTile!=null&&buildingTile.Key.Equals(item.Key),
                    budgetDeferred=deferred.Contains(item.Key),faceLimited=faceCaps.Contains(item.Key),faceCapColumns=faceCaps.Contains(item.Key)?(int)faceCaps[item.Key]:0};
                if(r.budgetDeferred&&Field(deferred[item.Key],"RequiredDetailBytes") is long bytes)r.deferredDetailBytes=bytes;
                r.pixelTargetUnmet=r.eligible&&currentColumns>0&&r.currentSpacingMeters>.10001&&r.currentSpacingMeters*d.focalPixels/Math.Max(.1,closest)>w.VoxelTargetPixels;
                r.reason=!r.desired?"old active plan":!r.resident?"coverage missing":r.dirty?"source dirty":r.building?"building":r.budgetDeferred?"mesh budget deferred":
                    r.faceLimited&&currentColumns<requested?"face capacity":d.travelDeferred&&currentColumns<target?"flight hold":r.queued?"queued":
                    currentColumns<target?"refinement pending":r.pixelTargetUnmet?"sampling/plan ceiling":"effective target reached";
                rows.Add(r);var b=buckets[Bucket(closest)];if(r.active)b.active++;
                if(!r.desired){b.activeOnly++;d.activeOnlyTiles++;continue;}
                b.desired++;if(r.resident)b.resident++;else{b.missing++;d.missingDesired++;}
                if(r.eligible)
                {b.eligible++;d.eligibleDesired++;if(r.resident&&!r.dirty&&currentColumns>=target)b.eligibleAtEffectiveTarget++;else d.eligibleBelowEffective++;
                    if(r.resident&&!r.dirty&&currentColumns>=requested)b.eligibleAtSupportedRequest++;else d.eligibleBelowSupported++;}
                if(r.dirty)b.dirty++;if(r.queued)b.queued++;if(r.building)b.building++;if(r.budgetDeferred)b.budgetDeferred++;if(r.faceLimited)b.faceLimited++;if(r.pixelTargetUnmet)b.pixelTargetUnmet++;
                b.currentColumnsTileCounts[LevelIndex(currentColumns)]++;b.targetColumnsTileCounts[LevelIndex(target)]++;
                b.currentSampleColumns+=(long)currentColumns*currentColumns;b.targetSampleColumns+=(long)target*target;b.supportedRequestSampleColumns+=(long)requested*requested;
            }
            d.eligibleEffectiveTargetsReached=!d.truncated&&!d.coarseStartup&&!d.planActivationPending&&!d.planning&&!d.travelDeferred&&d.eligibleDesired>0&&d.eligibleBelowEffective==0;
            d.eligibleSupportedRequestsReached=d.eligibleEffectiveTargetsReached&&d.eligibleBelowSupported==0;
            rows.Sort((a,b)=>a.nearest3dMeters.CompareTo(b.nearest3dMeters));d.tiles=rows.ToArray();d.distanceBuckets=buckets;d.collectionMilliseconds=clock.Elapsed.TotalMilliseconds;
            d.notes="Read-only main-thread sample. Buckets use closest3D tile AABB distance, not tile-center distance; large tiles span buckets. current/effective/supported columns are per axis; SampleColumns=sum(n*n). Target reach is a selection/admission state, not native visual fidelity, geometric error, occlusion, RAM or FPS proof. Active-only rows are older drawn coverage during a pending plan. Pixel/plan limits can remain after effective targets are reached. No Tick/replan/source sampling/GPU readback. At most8192rows. Zero process counters mean unavailable (use external Windows process evidence), not zero usage. Process private/working set, managed/Profiler and mesh/height/scratch values overlap and must NOT be summed. GPU capacity is NOT current VRAM use. Read-only GC.GetTotalMemory(false), no forced collection.";
            Directory.CreateDirectory(outputDirectory);string file=Path.Combine(outputDirectory,"terrain-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".json");
            File.WriteAllText(file,JsonUtility.ToJson(d,true));UnityEngine.Debug.Log("TERRAIN_DIAGNOSTIC "+file);
        }
    }
}
