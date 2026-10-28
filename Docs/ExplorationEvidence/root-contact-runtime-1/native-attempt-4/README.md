# Native root comparison V4 — limited static acceptance

Actual Unity 6000.3.27f1, RTX3080Ti. All four raw PNGs were independently inspected.
Both adjusted bases meet the recorded stepped terrain, preserve distinct root
lobes and join their original shared upper trunk without an obvious seam collar
or continuous wood skirt in these views. Site10 has a substantially narrower base.
Sharp/somewhat symmetric tips remain an aesthetic limitation.

| Specimen | Original | Terrain-following prototype |
| --- | --- | --- |
|10|[Authored](10-authored.png)|[Grounded](10-grounded.png)|
|11|[Authored](11-authored.png)|[Grounded](11-grounded.png)|

The fixed12 render calls completed in4.5447s. Recorded process-private growth
was0.5213GiB and minimum available commit3.6473GiB. These are checkpoints, not
continuous peaks or frame-time measurements. Both pairs contain foreground and
wood pixels, zero pink pixels; all6scene/shader/target restoration flags pass.
Original scene/settings/preferences hashes are unchanged. Exactly2owned temporary
hook files were removed after readable-backup/SHA checks; refresh consumed and
project integrity passed184assets/11assemblies. No user interface control or Play.

This is isolated LOD0 rendering at two fixed viewpoints with static wind. Terrain
heights/top pigments are recorded actual10cm cells, while subsurface material/AO
are representative neutral Dirt. Root Wood/RGB and shared upper mesh are actual.
Hidden faces, all slopes, composite coarseLOD seams, motion, collision, source
edits, runtime residency, and gameplay/FPS are outside this acceptance. No root
gameplay patch, asset pack, world or atlas replacement was installed.

The log preserves a fixture cleanup warning: its success path releases the owned
render target while active, before finally restores the original target. Final
restoration passed; future fixture revisions should omit the redundant early
release. The existing DLSS null-options warning is also retained. No warning-free
claim. Earlier attempts1/2 failures and attempt3 partial blank capture remain
separate immutable records; V4 does not relabel them.

See [run report](report.json), [cleanup](install/visual-review-cleanup.json),
[independent review](install/independent-visual-review.json), and frozen sources.
