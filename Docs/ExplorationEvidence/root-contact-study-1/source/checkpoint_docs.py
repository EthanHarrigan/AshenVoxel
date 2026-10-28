from pathlib import Path
import datetime,hashlib,json
HERE=Path(__file__).resolve().parent;DEV=HERE.parents[1];MAIN=DEV.parent/'AshenVoxel'
stamp=datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%SZ');backup=HERE/'checkpoint-preimages'/stamp
link='Docs/ExplorationEvidence/root-contact-study-1'
memory='''Root-contact study (2026-10-27 UTC): CPU alternative only, NOT INSTALLED.
The failed population-grounding-1 candidate remains blocked. Two actual natural
mature redwood sites (5.37/7.19 degrees) now have zero measured basal gap and
0.251/0.282m maximum burial in an isolated terrain-following base prototype.
Original wood/tints above3m are unchanged; continuous canonical central shafts
remove the splice shelf. All lower cells remain connected. Actual live packed
wood faces/RGB below8m match the offline baseline. This is changed generation
(about8% less total wood), not a mass-conserving edit or approved runtime patch.
Actual Core.Set/mesher CPU medians are about2.4+9.4–9.8ms per base, excluding
terrain queries/native upload. Six offline before/after views and exact evidence:
Docs/ExplorationEvidence/root-contact-study-1/checkpoint.json. Runtime bounded
work, all-slope fallback, collision/LOD/wind/edit coherence and Unity gates remain.
Independent read-only review found no visual blocker in these two sites, and
approved only bounded runtime prototyping. Lower deformation/cell/face limits
and halo-only seam stitching are still required; no general adoption approval.
The terrain32M native trial stopped BEFORE launch at4.32GiB available commit
against its5GiB guard; no new Unity process or retry. Main population staysv1.

'''
todo='''Root-contact study checkpoint (2026-10-27 UTC), isolated CPU only:

- [x] Preserve actual mature upper wood/pigments and inspect two natural sites.
- [x] Remove the geometric/pigment splice collar; retain connected lower roots.
- [x] Verify actual packed base geometry/RGB and exact Core.Set reconstruction.
- [x] Record six CPU views, material delta and actual mesher CPU cost.
- [x] Independent review of six views/contact math: viable for bounded prototyping.
- [ ] Establish bounded displacement/cell/face limits and a steep-site fallback.
- [ ] Implement cancellable/background base construction with shared templates;
      no unbounded per-instance tree regeneration or cache growth.
- [ ] Unify new base render/collision/matter edits, LODs, wind and source invalidation.
- [ ] Version changed geometry/provenance and validate native depth/shadows/Play/FPS.

This is NOT a replacement approval for failed population-grounding-1. Main
populationv1 remains installed. No root-contact runtime integration or native
launch occurred; see Docs/ExplorationEvidence/root-contact-study-1/checkpoint.json.

'''
session='''
### 2026-10-27 — Actual-data terrain-following mature root study

Kept the failed whole-tree-Y seating candidate blocked and main Assets unchanged.
Compared two unchanged natural mature-redwood placements on main14fb32 terrain.
The isolated base narrows only below3m, retains canonical upper wood/pigments,
restores the existing central shaft below the seam and maps lower column spans
to the minimum terrain height under exact positive-area rotated-cell overlap.
Both examples have zero sampled gap, 0.251/0.282m maximum overlap burial, no
disconnected lower cells and no unsupported seam cells. Canonical generation
matches45,578 actual packed LOD0 wood quads below8m (58,312 face-cell checks/RGB).
Material volume changes are explicit: total wood380,993→349,918 and
411,904→379,487 cells. This is a generation alternative, not conservation physics.

Preserved failures: the first splice left62 unsupported upper cells per seed,
up to two-cell shelves; regenerated lower pigments showed a collar. The final
variant preserves canonical pigment where available and restores shaft support.
Two first camera origins lay inside nearby terrain; those images remain marked
invalid. Final six before/after CPU views use at least1.7m camera-ground clearance.

Actual Core.Set reconstruction exactly matches all Python base occupancy/RGB.
With process-local tiered JIT disabled, CPU medians are2.34/2.39ms material build
and9.81/9.40ms actual GreedyChunkMesher with the existing Mesh shim. Arithmetic
mesh payload1.30/1.35MB;43 material chunks per base; terrain sampling, shared
template load, native upload and FPS excluded. Initial tiered timing disparity
is retained. No runtime code/pack/atlas/driver/system settings were changed.
Evidence and source snapshots: ExplorationEvidence/root-contact-study-1.
Independent read-only review found no obvious visual gap/collar/wood curtain in
these six views and accepted bounded prototyping only. It requires lower
displacement/cell/face limits and halo-only guard-row stitching; the cost fixture
currently includes an exposed top cap and is not a runtime mesh publication.
Bounded worker/cache implementation, all-species/slope policy, generation version,
snapshot decision, collision/LOD/wind coherence and native acceptance stay open.
'''
review='''
## Root-contact study1 — isolated CPU geometry, not installed

The failed rigid seating candidate is still blocked. A base-only alternative
preserves the mature trunk/crown source above3m and leaves each tree upright at
its original natural placement. Below that boundary it uses a narrower flare,
the original central shaft and terrain-following tapered columns. All geometry
remains10cm cubic material data. No main Assets or AVPM pack were replaced.

| Natural redwood site | Original max gap/burial | Candidate max gap/burial | Lower connectivity / seam |
| --- | --- | --- | --- |
| Seed714103, 5.37° | 0.549 /1.951m | 0 /0.251m | Connected /no unsupported upper cells |
| Seed271828, 7.19° | 0.118 /2.282m | 0 /0.282m | Connected /no unsupported upper cells |

These are exact positive-area rotated-cell/global-terrain-cell contact samples
for two placements, not general terrain guarantees. The original low8m exposed
wood and pigments match the current live pack. The final CPU imagery uses actual
top terrain pigments; subsurface pigment is representative at half-metre depth.
Foliage is omitted. Lighting is the existing illustrative CPU renderer, not URP.
See [six before/after views](ExplorationEvidence/root-contact-study-1/comparison.png)
and [checkpoint](ExplorationEvidence/root-contact-study-1/checkpoint.json).

The narrowed generation changes total wood by−8.16%/−7.87%; this is not a gravity
operation or conserved matter transfer. Every original wood voxel/tint above3m
is unchanged. Initial pigment/geometry collars and bad camera positions are
preserved as failed evidence. No1.26m relaxation was used to pass the contact gate.

Actual Core.Set plus the actual mesher with a non-Unity Mesh shim reproduces the
base cells and RGB. Warm CPU material build is about2.4ms, meshing9.4–9.8ms,
arithmetic output1.30–1.35MB/base. Source/template and terrain query costs, native
allocation/upload, total cache residency and Play/FPS are not included. Building
these synchronously during a frame is not accepted. Adoption requires bounded
worker/cancellation/cache behavior, geometry versioning, save/provenance handling,
render/collision/matter/edit agreement and matching LOD/depth/shadow/wind behavior.
'''
plans=[]
for root,label in [(DEV,'dev'),(MAIN,'main')]:
    assert (root/link/'checkpoint.json').exists(),'Publish evidence before documenting it'
    for relative,addition,prepend in [('MEMORY.md',memory,True),('TODO.md',todo,True),('Docs/SESSION_LOG.md',session,False),('Docs/EXPLORATION_REVIEW.md',review,False)]:
        p=root/relative;before=p.read_bytes();text=before.decode('utf-8')
        if 'Root-contact study (2026-10-27 UTC)' in text or relative.endswith('SESSION_LOG.md') and 'Actual-data terrain-following mature root study' in text or relative.endswith('EXPLORATION_REVIEW.md') and 'Root-contact study1' in text or relative=='TODO.md' and 'Root-contact study checkpoint' in text:raise RuntimeError('Already documented: '+str(p))
        target=backup/label/relative;target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(before);assert target.read_bytes()==before
        # Preserve the title as the first line in short current-state records.
        if prepend:
            first,rest=text.split('\n',1);after=first+'\n\n'+addition+rest.lstrip('\n')
        else:after=text.rstrip()+'\n'+addition
        plans.append((p,before,after.encode('utf-8'),target))
inventory=[]
for p,before,after,target in plans:
    if p.read_bytes()!=before:raise RuntimeError('Concurrent change detected; stop/reconcile: '+str(p))
    p.write_bytes(after);assert p.read_bytes()==after
    inventory.append({'path':str(p),'before':hashlib.sha256(before).hexdigest(),'after':hashlib.sha256(after).hexdigest(),'readable_backup':str(target)})
(backup/'inventory.json').write_text(json.dumps(inventory,indent=2),encoding='utf-8');print(json.dumps({'updated':len(plans),'backup':str(backup)},indent=2))
