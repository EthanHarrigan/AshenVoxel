"""AVCN64-v1: deterministic periodic RGBA8, x-fastest, headerless, linear data.

Requires NumPy. R=base Perlin-like value/Worley blend; G=fine erosion;
B=2D weather (constant through y); A=2D cloud-top variation (constant through y).
Never sRGB-decode these channels. Run offline, not during Unity startup.
"""
import argparse
import hashlib
import json
from pathlib import Path
import numpy as np

SIZE = 64
SEED = 0x41564331


def lattice(x, y, z, salt):
    # Deliberate uint32 modular arithmetic, independent of random generator APIs.
    with np.errstate(over='ignore'):
        h = (np.asarray(x, dtype=np.uint32) * np.uint32(0x8DA6B343)
             ^ np.asarray(y, dtype=np.uint32) * np.uint32(0xD8163841)
             ^ np.asarray(z, dtype=np.uint32) * np.uint32(0xCB1AB31F)
             ^ np.uint32(SEED ^ salt))
        h = (h ^ (h >> 16)) * np.uint32(0x7FEB352D)
        h = (h ^ (h >> 15)) * np.uint32(0x846CA68B)
        h ^= h >> 16
    return (h & np.uint32(65535)).astype(np.float32) / np.float32(65535)


def value_noise(cells, salt, two_d=False):
    # Output storage [z,y,x]; all lattice indices wrap, including edge neighbors.
    q = (np.arange(SIZE, dtype=np.float32) + np.float32(.5)) * np.float32(cells/SIZE)
    z, y, x = np.meshgrid(q, q if not two_d else q[:1]*0, q, indexing='ij')
    ix, iy, iz = [np.floor(a).astype(np.int32) for a in (x,y,z)]
    fx, fy, fz = [a-i for a,i in zip((x,y,z),(ix,iy,iz))]
    fx, fy, fz = [a*a*(3-2*a) for a in (fx,fy,fz)]
    out = np.zeros_like(x, dtype=np.float32)
    for dz in (0,1):
        for dy in (0,1):
            for dx in (0,1):
                w = (fx if dx else 1-fx)*(fy if dy else 1-fy)*(fz if dz else 1-fz)
                out += (lattice((ix+dx)%cells, (iy+dy)%cells,
                                (iz+dz)%cells, salt)*w).astype(np.float32)
    return out


def worley(cells, salt):
    q = (np.arange(SIZE, dtype=np.float32)+np.float32(.5))*np.float32(cells/SIZE)
    z,y,x = np.meshgrid(q,q,q,indexing='ij')
    ix,iy,iz = [np.floor(a).astype(np.int32) for a in (x,y,z)]
    fx,fy,fz = [a-i for a,i in zip((x,y,z),(ix,iy,iz))]
    nearest = np.full(x.shape, np.float32(4), dtype=np.float32)
    for dz in (-1,0,1):
        for dy in (-1,0,1):
            for dx in (-1,0,1):
                cx,cy,cz = (ix+dx)%cells,(iy+dy)%cells,(iz+dz)%cells
                px = lattice(cx,cy,cz,salt)
                py = lattice(cx,cy,cz,salt+1)
                pz = lattice(cx,cy,cz,salt+2)
                d2 = ((dx+px-fx)**2+(dy+py-fy)**2+(dz+pz-fz)**2)
                np.minimum(nearest,d2,out=nearest)
    return np.clip(1-np.sqrt(nearest),0,1).astype(np.float32)


def generate():
    fbm = (.60*value_noise(4,11)+.28*value_noise(8,23)+.12*value_noise(16,37))
    base = .65*fbm+.35*worley(4,101)
    erosion = .70*worley(8,211)+.30*value_noise(16,307)
    weather = .80*value_noise(4,401,True)+.20*value_noise(8,409,True)
    top = value_noise(2,503,True)
    out = np.empty((SIZE,SIZE,SIZE,4),dtype=np.uint8)
    for channel, data in enumerate((base,erosion,weather,top)):
        out[...,channel] = np.floor(np.clip(data,0,1)*255+.5).astype(np.uint8)
    return out.tobytes(order='C')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, default=Path(
        'Assets/AshenVoxel/Exploration/Resources/ExplorationCloudNoise.bytes'))
    args = parser.parse_args()
    payload = generate()
    assert len(payload) == SIZE**3*4
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_bytes(payload)
    identity = {'format':'AVCN64-v1','seed':SEED,'size':[64,64,64],
                'storage':'RGBA8_UNorm; x fastest, then y, then z; no header',
                'bytes':len(payload),'sha256':hashlib.sha256(payload).hexdigest(),
                'numpy':np.__version__,
                'channels':{'R':'base value/Worley blend','G':'erosion',
                            'B':'2D weather, invariant in y',
                            'A':'2D top variation, invariant in y'}}
    args.output.with_suffix('.identity.json').write_text(json.dumps(identity,indent=2)+'\n')
    print(json.dumps(identity,indent=2))


if __name__ == '__main__':
    main()
