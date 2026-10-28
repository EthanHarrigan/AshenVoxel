"""Pre-bake the in-game 160 m / 80 m learned terrain overview, land first, from spawn outward.

The game already refines distant terrain by asking the local overview service (serve_overview.py,
port 8788) for 2560 m cells at 160 m and then 80 m. This script fills that service's disk cache
ahead of time, using the exact same provider code, identity and window lattice. While playing,
every request is then an instant cache hit instead of a live model run.

* Land cells: computed with the pinned learned model (GPU job of the bundled 320 m overview).
* Ocean cells: no model work. Their patches are the bundled 320 m overview, smoothed the same
  way and clamped below sea level ("flat for now"), so the far-terrain pass never stalls on sea.
* Order: nearest to the recommended spawn first. Resumable: finished windows are cache hits.
* Crash avoidance: BelowNormal priority, the provider's own commit-memory admission
  (4 GiB to load, 2 GiB per query), indefinite back-off instead of failing, one model only.
"""
from pathlib import Path
import argparse
import datetime
import json
import math
import sys
import time
import traceback

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
_MARK = HERE.parents[1] / '.terrain-cache/prebake-overview'
_MARK.mkdir(parents=True, exist_ok=True)
with (_MARK / 'prebake.log').open('a', encoding='utf-8') as _f:
    _f.write(datetime.datetime.now().strftime('%H:%M:%S ') + f'process start (python {sys.version.split()[0]})\n')
try:
    import numpy as np
    from coarse_overview import ROOT, b, atomic_json, LATENT_WORLD_SPACING, NATIVE_WORLD_SPACING, LATENT_COMPRESSION
    from overview_provider import OverviewProvider, Busy
    from serve_overview import LearnedQuery, below_normal_priority
    from convert_world import read_avwf
except BaseException:
    with (_MARK / 'prebake.log').open('a', encoding='utf-8') as _f:
        _f.write('import failed:\n' + traceback.format_exc())
    raise

CORE = 2560
PADDING = 320
QUERY_KIND = 'pinned-coarse-base-model'  # must match serve_overview so cache keys are identical


