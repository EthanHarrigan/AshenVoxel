"""40 m learned overview windows (the model's native latent spacing), alongside 160/80 m.

Same wire format, admission and cache rules as overview_provider.OverviewProvider, with a
separate identity (so 160/80 m cache keys are untouched) and a 40 m sampler: the learned
low-frequency elevation is only lightly smoothed (sigma = half a latent cell) before the
bilinear lattice sample. 40 m windows are 81 x 81 nodes (2560 m core + 320 m halo).
Like the 160/80 m overview, the fine residual decoder is not used.
"""
from pathlib import Path
import hashlib
import json
import math
import os
import struct
import time
import zlib
import numpy as np
from scipy.ndimage import gaussian_filter, map_coordinates
from coarse_overview import VERSION, sha, latent_coordinate, LATENT_WORLD_SPACING
from overview_provider import OverviewProvider, Busy, Cancelled, canonical, encode, SCHEMA, MAGIC

FILTER40 = 'meter Gaussian sigma=20m (half latent) truncate3; bilinear global lattice; 40m learned low frequency'
MAX_SIDE = 81


def sample_overview40(query_low, xs, zs, spacing=40.):
    if spacing != 40:
        raise ValueError('40 m sampler only')
    x, z = latent_coordinate(xs), latent_coordinate(zs)
    sigma = .5
    halo = math.ceil(3 * sigma) + 2
    c0, r0 = math.floor(float(x[0])) - halo, math.floor(float(z[0])) - halo
    c1, r1 = math.ceil(float(x[-1])) + halo + 1, math.ceil(float(z[-1])) + halo + 1
    low = np.asarray(query_low(r0, r1, c0, c1), dtype=np.float64)
    if low.shape != (r1 - r0, c1 - c0) or not np.isfinite(low).all():
        raise ValueError('Invalid learned low-frequency region')
    meters = np.sign(low) * low * low
    filtered = gaussian_filter(meters, sigma=sigma, truncate=3., mode='nearest')
    coordinates = np.meshgrid(z - r0, x - c0, indexing='ij')
    return map_coordinates(filtered, coordinates, order=1, prefilter=False, mode='nearest').astype('f4')


def decode40(raw):
    if len(raw) < 16 or raw[:8] != MAGIC or zlib.crc32(raw[:-4]) & 0xffffffff != struct.unpack_from('<I', raw, len(raw) - 4)[0]:
        raise ValueError('Overview magic/checksum mismatch')
    n = struct.unpack_from('<I', raw, 8)[0]
    if n > 16384 or n + 16 > len(raw):
        raise ValueError('Overview metadata bounds')
    meta = json.loads(raw[12:12 + n]); w = meta['window']; h, c = w['rows'], w['cols']
    if not 1 <= h <= MAX_SIDE or not 1 <= c <= MAX_SIDE or len(raw) != 16 + n + h * c * 4:
        raise ValueError('Overview dimensions/length')
    data = np.frombuffer(raw, dtype='<f4', offset=12 + n, count=h * c).reshape(h, c)
    if not np.isfinite(data).all() or hashlib.sha256(data.tobytes()).hexdigest() != meta['height_sha256']:
        raise ValueError('Overview value/hash mismatch')
    return meta, data


