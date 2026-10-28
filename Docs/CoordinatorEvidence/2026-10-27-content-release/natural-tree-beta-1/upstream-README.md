# natural-tree-matter-1 — FROZEN integration handoff

Candidate is CPU/API checked and NOT installed or native-tested. Only this folder
was written. No Bootstrap, Settings, terrain/source/forest sources, main Assets,
Editor review/capture files, patch queue, native process or UI was modified.

## Eight exact payloads, no existing-file replacements

integration-manifest.json freezes four NEW source/.meta pairs for
Assets/AshenVoxel/Exploration: NaturalTreeTemplate, NaturalTreePhysics,
VegetationTreeMatter and ExplorationNaturalTreeMatter. Preserve their generated
GUIDs. The coordinator merges ONE current Bootstrap call:
`ExplorationNaturalTreeMatter.Attach(this);`.

No asmdef/reference change is needed. VegetationTreeMatter is a partial that uses
the INSTALLED one-slot ShrubLease and its existing atomic matrix publication,
collision composition, cell pin, source epoch checks and rollback. Layer0 models0/1
replace layer2 shrubs as the reserved target, exclusively; both cannot be owned.
HasReservedShrubMatter remains true and therefore invokes the already-installed
global source-generation hold. Do NOT install an old VegetationStreaming candidate.
Grounded-root bindings only cover models10/11 and are untouched.

Readable current preimages include streamer, lease, shrub helper/counters, branch,
Bootstrap, capture menu and original export provenance. They are evidence, not
replacement files. The API compiler checks the current streamer/lease/shrub preimages
and hashes all current runtime sources/references. Coordinator-owned changes to
Bootstrap/capture retirement are intentionally merged independently.

## User-facing path in the existing Exploration scene

Approach a real naturally populated young birch. T enables its complete editable
matter; no tree is spawned or substituted. LMB removes the aimed actual10cm Wood
cell within5m, skipping passable foliage. Hold LMB for successive150ms cuts.
The exact six-connected unsupported component transfers from source grid to a
falling island. It starts with ZERO linear/angular velocity. Gravity and contacts
determine motion; no lift, prescribed tilt, random impulse or timed animation.
Stump-top contact supports/torques the body; it is NOT a ground-crumble trigger.
Only terrain contact crumbles the component at its current pose, inheriting its
current point velocities/spin into independent10cm cube bodies.

Each loose cube owns its own position, quaternion, linear/angular velocity and
material/tint. Continuous gravity/contact integration, oriented-cube SAT/normal
impulses, mutual collision and rotational ground contact replace the old grid-step
debris. Walking contact can push loose cubes. G explicitly kicks loose cubes or a
nearby falling island; accepted commands are counted, never called automatically.
An island can balance on its stump: no hidden tip is added to make it fall.

I toggles vacuum, OFF on every new session. It accelerates only independent cubes
toward the actual player hand and credits a cube only within18cm, with a source-grid
line-of-sight check. It cannot instantly consume the coherent upper tree. O queues
restore and discards all private cargo/matter when the original instance republishes.
This is an explicitly labeled session rollback, not persistent harvesting/inventory.
Step outside the original hard trunk to permit restoration. Disable/unload/return
reuse the installed lease lifecycle; no original plus debris/cargo duplication.

Original StableKey/PopulationKey/epoch, position, yaw, scale1 and source identity
are preserved. Data poses are small metre offsets from the original instance; only
the shared render root subtracts the current double floating origin. Rebase never
rewrites physical poses. One shared dynamic mesh/material/buffer set; zero voxel
GameObjects/Rigidbodies/independent meshes. Source edits all use VoxelWorld.Set.

## Exact bounds and evidence

Only models0/1 are admitted: Birch young0 has3395 cells, young1 has3408. Complete
original recipe/seed/pigment replay matches ALL10896/11078 LOD0 unit material/color
faces in the pinned installed AVPM4 pack. Initial whole matter is connected to its
basal source support. No interior deletion, downsampling or altered preset occurs.
Other trees are REFUSED intact by the explicit4096-cell opt-in beta policy.

Pack SHA25641556b474ce7a26342e4dd7bcff2e4fe658ab0333e8360058262ee21c0f011aa.
Original receipts list3395/3408 young birch,22983/23152 mature birch,
6682–6874 young oak,45450–47257 mature oak,66957–68590 young redwood,
1111103–1159639 mature redwood,7363–7620 young pine,50442–51228 mature pine.
This candidate is NOT mature-tree/world-wide destruction.

