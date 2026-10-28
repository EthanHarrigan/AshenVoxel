"""Stage the three-file progressive LOD candidate; never writes production Assets."""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
STAGE = ROOT / '.terrain-cache/progressive-lod'

def edit(name, changes):
    text = (STAGE / 'baseline' / name).read_text(encoding='utf-8')
    for before, after in changes:
        if text.count(before) != 1:
            raise RuntimeError(f'{name}: expected unique source fragment: {before[:100]}')
        text = text.replace(before, after)
    (STAGE / 'candidate' / name).write_text(text, encoding='utf-8')

edit('WorldExplorer.cs', [
('pending.Count + contactPending.Count + contactDeferred.Count + (currentBuild != null ? 1 : 0)', 'pending.Count + contactPending.Count + contactDeferred.Count + UnqueuedVoxelRefinementTiles + (currentBuild != null ? 1 : 0)'),
('public readonly bool Contact;\n            public Work(LodTile tile, int revision, bool replace = false, bool contact = false) { Tile = tile; Revision = revision; Replace = replace; Contact=contact; }',
 '''public readonly bool Contact;
            public readonly int Quads;
            public readonly bool Progressive;
            public Work(LodTile tile, int revision, bool replace = false, bool contact = false, int quads = 0, bool progressive = false)
            { Tile = tile; Revision = revision; Replace = replace; Contact=contact; Quads=quads; Progressive=progressive; }'''),
('''            // Distant topology planning must not restart a contact tile in flight.
            if(currentBuild==null||!currentBuild.Work.Contact)CancelBuild();''',
 '''            // Preserve a still-useful immutable build across a view/topology plan.
            // The desired set is rebuilt below before deciding whether to cancel.
            if(!UseVoxelLodTerrain&&(currentBuild==null||!currentBuild.Work.Contact))CancelBuild();'''),
('''            foreach (LodTile tile in sorted)
                if (!surfaces.TryGetValue(tile.Key, out Surface existing) || existing.Quads != QuadsFor(tile) || externallyDirty.Contains(tile.Key))
                    pending.Enqueue(new Work(tile, revision, existing != null));
            planActivationPending = true;
            if (pending.Count == 0) ActivateReadyPlan();''',
 '''            if(currentBuild!=null&&!currentBuild.Work.Contact)
            {
                Work work=currentBuild.Work;
                if(work.Revision!=revision||!desired.Contains(work.Tile.Key)||
                    (UseVoxelLodTerrain&&work.Progressive&&work.Quads>VoxelQuadsFor(work.Tile)))CancelBuild();
            }
            ResetVoxelRefinementAdmission();
            foreach (LodTile tile in sorted)
            {
                if(currentBuild!=null&&!currentBuild.Work.Contact&&currentBuild.Work.Tile.Key.Equals(tile.Key))continue;
                surfaces.TryGetValue(tile.Key,out Surface existing);
                if(UseVoxelLodTerrain)QueueVoxelCoverage(tile,existing);
                else if(existing==null||existing.Quads!=QuadsFor(tile)||externallyDirty.Contains(tile.Key))
                    pending.Enqueue(new Work(tile,revision,existing!=null));
            }
            planActivationPending = true;
            if(UseVoxelLodTerrain||pending.Count==0)ActivateReadyPlan();
            RecordVoxelStatistics();'''),
('''            AdvanceBuilds();
            if (pending.Count == 0 && currentBuild == null) ActivateReadyPlan();''',
 '''            QueueNextVoxelRefinement();
            AdvanceBuilds();
            // Coverage publication is independent of the remaining quality queue.
            if(UseVoxelLodTerrain||(pending.Count==0&&currentBuild==null))ActivateReadyPlan();'''),
('''            surfaces.Remove(key);
        }
        void ClearTerrain()''',
 '''            if(surface.VoxelColumns){TerrainResidentMeshBytes-=SurfaceBytes(surface);voxelDetailMeshBytes-=DetailBytes(SurfaceBytes(surface));}
            surfaces.Remove(key);
        }
        void ClearTerrain()'''),
('''            voxelCapacityQuads.Clear();voxelRetryAt=0;''',
 '''            voxelCapacityQuads.Clear();voxelRetryAt=0;voxelDeferredQuads.Clear();
            TerrainResidentMeshBytes=voxelDetailMeshBytes=0;VoxelRefinementPendingTiles=0;voxelRefinementScanPending=false;consecutiveContactBuilds=0;'''),
])

