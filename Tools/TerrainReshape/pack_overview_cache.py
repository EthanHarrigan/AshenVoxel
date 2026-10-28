"""Pack the in-game overview cache (.avov windows) into one flat file for the reshape bake.

Keeps the finest spacing available per 2560 m cell (40 < 80 < 160). Output records:
int64 cell_x, int64 cell_z, int32 spacing, int32 columns, float32[columns*columns] heights.
Usage: python pack_overview_cache.py <cache dir> <out.bin>
"""
import struct
import sys
from pathlib import Path

import hashlib
import json
import zlib
import numpy as np

MAGIC = b'AVOV0001'


def decode(raw):
    """Same checks as overview_provider.decode (kept local: no model imports needed)."""
    if len(raw) < 16 or raw[:8] != MAGIC or zlib.crc32(raw[:-4]) & 0xffffffff != struct.unpack_from('<I', raw, len(raw) - 4)[0]:
        raise ValueError('magic/checksum')
    n = struct.unpack_from('<I', raw, 8)[0]
    meta = json.loads(raw[12:12 + n])
    w = meta['window']
    h, c = w['rows'], w['cols']
    if len(raw) != 16 + n + h * c * 4:
        raise ValueError('length')
    data = np.frombuffer(raw, dtype='<f4', offset=12 + n, count=h * c).reshape(h, c)
    if not np.isfinite(data).all() or hashlib.sha256(data.tobytes()).hexdigest() != meta['height_sha256']:
        raise ValueError('hash')
    return meta, data

CORE, PAD = 2560, 320
NATIVE = '0647786260a982ef6793a9e77fac850f0d9e23b8ae8ec3319fe6978b6538c84a'  # bundled overview model world


def main():
    cache, out = Path(sys.argv[1]), Path(sys.argv[2])
    best = {}
    bad = 0
    for path in cache.glob('*.avov'):
        try:
            meta, data = decode(path.read_bytes())
        except Exception:
            bad += 1
            continue
        if meta.get('native_model_world_id') != NATIVE or meta.get('query_kind') != 'pinned-coarse-base-model':
            continue
        w = meta['window']
        s = w['spacing_m']
        if w['rows'] != w['cols'] or w['rows'] != 3200 // s + 1:
            continue
        cx = (w['col0'] * s + PAD) // CORE
        cz = (w['row0'] * s + PAD) // CORE
        if cx * CORE - PAD != w['col0'] * s or cz * CORE - PAD != w['row0'] * s:
            continue
        if (cx, cz) not in best or s < best[(cx, cz)][0]:
            best[(cx, cz)] = (s, data)
    with out.open('wb') as f:
        f.write(struct.pack('<i', len(best)))
        for (cx, cz), (s, data) in sorted(best.items()):
            f.write(struct.pack('<qqii', cx, cz, s, data.shape[0]))
            f.write(data.astype('<f4').tobytes())
    counts = {}
    for s, _ in best.values():
        counts[s] = counts.get(s, 0) + 1
    print(f'{len(best)} cells packed, spacings {counts}, {bad} unreadable')


if __name__ == '__main__':
    main()
