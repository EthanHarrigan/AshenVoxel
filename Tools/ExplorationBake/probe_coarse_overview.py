"""Bounded probe of the pinned model's learned low-frequency elevation channel.

This is a source-map experiment, not a renderer or a full decoder bake.
Outputs stay outside Assets. The native 30m-to-world-5m mapping is preserved.
"""
from pathlib import Path
import argparse
import hashlib
import json
import math
import sys
import time

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'Tools/DiffusionBridge'))
import bridge as b


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--job', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    a = p.parse_args()
    a.output.mkdir(parents=True, exist_ok=True)
    job = b.read_job(a.job)
    started = time.perf_counter()
    backend = b.RealBackend(job)
    import torch
    load_seconds = time.perf_counter() - started
    report = {'scope': 'Actual learned latent low-frequency channel; no full decoder',
              'job_world_id': job['world_id'], 'device': job['spec']['device'],
              'load_seconds': load_seconds, 'queries': []}
    print(json.dumps({'model_loaded_seconds': load_seconds}), flush=True)
    try:
        for name, r, c, size in [('valley', -400, -1100, 128), ('adjacent', -400, -972, 128)]:
            t = time.perf_counter()
            with torch.inference_mode():
                v = backend.pipeline.latents[:, r:r+size, c:c+size]
                low = ((v[4] / v[5]) * 38.6 - 31.4).numpy()
            h = np.sign(low) * np.square(low)
            np.savez_compressed(a.output / (name + '.npz'), height=h, lowfreq=low,
                                latent_row0=r, latent_col0=c, world_spacing_m=40.)
            row = {'name': name, 'seconds': time.perf_counter()-t,
                   'latent_window': [r, c, size, size], 'height_range_m': [float(h.min()), float(h.max())],
                   'finite': bool(np.isfinite(h).all()),
                   'height_sha256': hashlib.sha256(h.tobytes()).hexdigest()}
            report['queries'].append(row)
            print(json.dumps(row), flush=True)
        report['status'] = 'PASS'
    finally:
        report['elapsed_seconds'] = time.perf_counter()-started
        (a.output / 'probe.json').write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
        backend.pipeline.close()


if __name__ == '__main__':
    main()
