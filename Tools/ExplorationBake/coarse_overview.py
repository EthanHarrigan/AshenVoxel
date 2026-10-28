"""Offline, bounded learned elevation overview helpers (no Unity/LOD changes)."""
from pathlib import Path
import copy
import ctypes
import gc
import hashlib
import json
import math
import os
import subprocess
import sys

import numpy as np
from scipy.ndimage import gaussian_filter, map_coordinates

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'Tools/DiffusionBridge'))
sys.path.insert(0, str(ROOT / 'Tools/WorldScaleImport'))
import bridge as b

VERSION = 'learned-lowfreq-overview-v1'
NATIVE_WORLD_SPACING = 5.
LATENT_COMPRESSION = 8
LATENT_WORLD_SPACING = NATIVE_WORLD_SPACING * LATENT_COMPRESSION
LOW_MEAN, LOW_STD = -31.4, 38.6


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def atomic_json(path, value):
    path = Path(path)
    temp = path.with_suffix(path.suffix + '.tmp')
    temp.write_text(json.dumps(value, indent=2, allow_nan=False)+'\n', encoding='utf-8')
    os.replace(temp, path)


def available_commit():
    if os.name != 'nt':
        return None
    class Memory(ctypes.Structure):
        _fields_ = [('length', ctypes.c_uint32), ('load', ctypes.c_uint32)] + [
            (name, ctypes.c_uint64) for name in ('physical', 'free_physical', 'commit',
             'free_commit', 'virtual', 'free_virtual', 'extended')]
    s = Memory()
    s.length = ctypes.sizeof(s)
    if not ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(s)):
        raise OSError('Cannot query Windows commit headroom')
    return int(s.free_commit)


def job_for_batch(path, batch):
    if batch not in (1, 2, 4, 8):
        raise ValueError('Bounded batch must be 1, 2, 4 or 8')
    job = copy.deepcopy(b.read_job(path))
    job['spec']['latents_batch_size'] = [batch]
    job['world_id'] = hashlib.sha256(b.canonical(b.identity_spec(job['spec']))).hexdigest()
    return b.validate_job(job)


def load_height_pipeline(job):
    """Load exact pinned weights once, without decoder or random CPU copies.

    Meta initialization + strict assigned state is equivalent parameter loading;
    no network download, dtype conversion, retraining or source patch occurs.
    """
    b.validate_job(job)
    b.validate_runtime(job)
    source = Path(job['upstream_source'])
    if subprocess.check_output(['git', '-C', str(source), 'rev-parse', 'HEAD'], text=True).strip() != b.UPSTREAM_COMMIT:
        raise ValueError('Pinned upstream commit mismatch')
    if subprocess.run(['git', '-C', str(source), 'diff', '--quiet', 'HEAD', '--', 'terrain_diffusion']).returncode:
        raise ValueError('Upstream source has modifications')
    model_dir = job.get('model_dir')
    if not model_dir:
        from huggingface_hub import snapshot_download
        model_dir = snapshot_download(job['spec']['model'], revision=job['spec']['model_revision'], local_files_only=True)
    model_dir = Path(model_dir)
    manifest = json.loads((ROOT / 'Tools/DiffusionBridge/model-manifest.json').read_text())
    for entry in manifest['files']:
        if sha(model_dir / entry['file']) != entry['sha256']:
            raise ValueError('Pinned model hash mismatch: ' + entry['file'])
    sys.path.insert(0, str(source))
    import torch
    from safetensors.torch import load_file
    from terrain_diffusion.inference.world_pipeline import WorldPipeline
    from terrain_diffusion.models.edm_unet import EDMUnet2D
    import terrain_diffusion.inference.synthetic_map as synthetic
    spec = job['spec']
    synthetic.STATS_CACHE_PATH = spec['statistics']['path']
    torch.set_num_threads(spec['torch_threads'])
    config = dict(WorldPipeline.load_config(str(model_dir)))
    config.update(seed=spec['seed'], latents_batch_size=spec['latents_batch_size'],
                  T=spec['T'], torch_compile=False, dtype=None,
                  caching_strategy='direct', cache_limit=spec['pipeline_cache_bytes'])
    pipeline = WorldPipeline(**config)
    for field, folder in (('coarse_model', 'coarse_model'), ('base_model', 'base_model')):
        with torch.device('meta'):
            model = EDMUnet2D.from_config(EDMUnet2D.load_config(str(model_dir / folder)))
        state = load_file(str(model_dir / folder / 'diffusion_pytorch_model.safetensors'), device='cpu')
        model.load_state_dict(state, strict=True, assign=True)
        model.eval().to(spec['device'])
        del state
        gc.collect()
        setattr(pipeline, field, model)
    pipeline.set_cond_snr(spec['conditioning_snr'])
    pipeline.bind()
    biomes = b.install_conditioning(pipeline, spec['conditioning'])
    if pipeline.native_resolution != 30 or pipeline.latent_compression != LATENT_COMPRESSION:
        raise ValueError('Unexpected model coordinate scale')
    return pipeline, biomes


def latent_coordinate(world_meters):
    # Same half-pixel convention as upstream bilinear 8x Laplacian reconstruction.
    return (np.asarray(world_meters) / NATIVE_WORLD_SPACING + .5) / LATENT_COMPRESSION - .5


def sample_overview(query_low, xs, zs, spacing=320.):
    """Filter learned 40m elevation in meters, then sample a global 320m lattice.

    Fixed finite halo gives the same filter result regardless of output partition.
    query_low must return denormalized signed-sqrt elevation, not height meters.
    """
    if spacing < 80 or spacing > 1280 or spacing % LATENT_WORLD_SPACING:
        raise ValueError('Overview spacing must be an integral 40m multiple in 80..1280')
    x, z = latent_coordinate(xs), latent_coordinate(zs)
    sigma = spacing / (2 * LATENT_WORLD_SPACING)
    halo = math.ceil(3 * sigma) + 2
    c0, r0 = math.floor(float(x[0]))-halo, math.floor(float(z[0]))-halo
    c1, r1 = math.ceil(float(x[-1]))+halo+1, math.ceil(float(z[-1]))+halo+1
    low = np.asarray(query_low(r0, r1, c0, c1), dtype=np.float64)
    if low.shape != (r1-r0, c1-c0) or not np.isfinite(low).all():
        raise ValueError('Invalid learned low-frequency region')
    meters = np.sign(low) * low * low
    filtered = gaussian_filter(meters, sigma=sigma, truncate=3., mode='nearest')
    coordinates = np.meshgrid(z-r0, x-c0, indexing='ij')
    return map_coordinates(filtered, coordinates, order=1, prefilter=False, mode='nearest').astype('f4')


def model_query(pipeline):
    import torch
    def query(r0, r1, c0, c1):
        with torch.inference_mode():
            data = pipeline.latents[:, r0:r1, c0:c1]
            low = (data[4]/data[5]) * LOW_STD + LOW_MEAN
            return low.numpy()
    return query


def release_temporary_gpu_memory():
    import torch
    gc.collect()
    if torch.cuda.is_available():
        torch.cuda.synchronize()
        before = {'allocated': torch.cuda.memory_allocated(), 'reserved': torch.cuda.memory_reserved(),
                  'commit': available_commit()}
        torch.cuda.empty_cache()
        gc.collect()
        return {'before': before, 'after': {'allocated': torch.cuda.memory_allocated(),
                 'reserved': torch.cuda.memory_reserved(), 'commit': available_commit()}}
    return {'commit': available_commit()}
