#!/usr/bin/env python3
"""CPU Mitsuba path-tracing study of actual exported 10 cm voxel tree data.

This does not run Unity and does not describe current URP performance or appearance.
Input is the deterministic sparse export produced by Tools/TreePreview/render_trees.py.
Only empty-neighbor voxel faces are emitted; no concept art or replacement tree mesh.
"""
from pathlib import Path
import argparse, hashlib, json, math, time, os, sys
from datetime import datetime, timezone
import numpy as np
ROOT=Path(__file__).resolve().parents[2]


def srgb_to_linear(rgb):
    rgb=np.asarray(rgb,dtype=np.float32)
    return np.where(rgb<=0.04045,rgb/12.92,((rgb+0.055)/1.055)**2.4).astype(np.float32)


def load_export(name,input_dir=None):
    src=input_dir or ROOT/'Tools/TreePreview/bin'
    raw=(src/(name+'.bin')).read_bytes()
    meta=json.loads((src/(name+'.json')).read_text())
    dims=np.frombuffer(raw,dtype='<i4',count=3);scale=float(np.frombuffer(raw,dtype='<f4',count=1,offset=12)[0])
    count=int(np.frombuffer(raw,dtype='<i4',count=1,offset=16)[0])
    dtype=np.dtype([('x','<u2'),('y','<u2'),('z','<u2'),('id','u1'),('color','<u4')])
    if len(raw)!=20+count*dtype.itemsize:raise ValueError('Sparse export size mismatch')
    records=np.frombuffer(raw,dtype=dtype,count=count,offset=20)
    vol=np.zeros((int(dims[2]),int(dims[1]),int(dims[0])),dtype=np.uint8)
    colors=np.zeros(vol.shape,dtype=np.uint32)
    index=(records['z'],records['y'],records['x']);vol[index]=records['id'];colors[index]=records['color']
    provenance_path=src/'source-provenance.json'
    if not provenance_path.is_file():provenance_path=ROOT/'Docs/TreeGallery/provenance.json'
    provenance=json.loads(provenance_path.read_text())
    return meta,vol,colors,scale,hashlib.sha256(raw).hexdigest(),provenance


def exposed_geometry(vol,colors,scale):
    """Emit the exact exposed voxel surfaces, carrying effective stored RGB per face.

    This deliberately does not merge faces; it is an offline geometry export,
    independent of the Unity greedy mesher. Interior opaque interfaces are omitted.
    """
    nz,ny,nx=vol.shape;pad=np.pad(vol,1)
    positions=[];rgbs=[];faces=[];offset=0;face_count=0
    for axis in range(3):
        u,v=(axis+1)%3,(axis+2)%3
        for sign in (1,-1):
            d=[0,0,0];d[axis]=sign;dx,dy,dz=d
            neighbor=pad[1+dz:nz+1+dz,1+dy:ny+1+dy,1+dx:nx+1+dx]
            occupied=(vol!=0)&(neighbor==0)
            ids=np.argwhere(occupied);n=len(ids)
            if not n:continue
            base=ids[:,::-1].astype(np.float32)
            if sign>0:base[:,axis]+=1
            du=np.zeros(3,dtype=np.float32);dv=du.copy();du[u]=1;dv[v]=1
            verts=np.stack((base,base+du,base+du+dv,base+dv),axis=1)*scale
            positions.append(verts.reshape(-1,3))
            packed=colors[occupied]
            rgb=np.stack(((packed>>16)&255,(packed>>8)&255,packed&255),axis=1).astype(np.float32)/255
            rgbs.append(np.repeat(srgb_to_linear(rgb),4,axis=0))
            indices=np.array([0,1,2,0,2,3] if sign>0 else [0,2,1,0,3,2],dtype=np.uint32)
            faces.append((indices[None,:]+np.arange(n,dtype=np.uint32)[:,None]*4+offset).reshape(-1,3))
            offset+=n*4;face_count+=n
    return np.concatenate(positions),np.concatenate(rgbs),np.concatenate(faces),face_count


