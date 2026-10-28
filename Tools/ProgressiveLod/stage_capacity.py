"""Stage capacity wakeups against installed aerial-lod-1; no production writes."""
from pathlib import Path
import datetime, difflib, hashlib, json

ROOT=Path(__file__).resolve().parents[2]
DEST=ROOT/'.terrain-cache/startup-throughput/capacity-candidate'
BASE=ROOT/'.terrain-cache/startup-throughput/capacity-baseline'
REL=Path('Assets/AshenVoxel/WorldScaleRuntime')
manifest={'generated_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'baseline':'installed aerial-lod-1','baseline_sha256':{},'candidate_sha256':{},'changed_files':[]}
patches=[]
def edit(name,changes):
    rel=REL/name;raw=(ROOT/rel).read_bytes();source=raw.decode().replace('\r\n','\n');candidate=source
    for before,after in changes:
        if candidate.count(before)!=1:raise RuntimeError(name+': expected unique source fragment '+before[:70])
        candidate=candidate.replace(before,after)
    for root in [BASE,DEST]:(root/rel).parent.mkdir(parents=True,exist_ok=True)
    (BASE/rel).write_bytes(raw);(DEST/rel).write_text(candidate,encoding='utf-8')
    manifest['baseline_sha256'][rel.as_posix()]=hashlib.sha256(raw).hexdigest()
    manifest['candidate_sha256'][rel.as_posix()]=hashlib.sha256((DEST/rel).read_bytes()).hexdigest()
    manifest['changed_files'].append(rel.as_posix())
    patches.extend(difflib.unified_diff(source.splitlines(True),candidate.splitlines(True),fromfile='a/'+rel.as_posix(),tofile='b/'+rel.as_posix()))

edit('WorldExplorer.cs',[
('''            active.Clear(); active.UnionWith(desired); planActivationPending = false;
            remove.Clear();''','''            active.Clear(); active.UnionWith(desired); planActivationPending = false;
            long previousDetailBytes=voxelDetailMeshBytes;
            remove.Clear();'''),
('''            foreach (TileKey key in remove) Release(key);
            RecordVoxelStatistics();''','''            foreach (TileKey key in remove) Release(key);
            WakeAdmissibleVoxelRefinements(previousDetailBytes);
            RecordVoxelStatistics();'''),
])
edit('WorldExplorerIntegration.cs',[
('''                        bool visible = active.Contains(work.Tile.Key);
                        if (surfaces.ContainsKey(work.Tile.Key)) Release(work.Tile.Key);''','''                        bool visible = active.Contains(work.Tile.Key);
                        long previousDetailBytes=voxelDetailMeshBytes;
                        if (surfaces.ContainsKey(work.Tile.Key)) Release(work.Tile.Key);'''),
('''                        built.Object.SetActive(visible && !inspection);''','''                        WakeAdmissibleVoxelRefinements(previousDetailBytes);
                        built.Object.SetActive(visible && !inspection);'''),
])
edit('WorldExplorerVoxels.cs',[
('''        readonly HashSet<TileKey> contactDesired=new HashSet<TileKey>(),contactDeferred=new HashSet<TileKey>();''','''        readonly HashSet<TileKey> contactDesired=new HashSet<TileKey>();
        readonly Dictionary<TileKey,long> contactDeferred=new Dictionary<TileKey,long>();
        public int ContactDeferredTiles=>contactDeferred.Count;
        long contactObservedBudgetBytes=-1;
        long ContactBudgetBytes=>(long)Mathf.Clamp(VoxelContactBudgetMB,128,2048)*1024*1024;'''),
('''            if(!UseVoxelLodTerrain||coarseStartup||field==null)return;
            double size=VoxelLodSurface.BaseTileMeters;''','''            if(!UseVoxelLodTerrain||coarseStartup||field==null)return;
            long budget=ContactBudgetBytes;
            if(contactObservedBudgetBytes>=0&&budget>contactObservedBudgetBytes)WakeAdmissibleContactRequests(budget);
            contactObservedBudgetBytes=budget;
            double size=VoxelLodSurface.BaseTileMeters;'''),
('''            long bytes=SurfaceBytes(built),limit=(long)Mathf.Clamp(VoxelContactBudgetMB,128,2048)*1024*1024;''','''            long bytes=SurfaceBytes(built),limit=ContactBudgetBytes,previousBytes=ContactMeshBytes;'''),
('''                Destroy(built.Object);Destroy(built.Mesh);contactDeferred.Add(work.Tile.Key);return;''','''                Destroy(built.Object);Destroy(built.Mesh);contactDeferred[work.Tile.Key]=bytes;return;'''),
('''            built.Object.SetActive(!inspection);UpdateContactMask();
        }
        void UpdateContactMask()''','''            contactDeferred.Remove(work.Tile.Key);
            built.Object.SetActive(!inspection);UpdateContactMask();
            if(ContactMeshBytes<previousBytes)WakeAdmissibleContactRequests(limit);
        }
        void WakeAdmissibleContactRequests(long limit)
        {
            remove.Clear();
            foreach(var request in contactDeferred)
            {
                if(!contactDesired.Contains(request.Key))continue;
                long previous=contactSurfaces.TryGetValue(request.Key,out Surface old)?SurfaceBytes(old):0;
                if(ContactMeshBytes-previous+request.Value>limit)continue;
                double size=VoxelLodSurface.BaseTileMeters;TileKey key=request.Key;
                var tile=new LodTile(key,key.X*size,key.Z*size,size,RemoteTerrainField.MinimumSupportedHeight,RemoteTerrainField.MaximumSupportedHeight);
                contactPending.Add(new Work(tile,revision,true,true));remove.Add(key);
            }
            foreach(TileKey key in remove)contactDeferred.Remove(key);
        }
        void UpdateContactMask()'''),
('''            ContactMeshBytes=ContactTriangles=0;contactCenterX=contactCenterZ=long.MinValue;contactRevision=-1;''','''            ContactMeshBytes=ContactTriangles=0;contactCenterX=contactCenterZ=long.MinValue;contactRevision=-1;contactObservedBudgetBytes=-1;'''),
('''        readonly Dictionary<TileKey,int> voxelDeferredQuads=new Dictionary<TileKey,int>();''','''        readonly struct DeferredVoxelRefinement
        {
            public readonly int Quads;
            public readonly long RequiredDetailBytes;
            public DeferredVoxelRefinement(int quads,long bytes){Quads=quads;RequiredDetailBytes=bytes;}
        }
        readonly Dictionary<TileKey,DeferredVoxelRefinement> voxelDeferredQuads=new Dictionary<TileKey,DeferredVoxelRefinement>();
        public int VoxelFaceCapacityLimitedTiles=>voxelCapacityQuads.Count;'''),
('''            else if(!coarseStartup&&!InVoxelView(tile)&&existing.Quads>VoxelQuadsFor(tile))
                // A turn releases rear-view detail before improving the new view.''','''            else if(!coarseStartup&&existing.Quads>VoxelQuadsFor(tile))
                // Release surplus cached detail after travel/turns while retaining
                // the current pixel target, including for still-visible terrain.'''),
('''        bool AdmitVoxelMesh(LodTile tile,int n,long bytes)''','''        void WakeAdmissibleVoxelRefinements(long previousDetailBytes)
        {
            // Run after complete replacement/retirement, never during the temporary
            // Release-before-add gap. Only measured requests which now fit wake.
            if(voxelDetailMeshBytes>=previousDetailBytes||voxelDeferredQuads.Count==0)return;
            remove.Clear();
            foreach(var request in voxelDeferredQuads)
            {
                if(!desired.Contains(request.Key))continue;
                long previous=surfaces.TryGetValue(request.Key,out Surface old)?DetailBytes(SurfaceBytes(old)):0;
                if(voxelDetailMeshBytes-previous+request.Value.RequiredDetailBytes>VoxelDetailBudgetBytes)continue;
                // Requeue the measured request itself: source refreshes can need
                // replacement even when the old mesh already has the target quads.
                pending.Enqueue(new Work(desiredTiles[request.Key],revision,true,quads:request.Value.Quads,progressive:true));
                VoxelRefinementBuilds++;remove.Add(request.Key);
            }
            foreach(TileKey key in remove)voxelDeferredQuads.Remove(key);
            if(remove.Count>0)voxelRefinementScanPending=true;
        }
        bool AdmitVoxelMesh(LodTile tile,int n,long bytes)'''),
('''                if(voxelDeferredQuads.TryGetValue(tile.Key,out int request)&&n>=request)voxelDeferredQuads.Remove(tile.Key);''','''                if(voxelDeferredQuads.TryGetValue(tile.Key,out DeferredVoxelRefinement request)&&n>=request.Quads)voxelDeferredQuads.Remove(tile.Key);'''),
('''            voxelDeferredQuads[tile.Key]=n;voxelRefinementScanPending=true;''','''            voxelDeferredQuads[tile.Key]=new DeferredVoxelRefinement(n,DetailBytes(bytes));voxelRefinementScanPending=true;'''),
])
(DEST/'source-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
(DEST/'capacity-wakeup.patch').write_text(''.join(patches),encoding='utf-8')
print(DEST)
