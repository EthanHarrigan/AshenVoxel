"""Pre-bake full-detail (5 m, decoded) terrain owner tiles around spawn, land only, nearest first.

The game's nearby detail comes from the local 5 m service (serve_exploration.py, port 8787),
which assembles every request from fixed 256 x 256-sample "owner" tiles (1.28 km at 5 m)
cached on disk. This fills that same cache ahead of time with the same pinned CPU job, so
walking around spawn later loads 5 m terrain instantly instead of waiting on the model.

Limits (from the pinned job, which also fixes the world identity):
* CPU inference (the detail world was generated on CPU; switching device changes identity).
* The owner disk cache is LRU-bounded by the job (owner_disk_cache_bytes). This stops before
  that bound so it never evicts tiles it just made; it covers roughly the nearest few
  hundred km^2 of land.
Crash avoidance: BelowNormal priority, waits whenever free commit memory is under 3 GiB,
retries failed tiles with back-off, resumable (finished tiles are cache hits).
"""
from pathlib import Path
import argparse
import datetime
import json
import math
import os
import sys
import time
import traceback

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
STATE = ROOT / '.terrain-cache/prebake-fine'
STATE.mkdir(parents=True, exist_ok=True)
LOG = STATE / 'prebake-fine.log'


def log(text):
    line = datetime.datetime.now().strftime('%H:%M:%S ') + text
    print(line, flush=True)
    with LOG.open('a', encoding='utf-8') as f:
        f.write(line + '\n')


log(f'process start (python {sys.version.split()[0]})')
try:
    sys.path.insert(0, str(HERE)); sys.path.insert(0, str(ROOT / 'Tools/DiffusionBridge')); sys.path.insert(0, str(ROOT / 'Tools/WorldScaleImport'))
    import numpy as np
    import bridge as b
    from physical_climate_policy import canonical_backend
    from convert_world import read_avwf
    from coarse_overview import available_commit, atomic_json
except BaseException:
    log('import failed:\n' + traceback.format_exc()); raise


def below_normal_priority():
    if os.name == 'nt':
        import ctypes
        k = ctypes.windll.kernel32; k.GetCurrentProcess.restype = ctypes.c_void_p
        k.SetPriorityClass.argtypes = (ctypes.c_void_p, ctypes.c_uint32)
        k.SetPriorityClass(k.GetCurrentProcess(), 0x4000)