def window(cell_x, cell_z, spacing):
    # Same lattice as ExplorationOverviewRefinement.MakeRequest (C#).
    return {'row0': (cell_z * CORE - PADDING) // spacing, 'col0': (cell_x * CORE - PADDING) // spacing,
            'rows': 3200 // spacing + 1, 'cols': 3200 // spacing + 1, 'spacing_m': spacing}


class MacroQuery:
    """Signed-sqrt 'learned low frequency' synthesised from the bundled 320 m overview."""
    def __init__(self, macro):
        self.h = macro['heights'].astype('f8'); self.m = macro
    def height(self, x, z):
        m = self.m
        fx = np.clip((x - m['origin_x']) / m['extent_x'] * (m['columns'] - 1), 0, m['columns'] - 1)
        fz = np.clip((z - m['origin_z']) / m['extent_z'] * (m['rows'] - 1), 0, m['rows'] - 1)
        x0 = np.floor(fx).astype(int); z0 = np.floor(fz).astype(int)
        x1 = np.minimum(x0 + 1, m['columns'] - 1); z1 = np.minimum(z0 + 1, m['rows'] - 1)
        tx = fx - x0; tz = fz - z0
        a = self.h[z0, x0] * (1 - tx) + self.h[z0, x1] * tx
        c = self.h[z1, x0] * (1 - tx) + self.h[z1, x1] * tx
        return a * (1 - tz) + c * tz
    def __call__(self, r0, r1, c0, c1):
        # Inverse of coarse_overview.latent_coordinate.
        xs = ((np.arange(c0, c1) + .5) * LATENT_COMPRESSION - .5) * NATIVE_WORLD_SPACING
        zs = ((np.arange(r0, r1) + .5) * LATENT_COMPRESSION - .5) * NATIVE_WORLD_SPACING
        X, Z = np.meshgrid(xs, zs)
        h = np.minimum(self.height(X, Z), -1.0)
        return np.sign(h) * np.sqrt(np.abs(h))
    def close(self):
        pass


def is_land(macro_query, cx, cz):
    pad = PADDING + 960
    xs = np.linspace(cx * CORE - pad, (cx + 1) * CORE + pad, 25)
    zs = np.linspace(cz * CORE - pad, (cz + 1) * CORE + pad, 25)
    X, Z = np.meshgrid(xs, zs)
    return float(macro_query.height(X, Z).max()) >= -30.0


def run_window(provider, request, log):
    backoff = 5
    while True:
        try:
            key = provider.submit(request)
        except Busy:
            time.sleep(1); continue
        while True:
            value = provider.result(key)
            if isinstance(value, bytes):
                return
            state = value['state']
            if state in ('FAILED', 'CANCELLED'):
                log(f"window {request} {state}: {value['error']}; retrying in {backoff}s")
                time.sleep(backoff); backoff = min(backoff * 2, 600)
                break
            time.sleep(.2)


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--job', type=Path, default=ROOT / '.terrain-cache/coarse320-study/bake320-v1/job.json')
    p.add_argument('--cache-dir', type=Path, default=ROOT / '.terrain-cache/regional-overview-cache')
    p.add_argument('--samples', type=Path, default=ROOT / 'Assets/AshenVoxel/Exploration/Samples')
    p.add_argument('--spacings', default='160,80')
    p.add_argument('--status', type=Path, default=ROOT / '.terrain-cache/prebake-overview/progress.json')
    a = p.parse_args()
    a.status.parent.mkdir(parents=True, exist_ok=True)
    log_path = a.status.with_name('prebake.log')
    def log(text):
        line = datetime.datetime.now().strftime('%H:%M:%S ') + text
        print(line, flush=True)
        with log_path.open('a', encoding='utf-8') as f: f.write(line + '\n')
    below_normal_priority()
    job = b.read_job(a.job)
    macro = read_avwf(a.samples / 'ExplorationMacro.bytes')
    world = json.loads((a.samples / 'exploration-world.json').read_text(encoding='utf-8-sig'))
    native = json.loads(macro['source_identity']).get('native_model_world_id')
    if native != job['world_id']:
        raise SystemExit(f'Job {job["world_id"]} does not match the bundled overview model {native}')
    spawn = world.get('recommended_spawn') or {'x_m': 0, 'z_m': 0}
    sx, sz = spawn['x_m'], spawn['z_m']
    spacings = [int(s) for s in a.spacings.split(',')]
    big = 4 * 1024 ** 3
    macro_query = MacroQuery(macro)
    def pair(cls, shared):
        model = cls(job, a.cache_dir, shared, fine_idle=lambda: True, strip_delay=0.,
                    query_kind=QUERY_KIND, idle_grace=3600., disk_limit=big, wait_seconds=3600)
        sea = cls(job, a.cache_dir, lambda: macro_query, fine_idle=lambda: True, strip_delay=0.,
                  query_kind=QUERY_KIND, idle_grace=3600., disk_limit=big, wait_seconds=3600,
                  commit=lambda: 1 << 62, min_commit=0, load_commit=0)  # no model: no memory admission
        if model.source_id != sea.source_id:
            raise SystemExit('Provider identities differ')
        return model, sea
    try:
        from serve_overview import SharedQuery
        shared = SharedQuery(job).factory
    except ImportError:
        shared = lambda: LearnedQuery(job)
    providers = {}
    if any(s in (160, 80) for s in spacings):
        providers[160] = providers[80] = pair(OverviewProvider, shared)
    if 40 in spacings:
        from overview_provider40 import OverviewProvider40
        providers[40] = pair(OverviewProvider40, shared)
    model, sea = providers[spacings[0]]
    w2, d2 = world['world_width_m'] / 2, world['world_depth_m'] / 2
    cells = [(x, z) for z in range(math.floor(-d2 / CORE), math.ceil(d2 / CORE))
                    for x in range(math.floor(-w2 / CORE), math.ceil(w2 / CORE))]
    cells.sort(key=lambda c: ((c[0] + .5) * CORE - sx) ** 2 + ((c[1] + .5) * CORE - sz) ** 2)
    land = {c: is_land(macro_query, *c) for c in cells}
    total_land = sum(land.values())
    status = {'status': 'RUNNING', 'source_id': model.source_id, 'cells': len(cells), 'land_cells': total_land,
              'spacings': spacings, 'spawn': [sx, sz], 'done_land': 0, 'done_sea': 0, 'started_utc': datetime.datetime.utcnow().isoformat()}
    log(f'{len(cells)} cells ({total_land} land) from spawn ({sx:.0f},{sz:.0f}); cache {a.cache_dir}')
    started = time.time(); model_seconds = 0.
    try:
        for i, (cx, cz) in enumerate(cells):
            t = time.time()
            for spacing in spacings:
                provider = providers[spacing][0 if land[(cx, cz)] else 1]
                run_window(provider, {'native_model_world_id': job['world_id'], **window(cx, cz, spacing)}, log)
            if land[(cx, cz)]:
                status['done_land'] += 1; model_seconds += time.time() - t
            else:
                status['done_sea'] += 1
            if land[(cx, cz)] or i % 50 == 0:
                rate = model_seconds / max(1, status['done_land'])
                status.update(last_cell=[cx, cz], elapsed_seconds=round(time.time() - started),
                              seconds_per_land_cell=round(rate, 2),
                              estimated_remaining_hours=round(rate * (total_land - status['done_land']) / 3600, 2),
                              cache_hits=sum(p.hits for m in providers.values() for p in m), model_queries=sum(m[0].model_queries for m in set(providers.values())),
                              updated_utc=datetime.datetime.utcnow().isoformat())
                atomic_json(a.status, status)
            if land[(cx, cz)] and status['done_land'] % 25 == 0:
                log(f"land {status['done_land']}/{total_land}, sea {status['done_sea']}, {status['seconds_per_land_cell']} s/land cell, ~{status['estimated_remaining_hours']} h left")
        status['status'] = 'PASS'
        log('All cells cached. Start the overview service (8788) while playing to use them.')
    except BaseException as error:
        status['status'] = 'STOPPED'; status['error'] = ''.join(traceback.format_exception_only(type(error), error)).strip()
        log('Stopped: ' + status['error'])
        raise
    finally:
        status['elapsed_seconds'] = round(time.time() - started)
        atomic_json(a.status, status)
        for m in set(providers.values()):
            m[0].close(); m[1].close()


if __name__ == '__main__':
    main()