edit('WorldExplorerIntegration.cs', [
('''                    Work work = contactPending.Count>0?NextContactWork():NextWork();''',
 '''                    bool contactFirst=contactPending.Count>0&&(pending.Count==0||consecutiveContactBuilds<2||HasUrgentContactWork());
                    Work work=contactFirst?NextContactWork():NextWork();
                    if(!work.Contact)consecutiveContactBuilds=0;'''),
('''                    if(work.Contact)PublishContact(built,work);''',
 '''                    if(work.Contact)
                    {PublishContact(built,work);consecutiveContactBuilds=Math.Min(2,consecutiveContactBuilds+1);voxelRefinementScanPending=true;}'''),
('''            revision++;CancelBuild();pending.Clear();refining.Clear();
            foreach(var surface in surfaces.Values)if(Intersects(surface.Tile,bounds))externallyDirty.Add(surface.Tile.Key);''',
 '''            revision++;CancelBuild();pending.Clear();refining.Clear();
            ResetVoxelRefinementAdmission();
            foreach(var surface in surfaces.Values)if(Intersects(surface.Tile,bounds))
            {externallyDirty.Add(surface.Tile.Key);voxelCapacityQuads.Remove(surface.Tile.Key);}'''),
('''                if(!surfaces.TryGetValue(tile.Key,out Surface existing)||externallyDirty.Contains(tile.Key)||existing.Quads!=QuadsFor(tile))
                    pending.Enqueue(new Work(tile,revision,existing!=null));''',
 '''                surfaces.TryGetValue(tile.Key,out Surface existing);
                if(UseVoxelLodTerrain)QueueVoxelCoverage(tile,existing);
                else if(existing==null||externallyDirty.Contains(tile.Key)||existing.Quads!=QuadsFor(tile))
                    pending.Enqueue(new Work(tile,revision,existing!=null));'''),
('''                    currentBuild = new SurfaceBuild { Work = work, Steps = UseVoxelLodTerrain ? BuildVoxelSurfaceSteps(work.Tile, work.Contact?256:QuadsFor(work.Tile),work.Contact) : BuildSurfaceSteps(work.Tile, QuadsFor(work.Tile)) };''',
 '''                    int quads=work.Contact?256:work.Quads>0?work.Quads:QuadsFor(work.Tile);
                    if(UseVoxelLodTerrain&&!work.Contact&&voxelCapacityQuads.TryGetValue(work.Tile.Key,out int cap))quads=Math.Min(quads,cap);
                    currentBuild = new SurfaceBuild { Work = work, Steps = UseVoxelLodTerrain ? BuildVoxelSurfaceSteps(work.Tile,quads,work.Contact) : BuildSurfaceSteps(work.Tile,quads) };'''),
('''                        surfaces.Add(work.Tile.Key, built); refining.Remove(work.Tile.Key); externallyDirty.Remove(work.Tile.Key);
                        built.Object.SetActive(visible && !inspection);''',
 '''                        surfaces.Add(work.Tile.Key, built); refining.Remove(work.Tile.Key); externallyDirty.Remove(work.Tile.Key);
                        if(built.VoxelColumns)
                        {TerrainResidentMeshBytes+=SurfaceBytes(built);voxelDetailMeshBytes+=DetailBytes(SurfaceBytes(built));voxelRefinementScanPending=true;}
                        built.Object.SetActive(visible && !inspection);'''),
('''                else if (!more) CancelBuild();''',
 '''                else if (!more){CancelBuild();if(UseVoxelLodTerrain)RecordVoxelStatistics();}'''),
])