def macro_height(m, x, z):
    fx = np.clip((x - m['origin_x']) / m['extent_x'] * (m['columns'] - 1), 0, m['columns'] - 1)
    fz = np.clip((z - m['origin_z']) / m['extent_z'] * (m['rows'] - 1), 0, m['rows'] - 1)
    x0 = np.floor(fx).astype(int); z0 = np.floor(fz).astype(int)
    x1 = np.minimum(x0 + 1, m['columns'] - 1); z1 = np.minimum(z0 + 1, m['rows'] - 1)
    tx = fx - x0; tz = fz - z0; h = m['heights']
    return (h[z0, x0] * (1 - tx) + h[z0, x1] * tx) * (1 - tz) + (h[z1, x0] * (1 - tx) + h[z1, x1] * tx) * tz


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--job', type=Path, default=ROOT / '.terrain-cache/world-job.json')
    p.add_argument('--owner-cache-dir', type=Path, default=ROOT / '.terrain-cache/exploration-bake/owners')
    p.add_argument('--samples', type=Path, default=ROOT / 'Assets/AshenVoxel/Exploration/Samples')
    p.add_argument('--world-spacing-m', type=float, default=5.)
    p.add_argument('--radius-km', type=float, default=40.)
    p.add_argument('--fill', type=float, default=.92, help='stop at this fraction of the job owner cache bound')
    p.add_argument('--threads', type=int, default=max(2, (os.cpu_count() or 4) - 2))
    p.add_argument('--dry-run', action='store_true', help='print the plan only')
    a = p.parse_args()
    below_normal_priority()
    job = b.read_job(a.job)
    world = json.loads((a.samples / 'exploration-world.json').read_text(encoding='utf-8-sig'))
    if world.get('detail_world_id') not in (None, job['world_id']):
        raise SystemExit(f"Job {job['world_id']} is not the bundled detail world {world.get('detail_world_id')}")
    macro = read_avwf(a.samples / 'ExplorationMacro.bytes')
    spawn = world.get('recommended_spawn') or {'x_m': 0, 'z_m': 0}
    size = job['spec']['owner_samples']; tile_m = size * a.world_spacing_m; limit = job['spec']['owner_disk_cache_bytes']
    reach = int(math.ceil(a.radius_km * 1000 / tile_m))
    sr, sc = math.floor(spawn['z_m'] / tile_m), math.floor(spawn['x_m'] / tile_m)
    tiles = []
    for dr in range(-reach, reach + 1):
        for dc in range(-reach, reach + 1):
            tr, tc = sr + dr, sc + dc
            x0, z0 = tc * tile_m, tr * tile_m
            X, Z = np.meshgrid(np.linspace(x0 - 160, x0 + tile_m + 160, 9), np.linspace(z0 - 160, z0 + tile_m + 160, 9))
            if float(macro_height(macro, X, Z).max()) < -20:
                continue  # open sea: keep the coarse terrain
            d = ((tc + .5) * tile_m - spawn['x_m']) ** 2 + ((tr + .5) * tile_m - spawn['z_m']) ** 2
            if d <= (a.radius_km * 1000) ** 2:
                tiles.append((d, tr, tc))
    tiles.sort()
    log(f"{len(tiles)} land owner tiles within {a.radius_km:.0f} km of spawn; cache bound {limit >> 20} MiB at {a.fill:.0%}; {a.threads} CPU threads")
    if a.dry_run:
        print(json.dumps({'tiles': len(tiles), 'tile_m': tile_m, 'nearest': tiles[:3], 'limit_mib': limit >> 20})); return
    backend = None; directory = None
    status = {'status': 'RUNNING', 'tiles_planned': len(tiles), 'done': 0, 'new': 0, 'started_utc': datetime.datetime.utcnow().isoformat()}
    started = time.time(); new_seconds = 0.
    try:
        for i, (_, tr, tc) in enumerate(tiles):
            if directory is None:
                # Cache directory layout is owned by canonical_backend; build it once.
                pass
            if backend is not None:
                files = list(backend.directory.glob('*.atdr'))
                used = sum(f.stat().st_size for f in files)
                if used >= a.fill * limit:
                    status['status'] = 'PASS'; status['stopped_reason'] = 'owner cache bound reached'
                    log(f'Owner cache {used >> 20} MiB reached {a.fill:.0%} of its bound; stopping so nothing is evicted.')
                    break
                if (backend.directory / f'{tr}_{tc}.atdr').exists():
                    status['done'] += 1; continue
            while True:
                commit = available_commit()
                if commit is None or commit >= 3 * 1024 ** 3:
                    break
                status['status'] = 'MEMORY_WAIT'; atomic_json(STATE / 'progress.json', status)
                time.sleep(30)
            status['status'] = 'RUNNING'
            if backend is None:
                t = time.time()
                real = b.RealBackend(job)
                import torch
                torch.set_num_threads(a.threads)
                backend = canonical_backend(b, job, real, a.owner_cache_dir)
                log(f'model loaded in {time.time() - t:.0f} s; owner cache {backend.directory}')
                if (backend.directory / f'{tr}_{tc}.atdr').exists():
                    status['done'] += 1; continue
            t = time.time(); backoff = 30
            while True:
                try:
                    backend.query((tr * size, tc * size, size, size)); break
                except MemoryError:
                    log('out of memory; waiting'); time.sleep(120)
                except Exception:
                    log(f'tile {tr},{tc} failed, retrying in {backoff}s:\n' + traceback.format_exc(limit=3))
                    time.sleep(backoff); backoff = min(backoff * 2, 900)
            spent = time.time() - t; new_seconds += spent
            status['done'] += 1; status['new'] += 1
            rate = new_seconds / status['new']
            status.update(last_tile=[tr, tc], seconds_per_tile=round(rate, 1), elapsed_seconds=round(time.time() - started),
                          updated_utc=datetime.datetime.utcnow().isoformat())
            atomic_json(STATE / 'progress.json', status)
            if status['new'] % 5 == 0 or status['new'] == 1:
                log(f"tile {status['done']}/{len(tiles)} ({status['new']} new), {rate:.0f} s/tile")
        else:
            status['status'] = 'PASS'
        log('Fine pre-bake finished: ' + status.get('stopped_reason', 'all planned tiles cached'))
    except BaseException as error:
        status['status'] = 'STOPPED'; status['error'] = ''.join(traceback.format_exception_only(type(error), error)).strip()
        log('Stopped: ' + status['error']); raise
    finally:
        status['elapsed_seconds'] = round(time.time() - started)
        atomic_json(STATE / 'progress.json', status)


if __name__ == '__main__':
    main()
