"""CPU scalar checks, not Unity/HLSL compile, visual or performance evidence."""
import math
import random
from pathlib import Path
import numpy as np


def clamp(x, lo, hi):
    return max(lo, min(x, hi))


def fog(camera, target, density, falloff, base, origin, start):
    # Scalar reference matching the supplied HLSL's calculation order.
    ray = [b-a for a,b in zip(camera,target)]
    distance = math.sqrt(sum(x*x for x in ray))
    start, density = max(start,0), max(density,0)
    if distance <= start or distance < 1e-5 or density <= 0:
        return 0
    segment = distance-start
    fraction = start/distance
    falloff = max(falloff,0)
    height = (origin-base)+camera[1]+ray[1]*fraction
    a = clamp(-falloff*height,-1e20,1e20)
    delta = clamp(-falloff*ray[1]*(1-fraction),-1e20,1e20)
    b = clamp(a+delta,-1e20,1e20)
    d = abs(b-a)
    correction = (-.5*d+d*d/24 if d < .01 else
                  math.log(max(1-math.exp(-min(d,80)),1e-30))-math.log(d))
    log_tau = math.log(density)+math.log(segment)+max(a,b)+correction
    return math.exp(clamp(log_tau,-80,4.382026635))


def reference_quadrature(camera, target, density, falloff, base, origin, start):
    ray = [b-a for a,b in zip(camera,target)]
    distance = math.sqrt(sum(x*x for x in ray))
    if distance <= start:
        return 0
    n = 2048  # Simpson quadrature, independent from analytical expression.
    t0 = start/distance
    step = (1-t0)/n
    def f(t):
        h = origin+camera[1]+ray[1]*t-base
        return density*math.exp(-falloff*h)
    total = f(t0)+f(1)
    total += sum((4 if i%2 else 2)*f(t0+i*step) for i in range(1,n))
    return min(80,total*step*distance/3)


def noise(p):
    def fract(x): return x-math.floor(x)
    def hash3(v):
        v = [fract((x-math.floor(x/64)*64)*.1031) for x in v]
        d = sum(a*(b+33.33) for a,b in zip(v,[v[1],v[2],v[0]]))
        v = [x+d for x in v]
        return fract((v[0]+v[1])*v[2])
    i = [math.floor(x) for x in p]
    f = [fract(x) for x in p]
    f = [x*x*(3-2*x) for x in f]
    return sum(hash3([i[k]+c[k] for k in range(3)]) *
               math.prod(f[k] if c[k] else 1-f[k] for k in range(3))
               for c in [(x,y,z) for x in [0,1] for y in [0,1] for z in [0,1]])


rng = random.Random(20261028)
worst = 0
for _ in range(150):
    camera = (0,rng.uniform(-1000,5000),0)
    target = (rng.uniform(101,20000),rng.uniform(-1000,5000),0)
    density = rng.uniform(1e-6,1e-4)
    falloff = rng.uniform(0,.002)
    start = rng.uniform(0,100)
    a = fog(camera,target,density,falloff,0,0,start)
    b = reference_quadrature(camera,target,density,falloff,0,0,start)
    error = abs(a-b)/max(b,1e-12)
    worst = max(worst,error)
    assert error < 2e-8, (a,b,error)

# Equal-height limit, zero falloff, no fog, near start and origin rebasing.
assert abs(fog((0,500,0),(1000,500,0),.00004,.001,0,0,100)
           -.00004*900*math.exp(-.5)) < 1e-12
assert abs(fog((0,500,0),(1000,900,0),.00004,0,0,0,100)
           -.00004*(math.hypot(1000,400)-100)) < 1e-12
assert fog((0,0,0),(1000,0,0),0,.001,0,0,100) == 0
assert fog((0,0,0),(10,0,0),1,.001,0,0,100) == 0
assert fog((0,0,0),(0,0,0),1,.001,0,0,0) == 0
for y in [-1e9,-1e6,-100,0,100,1e6,1e9]:
    for dy in [-1e9,-1e-6,0,1e-6,1e9]:
        value = fog((0,y,0),(1e7,y+dy,0),.00004,.001,0,0,100)
        assert math.isfinite(value) and 0 <= value <= 80.000001
for _ in range(200):
    cam = (0,rng.uniform(-2000,5000),0)
    target = (5000,rng.uniform(-2000,5000),0)
    offset = 100000
    a = fog(cam,target,.00004,.001,350,0,100)
    b = fog((0,cam[1]-offset,0),(5000,target[1]-offset,0),.00004,.001,350,offset,100)
    assert abs(a-b) < 1e-10

# Float32 arithmetic stress; still a CPU reference, not GPU/HLSL execution.
def fog32(camera, target, density, falloff, base, origin, start):
    F = np.float32
    ray = np.array(target,dtype=F)-np.array(camera,dtype=F)
    distance = F(np.sqrt(np.dot(ray,ray)))
    if distance <= start or distance < 1e-5 or density <= 0:
        return F(0)
    fraction = F(F(start)/distance)
    height = F(F(F(origin)-F(base))+F(camera[1])+F(ray[1]*fraction))
    a = F(np.clip(F(-F(falloff)*height),-1e20,1e20))
    delta = F(np.clip(F(-F(falloff)*ray[1]*F(1-fraction)),-1e20,1e20))
    b = F(np.clip(F(a+delta),-1e20,1e20))
    d = F(abs(F(b-a)))
    if d < .01:
        correction = F(F(-.5)*d+F(d*d/F(24)))
    else:
        correction = F(np.log(F(max(F(1)-np.exp(-F(min(d,80))),F(1e-30))))-np.log(d))
    logtau = F(np.log(F(density))+np.log(F(distance-F(start)))+max(a,b)+correction)
    return F(np.exp(F(np.clip(logtau,-80,4.382026635))))

