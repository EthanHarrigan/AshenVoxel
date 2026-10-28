"""Exact rational counterexample; does not execute or alter a Unity shader."""
from pathlib import Path
from fractions import Fraction as F
import hashlib,json

HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[1]
paths=[ROOT/'Assets/AshenVoxel/Core/VertexAppearance.cs',ROOT/'Assets/AshenVoxel/Rendering/MaterialOptics.hlsl',ROOT/'Assets/AshenVoxel/Rendering/WorldSurface.shader']
hashes={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in paths}
assert '((int)material<<2)|occlusion' in paths[0].read_text()
assert '(64.0+(bits&3u)*(191.0/3.0))/255.0' in paths[1].read_text()
assert 'output.color.a=optics.z' in paths[2].read_text()
assert 'nointerpolation uint material' in paths[2].read_text()
checks=4
def check(ok):
    global checks
    assert ok
    checks+=1
def interpolate(a,b,t):return tuple(a[i]+t*(b[i]-a[i]) for i in range(3))
def clip(poly,upper):
    out=[]
    for a,b in zip(poly,poly[1:]+poly[:1]):
        ain=a[1]>=30 if upper else a[1]<=30
        bin=b[1]>=30 if upper else b[1]<=30
        if ain:out.append(a)
        if ain!=bin:out.append(interpolate(a,b,(F(30)-a[1])/(b[1]-a[1])))
    return out
def triangles(poly):return [(poly[0],poly[i],poly[i+1]) for i in range(1,len(poly)-1)]
def area(tri):
    a,b,c=tri
    return ((b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0]))/2
def sample(tri,x,y):
    a,b,c=tri
    den=(b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])
    u=((x-a[0])*(c[1]-a[1])-(y-a[1])*(c[0]-a[0]))/den
    v=((b[0]-a[0])*(y-a[1])-(b[1]-a[1])*(x-a[0]))/den
    return a[2]+u*(b[2]-a[2])+v*(c[2]-a[2])

# Original nonuniform-AO square on a real coarse-cell Y grid. Material and RGB
# are constant. The diagonal is retained when clipping each original triangle.
v=[tuple(map(F,p)) for p in [(0,-4,0),(64,-4,0),(64,60,3),(0,60,0)]]
source=[(v[0],v[1],v[2]),(v[0],v[2],v[3])]
upper=[];lower=[];intersections=set()
for original in source:
    for is_upper,output in [(True,upper),(False,lower)]:
        poly=clip(list(original),is_upper)
        intersections.update(p for p in poly if p[1]==30)
        for tri in triangles(poly):
            check(area(tri)>0)
            x=sum(p[0] for p in tri)/3;y=sum(p[1] for p in tri)/3
            check(sample(original,x,y)==sum(p[2] for p in tri)/3)
            output.append(tri)
check(len(upper)==3 and len(lower)==3 and len(intersections)==3)
check(sum(map(area,upper+lower))==sum(map(area,source)))
check(sum(map(area,upper))==F(64*30))
check(sum(map(area,lower))==F(64*34))
t=F(17,32);raw=3*t;rounded=round(raw);material=6
check(raw==F(51,32) and raw.denominator!=1 and rounded==2)
packed=(material<<2)|rounded
check(packed>>2==material and packed&3==2)
shader=lambda q:(64+q*F(191,3))/255
exact=shader(raw);quantized=shader(F(packed&3));error=quantized-exact
check(error==F(2483,24480) and error>0)
check(raw in [p[2] for p in intersections])
check(hashes=={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in paths})
result={'status':'PASS','assertions':checks,'scope':'Synthetic rational triangle clipping and CPU model of inspected shader formula, not HLSL/native rendering verification.',
 'source_sha256':hashes,'y_edge':[-4,60],'cut_cells':30,'edge_fraction':str(t),'material_id':material,'material_unchanged':True,
 'required_raw_ao':str(raw),'rounded_packed_ao':rounded,'exact_shader_ao':str(exact),'rounded_shader_ao':str(quantized),'shader_ao_error':str(error),'shader_ao_error_float':float(error),
 'original_triangles':len(source),'clipped_upper_triangles':len(upper),'clipped_lower_triangles':len(lower),'unique_new_intersection_vertices':len(intersections),
 'extra_index_bytes_32':(len(upper)+len(lower)-len(source))*3*4,'extra_vertex_bytes_original_stride_28':len(intersections)*28,
 'cost_limit':'132 extra bytes for this sharing model before an AO stream; adding float AO costs4bytes per vertex if tightly packed, actual stream layout/alignment and meshwide allocation unmeasured.'}
(HERE/'clipping-counterexample.json').write_text(json.dumps(result,indent=2),encoding='utf8')
print(f'PASS {checks} rational clipping assertions; rawAO={raw}, packed={rounded}, material={material} unchanged, shaderAOerror={error}.')
