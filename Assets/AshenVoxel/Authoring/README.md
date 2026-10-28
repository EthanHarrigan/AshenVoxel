# Tree Workshop

Open `Assets/Scenes/TreeWorkshop.unity`, then press **Play**. The workshop compares Birch, Oak, Redwood, and Pine at the shared **10 cm** material scale. The separate forest arena is unchanged.

Use **Ashen Voxel → Trees → Open tree workshop** to open or recreate the scene, and **Create species profiles** to create any missing profile assets without overwriting existing work. The bundled profiles are in `Assets/AshenVoxel/Settings/Trees`.

Select a profile asset to edit mature height, trunk/crown size, branch angle/count/levels, upright leader strength, upward growth, branch whorls, fork spread, droop, gnarling, root flare, crown start/taper, foliage and bark styles, geometric bark ridge depth/count, fruit, age, seed, and palette. **Changing Species preserves your edits.** **Apply species defaults** deliberately replaces shape/palette values; it preserves the seed. **New seed** changes only the seed. Profile changes support editor Undo.

Select the Tree Workshop object to choose **Single Tree** or **Four Species**. In single mode, assign a profile; the Species field supplies a preset when no profile is assigned. During Play, use the inspector's **Regenerate trees** button, the profile's **Regenerate active workshop** button, or **G**. Values do not regenerate on every slider change, so a large tree cannot accidentally rebuild on every editor event. Profile assets retain edits after leaving Play; generated cells and meshes are temporary.

- Drag the right mouse button to orbit.
- Drag the middle mouse button to pan toward the roots, trunk or crown; pan speed scales with viewing distance.
- Use the mouse wheel to zoom. Pan toward the trunk base before zooming in to inspect its bark ridges.
- Press Home to restore the original target, distance and viewing angles.

The height control spans 2–120 m and the bundled redwood now depicts a mature 75 m coast redwood. Trunk radius supports up to 6 m and crown radius up to 35 m. Bark Relief controls actual material geometry in meters, while Bark Ridge Count controls the main longitudinal ridges. These are separate from bark color variation.

The sparse preview bounds include age, seed variation, canopy sprays and root flare, with explicit caps of 204.8 m vertically and 409.6 m horizontally. The inspection camera can frame distant specimens at up to 700 m, within its 1000 m far plane. These are inspection limits rather than promises of interactive performance at the largest settings. Tall/heavily branched edits can reach generation caps and are reported in the view and Console. The forest gameplay arena remains separate.

The preview uses the shared chunk renderer. It generates one bounded specimen at a time and builds at most eight chunk surfaces per frame by default. Tree growth has explicit segment, voxel-candidate, and foliage-cluster caps. A clipped or budget-limited result is reported in the preview and Console. The per-tree generation call is synchronous: count caps do not guarantee a particular frame time. Ground exists only in root patches; the large sparse domain does not contain a dense ground plane.

Edit-mode mesh generation is intentionally disabled. Play mode uses the renderer's normal destruction lifecycle and prevents preview meshes/materials from becoming saved editor artifacts.

Unity Editor import, asset deserialization, inspector interaction, and play-mode rendering remain unverified in this environment. `python3 Tools/run_authoring_checks.py` compiles the actual TreeProfile mapping with test-only Unity/input/renderer substitutes; its checks are not Unity tests.

Developers can deliberately refresh the four bundled preset assets after changing the core presets with `python3 Tools/run_authoring_checks.py --refresh-presets`. This exports the actual compiled presets, including every habit control and palette. The flag overwrites those four bundled assets; ordinary validation runs preserve artist edits.