class OverviewProvider40(OverviewProvider):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.identity = dict(self.identity, filter=FILTER40, spacing_variant='40m-v1',
                             source_sha256=dict(self.identity['source_sha256'], **{'overview_provider40.py': sha(__file__)}))
        self.source_id = hashlib.sha256(canonical(self.identity)).hexdigest()

    def _account(self, written):
        # The base provider re-lists and stats the whole cache after every write; with >10k
        # windows that costs seconds per window on Windows. Keep a running total instead and
        # only sort/evict when over the limit (full resync every 500 writes).
        with self.lock:
            total = getattr(self, '_disk_total', None)
            writes = getattr(self, '_disk_writes', 0) + 1
            self._disk_writes = writes
            if total is None or writes % 500 == 0:
                total = sum(p.stat().st_size for p in self.cache.glob('*.avov'))
            else:
                total += written
            if total > self.disk_limit:
                files = sorted(self.cache.glob('*.avov'), key=lambda p: p.stat().st_mtime_ns)
                total = sum(p.stat().st_size for p in files)
                for old in files:
                    if total <= self.disk_limit:
                        break
                    total -= old.stat().st_size; old.unlink()
            self._disk_total = total

    def validate(self, request):
        if request.get('native_model_world_id') != self.job['world_id']:
            raise ValueError('Pinned native model job identity mismatch')
        names = ('row0', 'col0', 'rows', 'cols', 'spacing_m')
        if any(type(request.get(k)) is not int for k in names):
            raise ValueError('Integer lattice/window fields required')
        r, c, h, w, s = (request[k] for k in names)
        if s != 40 or not 1 <= h <= MAX_SIDE or not 1 <= w <= MAX_SIDE or abs(r) > 200000 or abs(c) > 200000:
            raise ValueError('Bounded 40 m window required')
        return {k: request[k] for k in names}

    def status(self):
        value = super().status(); value['spacing_m'] = [40]; value['max_window_side'] = MAX_SIDE
        return value

    def _run(self, record):
        try:
            key = record['request_id']; path = self.cache / (key + '.avov')
            if path.exists():
                if path.stat().st_size > 131072:
                    raise ValueError('Oversized overview cache')
                raw = path.read_bytes(); meta, _ = decode40(raw)
                if meta['source_id'] != self.source_id or meta['window'] != record['window']:
                    raise ValueError('Overview cache identity mismatch')
                with self.lock:
                    self.hits += 1
                os.utime(path, None)
            else:
                w = record['window']
                xs = np.arange(w['col0'], w['col0'] + w['cols'], dtype='f8') * w['spacing_m']
                zs = np.arange(w['row0'], w['row0'] + w['rows'], dtype='f8') * w['spacing_m']
                height = np.empty((w['rows'], w['cols']), dtype='f4')
                for row in range(0, w['rows'], 8):
                    for col in range(0, w['cols'], 27):
                        self._wait_admission(record)
                        def guarded_query(r0, r1, c0, c1):
                            self._wait_admission(record)
                            while self.query is None:
                                with self.lock:
                                    record['state'] = 'LOADING'
                                self.query = self.factory()
                                self._wait_admission(record)
                            with self.lock:
                                self.model_queries += 1
                            return self.query(r0, r1, c0, c1)
                        height[row:row + 8, col:col + 27] = sample_overview40(guarded_query, xs[col:col + 27], zs[row:row + 8])
                        if record['cancel'].wait(self.strip_delay):
                            raise Cancelled('Cancelled between overview strips')
                    with self.lock:
                        record['completed_rows'] = min(row + 8, w['rows'])
                if record['cancel'].is_set():
                    raise Cancelled('Cancelled; output not published')
                meta = {'schema': SCHEMA, 'source_id': self.source_id, 'native_model_world_id': self.job['world_id'], 'window': w, **w,
                        'origin_x_m': float(xs[0]), 'origin_z_m': float(zs[0]), 'height_units': 'meters', 'filter': FILTER40,
                        'query_kind': self.query_kind, 'native_model_spacing_m': 30., 'world_meters_per_native_sample': 5.,
                        'latent_spacing_world_m': 40., 'generator_scope': 'Pinned learned coarse/base low-frequency output at latent spacing; fine residual decoder omitted',
                        'height_sha256': hashlib.sha256(height.astype('<f4').tobytes()).hexdigest(), 'identity': self.identity}
                raw = encode(meta, height); temporary = path.with_suffix('.tmp'); temporary.write_bytes(raw); os.replace(temporary, path)
                self._account(len(raw))
            with self.lock:
                if record['cancel'].is_set():
                    raise Cancelled('Cancelled before response publication')
                record['raw'] = raw; record['state'] = 'RELEASING'
        except Exception as error:
            with self.lock:
                record['state'] = 'CANCELLED' if isinstance(error, Cancelled) else 'FAILED'
                record['error'] = type(error).__name__ + ': ' + str(error)
        finally:
            with self.lock:
                if record['state'] == 'RELEASING':
                    if record['cancel'].is_set():
                        record['state'] = 'CANCELLED'; record['raw'] = None
                    else:
                        record['state'] = 'READY'
                self.last = record; self.active = None; self.idle_since = time.monotonic()
