# dense-forest-canopy-1 — frozen candidate, NOT INSTALLED

Authorized scope: denser massive-distance foliage/cluster coverage using the actual
current ForestHorizon, existing atlas and GPU representation. No MAIN edits,
imports, queue/helper/native/GPU/UI actions, new Editor or agents were performed.

## Exact payload and recovery

Only these existing sources are proposed, as one atomic change:

- `Assets/AshenVoxel/Exploration/ForestHorizon.cs`
- `Assets/AshenVoxel/Exploration/Resources/ForestHorizonGpu.shader`
- `Assets/AshenVoxel/Exploration/Resources/ForestHorizonCull.compute`

`baseline.json` and `manifest.json` contain exact before/after SHA256 identities.
`preimages/` holds readable byte-exact originals. `candidate/` holds replacements;
`candidate.patch` is a readable narrow diff. Existing metas/GUIDs are unchanged.
Coordinator must compare all MAIN preimages and protected dependency hashes before
an idle atomic install. Roll back all three together from the readable preimages,
only if installed hashes still match the candidate; unknown changes need a merge.
VegetationStreaming, Bootstrap/Settings, WorldExplorer/HeightProxySurface, old
atlas3 images, old lighting include and other forest shaders were not edited.

## Concrete change/defaults

The old width mapping divided by crown diameter while geometry used a square
span often based on tree height. It could stretch a conifer into a broad card
without adding canopy mass. The candidate uses metres of stand footprint divided
by that actual square span; the shader crops/repeats existing crown imagery at
native crown width, rather than stretching one tree over that footprint.

- Dedicated stand grid: `8x8` to `12x12` per existing tile, same256tile maximum;
  maximum sample records16,384 to36,864. No extra worker, asset pipeline, atlas
  rebake, full-detail tree multiplication, collision or simulation.
- Maximum footprint:2048m of **forest patch**, containing many crown stamps.
  Tree envelope height stays86–114% of the actual species/stage bounds. Individual
  column crowns are88–100% of that envelope. No mountain-sized tree heights.
- Coverage: `1-exp(-3.2*standDensity*TreeDensity)`, modulated by a stable180m
  field, preserves open stands. Existing TreeDensity is the only external control;
  no new settings/UI changes. Other tuning values are named constants/source
  coefficients for this frozen candidate.
- Growth/tint: correlated640m region +96m local fields, with restrained per-cell
  variation. Albedo multipliers are roughly.84–1.13 overall. No frame/time/screen
  seed was added. Re-entering an identical world pose yields exact CPU payload.
- Preserve ForestBiomes source-water/glacier/cold/slope rules, plus a conservative
  `SnowlineMeters-350` alpine cutoff. Four expanded-footprint corner checks require
  compatible wooded species and bounded terrain-plane residual. Failed fits
  shrink to a native-size crown rather than bridge cliffs/coasts. This is a coarse
  visual guard, not a proof of an ecological treeline or detection of every hole
  inside a large patch. Whole-world source is1280m except the5m starter; no learned
  botanical or terrain detail was invented.
- Patches follow a sampled terrain plane. Cropping/occupancy/color logic is in the
  same Tree() function used by forward/depth/normal passes. Culling expands bounds
  to enclose that sloped plane. Existing atlas3 lighting and optical sampling stay
  intact, but their native visual agreement still needs acceptance.

## Existing GPU record compatibility

The32-byte record and existing renderer are retained. New records use reserved
Style.w's exactly representable24 integer bits for four6-bit quantities: crown
crop, envelope growth, correlated tint, coverage. Style.y is `prototypeId + slope/
1048576`, packing two10-bit gradients into its exact fractional bits; shader floors
the prototype ID before atlas lookup. Zero Style.w retains the prior shader and
compute behavior for old records/fixtures. All three files must install together.
The old CPU atlas shader is unchanged: unsupported-GPU fallback gets the denser
grid and modest real-size height variation, **not** the new mass/tint tiling.

## Cost boundary

GPU path still submits one existing indirect draw. At the new worst record cap,
the existing next-power-of-two double buffers can reserve about4.5MiB instead of
1.125MiB (36 bytes/slot, two65,536-slot sets, plus arguments). CPU result/pending
arrays grow too. Publication remains incremental on the old worker/main boundary.
Shader crown repetition is O(1) UV/hash math per fragment, not per-stamp geometry
or extra atlas fetch loops. **Retained alpha coverage and overdraw increase.**

In the one controlled flat-forest fixture:9,205 to20,702 patches (2.249x), with
side quad-area sum2.90x and overhead quad-area sum5.72x before alpha coverage.
These are aggregate area sums, not screen overdraw or GPU timings. New crown
coverage can increase shaded pixels further. This is an intentional visual-cost
tradeoff; do not advertise it as free or an FPS improvement. Corner eligibility
checks raised controlled source-height queries about9.77x; background build time
was about0.11s versus0.02s in that .NET fixture, not actual Unity/model timings.
An actual terrain source can cost more and produce fewer large patches.

## Executed check and limits

`python -X utf8 .terrain-cache/dense-forest-canopy-1/check.py`

One focused CPU fixture passed13 assertions: density increase, sample budget,
exact repeated payload, native species-scale bounds, packed ID/height/gradient
precision, many-crown representation, coherent variation, shoreline/glacier/
alpine/zero-density gaps. It uses current horizon Build plus actual-bake prototype
bounds and controlled sources, not a substitute game scene. Actual controlled
counts and source snapshots/hashes are in `last-check.json` and its check folder.

Current Exploration runtime assembly with this overlay API-compiles against the
installed Unity6000.3.27f1/.NETStandard2.1 references and current dependency DLLs.
Reference hashes are guarded through compilation. The first API attempt omitted
nested RootContact/Wildlife sources and failed; retained in the earlier check
folder, then corrected to the actual recursive runtime source set. No MAIN fix.

**HLSL/compute compilation, GPU execution, native images, motion, alpha cost,
FPS/VRAM and installation are OPEN.** Coordinator owns the single MAIN/native/UI
slot. Eventual acceptance should use an actual forested decoded starter/foothill
view (original starter near X85760/Z15330), preserve user spawn/options, compare
side/oblique/overhead transitions, coast/treeline and sloped patches. A sparse
6000m mountain view is not the appropriate positive density test. Old evidence
retains its original identities; no native success is claimed by this lane.
