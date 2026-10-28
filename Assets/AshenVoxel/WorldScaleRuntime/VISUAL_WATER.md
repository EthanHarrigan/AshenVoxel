# Visual water

`VisualWater.SetFrame` positions one bounded32×32 mean-sea grid around the
camera. Terrain depth occludes land. A coarse sampled seabed hint blends shallow
and deep colors; it is not per-pixel bathymetry. Analytic filtered wave normals,
Fresnel color and a sun highlight provide basic water appearance without an
opaque/depth-texture dependency. No reflection probes, SSR, refraction, foam,
hydrological connectivity or fluid simulation are claimed.

The grid spans twice the nominal view range in each horizontal direction,
capped at200 km half-extent. It rebuilds only after moving to another snapped
region or changing source/range/mean level, with1,089 depth-hint samples. Idle
above-water frames do not resample the bed. Underwater frames perform one local
bed query. The caller should pass the base terrain field rather than the full
surface-material classifier.

`InvalidateDepthHints()` marks a model-region/bed revision for one bounded
refresh, coalescing repeated invalidations. Runtime model revision handling
should call it so unchanged source identity does not retain old rough hints.

Double global-origin phases preserve waves through camera rebasing. The mesh
stays flat at mean sea level: waves are visual normals, not moving occupancy.
The component owns its mesh and a clone of the retained material and releases
both on teardown. It does not create Water cells, colliders, buoyancy, currents
or conservation behavior. Any depression below the chosen global sea plane can
visually flood; connected oceans, lakes and rivers require a later waterbody
field and are not inferred from these visuals.

A small underwater color wash appears only when the camera is below mean sea
and above the sampled bed. A camera underground does not become underwater just
because its Y coordinate is below sea level. The wash is scoped to this single
explorer view, restores GUI state and changes no global RenderSettings. It is a
simple full-game-view tint, not physical volumetric scattering or split-screen
post processing.

During finite model inspection, the caller supplies an immutable local
`cameraBedField` to the underwater point test while retaining the global source
for wide visual depth hints. Model-cache eviction therefore cannot make water
occupancy disagree with already materialized matter. That local field must only
be supplied while the camera lies inside its valid footprint; outside it the
caller returns to the global bed source.

The transparent shader tests against opaque depth, draws both sides, and does
not write depth. It is retained by `Resources/VisualWater.mat`. Actual Unity
shader compilation, transparency ordering, shore appearance and underwater
play-mode behavior remain unverified. `python3 Tools/run_water_checks.py` runs
actual C# logic with test API substitutes and checks references/static shader
contracts; it cannot prove native URP behavior.