opacity_error = 0
with np.errstate(over='raise',invalid='raise',divide='raise'):
    for _ in range(5000):
        cam = (0,rng.uniform(-100000,100000),0)
        target = (rng.uniform(100,1e7),rng.uniform(-100000,100000),0)
        a = fog(cam,target,.00004,.001,0,0,100)
        b = float(fog32(cam,target,.00004,.001,0,0,100))
        assert math.isfinite(b)
        error = abs(math.exp(-a)-math.exp(-b))
        opacity_error = max(opacity_error,error)
        assert error < 2e-5, (a,b,error)

shell_error = 0
for _ in range(100000):
    y = rng.uniform(-1,1)
    angle = rng.uniform(-math.pi,math.pi)
    r = math.sqrt(1-y*y)
    t = .0609/(math.sqrt(y*y+.0609)+y)
    p = (t*r*math.cos(angle),1+t*y,t*r*math.sin(angle))
    error = abs(math.sqrt(sum(x*x for x in p))-1.03)
    shell_error = max(shell_error,error)
    assert error < 1e-12 and all(math.isfinite(x) for x in p)
for _ in range(100):
    p = [rng.uniform(-100,100) for _ in range(3)]
    for axis in range(3):
        q = p.copy()
        q[axis] += 64
        assert abs(noise(p)-noise(q)) < 1e-10

# Default tint normalization in both working spaces, without decoding uniforms.
def srgb_to_linear(x):
    return x/12.92 if x <= .04045 else ((x+.055)/1.055)**2.4

default_srgb = np.array([.42,.53,.65],dtype=np.float32)
default_linear = np.array([srgb_to_linear(float(x)) for x in default_srgb],dtype=np.float32)
linear_reference = np.array([.147318999244,.242866813904,.380056336450],dtype=np.float32)
base_zenith = np.array([.13,.32,.63],dtype=np.float32)
for uniform, reference in [(default_srgb,default_srgb),(default_linear,linear_reference)]:
    ratio = uniform/reference
    zenith = base_zenith*(np.float32(.35)+np.float32(.65)*ratio)
    assert np.allclose(ratio,1,rtol=5e-7,atol=0), ratio
    assert np.allclose(zenith,base_zenith,rtol=5e-7,atol=0), zenith

root = Path(__file__).resolve().parents[1]
shader = (root/'Assets/AshenVoxel/Exploration/Resources/ExplorationSky.shader').read_text()
include = (root/'Assets/AshenVoxel/Rendering/AshenAtmosphere.hlsl').read_text()
assert 'Shader "AshenVoxel/ExplorationSky"' in shader
for name in ['_SkyTint','_GroundColor','_SunDirection','_Exposure','_HorizonColor',
             '_SunColor','_SunAngularDiameter','_SunRadiance','_Haze','_CloudCoverage',
             '_CloudDensity','_CloudScale','_CloudWind','_CloudSpeed','_CloudQuality',
             '_CloudLightColor','_CloudShadowColor']:
    assert name in shader
assert 'return MixFog(color, legacyFogFactor);' in include
assert 'half3 AshenApplyFog(half3 color, float legacyFogFactor, float3 positionWS)' in include
assert 'max(_SkyTint.rgb,0)/defaultSkyTint' in shader
assert 'UNITY_COLORSPACE_GAMMA' in shader
assert '.147318999244,.242866813904,.380056336450' in shader

# Integration boundaries: each production surface replaces its legacy fog once,
# and runtime shader controls actually exist as material properties.
import re
surface_paths = [
    'Assets/AshenVoxel/Rendering/WorldSurface.shader',
    'Assets/AshenVoxel/WorldScaleRuntime/GroundSurface.shader',
    'Assets/AshenVoxel/WorldScaleRuntime/VisualWater.shader',
    'Assets/AshenVoxel/Exploration/ForestImpostor.shader',
    'Assets/AshenVoxel/Exploration/Resources/ForestHorizonGpu.shader',
]
for path in surface_paths:
    text = (root/path).read_text(encoding='utf-8')
    assert text.count('AshenApplyFog(') == 1, path
    assert not re.search(r'\bMixFog\(', text), path
    assert 'AshenAtmosphere.hlsl' in text, path
runtime = (root/'Assets/AshenVoxel/Exploration/ExplorationEnvironmentRuntime.cs').read_text(encoding='utf-8')
properties = shader.split('SubShader', 1)[0]
for name in set(re.findall(r'sky\.Set(?:Float|Vector|Color)\("([^"]+)"', runtime)):
    assert re.search(re.escape(name)+r'\s*\(', properties), name
assert 'Material.SetVector' in (root/'Docs/AtmosphereEvidence/cloud-research.md').read_text(encoding='utf-8')
print('PASS: all 5 surface fog integrations and runtime sky property bindings')
print(f'PASS: 150 fog integrals vs independent Simpson quadrature; worst relative error {worst:.3g}')
print('PASS: equal-height, zero falloff/density/distance, start exclusion, 35 extreme altitude rays, 200 origin shifts')
print(f'PASS: 100000 shell directions; max radius error {shell_error:.3g}; 300 noise period checks')
print('PASS: shader name, requested property presence and legacy fallback contract')
print(f'PASS: 5000 float32 fog stress rays; worst absolute transmission error {opacity_error:.3g}')
print('PASS: float32 default tint preserves baseline zenith in linear and gamma working spaces')
print('NOT RUN: Unity/HLSL compile, scene validation, native appearance or performance')
