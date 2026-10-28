# Assumptions and options menu

No answers are required before trying the demo. These are the working choices used to begin. You can request a change by ID, for example: “Use A03 third-person and A07 falling trees next.” The longer rationale and migration conditions live in [DECISIONS.md](DECISIONS.md).

Previous Unity project conversations were **not accessible in this workspace**. Their preferences have not been invented or treated as known requirements.

## Requirements already supplied

| Requirement | Current interpretation |
| --- | --- |
| Stable recent Unity with addon flexibility | Unity 6.3 LTS; assess addon compatibility individually |
| 3D material world with terrain deformation | Authoritative voxel material grid; player/boss edits change matter |
| Voxel scale | **One voxel per 10 cm** for matter and prop generation/hard collision; exploration terrain uses a separate smooth0.5–1 m near height proxy |
| Visual reference | **Avroid is primary**; earlier Souls-like dark fantasy mood remains a secondary direction |
| No Minecraft feel | Coherent terrain, material shading, vegetation silhouettes, atmosphere, and composition rather than giant building blocks |
| Noita-like interaction | Begin with granular gravity and material distinctions; rigid matter, fluids, and reactions remain separate systems |
| Ground surface variety |24 catalog IDs including air; coherent geology, sediments, cover, snow, Fungus and Petal; original arena placement choices retained |
| Strong foundations before broader game | Correctness checks, replaceable renderer, bounded work, versioned saves, and measurable gates |
| Work without repeated questions | Make provisional choices, document alternatives, continue within authorized scope |

## Changeable working choices

