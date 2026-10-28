"""CPU data/math checks only; not shader execution or visual acceptance."""
import hashlib
import math
from pathlib import Path
import numpy as np
from generate_cloud_noise import generate

ROOT=Path(__file__).resolve().parents[1]
ASSET=ROOT/'Assets/AshenVoxel/Exploration/Resources/ExplorationCloudNoise.bytes'
EXPECTED='da810b3ea1837fe254b7326fd36ef4d9a4042f76d90c8c24f3294fce5319f92b'
raw=ASSET.read_bytes()
assert len(raw)==64**3*4
assert hashlib.sha256(raw).hexdigest()==EXPECTED
assert generate()==raw, 'Generator must reproduce exact asset bytes'
data=np.frombuffer(raw,dtype=np.uint8).reshape(64,64,64,4).astype(np.float64)/255
assert np.array_equal(data[...,2],np.broadcast_to(data[:,0:1,:,2],(64,64,64)))
assert np.array_equal(data[...,3],np.broadcast_to(data[:,0:1,:,3],(64,64,64)))

def smooth(a,b,x):
    t=np.clip((x-a)/(b-a),0,1)
    return t*t*(3-2*t)

def sample(uv):
    # GPU repeat/trilinear convention, mip zero only; integer voxel centres.
    p=np.asarray(uv)*64-.5
    i=np.floor(p).astype(np.int64)
    f=p-i
    out=np.zeros(4)
    for z in (0,1):
        for y in (0,1):
            for x in (0,1):
                q=(i+np.array([x,y,z]))%64
                weight=np.prod(np.where([x,y,z],f,1-f))
                out+=data[q[2],q[1],q[0]]*weight
    return out

def density(uv,h,coverage=.58,amount=.8,erosion=.22):
    if h<=0 or h>=1:
        return 0.
    n=sample(uv)
    weather=smooth(1-coverage-.18,1-coverage+.18,n[2])*smooth(0,.025,coverage)
    cap=np.clip((n[3]-.28)*2.2,0,1);lobe=np.clip((n[0]-.22)*1.8,0,1)
    top=np.clip(.38+.30*cap+.30*lobe+.32*(n[1]-.5),.38,.98)
    bottom=.025+.18*smooth(.15,.85,n[1])
    core=smooth(bottom,bottom+.08,h)*(1-smooth(top-.16,top,h))
    fringe=smooth(max(0,bottom-.06),bottom+.04,h)*(1-smooth(top-.04,min(1,top+.12),h))
    profile=core*.82+fringe*.18
    field=n[0]-(.12+(1-weather)*.58);margin=.08
    x=(0 if field<=-margin else field if field>=margin else (field+margin)**2/(4*margin))*2.2
    body=1.2*x/(.4+x)
    coarse=float(body*profile*math.sqrt(weather)*amount)
    detail=sample(np.asarray(uv)*4+[.17,.31,.53])
    fine_erosion=.65*detail[1]+.35*detail[0]
    warp=np.clip(((detail[0]-.45)*.45+(detail[1]-.40)*.30)*min(erosion/.22,1),-.12,.12)
    wh=h+warp
    fine_core=smooth(bottom,bottom+.08,wh)*(1-smooth(top-.16,top,wh))
    fine_fringe=smooth(max(0,bottom-.06),bottom+.04,wh)*(1-smooth(top-.04,min(1,top+.12),wh))
    coarse=body*math.sqrt(weather)*amount*(fine_core*.82+fine_fringe*.18)*smooth(0,.03,h)*(1-smooth(.96,1,h))
    edge=1-np.clip(coarse/max(amount,1e-5),0,1)
    retention=2**(-erosion*10*(1-fine_erosion)*edge*edge)
    return np.clip(coarse*(.90+.20*detail[0])*retention,0,amount)

def interval(height,dy,thickness=700,maximum=30000):
    if abs(dy)<1e-6:
        return (0,maximum) if 0<=height<=thickness else None
    a,b=-height/dy,(thickness-height)/dy
    entry,exit=max(0,min(a,b)),min(maximum,max(a,b))
    return (entry,exit) if exit>entry else None

rng=np.random.default_rng(19)
for _ in range(200):
    p=rng.uniform(-10,10,3)
    for axis in range(3):
        q=p.copy();q[axis]+=1
        assert np.allclose(sample(p),sample(q),rtol=0,atol=2e-13)
assert interval(-1200,0) is None
assert interval(350,0)==(0,30000)
assert interval(-1200,1)==(1200,1900)
assert interval(2000,-1)==(1300,2000)
assert interval(-1200,-1) is None
for y in (-1e6,-1200,0,350,700,1e6):
    for dy in (-1,-1e-7,0,1e-7,1):
        hit=interval(y,dy)
        assert hit is None or all(math.isfinite(v) and 0<=v<=30000 for v in hit)

for _ in range(200):
    uv=rng.uniform(-1,1,3)
    h=float(rng.uniform(.01,.99))
    assert density(uv,h,0)==0
    assert density(uv,h,.58,0)==0
    previous=-1
    for c in np.linspace(0,1,21):
        value=density(uv,h,float(c))
        assert 0<=value<=.8 and value>=previous-1e-12
        previous=value

# Compositing is exact for constant extinction regardless of chosen step count.
for count in (24,32,40,64):
    tau=.008*700
    t=1.
    radiance=0.
    for _ in range(count):
        alpha=1-math.exp(-tau/count)
        radiance+=t*alpha*1.3
        t*=1-alpha
    assert abs(t-math.exp(-tau))<1e-14
    assert abs(radiance-1.3*(1-t))<1e-14

# Sample vertical density columns. No mip filtering, lighting, or rendered image.
opacities=[]
for phase_x in np.linspace(0,1,12,endpoint=False):
    for phase_z in np.linspace(0,1,12,endpoint=False):
        tau=0.
        for k in range(32):
            height=(k+.5)*700/32
            uv=[phase_x,height/6400*3,phase_z]
            tau+=density(uv,height/700)*.018*700/32
        opacities.append(1-math.exp(-tau))
assert max(opacities)>.99
print('PASS AVCN64-v1 identity, exact regeneration, 1 MiB payload, x-fastest layout')
print('PASS weather/top y-invariance; 600 repeat/trilinear periodic comparisons')
print('PASS horizontal/up/down/extreme layer intersections; finite bounded intervals')
print('PASS zero coverage/density and 4200 sampled monotonic density cases')
print('PASS Beer-Lambert/compositing at 24/32/40/64 steps')
print('144 vertical columns, coverage=.58 density=.8: opacity quantiles',
      np.round(np.quantile(opacities,[0,.25,.5,.75,1]),4).tolist())
print('PASS at least one dense column exceeds .99 opacity; not an appearance test')
print('NOT RUN Unity/HLSL compile, GPU texture upload, mip generation, native captures/timings')