edit('WorldExplorerVoxels.cs', [
('''        int VoxelQuadsFor(LodTile tile)
        {
            int wanted=VoxelLodQuality.Select(tile.SizeMeters,Math.Sqrt(DistanceSquared(tile)),FocalPixels,
                Mathf.Clamp(VoxelTargetPixels,.5f,8),32,InVoxelView(tile),coarseStartup);
            return voxelCapacityQuads.TryGetValue(tile.Key,out int cap)?Math.Min(wanted,cap):wanted;
        }''',
 '''        // An8-column tile has at most64 tops,112 internal exposed sides and32
        // boundary sides. Each side has at most38 layer/pigment segments, plus
        // one boundary apron. Reserve this proven worst case even when merging
        // makes the actual coarse mesh much smaller. This guarantees room for
        // both bounded coverage plans without retaining a second mesh per tile.
        const long VoxelCoverageTileReserveBytes=(64+112*38+32*39)*120L;
        const long VoxelDetailBudgetBytes=1536L*1024*1024;
        long voxelDetailMeshBytes;
        bool voxelRefinementScanPending;
        int consecutiveContactBuilds;
        readonly Dictionary<TileKey,int> voxelDeferredQuads=new Dictionary<TileKey,int>();
        public long TerrainResidentMeshBytes {get;private set;}
        public long TerrainSteadyMeshLimitBytes=>VoxelDetailBudgetBytes+768*VoxelCoverageTileReserveBytes;
        public long TerrainTransitionMeshLimitBytes=>VoxelDetailBudgetBytes+1536*VoxelCoverageTileReserveBytes;
        public int VoxelRefinementPendingTiles {get;private set;}
        public int VoxelRefinementDeferredTiles=>voxelDeferredQuads.Count;
        public int VoxelRefinementBuilds {get;private set;}
        int UnqueuedVoxelRefinementTiles
        {
            get
            {
                // Quality demand is per tile; do not count its queued/running
                // request again in the public total.
                int count=VoxelRefinementPendingTiles;
                if(count==0)return 0;
                bool NeedsQuality(Work work)=>!work.Contact&&surfaces.TryGetValue(work.Tile.Key,out Surface s)&&s.Quads<VoxelQuadsFor(work.Tile);
                foreach(Work work in pending)if(NeedsQuality(work))count--;
                if(currentBuild!=null&&NeedsQuality(currentBuild.Work))count--;
                return Math.Max(0,count);
            }
        }
        static long DetailBytes(long bytes)=>Math.Max(0,bytes-VoxelCoverageTileReserveBytes);
        int VoxelQuadsFor(LodTile tile)
        {
            double distance=Math.Sqrt(DistanceSquared(tile));bool visible=InVoxelView(tile);
            double target=Mathf.Clamp(VoxelTargetPixels,.5f,8);
            int wanted=VoxelLodQuality.Select(tile.SizeMeters,distance,FocalPixels,target,32,visible,coarseStartup);
            // After coverage, visible tiles may earn the next rung beyond the
            // old128-column ceiling. Source resolution and10cm matter stay fixed.
            if(!coarseStartup&&visible)
                while(wanted<256&&tile.SizeMeters/wanted*FocalPixels/Math.Max(.1,distance)>target)wanted*=2;
            return voxelCapacityQuads.TryGetValue(tile.Key,out int cap)?Math.Min(wanted,cap):wanted;
        }
        void ResetVoxelRefinementAdmission()
        {voxelDeferredQuads.Clear();voxelRefinementScanPending=true;}
        void QueueVoxelCoverage(LodTile tile,Surface existing)
        {
            if(existing==null){pending.Enqueue(new Work(tile,revision,quads:8));return;}
            if(externallyDirty.Contains(tile.Key))
                pending.Enqueue(new Work(tile,revision,true,quads:existing.Quads));
            else if(!coarseStartup&&!InVoxelView(tile)&&existing.Quads>VoxelQuadsFor(tile))
                // A turn releases rear-view detail before improving the new view.
                pending.Enqueue(new Work(tile,revision,true,quads:VoxelQuadsFor(tile)));
        }
        void QueueNextVoxelRefinement()
        {
            if(!UseVoxelLodTerrain||coarseStartup||planActivationPending||!voxelRefinementScanPending||
                currentBuild!=null||pending.Count>0)return;
            if(contactPending.Count>0&&(consecutiveContactBuilds<2||HasUrgentContactWork()))return;
            Surface best=null;double priority=double.MaxValue;int next=0;
            foreach(var tile in desiredTiles.Values)
            {
                if(!active.Contains(tile.Key)||!surfaces.TryGetValue(tile.Key,out Surface s))continue;
                // While the outer contact ring fills, spend fair distant slots on
                // visible coverage rather than terrain already masked by10cm contact.
                if(contactPending.Count>0&&tile.Key.Level==0&&contactSurfaces.ContainsKey(tile.Key))continue;
                int target=VoxelQuadsFor(tile);if(s.Quads>=target||voxelDeferredQuads.ContainsKey(tile.Key))continue;
                // Favor near/in-view terrain; at similar distance, improve the
                // roughest tiles first. Scan the bounded plan, reuse its one queue.
                double score=(WorkPriority(tile)+tile.SizeMeters*tile.SizeMeters*.01)*s.Quads*s.Quads;
                if(score<priority){priority=score;best=s;next=Math.Min(target,Math.Max(32,s.Quads*2));}
            }
            if(best==null){voxelRefinementScanPending=false;RecordVoxelStatistics();return;}
            pending.Enqueue(new Work(best.Tile,revision,true,quads:next,progressive:true));
            VoxelRefinementBuilds++;
        }
        bool HasUrgentContactWork()
        {
            // Protect the near32m ring before sharing the single build slot.
            foreach(Work work in contactPending)if(DistanceSquared(work.Tile)<=32*32)return true;
            return false;
        }
        bool AdmitVoxelMesh(LodTile tile,int n,long bytes)
        {
            if(n==8&&bytes>VoxelCoverageTileReserveBytes)
                throw new InvalidOperationException("Voxel coverage exceeded its proven face reserve.");
            long previous=surfaces.TryGetValue(tile.Key,out Surface old)?DetailBytes(SurfaceBytes(old)):0;
            if(voxelDetailMeshBytes-previous+DetailBytes(bytes)<=VoxelDetailBudgetBytes)
            {
                if(voxelDeferredQuads.TryGetValue(tile.Key,out int request)&&n>=request)voxelDeferredQuads.Remove(tile.Key);
                return true;
            }
            // Reject before vertex expansion/GPU upload, remember the request and
            // back off until a plan/source change can free/reprioritize capacity.
            voxelDeferredQuads[tile.Key]=n;voxelRefinementScanPending=true;
            if(externallyDirty.Contains(tile.Key))pending.Enqueue(new Work(tile,revision,true,quads:8));
            return false;
        }'''),
('''            int n=VoxelQuadsFor(work.Tile);
            if(!work.Contact&&error is VoxelLodCapacityException&&n>8)voxelCapacityQuads[work.Tile.Key]=n/2;''',
 '''            int n=work.Quads>0?work.Quads:VoxelQuadsFor(work.Tile);
            if(voxelCapacityQuads.TryGetValue(work.Tile.Key,out int cap))n=Math.Min(n,cap);
            if(!work.Contact&&error is VoxelLodCapacityException&&n>8)voxelCapacityQuads[work.Tile.Key]=n/2;'''),
('''            VoxelLodTiles=CameraPriorityTiles=PixelTargetLimitedTiles=0;TerrainTriangles=0;''',
 '''            VoxelLodTiles=CameraPriorityTiles=PixelTargetLimitedTiles=VoxelRefinementPendingTiles=0;TerrainTriangles=0;
            if(UseVoxelLodTerrain&&!coarseStartup)
                foreach(var tile in desiredTiles.Values)
                    if(surfaces.TryGetValue(tile.Key,out Surface existing)&&existing.Quads<VoxelQuadsFor(tile))VoxelRefinementPendingTiles++;'''),
('''                VoxelLodSurface data=voxelBuildJob.Result;voxelBuildJob=null;LastVoxelWorkerMilliseconds=workerMilliseconds;
                int vertexCount''',
 '''                VoxelLodSurface data=voxelBuildJob.Result;voxelBuildJob=null;LastVoxelWorkerMilliseconds=workerMilliseconds;
                if(!contact&&!AdmitVoxelMesh(tile,n,(long)data.Faces.Count*120))yield break;
                int vertexCount'''),
])

if __name__ == '__main__':
    print(STAGE / 'candidate')