| ID | Topic | Current answer | Other useful options | Consequence of changing |
| --- | --- | --- | --- | --- |
| A01 | First platform | Desktop PC | Steam Deck-class PC; console-first; mobile | Changes graphics features, memory budgets, input, and world residency |
| A02 | First performance target | Establish a measured 60 FPS desktop baseline before promising production numbers | 30 FPS quality focus; 120 FPS responsiveness | Requires named minimum hardware and a different frame allocation |
| A03 | Demo camera | First-person inspection/tooling camera | Third-person Souls-style movement; controller-first lock-on | Final movement/camera/animation becomes a separate vertical slice |
| A04 | Demo domain | Default exploration: imported299.52 ×149.76 km map; separate25.6 ×12.8 ×25.6 m destruction arena retained | Persistent open world; authored encounter sequence | Exploration currently streams visual props and height proxies, not continuous editable matter |
| A05 | World authorship | Azgaar-conditioned original InfiniteDiffusion; actual learned macro everywhere and decoded starter region; optional live decoding | Different map/seed; analytic research fixture; authored landscape | New model jobs require matching immutable source identities and paired data |
| A06 | Voxel renderer | Greedy exposed-face meshes through URP | Sparse-brick ray traversal; SVO; hybrid | Replacement must earn its cost through the same editable scene and lighting benchmark |
| A07 | Structural support | Coherent wood/stone remain until explicitly edited | Root-connected collapse; rigid islands; stress/fracture model | Falling trees and arches require bounded connectivity and moving-body collision |
| A08 | Granular matter | Local cellular falling/piling, at most one downward cell per call; no physical acceleration or momentum | Cohesive soil; ballistic grains; particle/continuum hybrid | Changes settling, stability, material conservation tests, and update cost |
| A09 | Material chemistry | Metadata extension points; no complete fire/water system yet | Fire/heat first; water first; reactions-first sandbox | Needs state channels, conservation, chunk exchange, and sleeping rules |
| A10 | Tree generation | Shared birch/oak/redwood/pine genome, connected curved scaffolds, foliage sprays, bark patterns and optional oak acorns | Space colonization; authored species assets; ecological growth simulation | Preserves the material/appearance and branch-skeleton contracts while changing growth strategy |
| A11 | Landscape detail | Shared fixed-scale terrain attributes, coherent geology, source-biome climate, layered soil/sediment and physical patchy snow | Erosion/energy-balance simulation; authored masks/scans | Adds generation time, state, dependencies and source-halo requirements |
| A12 | Player collision | Exploration: continuous shared terrain field plus10 cm hard wood/rock queries; arena: authoritative matter | Native capsule/mesh proxies; hybrid controller | Keep soft foliage passable, unknown solids gated and render LOD independent of hard collision |
| A13 | Moving debris | No per-cell rigid bodies; bounded visual/structural work later | Coherent voxel islands; pooled fragments; grain conversion | Requires explicit body limits and collision simplification |
| A14 | Boss scope | Telegraphed terrain-attack harness | Full lock-on encounter; melee sweeps; spell attacks; multiple bosses | Adds combat design, AI, animation, damage, camera, and navigation |
| A15 | Save model | Format2 material/appearance snapshots with Format1 migration; bounded load rescan reawakens grains; exact frontier/tick is not restored | Changed-chunk region database; checkpointed edit journal; exact simulation checkpoints | Affects recovery, migration, storage cost, and replay semantics |
| A16 | Multiplayer | Single authoritative local world | Server-authoritative co-op; deterministic lockstep | Major authority, correction, bandwidth, and simulation scheduling work |
| A17 | Rendering pipeline | URP | HDRP for high-end PC; custom SRP | Pipeline migration changes shaders, effects, and addon integration |
| A18 | Addons | Minimal initial dependency set | Targeted camera/animation/input tools; third-party voxel middleware | Confirm ownership, source/license, editor/pipeline/platform support, and rollback |
| A19 | Art workflow | Procedural placement with controlled palette and atmosphere | MagicaVoxel assets; authored voxel species/props; imported models voxelized offline | Requires an asset scale/material mapping/import convention |
| A20 | Fine surface detail | Shader variation below the required 10 cm grid | Selected higher-detail visual assets; optional future cell-scale revision | Extra visual detail need not change physical matter scale |
| A21 | Input | Keyboard/mouse tooling controls | Gamepad; remapping; accessibility presets | Input Actions and UI settings should precede a controller-first combat slice |
| A22 | World persistence rules | Arena edits persist in a local demo save; exploration has no edits and saves only numeric options | Changed-region database; per-run world; permanent campaign world | Exploration population/distant proxy caches are replaceable and not a world-save format |
| A23 | Destruction limits | Protected bottom boundary; hardness-based tool edits | Fully breakable world; protected encounter geometry; energy-based damage | Must prevent invalid traversal and define support anchors |
| A24 | Visual darkness | Readable materials and attacks within a dark atmosphere | Bleaker low-key exposure; brighter stylization; more saturated foliage | Tune art through fixed reference views and player readability tests |
| A25 | Snow | Static10 cm depth from climate, slope, wind, sun and shelter; max2.4 m by default | Seasonal regeneration; conservative transport/energy/avalanche solver | Adds time-dependent state, mass accounting and persistence |
| A26 | Water | Visual sea surface and small underwater wash; no connectivity or fluid cells | Ocean/lake masks; rivers; local fluid simulation | Changes water authority, shoreline handling and conservation |
| A27 | Ground ecology | Source biomes/climate choose five dominant forest habitats, separate young/mature patches and open/dense variation | Full ecological succession; authored cover masks | Current stage/density fields are static procedural controls, not simulated forest growth |
| A28 | Small plant representation | Actual10 cm matter, one cell through hundreds; shared leaf/wood organ rasterizer |16×16 cutout details; instanced visual prototypes; authored voxel assets | Requires explicit picking, depth, alpha and distant replacement contracts |
| A29 | Ground-cover placement | Global keyed candidates, habitat/thinning rules and bounded shared-instance streaming; finite inspection tools retained | Canopy/light input; competition; full local matter residency | Exploration props retain physical scale but do not yet support edits or physical growth |
| A30 | Soft foliage collision | Leaves/Fungus/Petal pass the player body while remaining targetable; wood/ground block | Enable GridWalker solid-foliage flag; per-object categories | Affects tree canopy traversal and ground-clutter passability |
| A31 | Rock weathering | Six nonradial shape families, seeded material-biased Auto choice, weathered polygon caps, buried connectors and coherent pigment | Explicit shape overrides; full3D voxelized scans; physical dissolution/fracture | Changes silhouette, support topology, generation cost and validation |
| A32 | Terrain visibility |30 km default,20–40 km exposed, optional up to100 km extra coarse coverage | Higher-detail clipmaps; measured occlusion; GPU-driven renderer | Source relief and tile detail are independent; native target-machine performance remains unmeasured |
| A33 | Fine learned source | Bundled5.115 km decoded region and optional original-model service ahead;1280 m learned macro elsewhere | Larger prebake; remote generation; more aggressive retention | Model latency and resource limits must not change occupied collision terrain |

## Most useful next decisions after a playtest

The first playtest can answer these through observation rather than interrupting setup:

- Does 10 cm at the chosen camera distance achieve the desired Avroid-informed material and silhouette feel?
- Is the highest-value next feature third-person combat, falling trees/stone islands, or richer surface lighting?
- What minimum hardware, resolution, and FPS should become a hard acceptance gate?
- Is the intended world a persistent open region, separate boss arenas, or regenerated runs?
- Which specific addon is worth integrating first, and which responsibility should it own?

These are an options menu, not pending blockers. The current demo is meant to make those choices concrete.
