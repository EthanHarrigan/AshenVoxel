"""Isolated urgency experiment layered on frozen capacity sources; never edits Assets."""
from pathlib import Path
import datetime, difflib, hashlib, json

ROOT = Path(__file__).resolve().parents[2]
BASE = ROOT / '.terrain-cache/startup-throughput/capacity-candidate'
DEST = ROOT / '.terrain-cache/startup-throughput/altitude-candidate'
REL = Path('Assets/AshenVoxel/WorldScaleRuntime')
patches = []
manifest = {'utc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
            'status': 'EXPERIMENT ONLY; NOT INSTALLED', 'baseline': str(BASE), 'files': []}
changes = {
    'WorldExplorer.cs': [('            limitedSplits = plan.PendingSplits;',
                        '            voxelPlanBoundsRevision = revision;\n            limitedSplits = plan.PendingSplits;')],
    'WorldExplorerVoxels.cs': [(
        '''        bool HasUrgentContactWork()
        {
            // Protect the near32m ring before sharing the single build slot.
            foreach(Work work in contactPending)if(DistanceSquared(work.Tile)<=32*32)return true;
            return false;
        }''',
        '''        int voxelPlanBoundsRevision=-1;
        bool HasUrgentContactWork()
        {
            foreach(Work work in contactPending)if(ContactWorkIsUrgent(work.Tile))return true;
            return false;
        }
        bool ContactWorkIsUrgent(LodTile tile)
        {
            double distance=DistanceSquared(tile);
            if(distance>32*32)return false;
            // Reuse immutable planner enclosures, never query the field here.
            // Unknown/stale bounds retain the original arrival-safe priority.
            if(voxelPlanBoundsRevision!=revision||!(BareField(field) is IWorldFieldBounds))return true;
            for(int level=0;level<=14;level++)
            {
                long divisor=1L<<level;
                var key=new TileKey((long)Math.Floor((double)tile.Key.X/divisor),(long)Math.Floor((double)tile.Key.Z/divisor),level);
                if(!desiredTiles.TryGetValue(key,out LodTile enclosure))continue;
                // The contact grid and planner share aligned25.6m base tiles.
                // One cell pads the continuous bounds for voxel quantization.
                double dy=Math.Max(enclosure.MinimumHeight-.1-cameraY,Math.Max(0,cameraY-enclosure.MaximumHeight-.1));
                return distance+dy*dy<=32*32;
            }
            return true;
        }''')]
}
for name in ['WorldExplorer.cs', 'WorldExplorerIntegration.cs', 'WorldExplorerVoxels.cs']:
    rel = REL / name
    raw = (BASE / rel).read_bytes()
    before = raw.decode().replace('\r\n', '\n')
    after = before
    for old, new in changes.get(name, []):
        if after.count(old) != 1:
            raise RuntimeError('Expected exact unique source: ' + name)
        after = after.replace(old, new)
    target = DEST / rel
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(after, encoding='utf-8')
    manifest['files'].append({'path': rel.as_posix(), 'before': hashlib.sha256(raw).hexdigest(),
                              'sha256': hashlib.sha256(target.read_bytes()).hexdigest()})
    patches.extend(difflib.unified_diff(before.splitlines(True), after.splitlines(True),
                                      fromfile='a/' + rel.as_posix(), tofile='b/' + rel.as_posix()))
(DEST / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
(DEST / 'experiment.diff').write_text(''.join(patches))
print(DEST)
