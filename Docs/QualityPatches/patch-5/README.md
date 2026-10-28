# Material transparency and readable forest shadows

Every non-air material has independent transparency and light-transmission controls.
In Unity choose **Ashen Voxel > Appearance > Material transparency and transmission**.
The shared asset is `Assets/AshenVoxel/Rendering/Resources/MaterialOptics.asset`.
Changes in Play update the global optics table immediately. Collision, stored RGB,
granular physics and tree matter generation do not change with these sliders.

Defaults: leaves6% transparent /55% transmission; ice52% /70%; petals15% /60%;
webs70% /65%. All other visible materials are opaque with no transmission.
Web is append-only material24, available for authoring; no new web placement system
was added. Air remains empty space. Intrinsic colors remain opaque sRGB.

Transparency uses stable interleaved coverage in ordinary URP meshes. Color,
depth and normals share the same coverage test; shadows additionally account for
transmission. Thin leaf backlighting is a diffuse approximation. This is not
refraction, volumetric scattering, ray-traced GI or physically measured optical
thickness. Coverage grain may be visible at high transparency. Deep terrain is
still an implicit surface shell, not a rendered transparent geological volume.

Shaded surfaces now receive shared hemispherical sky/equator/ground fill, including
instanced near props and the distant tree atlas. The default ambient colors are
brighter and AO retains a lower bound on indirect illumination. Directional sun,
shadows and geometric shading remain. **F1 > Shadow fill** adjusts the strength
from0 to2 and Save these options persists it; older saved options retain default1.5.

Render cache changes: exact material IDs and2-bit AO share vertex alpha for Core
meshes. Terrain keeps its24-byte vertex layout and carries128+MaterialId in UV.w.
AVPM4 adds one material byte per quad. Old AVPM1–3 decode with explicit unknown
material31 and opaque fallback, never color-based identity guesses. Snapshot
Format2 stays unchanged and still reads Format1; old saves load unchanged, while
older software cannot read new Web cells. Both sides of different-material
interfaces are retained, including behind a translucent leaf or ice surface.

The99 prototypes/412 LODs were regenerated from unchanged authoritative generator
sources. Occupied cell counts/bounds remain identical; all three interaction
channels are copied byte-for-byte from AVPM3 and checksummed independently.
Forest atlasv3 has unlit pigment plus linear normal/material/AO data. Material
identity uses uncompressed point-sampled alpha; normal RGB still filters. This
avoids making the wrong tissue translucent when atlas filtering mixes colors.

Validation evidence and native images are recorded beside this file. Tests cover
91 Core,55 materials,73 tree,40 authoring,144 prototype/population,31 bootstrap,
31 vegetation streaming,19 terrain runtime and native mesh oracle checks. Native
DX12 on RTX3080Ti tests all24 visible materials at opaque/half/invisible, directional
backlighting, four real redwood terrain tiles and11 GPU forest checks. These are
bounded fixtures, not sustained full-world FPS or final artistic acceptance.

Rebake is a render-only migration with `Tools/ExplorationPrototypes/bake_prototypes.py`:
pass `--interaction-source` pointing to the backed-up AVPM3 pack and
`--interaction-provenance` to its original bake JSON. Changed authoritative source
hashes fail; geometry changes require a fresh authoritative interaction bake.
Do not run the older occupancy-from-surfaces script on AVPM4 internal interfaces.
The live patch backup retains the previous pack and asset GUIDs are preserved.