One4096-cell source/ownership pool, at most8 simultaneous coherent islands;
support work2048 candidates/call; at most8192 body integrations and131072 broadphase
candidate visits/call, with retained pair cursor and observable physics-time debt.
Budget exhaustion does not discard contact work or accumulated simulation time.
Cut/kick requests during unfinished collision work are explicitly rejected.
Terrain contact cache is128x128 globally aligned10cm columns (12.8m square), captured
256 columns/update before original visibility/collision transfer. Heights come from
the CURRENT Vista.SurfaceSampler and VoxelTerrainPage.SurfaceTop, the SAME column
quantization as actual voxel terrain. No private floating floor/duplicate collider.
Cubes at the contact-page boundary are retained rather than deleted; status names
the boundary. Maximum mesh pool98304 vertices/147456 indices, one buffer per channel.
Budgets are count/memory bounds, NOT native millisecond/FPS promises.

Focused existing check entrypoint CheckShrub/check.py was adapted for these two
real trees, not a new scene/harness framework. Final replay-check.json/check.log
prove complete face identity, coherent neighbor culling, actual one-cell trunk
cut, exclusive source/island/individual/cargo ownership, real gravity/stump/flat-ground
contacts, impact crumble, independent cube kick, explicitly enabled nearby collection
and exact total/per-material/tint conservation. Both cases reached ground impact
WITHOUT the optional island kick (27/36 CPU count calls). Vacuum stayed OFF until
the deliberate collection assertion. Cargo1 in the test is a manual pickup proof,
not a claim that all3395/3408 were collected. The height callback is flat in this
CPU fixture; it does not execute Unity or the real-terrain capture bridge.

integration-api.json compiles the WHOLE current Exploration runtime with these
four sources against actual installed Unity6000.3.27f1/Core/Data/FiniteMatter/URP
assemblies, C#9/.NET Standard2.1: PASS. manifest.json pins exact CPU source hashes;
integration-manifest.json pins the final8payloads. Earlier results retain their
own filenames/identities. No native import, renderer, feel, FPS or clip result is
claimed here.

## Coordinator native acquisition and acceptance

Read actual published layer0 CachedCell.Data.Instances in the installed registry;
find PrototypeId0 or1. That immutable Position/StableKey is the deterministic
acquisition seam. Do not spawn a lookalike or pretend a mature tree was edited.
Stand1–2m beside it, camera aimed at the trunk roughly30cm above its base; T or
public ActivateNearby(), then actual LMB/public CutTrunk(). Public Kick() is the
SAME explicit G gameplay action, counted by ExplicitKickCommands. Do not call it
silently in a recorder. Public SuctionEnabled starts false; only explicit player
collection should enable it. ShowBetaHud=false hides UI without suspending physics.

Capture: complete original -> actual removed trunk cell -> supported/unsupported
component -> gravity/contact fall -> actual terrain impact -> independently moving,
rotating/contacting cubes -> explicit kick -> separate manual vacuum pickup.
Record source/island/individual/cargo totals, GroundImpacts, PairContacts, debt,
explicit kick commands and suction state. Test source hold, rebase, rollback/return
and Stop. Preserve existing world/session/settings and one native owner.

Known beta approximations: coherent stump contacts use top-column contact impulses
(side/friction support is approximate); terrain is a heightfield without overhangs;
oriented cube pairs use approximate contact points/normal impulses without a full
friction manifold; coherent-island/loose-cube and unrelated static-prop contacts
are not complete. Player queries conservatively use rotated-cube AABBs. No player
damage/momentum response, sleep solver, full forestry/save system or mature-tree
admission. Dense3395-body contact backlog may slow simulation; debt is visible and
retained. Native cost and correctness must earn adoption, not be inferred from
the count limits or the small CPU test.

## Diagnosis of the rejected recording

Source evidence is preserved in preimages/sequence.json and the current capture/
branch source preimages. Sequence frame19 first sets suction=true at3.122s. Next
frame aggregate56 ->0 and cargo0 ->56, matching Actor.Suction's direct aggregate
credit path. Before that, frame10 fills the16-chip cap and leaves56 frozen retained
cells at17.09degrees after the private-floor breakup. The recorder explicitly sets
SuctionEnabled=true at elapsed>=3; support evacuation ALSO calls StepDebris(...true)
before any manual suction. InitialAngularVelocity=(0,0,1.5) forces spin. The fixture
is eyeY-1 with a floor at localY.1, unrelated to world ground. Later chips climb
the dominant-axis suction path toward the player; they were never independent
rigid cube dynamics. The record contains no body position telemetry, so it cannot
establish an independently measured upward aggregate trajectory. These specific
code/log/image facts explain the observed floating/stepped animation; they do not
constitute natural-tree gravity evidence. Coordinator owns retiring those hooks
and all timed recorder suction; this lane did not edit its capture/Bootstrap files.
