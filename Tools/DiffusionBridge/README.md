# InfiniteDiffusion provider and Unity bridge

This tool calls the **actual pinned upstream `WorldPipeline` and pretrained
30 m models**. It exposes arbitrary signed native-pixel regions through a
persistent local HTTP provider and can bake float terrain into the project's
AVWF atlas format. It does not replace the learned model with procedural noise.
The five climate channels are retained. Unity still adds its own materials,
10 cm matter and distant meshes; the model itself does not synthesize10 cm
voxels.

New prepared jobs use explicit `physical-si-v2` climate encoding. Upstream
seasonality is Celsius×100 and is divided by100 once before publication; the
other four channels are unchanged. Legacy job/cache metadata without an
encoding remains `upstream-native-v1` and must be converted once by the
consumer. Unit versions enter world identity so raw and normalized owner data
cannot mix. See [the unit correction](../../Docs/CLIMATE_UNITS.md). Existing
recorded raw climate arrays are retained rather than relabelled.

The separately named fake backend in `check_bridge.py` tests the transport,
scheduler and format contracts. Passing those checks is not evidence that
weights ran. Actual model execution is recorded separately by the project.

## Prepare the inference environment

Use a separate Python environment, a matched PyTorch/Torchvision build for the
target platform, and `requirements-inference.txt`. Training/data-download extras
from the upstream repository are not required by this bridge.

The source checkout must be the inspected commit:

```sh
git clone https://github.com/xandergos/terrain-diffusion terrain-diffusion
git -C terrain-diffusion checkout e8dcb4b1a834ab2f6b1a6f5256ed7c9f2f3e8230
python -m pip install -r Tools/DiffusionBridge/requirements-inference.txt
python Tools/DiffusionBridge/bridge.py check-dependencies
```

