#!/usr/bin/env python3
"""Append foliage-lab prototypes (IDs 300+) to the AVPM0004 pack without touching the original 99.

Inputs: the current pack and v4-encoded lab records (ExportPrototypes.cs `lab` mode followed by
quad_pack.py). Interaction channels are derived with the same rules as bake_interactions.py:
hard (body-blocking) spans, leaf spans for soft occupied cells, stick spans for thin unsupported
wood. Existing records are copied byte-for-byte; the script asserts that before writing.
"""
from pathlib import Path
import argparse, io, struct, gzip, zlib, hashlib, json
import numpy as np
from scipy import ndimage

ROOT = Path(__file__).resolve().parents[2]
PACK = ROOT / 'Assets/AshenVoxel/Exploration/Prototypes/ExplorationPrototypes.bytes'

def sha(b): return hashlib.sha256(b).hexdigest()

def var(s):
    v = 0
    for shift in range(0, 35, 7):
        b = s.read(1)[0]; v |= (b & 127) << shift
        if b < 128: return v
    raise ValueError('bad varint')

def columns(mask, offset):
    out = io.BytesIO(); items = []
    for x, z in np.argwhere(mask.any(axis=1)):
        ys = np.flatnonzero(np.diff(np.pad(mask[x, :, z].astype(np.int8), (1, 1)))) + offset[1]
        items.append((x + offset[0], z + offset[2], ys))
    out.write(struct.pack('<i', len(items)))
    for x, z, ys in items: out.write(struct.pack('<hhH', x, z, len(ys) // 2) + ys.astype('<i2').tobytes())
    return out.getvalue()

def split_pack(data):
    assert data[:8] == b'AVPM0004'
    s = io.BytesIO(data); s.seek(8); count = struct.unpack('<I', s.read(4))[0]; records = []
    for _ in range(count):
        start = s.tell(); n, r, crc = struct.unpack('<III', s.read(12)); s.seek(n, 1)
        raw = gzip.decompress(data[start + 12:start + 12 + n]); assert len(raw) == r and zlib.crc32(raw) == crc
        ident = struct.unpack_from('<I', raw, 0)[0]
        records.append((ident, data[start:s.tell()]))
    assert s.tell() == len(data)
    return records

def interactions(raw):
    """raw = v4 geometry + collision columns (no interaction channels yet)."""
    s = io.BytesIO(raw); model, kind, ln = struct.unpack('<iBH', s.read(7)); name = s.read(ln).decode()
    bounds = np.array(struct.unpack('<6f', s.read(24)))
    offset = np.rint(bounds[:3] * 10).astype(int); shape = np.rint(bounds[3:] * 10).astype(int) - offset
    events = np.zeros((shape[0], shape[1] + 1, shape[2]), dtype=np.int8) if kind != 2 else None
    lods = s.read(1)[0]
    for lod in range(lods):
        quads, palette = struct.unpack('<ii', s.read(8)); s.seek(palette * 3, 1); prev = np.zeros(3, dtype=int)
        for q in range(quads):
            for axis in range(3):
                v = var(s); prev[axis] += (v >> 1) ^ -(v & 1)
            width, height = var(s), var(s); normal = s.read(1)[0] & 7; var(s); s.read(2)  # ao, material
            if lod == 0 and events is not None and normal in (2, 3):
                x, y, z = prev - offset
                events[x:x + height, y, z:z + width] += 1 if normal == 3 else -1
    geometry_end = s.tell()
    hard = np.zeros(tuple(shape), dtype=bool)
    cols = struct.unpack('<i', s.read(4))[0]
    for _ in range(cols):
        x, z, nsp = struct.unpack('<hhH', s.read(6))
        for _ in range(nsp):
            a, b = struct.unpack('<hh', s.read(4)); hard[x - offset[0], a - offset[1]:b - offset[1], z - offset[2]] = True
    assert s.tell() == len(raw)
    if kind == 2:
        channels = raw[geometry_end:] + struct.pack('<ii', 0, 0); return raw[:geometry_end] + channels, dict(id=model, name=name, kind=kind, hard=int(hard.sum()), leaf=0, stick=0)
    winding = np.cumsum(events, axis=1)[:, :-1, :]; assert np.all((winding == 0) | (winding == 1)), name
    occupied = winding == 1; assert np.all(~hard | occupied), name
    core = ndimage.binary_erosion(hard, structure=np.ones((3, 3, 3), bool)) if kind == 0 else np.zeros_like(hard)
    retained = hard & ndimage.binary_dilation(core, structure=np.ones((5, 5, 5), bool))
    if kind == 0:
        center = -offset[[0, 2]].astype(float)
        for y in range(max(0, -offset[1]), shape[1]):
            points = np.argwhere(hard[:, y, :])
            if not len(points): break
            d = ((points - center) ** 2).sum(axis=1); near = points[d <= 16]
            if not len(near): break
            closest = points[d.argmin()]; local = near[((near - closest) ** 2).sum(axis=1) <= 9]
            center = local.mean(axis=0)
            keep = points[((points - center) ** 2).sum(axis=1) <= 9]
            retained[keep[:, 0], y, keep[:, 1]] = True
    else:
        # Plants: woody parts thicker than a single stem stay hard (fungus logs, palmetto trunks);
        # isolated 10 cm stems become soft sticks, everything else soft foliage.
        retained = hard & ndimage.binary_opening(hard, structure=np.ones((2, 2, 2), bool))
    sticks = hard & ~retained; leaves = occupied & ~hard
    channels = columns(retained, offset) + columns(leaves, offset) + columns(sticks, offset)
    return raw[:geometry_end] + channels, dict(id=model, name=name, kind=kind, hard=int(retained.sum()), leaf=int(leaves.sum()), stick=int(sticks.sum()))

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--records', type=Path, required=True, help='folder of v4 lab .avpr records')
    p.add_argument('--pack', type=Path, default=PACK)
    p.add_argument('--report', type=Path, default=ROOT / 'Docs/lab-prototypes-bake.json')
    a = p.parse_args()
    original = a.pack.read_bytes(); existing = split_pack(original)
    base = [(i, b) for i, b in existing if i < 300]
    assert len(base) == 99, f'expected the 99 original prototypes, found {len(base)}'
    out = io.BytesIO(); reports = []; lab = []
    for path in sorted(a.records.glob('*.avpr')):
        src = path.read_bytes(); n, r, crc = struct.unpack_from('<III', src); raw = gzip.decompress(src[12:]); assert len(raw) == r and zlib.crc32(raw) == crc
        ident = struct.unpack_from('<I', raw, 0)[0]; assert 300 <= ident < 1000
        payload, rep = interactions(raw); comp = gzip.compress(payload, compresslevel=9, mtime=0)
        lab.append((ident, struct.pack('<III', len(comp), len(payload), zlib.crc32(payload)) + comp))
        info = json.loads(path.with_suffix('.json').read_text()); rep.update(bytes=len(comp), occupied_cells=info['occupied_cells'], lods=info['lods'], generation=info['generation'])
        reports.append(rep); print(rep['name'], 'hard', rep['hard'], 'leaf', rep['leaf'], 'stick', rep['stick'], flush=True)
    lab.sort()
    out.write(b'AVPM0004' + struct.pack('<I', len(base) + len(lab)))
    for _, b in base: out.write(b)
    for _, b in lab: out.write(b)
    data = out.getvalue()
    # The original 99 records are byte-identical prefixes of the new pack.
    check = split_pack(data); assert [b for _, b in check[:99]] == [b for _, b in base]
    a.pack.write_bytes(data)
    a.report.write_text(json.dumps(dict(status='PASS', scope='Foliage-lab exports baked with the actual greedy mesher; offline, not native Unity',
        original_pack_sha256=sha(original), pack_sha256=sha(data), bytes=len(data), original_records=99, lab_records=len(lab), records=reports), indent=1) + '\n')
    print('PACKED', len(base) + len(lab), 'prototypes,', len(data), 'bytes')

if __name__ == '__main__': main()
