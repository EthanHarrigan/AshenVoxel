# Rock shading reference and exploration-renderer changes

Checked 26 October 2026. The requested visual reference is Avroid's newest clip
showing tree and rock destruction. The new-sorted profile exposed
[v1.7 — survival](https://www.reddit.com/r/VOXEL/comments/1xddu2o/v17_survival/)
first, followed by v1.6 and v1.5. The user identifies the relevant action in
that clip; this pass could read its discussion but **could not watch its video**.

## What was actually retrieved

The [profile](https://www.reddit.com/user/Avroid/submitted/?sort=new), latest
post text and [author's rendering discussion](https://www.reddit.com/r/VOXEL/comments/1xddu2o/comment/19rjcztu71d37/?force-legacy-sct=1)
were retrieved through the web tool. Avroid states that he uses path tracing,
voxelizes polygon models and renders greedy-meshed object triangles through
DXR. He distinguishes ray-marched ground from object acceleration structures.
He also reports 20 km view distance with LOD, 100 m without LOD and 120 FPS on a 4070;
the resolution, settings and full workload were not established here.

Those are author statements, not an audit or a visual comparison. They do not
disclose his boulder albedo palette, roughness values, mineral-noise method,
exposure, tone mapper, denoiser settings or contact-occlusion parameters.

The exact public poster URL redirects to Reddit's media wrapper in the web
tool. Ordinary poster and video-CDN requests returned 403. The direct post HTML
returned a JavaScript challenge page rather than the clip. Public oEmbed
metadata confirmed the post's title/author; it did not supply playable video.
The ordinary public JSON endpoint also returned 403. No challenge was solved,
alternate identity used or access control bypassed. Author-linked Instagram
was throttled, and TikTok was restricted by the retrieval tool. Search did not
identify a verified public YouTube mirror of this clip.

The earlier accessible official landing clip only showed an apple and cannot
substitute for boulder/tree-break footage. The new primary evidence therefore
**does not establish a frame-level or sampled-color match**. A direct accessible
copy of the requested video remains necessary for that calibration. The
retrieval artifacts are outside active assets at
`/workspace/research-avroid-shading` and are not bundled as project content.

## Implemented wins and their actual basis

These improvements are our renderer/material choices supported by the existing
voxel data and official URP interfaces, not attributed to unseen frames.

**Calm intrinsic stone color.** Rock shape generation remains version 2. A
separate `RockGenerator.AppearanceVersion=2` records revised pigment, and each
RockProfile identity includes `/appearance-v2`. The six-color parent-material
palette now spans roughly ±6% brightness instead of the prior roughly ±11%, with
small correlated warm/cool mineral shifts. Large coherent patches dominate;
the finer noise contributes 6% rather than 20%. Sedimentary bands remain but have
lower contrast. Shapes, occupancy, material IDs and base material colors are
unchanged. Existing saved colors are self-contained and are not repainted by
loading a snapshot.

This prevents 10 cm cells from becoming oversized isolated mineral crystals or
substitute shading. It is a controllable restrained starting palette; it is
not a claim that Avroid uses those numbers. Exact visual calibration remains
pending the actual reference frames.

**Light response stays separate from pigment.** WorldSurface now uses URP's
dielectric BRDF for direct sunlight and additional pixel lights, with the
existing roughness control. Sky spherical harmonics, voxel corner AO, shadow
maps and optional screen-space AO remain distinct signals. The object and terrain camera-normal
passes supply actual transformed mesh normals for SSAO when that renderer
feature is enabled; both forward shaders consume that AO texture. Vertex AO and screen AO combine with a minimum rather than
double-multiplying overlapping dark rims. This is conventional raster shading,
not Avroid's path tracer, dynamic diffuse GI or a weathering optics solver.

**GPU instancing renders the same geometry in every pass.** Forward, shadow,
depth and normal passes compile uniform-scale instancing variants and set the instance
transform. Exploration instance transforms use scale 1, supporting batches up
to 1023 rather than the default paired-matrix limit. Forward/normal fragments receive the instance ID as needed. The
Resources material retains those variants for builds. Its material is a shared
template; exploration renderers own their mutable instance. This enables
batched vegetation/rock prototypes without separate per-cell objects.

**No default extra white grain.** Saved pigment is sufficient by default;
shader cell/fine grain defaults to zero. If enabled deliberately, the optional
pattern uses object coordinates so floating-origin rebasing cannot replace it
with a new unrelated world-space hash. It remains filtered as cells become
subpixel. Instance prototypes can share that cosmetic pattern while their baked
seed variants retain their own authoritative colors.

The root exploration scene configures editable directional light, sky ambient,
fog and shadow distances. The shader does not silently add a fake directional
dark tint to saved rock colors. A shadowed side can change with the scene light.

## Verification and limitations

The actual revised pigment passed all 69 rock-generation checks, all 87 core
checks and C# 9/.NET Standard 2.1 compilation. Shapes remain connected, bounded and deterministic;
the palette remains opaque and at most six colors per rock. The exploration
prototype baker was told to bake rocks only after this pigment freeze.

`python3 Tools/check_surface_shading.py` checks 22 static shader/material
contracts, including instance transforms in all four passes, identical
material CBUFFER layouts, sRGB conversion, object-space detail, normal packing
and the material's instancing-retention flag. The report is
`Docs/shading-validation.json`. Static contracts are **not** an HLSL compiler,
native Unity render test, image-quality result or GPU timing.

The implementation signatures were checked against Unity's official 6000.3
Graphics source: [Lighting.hlsl](https://github.com/Unity-Technologies/Graphics/blob/6000.3/staging/Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl),
[BRDF.hlsl](https://github.com/Unity-Technologies/Graphics/blob/6000.3/staging/Packages/com.unity.render-pipelines.universal/ShaderLibrary/BRDF.hlsl),
[DepthNormalsPass.hlsl](https://github.com/Unity-Technologies/Graphics/blob/6000.3/staging/Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl)
and [Lit.shader](https://github.com/Unity-Technologies/Graphics/blob/6000.3/staging/Packages/com.unity.render-pipelines.universal/Shaders/Lit.shader).
The exploration Editor/pipeline setup also compiled against the official
6000.3 engine, Editor, URP and patched Unity Framework reference assemblies.
That C# API check does not execute first-open setup or compile the HLSL shaders.
These checks do not establish successful package resolution or
native shader compilation of this project. The branch can evolve; downloaded files
are retained outside project assets for this inspection.

Native Unity import, instanced color/shadow/depth agreement, SSAO appearance,
specular response, moving-camera stability and target-hardware performance
still require the Editor/player. Generated pictures from the earlier shape
update retain their historical pigment/source hashes; they are not relabelled
as previews of the new appearance or this URP shader.