The bridge requires the official synthetic-map statistics file explicitly via
`--stats-file`. Its hash becomes part of world identity. This avoids the
upstream interactive WorldClim download prompt during unattended generation.
The tested official statistics hash is
`e3132c3ef0c65d8613615f9278ffe23bbd9363ddcd87f1cc6f18456bcc9efe5c`.
Its pinned source is the [official ONNX model's statistics file](https://huggingface.co/xandergos/terrain-diffusion-30m-onnx/resolve/ad2df557eca5645f588766101cf3bc3682455c3e/pipeline_data.json).
Prepare the statistics before starting the provider; the bridge does not answer
interactive setup prompts.

Model identity is pinned to `xandergos/terrain-diffusion-30m` revision
`9ef8030cb805b433b98ec25c5dddefbac07a9e26`. `model-manifest.json` records
SHA-256 for every inspected configuration and weight file. An existing local
snapshot can be supplied through `--model-dir`; otherwise the model must already
exist in the Hugging Face cache. `--allow-download` on `serve` or `bake` explicitly
enables fetching that pinned snapshot. Model weights are not bundled here.

```sh
python Tools/DiffusionBridge/bridge.py plan \
  --upstream-source /path/to/terrain-diffusion \
  --model-dir /path/to/terrain-diffusion-30m \
  --stats-file /path/to/pipeline_data.json \
  --seed 714103 --device cpu --torch-threads 4 --T 2 \
  --output /path/to/world-job.json

python Tools/DiffusionBridge/bridge.py serve \
  --job /path/to/world-job.json --port 8765
```

Prepare jobs with the same Python environment that will execute them. Planning
never loads weights or generates terrain. A job captures model/source
revisions, seed, physical spacing, statistics and conditioning hashes, runtime
precision/device, Python and inference-package versions, blending phases, SNR
controls and cache/work limits. Executing a prepared job under different package
versions is rejected; prepare a new job for that environment. Editing
those fields without producing a new world identity is rejected. The HTTP
provider has no mutable global seed endpoint. Source working-tree modifications
under `terrain_diffusion` are rejected rather than mislabeled as the pinned
implementation.

## HTTP contract

`GET /status` returns readiness, `world_id`, native spacing, queue/in-flight
counts, resident response bytes/count and limits. The server begins accepting
requests only after model preparation succeeds. Inference failure latches
`ready:false` and its error; later requests do not repeatedly invoke a failed
native backend. Correct the cause and restart. Failure never becomes a successful
flat fallback world.

`POST /region` accepts JSON:

```json
{"world_id":"<from status>","row0":608,"col0":1120,"rows":65,"cols":65}
```

Rows address global Z; columns address global X. Coordinates are **native pixel
indices**, including negative values. At30 m spacing the example origin is
X33,600 m/Z18,240 m. Elevations remain float32 **meters**, with no Minecraft
sea-level offset or vertical compression. `rows`/`cols` are sample counts;
physical extent is `(count-1)*spacing`.

The200 response is `application/vnd.ashen.terrain-region-v1`:

| Part | Encoding |
| --- | --- |
| Magic | Eight ASCII bytes `ATDR0001` |
| Metadata length | uint32 little endian |
| Metadata | UTF-8 JSON with schema, world identity, window, spacing, origin, units, backend and channel order |
| Height | `rows*cols` little-endian float32, row-major |
| Climate | Five float32 planes: temperature°C, seasonality°C, annual precipitationmm, precipitation variability%, beta |
| Source biomes | One byte per sample; imported IDs or0 for unclassified |
| Land | One byte per sample, generated height>=0 |
| Integrity | uint32 IEEE CRC32 over every preceding byte |

The model API returns its beta channel unchanged; this bridge does not invent
physical lapse-rate units for it. Imported biome IDs remain separate from
Unity's provisional Forest display override. No climate-to-biome classification
is falsely implied when IDs are0.

Bad requests return400, wrong world identity409, full queue429, and inference
failure500. Unity should issue asynchronous region requests, back off on429 and
never make network requests inside synchronous `Sample`. The first cold CPU
request can take minutes; cached/ready queries are a different workload. A caller
disconnect does not cancel work shared by other callers.

## Ownership, partition consistency and limits

Actual upstream tests found cropped-query border differences up to roughly
0.176 m, enough to change10 cm voxel quantization. Therefore this adapter always
generates **fixed256×256-pixel owner blocks with a32-pixel halo**, then returns
only each pixel's uniquely owned interior. Arbitrary region queries assemble
those same owners. They do not independently own their overlapping halos.

Checksum-verified owner bytes are persisted under the immutable world ID.
Response LRU memory is capped at128 MiB by default, upstream cache at256 MiB,
owner disk cache at512 MiB, distinct pending work at one active plus eight queued,
and each requested region at262,144 samples/1,024 pixels per side. Owner disk
cache uses LRU eviction and is **regenerable cache**, not an authoritative save
format. Persistent edited worlds still need authoritative region/snapshot
storage. Canonical windows fix request partitioning; they do not prove bitwise
equality between different GPU/precision/runtime implementations. Device and
precision are part of generation identity.

One inference worker owns the model. Concurrent identical requests share a
future and the completed response. A large region spanning multiple owners can
cost more cold inference even when it contains few requested samples. Far
32–100 km render tiles must not request enormous native bounding rectangles:
use the explicitly approximate macro/map preview for distant LOD, then replace
bounded nearby regions when neural data is ready. This preserves the requested
fast-preview behavior without calling approximate terrain full inference.

## Azgaar conditioning

The structured Azgaar control-atlas converter is in `Tools/WorldScaleImport`.
To prepare the actual five-channel neural inputs directly from Azgaar Full JSON:

```sh
python Tools/DiffusionBridge/prepare_azgaar.py \
  "World Full.json" /path/to/conditioning \
  --upstream-source /path/to/terrain-diffusion --width-km 1800

python Tools/DiffusionBridge/bridge.py plan \
  --upstream-source /path/to/terrain-diffusion \
  --model-dir /path/to/terrain-diffusion-30m \
  --stats-file /path/to/pipeline_data.json \
  --conditioning /path/to/conditioning/conditioning.json \
  --snr .35,.2,.2,.2,.2 --seed 714103 --device cpu \
  --output /path/to/authored-world-job.json
```

Preparation calls the pinned upstream polygon/climate conversion; it loads no
weights. Original grid elevations/climate and packed biome polygons remain
distinct. Source file hash, algorithm, physical bounds, coverage bounds, units
and assumed climate conversions are recorded. Horizontal width is independent
of elevation and the10 cm matter scale. Height follows the upstream Azgaar
conversion, not Minecraft's compressed block-height conversion.

Use `--elevation-scale 1.5` to raise positive land elevations independently of
horizontal map size, or `--peak-meters 8000` to target the sampled conditioning
peak. The two modes are mutually exclusive. Both preserve sea level and ocean
depths and become part of conditioning provenance/world identity. A conditioning
peak is a model input target, not a guaranteed maximum after neural refinement.
Default scale1 preserves the original conversion bytes. To compare rough and
neural terrain, prepare both with consistent physical bounds and height mapping;
mixing a scaled rough atlas with unscaled neural inputs does not establish parity.

The authored map is centered by default; explicit physical X/Z origin and depth
are available. Arrays are rasterized onto the fixed global7,680 m conditioning
lattice. Coverage may extend beyond the requested physical bounds, which remain
unchanged: a750 km map is not silently stretched to752.64 km. Outside-map cell
centers get explicit water elevation. The later64-cell context pad is installed
**before** the original global origin; callers do not add491,520 m to native
query positions. Periodic maps retain their phase at nonzero signed origins.

The user map was exercised through this preparer; this does not imply exact
parity with every hand-authored summit or coastal rule from the Minecraft fork.
`check_azgaar_prepare.py --upstream-source /path/to/terrain-diffusion` validates
known heights, source climate/biomes, exact physical scale, negative origins,
water context and invalid input using a synthetic schema fixture. It executes
the actual upstream converter without loading weights.

For neural conditioning, use the upstream Full-JSON raster workflow described in
[the research report](../../Docs/INFINITE_DIFFUSION_RESEARCH.md), then provide
this bridge a manifest. TIFF needs Rasterio; NumPy `.npy` files are also accepted.

```json
{
  "schema":"ashen-conditioning-v1",
  "coarse_spacing_m":7680,
  "origin_i":0,
  "origin_j":0,
  "periodic":false,
  "channels":{
    "0":{"path":"heightmap.tif","units":"m"},
    "1":{"path":"temperature.tif","units":"degC"},
    "2":{"path":"temperature_std.tif","units":"degC"},
    "3":{"path":"precipitation.tif","units":"mm/year"},
    "4":{"path":"precipitation_cv.tif","units":"percent"}
  },
  "source_biome":"source-biome.npy"
}
```

All supplied arrays share a lattice and dimensions. `source_biome` is optional
and has source byte IDs on that lattice. Channel2 Celsius is converted to the
upstream Celsius×100 representation. Finite imports get64 cells of edge context
while retaining their original signed coordinate anchor. Periodic imports keep
their original period and do not receive edge padding. The30 m checkpoint's
conditioning lattice is256×30=7,680 m: other spacing is rejected until explicitly
resampled. Missing channels deliberately retain upstream synthetic conditioning.

Add `--conditioning manifest.json` when preparing a job. `--snr` accepts the five
upstream values directly; despite the parameter's name, do **not** invert the
Minecraft fork's noise-ratio values. For example, elevation0.35 and climate0.2
can be specified as `--snr .35,.2,.2,.2,.2`: the fork applies its climate ratio to
all four climate-conditioning channels. Exact coastline preservation is not
claimed: conditioning remains soft. A hard authored-coast envelope, exceptional
summit anchors and ecological reservations require their own explicit mapping
layer; no settlement, road or unrelated mod subsystem is ported here.

## Bake a Unity-readable region

```sh
python Tools/DiffusionBridge/bridge.py bake \
  --job /path/to/world-job.json \
  --row0 608 --col0 1120 --rows 65 --cols 65 \
  --output /path/to/neural-region.avwf
```

AVWF carries exact float heights, source biome IDs and generated land flags.
All five climate planes survive in the adjacent `.climate.npz`; `.report.json`
records the complete job, region identity and checksums. This is an additional
baking workflow over the same real provider, not a substitute diffusion model.

Run `python3 Tools/DiffusionBridge/check_bridge.py` for NumPy-only transport,
conditioning, scheduler, failure, cache, canonical ownership and AVWF checks.
They are explicitly fake-backend contract tests. Real inference and Unity
integration have separate validation records.

For an optional real-weight bridge run after preparing the inference environment:

```sh
python Tools/DiffusionBridge/check_real_bridge.py \
  --job /path/to/world-job.json --output-dir /path/to/bridge-validation
```

This runs the selected model, validates a canonical region and overlapping
subqueries, verifies owner-disk replay, and bakes a Unity-readable AVWF sample.
The output report records latency, package/source identities and the limits of
the actual test. It can take several minutes on CPU. It does not establish
adjacent-owner geometry, GPU performance, full streaming gameplay or Unity.
