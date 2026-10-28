# Cheaper destruction and progressive source refinement

This is the implementation/evidence record for the user's October27 requests,
including hollow interiors and weighted debris. It supersedes the narrow,
uninstalled `.terrain-cache/tree-click-fix-1` candidate. Historical ZIPs and prior
native evidence are unchanged. See MEMORY.md for final validation status.

Final checkpoint: [frozen source and acceptance limits](NaturalMatterEvidence/checkpoint-20261028-003705/status.json).
The final [native GPU fixture](NaturalMatterEvidence/native/20261028-003433-272/result.json)
passed in Unity6.3/DX12 with10 shader passes, mesh regression, four actual-cache
captures and exact3395-cell conservation. The scene stayed clean with its original
root count. [Falling section](NaturalMatterEvidence/native/20261028-003433-272/02-falling-section.png)
and [weighted pile](NaturalMatterEvidence/native/20261028-003433-272/03-settled-weighted-debris.png)
are isolated offscreen draws, not full Exploration gameplay or FPS evidence.
The final132087-assertion CPU suite also requires actual birch toppling before
release on flat and sloped floors. Broad crowns previously suppressed the tall
section rule; that failure is preserved and fixed by recognizing wood sections.

## Installed matter changes

LMB automatically reserves the aimed published tree or rock, retains the original
while exact matter prepares on a worker, and transfers ownership at the existing
atomic vegetation publication boundary. All16 baked trees and60 rock prototypes
have exact replay recipes, pinned counts and canonical matter hashes. Adapted
root specimens are refused because they have a different authoritative recipe.
There is one bounded preparation/cache entry. No visual triangles are converted
into gameplay matter. Being close no longer causes the old broad activation refusal;
the replacement preserves the original hard-cell collision mask for attached matter.

The old per-cell SAT/angular/pair solver is replaced by up to8 connected sections
and lightweight debris. Connectivity and source removal remain bounded and use
VoxelWorld.Set. A section moves as one pose, with14 actual-member extremal
probes, gravity, approximate contact tipping and damping. A deliberate post-separation
instability term makes tall sections topple; this is a gameplay approximation, not
a claim of engineering stress or rigid-contact fidelity. Ground contact followed
by0.6s rest, or3s after first contact, starts bounded release. There are no per-cell
rigidbodies or objects. Loose matter has no pair/actor/prop-volume collision or
occlusion raycast. Cached10cm terrain-column heights stop it at the floor; a
coarse occupancy counter creates approximate piles. Missing floor samples retain
matter until the bounded query queue supplies them.

Collapse produces parcels of identical material and intrinsic tint. The default
weight is `max(4, ceil(specimenCells / 4096))`, with exact shorter tails. Partial
parcels remain owned during a budgeted release and are flushed when the section
finishes. A leader carries motion and count; follower ownership resolves through
the leader. Collection credits the exact weight in constant time, without looping
over every represented cell. The physical10cm grid is unchanged. Nearby pickup is
on by default; I toggles it. The HUD exposes counts by material.

The renderer retains ordinary URP meshes. Up to9 rigid meshes are rebuilt on
ownership changes, then move by transforms, avoiding a complete vertex rewrite
every frame. Geometry building visits at most8192 cells/bins per update, admits
at most32768 spatial bins, and uses explicit coarse render cells for huge trees.
The16-cubed source chunk containing the latest cut renders at10cm, so a cut can
expose interior matter even when the rest of a large tree is coarse. New sections
wait for their geometry publication before moving. Eight-slot congestion retains
pending support and permits published bodies to progress, avoiding a render gate
deadlock. Loose visuals use one shared cube, up to4096 displayed representatives,
1023 per instanced batch, no debris shadows and per-instance intrinsic color.
Visual sampling does not remove or collect undisplayed material.

## Hollow/hidden interior findings

The actual largest redwood, model11, contains1,159,639 cells.705,089 (60.8%) have
no air neighbor. Of its411,904 wood cells,350,914 are buried;354,175 leaf cells are
also buried. Preserving all non-air material interfaces leaves647,522 cells
(55.8%) completely enclosed in their own material. These figures come from the
actual generator, not an estimate from the render mesh. See
[read-only measurements](NaturalMatterEvidence/hollow-probe.json).

The useful approaches have different consequences:

| Approach | Benefit | Boundary |
| --- | --- | --- |
| Exposed-face meshes | Removes hidden triangles while a section remains whole | Implemented; doesn't itself free interior matter storage |
| Compact solid runs plus local edits | Stores a solid interior implicitly and reveals it where cutting exposes it | Measured run-count opportunity; not implemented as a new matter format |
| Weighted debris at collapse | Few moving objects can represent many collected cells exactly | Implemented; representative counts adapt to specimen size |
| Camera/frustum/occlusion culling | Avoids drawing currently hidden exterior pieces | Must be reversible when the player walks around; never delete material counts |
| Discarding hidden matter outright | Saves state | Rejected: loses inventory, future exposed surfaces and support truth |

