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
    p.add_argument('--fill', type=float, default=.95, help='stop at this fraction of the owner cache bound')
    p.add_argument('--owner-limit-gib', type=float, default=8., help='owner cache bound used by this bake and by serve_exploration (overrides the job default of 512 MiB; not part of world identity)')
    p.add_argument('--workers', default='auto', help='parallel model processes (auto: from CPU count and free memory)')
    p.add_argument('--worker-index', type=int, default=-1)
    p.add_argument('--threads', type=int, default=0)
    p.add_argument('--dry-run', action='store_true', help='print the plan only')
    a = p.parse_args()
    below_normal_priority()
    STATE.mkdir(parents=True, exist_ok=True)
    (STATE / 'owner-limit-bytes.txt').write_text(str(int(a.owner_limit_gib * 1024 ** 3)))
    job = b.read_job(a.job)
    world = json.loads((a.samples / 'exploration-world.json').read_text(encoding='utf-8-sig'))
    expected = world.get('detail_native_model_world_id')
    if expected not in (None, job['world_id']):
        raise SystemExit(f"Job {job['world_id']} is not the bundled detail model world {expected}")
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
    limit = int(a.owner_limit_gib * 1024 ** 3)
    if a.worker_index < 0:
        log(f"{len(tiles)} land owner tiles within {a.radius_km:.0f} km of spawn; cache bound {limit >> 20} MiB at {a.fill:.0%}")
    cpus = os.cpu_count() or 4
    if a.worker_index < 0:
        if a.workers == 'auto':
            commit = available_commit()
            by_memory = 8 if commit is None else int(max(0, commit - 6 * 1024 ** 3) // int(3.5 * 1024 ** 3))
            workers = max(1, min(cpus // 4, by_memory, 8))
        else:
            workers = max(1, int(a.workers))
        threads = a.threads or max(2, cpus // workers)
        if a.dry_run:
            print(json.dumps({'tiles': len(tiles), 'tile_m': tile_m, 'nearest': tiles[:3], 'limit_mib': limit >> 20, 'workers': workers, 'threads': threads})); return
        return run_parent(a, tiles, workers, threads, limit)
    return run_worker(a, job, tiles[a.worker_index::int(a.workers)], size, limit)


def lock_or_exit():
    """One fine bake at a time across launchers. A lock older than 30 min without heartbeat is stale."""
    lock = STATE / 'bake.lock'
    try:
        if lock.exists() and time.time() - lock.stat().st_mtime < 1800:
            log('Another 5 m bake is already running (fresh lock); leaving it to finish.')
            raise SystemExit(0)
    except OSError:
        pass
    lock.write_text(str(os.getpid()))
    return lock


def run_parent(a, tiles, workers, threads, limit):
    import subprocess
    lock = lock_or_exit()
    log(f'starting {workers} worker processes x {threads} threads on {os.cpu_count()} logical CPUs; owner cache bound {limit >> 20} MiB')
    cmd = [sys.executable, '-X', 'utf8', str(Path(__file__).resolve())]
    passthrough = ['--job', str(a.job), '--owner-cache-dir', str(a.owner_cache_dir), '--samples', str(a.samples), '--world-spacing-m', str(a.world_spacing_m),
                   '--radius-km', str(a.radius_km), '--fill', str(a.fill), '--owner-limit-gib', str(a.owner_limit_gib), '--workers', str(workers), '--threads', str(threads)]
    procs = []
    for i in range(workers):  # staggered: model loads do not all peak at once
        procs.append(subprocess.Popen(cmd + passthrough + ['--worker-index', str(i)]))
        if i + 1 < workers: time.sleep(25)
    started = time.time()
    while True:
        alive = [p for p in procs if p.poll() is None]
        total = {'status': 'RUNNING' if alive else 'DONE', 'workers': workers, 'threads_per_worker': threads, 'tiles_planned': len(tiles), 'done': 0, 'new': 0,
                 'elapsed_seconds': round(time.time() - started), 'updated_utc': datetime.datetime.utcnow().isoformat()}
        rates = []
        for i in range(workers):
            try:
                w = json.loads((STATE / f'progress-w{i}.json').read_text())
                total['done'] += w.get('done', 0); total['new'] += w.get('new', 0)
                if w.get('seconds_per_tile'): rates.append(w['seconds_per_tile'])
            except (OSError, ValueError):
                pass
        if rates:
            per = sum(rates) / len(rates) / workers
            total['effective_seconds_per_tile'] = round(per, 1)
            total['estimated_remaining_hours'] = round(per * max(0, len(tiles) - total['done']) / 3600, 2)
        atomic_json(STATE / 'progress.json', total)
        lock.touch()
        if not alive:
            break
        time.sleep(20)
    codes = [p.returncode for p in procs]
    try: lock.unlink()
    except OSError: pass
    log(f'workers finished with exit codes {codes}')
    if any(codes):
        raise SystemExit(1)


def run_worker(a, job, tiles, size, limit):
    threads = a.threads or 2
    STATUS = STATE / f'progress-w{a.worker_index}.json'
    def log(text):  # noqa: shadow with worker prefix
        line = datetime.datetime.now().strftime('%H:%M:%S ') + f'[w{a.worker_index}] ' + text
        print(line, flush=True)
        with LOG.open('a', encoding='utf-8') as f: f.write(line + '\n')
    backend = None
    status = {'status': 'RUNNING', 'tiles_planned': len(tiles), 'done': 0, 'new': 0, 'started_utc': datetime.datetime.utcnow().isoformat()}
    started = time.time(); new_seconds = 0.
    try:
        for i, (_, tr, tc) in enumerate(tiles):
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
                status['status'] = 'MEMORY_WAIT'; atomic_json(STATUS, status)
                time.sleep(30)
            status['status'] = 'RUNNING'
            if backend is None:
                t = time.time()
                real = b.RealBackend(job)
                import torch
                torch.set_num_threads(threads)
                backend = canonical_backend(b, job, real, a.owner_cache_dir)
                backend.limit = limit  # raised bound, matched by serve_exploration via owner-limit-bytes.txt
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
            atomic_json(STATUS, status)
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
        atomic_json(STATUS, status)


if __name__ == '__main__':
    main()