def make_scene(mi,vertices,colors,faces,meta,width,height,spp,depth,view):
    bsdf=mi.load_dict({'type':'diffuse','reflectance':{'type':'mesh_attribute','name':'vertex_color'}})
    props=mi.Properties();props['bsdf']=bsdf
    mesh=mi.Mesh('actual generated voxel tree and ground',len(vertices),len(faces),props)
    params=mi.traverse(mesh);params['vertex_positions']=vertices.ravel();params['faces']=faces.ravel();params.update()
    mesh.add_attribute('vertex_color',3,colors.ravel())
    low=np.array(meta['bounds_min_m']);high=np.array(meta['bounds_max_m'])
    center=(low+high)*.5;size=high-low
    # Fit the physical specimen into the requested aspect ratio. A fixed
    # distance multiplier leaves very tall narrow trees unnecessarily tiny.
    horizontal_half=math.tan(math.radians(39)/2)
    vertical_half=horizontal_half*height/width
    distance=max(size[1]*.5/vertical_half,max(size[0],size[2])*.5/horizontal_half)*1.18+max(size[0],size[2])*.3
    direction=np.array([1.2,.20,-1.7]);direction/=np.linalg.norm(direction)
    if view=='base':
        # Frame actual root buttresses and the first several metres of trunk.
        # Use genome dimensions rather than the (much wider) canopy bounds.
        parameters=meta['parameters'];radius=float(parameters['TrunkRadius'])
        flare=float(parameters.get('RootFlare',0));relief=float(parameters.get('BarkRelief',0))
        halfspan=max(2.2,radius+flare+relief)*1.45
        distance=halfspan/math.tan(math.radians(46)/2)
        target=np.array([meta['center_m'][0],low[1]+halfspan*.55,meta['center_m'][2]])
        heading=np.array([1.2,0,-1.7]);heading/=np.linalg.norm(heading)
        origin=target+heading*distance;origin[1]=low[1]+max(1.8,halfspan*.35);fov=46
    elif view=='detail':
        target=np.array([meta['center_m'][0],low[1]+size[1]*.35,meta['center_m'][2]])
        distance=max(size[0],size[2])*.95
        origin=target+np.array([distance*.47,1,-distance*.88]);fov=38
    else:
        target=center.copy();target[1]=low[1]+size[1]*.47
        origin=target+direction*distance;fov=39
    T=mi.ScalarTransform4f
    # A broad warm source and a dim blue sky provide soft shadows and real
    # multibounce transport. Their geometry is a lighting fixture, not game data.
    # Keep illumination comparable across a 10 m birch and a 75 m redwood.
    # Moving/scaling the same area source together preserves angular size and
    # prevents its inverse-square falloff from artificially blackening the base.
    light_distance=max(34.0,float(size[1])*3.5)
    light_direction=np.array([-17.,24.,-14.]);light_direction/=np.linalg.norm(light_direction)
    sun=center+light_direction*light_distance;light_halfsize=light_distance*(5/math.sqrt(17**2+24**2+14**2))
    # Procedural environment only supplies sky illumination/background; tree and
    # terrain geometry remain the exact exported voxels.
    theta=(np.arange(128,dtype=np.float32)+.5)/128*np.pi
    up=np.cos(theta)
    horizon=np.array([.45,.52,.68],dtype=np.float32)
    zenith=np.array([.25,.35,.55],dtype=np.float32)
    lower=np.array([.06,.055,.04],dtype=np.float32)
    sky_rows=np.where(up[:,None]>=0,horizon[None,:]*(1-up[:,None])+zenith[None,:]*up[:,None],horizon[None,:]*(1+up[:,None])-lower[None,:]*up[:,None])
    environment=np.repeat(sky_rows[:,None,:],256,axis=1).astype(np.float32)
    scene={'type':'scene',
      'integrator':{'type':'path','max_depth':depth,'rr_depth':3},
      'sensor':{'type':'perspective','fov':fov,'to_world':T.look_at(origin=origin.tolist(),target=target.tolist(),up=[0,1,0]),
        'sampler':{'type':'independent','sample_count':spp},
        'film':{'type':'hdrfilm','width':width,'height':height,'pixel_format':'rgb','rfilter':{'type':'tent'}}},
      'world':mesh,
      'sky':{'type':'envmap','bitmap':mi.Bitmap(environment)},
      'soft_sun':{'type':'rectangle','to_world':T.look_at(origin=sun.tolist(),target=center.tolist(),up=[0,0,1])@T.scale([light_halfsize,light_halfsize,1]),
        'emitter':{'type':'area','radiance':{'type':'rgb','value':[42,34,24]}}}}
    return mi.load_dict(scene),{'view':view,'camera_origin_m':origin.tolist(),'camera_target_m':target.tolist(),'fov_degrees':fov,'sun_position_m':sun.tolist(),'sun_halfsize_m':light_halfsize,'sun_linear_radiance':[42,34,24],'sky':'procedural blue zenith / lighter horizon / dark lower hemisphere'}


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--input-dir',type=Path,help='Explicit frozen sparse-data directory; default is latest TreePreview/bin export')
    p.add_argument('--name',default='oak-fruit-0');p.add_argument('--width',type=int,default=800);p.add_argument('--height',type=int,default=900)
    p.add_argument('--spp',type=int,default=64);p.add_argument('--depth',type=int,default=5);p.add_argument('--view',choices=('full','detail','base'),default='full')
    p.add_argument('--threads',type=int,default=8);p.add_argument('--exposure',type=float,default=1.3);p.add_argument('--output',type=Path)
    a=p.parse_args();import mitsuba as mi;import drjit as dr
    mi.set_variant('llvm_ad_rgb');dr.set_thread_count(a.threads)
    print(f'Mitsuba {mi.__version__}, CPU llvm_ad_rgb, {a.threads} threads',flush=True)
    started=time.monotonic();meta,vol,color,scale,sha,source_provenance=load_export(a.name,a.input_dir)
    vertices,colors,faces,quads=exposed_geometry(vol,color,scale)
    print(f'Actual data {a.name}: {np.count_nonzero(vol):,} cells; {quads:,} exposed quads; {len(faces):,} triangles',flush=True)
    scene,camera=make_scene(mi,vertices,colors,faces,meta,a.width,a.height,a.spp,a.depth,a.view)
    print('Scene loaded; rendering...',flush=True)
    linear=mi.render(scene,spp=a.spp,seed=42)
    rgb=np.asarray(linear,dtype=np.float32)
    # An explicit photographic display transform, not Unity's tone mapper.
    rgb=1-np.exp(-np.maximum(rgb,0)*a.exposure)
    rgb=np.where(rgb<=.0031308,rgb*12.92,1.055*np.power(rgb,1/2.4)-.055)
    from PIL import Image,ImageDraw,ImageFont
    image=Image.fromarray(np.clip(rgb*255+.5,0,255).astype(np.uint8),'RGB')
    draw=ImageDraw.Draw(image)
    caption=f'{meta["species"].upper()} / '+('BASE DETAIL / ' if a.view=='base' else '')+'ACTUAL 10 CM VOXELS / CPU PATH TRACE / NOT UNITY'
    draw.rectangle((0,a.height-36,a.width,a.height),fill=(12,16,20));draw.text((14,a.height-24),caption,fill=(195,203,202))
    out=(a.output or ROOT/'Docs/TreeGallery'/f'pathtraced-{a.name}-{a.view}.png').resolve();out.parent.mkdir(parents=True,exist_ok=True);image.save(out)
    elapsed=time.monotonic()-started
    report={'created_utc':datetime.now(timezone.utc).isoformat(),'image':str(out.relative_to(ROOT)),'note':'Actual generated voxel data, CPU Mitsuba path-tracing lighting study; NOT Unity output or performance. No synthetic tree geometry. Ground is actual exported voxels; only camera, sky and area-light fixtures are added.',
      'renderer':'Mitsuba '+mi.__version__,'drjit_version':dr.__version__,'numpy_version':np.__version__,'python_version':sys.version,'image_sha256':hashlib.sha256(out.read_bytes()).hexdigest(),'variant':'llvm_ad_rgb','width':a.width,'height':a.height,'spp':a.spp,'max_path_depth':a.depth,'render_seed':42,'threads':a.threads,'exposure':a.exposure,'elapsed_seconds':round(elapsed,3),
      'export_name':a.name,'export_sha256':sha,'voxel_m':scale,'exposed_quads':quads,'triangles':int(len(faces)),'source_metadata':meta,'source_provenance':source_provenance,'camera':camera,
      'tool_sha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest()}
    # Keep reports alongside the isolated tool; gallery ownership is PNG only.
    report_dir=Path(__file__).resolve().parent/'reports';report_dir.mkdir(exist_ok=True)
    (report_dir/(out.stem+'.json')).write_text(json.dumps(report,indent=2))
    print(f'Saved {out} in {elapsed:.1f}s',flush=True)

if __name__=='__main__':main()