The recommended next memory optimization is compacting the buried interior at
the section-to-debris transition, retaining per-material/tint totals and surface
representatives. Current code still retains its original per-cell arrays: the
rough86MB direct-array saving measured for model11 is prospective, not achieved.
Run encoding the full model's same-material/tint Y spans would require about6.56MB
of raw13-byte records versus19.71MB of17-byte cell records, before indexes/headers;
no new on-disk format or migration is claimed. Render culling alone is not a CPU
simulation optimization.

## Research and what it supports

- Noita's developers describe pixels following a shared collision polygon/rigid
  body, with grid and sub-grid positions. They also explicitly describe CPU
  simulation and note the importance of being2D. This supports separating shape
  motion from individual material cells; it does not establish that Noita uses
  hollow interiors or weighted inventory debris. [Developer AMA](https://www.reddit.com/r/Games/comments/d7cqjz/we_are_nolla_games_the_team_behind_the_upcoming/),
  [official engine page and GDC talk](https://nollagames.com/fallingeverything/).
- Teardown's public API distinguishes bodies from visual particles and permits
  disabling particle collisions. That supports using cheaper effects where
  gameplay does not require physical contacts; it is not evidence that every
  Teardown fragment is a collision-free particle. [Official API](https://teardowngame.com/modding/api.html#ParticleCollide).
- Chaos clusters provide an established whole-section/fragment hierarchy;
  Niagara's lightweight emitters reduce persistent simulation work for suitable
  effects. These are architectural comparisons, not a proposal to replace Unity.
  [Chaos overview](https://dev.epicgames.com/documentation/unreal-engine/destruction-overview?lang=en-US),
  [Niagara lightweight emitters](https://dev.epicgames.com/documentation/en-us/unreal-engine/niagara-lightweight-emitters-overview).

## Terrain changes

The existing5m learned-source pipeline now includes nearby rear/side demand,
predicts6s ahead from travel velocity, and retires at most one distant decoded
patch per0.5s demand interval with640m hysteresis. Regional source invalidation
regenerates dependent LODs. Fast flight can downshift fine meshes even when their
plan keys do not change; slowing resumes progressive detail. Old complete
coverage remains until replacement meshes are ready. Settings/caps are preserved.

This is5m sample spacing, not demonstrated5m real-world accuracy. Source sampling,
render resolution and10cm matter are distinct. The retained16/32-patch moving
window is not full-view5m coverage, a whole-world background bake, or proof of
thousands of neural chunks per second. Distant Horizons' distant-generation and
LOD separation informed this work; the predictor here is our implementation,
not an asserted reproduction of its internal algorithm. Details, primary sources
and43 source/32 progressive checks are in the
[terrain record](TerrainRefinementEvidence/2026-10-27-predictive/README.md).

## Remaining acceptance and implementation limits

- This is still one active leased matter session. O, automatic range rollback,
  stopping Play and source-lifecycle restoration discard session inventory.
  Persistent harvesting, multiple concurrently edited specimens and durable
  player inventory require a shared edit/ownership ledger. No duplication-prone
  persistent inventory has been added alongside automatically restored trees.
- Automatic breaking applies to the76 supported published props, not arbitrary
  terrain surfaces. Terrain cuts need authoritative sparse edits, collision and
  LOD invalidation, support across page boundaries, and persistence as one change.
- Huge specimens prepare asynchronously but still allocate full editable matter
  and arrays. Connectivity remains per-cell work spread over ticks; it can take
  seconds and the32-cut queue can fill. Chunk-component support and compact
  interiors are needed before claiming prompt interaction on million-cell trees.
- Contacts are approximate2.5D terrain/support probes, not general cave/bridge/
  unrelated-prop collision. Approximate piles do not provide solid player support.
  Tipping starts after separation; material stress/partial-trunk strength is not
  modeled. Ground-cache saturation retains pending matter instead of dropping it.
- Render cache updates can lag edits; only the latest cut chunk receives forced
  fine geometry. Whole-scene motion, slopes, large-tree memory, maximum settings,
  GPU/FPS and source-rebase interaction require native gameplay acceptance.

Validation records distinguish .NET CPU probes, numerical Unity substitutes,
actual Unity API compilation and native GPU fixtures. None of the CPU timings is
a full-game frame-time guarantee. The old costly solver remains recoverable in
`.terrain-cache/cheap-matter-2026-10-27/preimages` and its baseline report is retained
under [NaturalMatterEvidence](NaturalMatterEvidence/README.md).
