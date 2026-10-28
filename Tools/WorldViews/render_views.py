#!/usr/bin/env python3
"""Offline lighting views of exported ACTUAL WorldExplorer/LOD mesh data.

No replacement terrain or concept-art generation. This does not execute Unity.
"""
from pathlib import Path
import argparse,hashlib,json,math,time
import numpy as np
from PIL import Image,ImageDraw,ImageFont
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Docs/WorldViews'
PALETTE=np.array([tuple(int(v[i:i+2],16) for i in (1,3,5)) for v in
    json.loads((OUT/'lod-palette.json').read_text())['rgb_hex']],dtype=np.float32)/255

def font(size):
    try:return ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf',size)
    except OSError:return ImageFont.load_default()
def srgb(x):return np.where(x<=.04045,x/12.92,((x+.055)/1.055)**2.4).astype(np.float32)
def load(name):
    meta=json.loads((OUT/(name+'.json')).read_text())
    path=ROOT/'Tools/WorldViews/bin'/(name+'.bin');raw=path.read_bytes()
    assert raw[:8]==b'AVWM0001'
    nv,nf=np.frombuffer(raw,dtype='<i4',count=2,offset=8)
    assert 0<nv<10000000 and 0<nf<20000000 and len(raw)==16+int(nv)*29+int(nf)*12
    off=16
    def take(dtype,count,shape):
        nonlocal off
        a=np.frombuffer(raw,dtype=dtype,count=int(count),offset=off).reshape(shape).copy();off+=a.nbytes;return a
    pos=take('<f4',nv*3,(-1,3));norm=take('<f4',nv*3,(-1,3));color=take('u1',nv*4,(-1,4))
    faces=take('<u4',nf*3,(-1,3));levels=take('u1',nv,(-1,))
    assert faces.max()<nv and np.isfinite(pos).all() and np.isfinite(norm).all() and np.all(color[:,3]==255)
    return meta,pos,norm,color,faces,levels,hashlib.sha256(raw).hexdigest()

def render(args):
    import mitsuba as mi
    import drjit as dr
    mi.set_variant('llvm_ad_rgb');dr.set_thread_count(args.threads)
    started=time.monotonic();meta,pos,norm,color,faces,levels,digest=load(args.name)
    rgb=srgb(PALETTE[np.minimum(levels,len(PALETTE)-1)]) if args.lod else srgb(color[:,:3].astype(np.float32)/255)
    props=mi.Properties();props['bsdf']=mi.load_dict({'type':'diffuse','reflectance':{'type':'mesh_attribute','name':'vertex_color'}})
    # Geometric face normals are stable for the path tracer even on the very
    # deep vertical skirts. Runtime smooth normals are exported/verified above,
    # but can be tangent to skirt faces; their radiometric correction is not
    # meaningful in this separate offline lighting model.
    mesh=mi.Mesh('actual WorldExplorer terrain mesh',len(pos),len(faces),props)
    p=mi.traverse(mesh);p['vertex_positions']=pos.ravel();p['faces']=faces.ravel();p.update()
    mesh.add_attribute('vertex_color',3,rgb.ravel())
    origin=np.array(meta.get('camera_origin_m',meta.get('camera_origin')),dtype=float)
    target=np.array(meta.get('camera_target_m',meta.get('camera_target')),dtype=float)
    fov=args.fov if args.fov is not None else float(meta.get('fov_degrees',62))
    theta=(np.arange(128,dtype=np.float32)+.5)/128*np.pi;up=np.cos(theta)
    horizon=np.array([.48,.58,.70]);zenith=np.array([.18,.29,.47]);lower=np.array([.065,.074,.08])
    sky=np.where(up[:,None]>=0,horizon[None,:]*(1-up[:,None])+zenith[None,:]*up[:,None],horizon[None,:]*(1+up[:,None])-lower[None,:]*up[:,None])
    environment=np.repeat(sky[:,None,:],256,axis=1).astype(np.float32)
    far=float(meta.get('far_distance_m',meta.get('far_distance',32000)))
    if args.name=='neural-study':far=max(far,10000)
    scene=mi.load_dict({'type':'scene',
      'integrator':{'type':'aov','aovs':'range:depth','lighting':{'type':'path','max_depth':3,'rr_depth':3}},
      'sensor':{'type':'perspective','fov':fov,'near_clip':.3,'far_clip':far,
        'to_world':mi.ScalarTransform4f.look_at(origin=origin.tolist(),target=target.tolist(),up=[0,1,0]),
        'sampler':{'type':'independent','sample_count':args.spp},
        'film':{'type':'hdrfilm','width':args.width,'height':args.height,'pixel_format':'rgb','rfilter':{'type':'tent'}}},
      'terrain':mesh,'sky':{'type':'envmap','bitmap':mi.Bitmap(environment)},
      'sun':{'type':'directional','direction':[.48,-.71,.32],
        'irradiance':{'type':'rgb','value':[2.9,2.6,2.15]}}})
    result=np.asarray(mi.render(scene,spp=args.spp,seed=42),dtype=np.float32)
    assert result.shape==(args.height,args.width,4),result.shape
    assert np.isfinite(result[:,:,:3]).all(),'Nonfinite radiance; inspect camera/edge geometry before delivering'
    linear=result[:,:,:3].copy();depth=result[:,:,3]
    # Illustrative aerial perspective only; no occupied geometry is changed.
    if not args.lod and args.name!='neural-study':
        fog=np.where(depth>0,1-np.exp(-np.maximum(depth,0)/52000),0)
        linear=linear*(1-fog[:,:,None])+np.array([.38,.49,.64])*fog[:,:,None]
    rgb=1-np.exp(-np.maximum(linear,0)*1.2)
    rgb=np.where(rgb<=.0031308,rgb*12.92,1.055*np.power(rgb,1/2.4)-.055)
    pic=Image.fromarray(np.uint8(np.clip(rgb*255+.5,0,255)))
    # Leave the render camera/composition intact; attach labels outside the image.
    board=Image.new('RGB',(args.width,args.height+116),(13,20,25));board.paste(pic,(0,68));draw=ImageDraw.Draw(board)
    titles={'analytic-observer':'MOUNTAINTOP VIEW / ANALYTIC FALLBACK',
      'analytic-high':'HIGH ALTITUDE / ANALYTIC FALLBACK','ceteviles-observer':'CETEVILES / IMPORTED MACRO TERRAIN',
      'neural-study':'CETEVILES / ACTUAL LEARNED TERRAIN REGION'}
    title=titles.get(args.name,args.name.upper())+(' / LOD COLORS' if args.lod else '')
    draw.text((22,12),title,font=font(22),fill=(231,235,225))
    draw.text((22,42),'Actual C# WorldExplorer mesh export / '+('color = planner level' if args.lod else 'runtime vertex colors, offline lighting'),font=font(13),fill=(163,183,183))
    footer='OFFLINE CPU RENDER / NOT A UNITY SCREENSHOT / NO TREES OR DECORATION ADDED'
    draw.text((22,board.height-32),footer,font=font(13),fill=(212,177,112))
    output=OUT/(args.name+('-lod' if args.lod else '-view')+'.png');board.save(output)
    report={'image':str(output.relative_to(ROOT)),'image_sha256':hashlib.sha256(output.read_bytes()).hexdigest(),
      'binary_sha256':digest,'metadata_sha256':hashlib.sha256((OUT/(args.name+'.json')).read_bytes()).hexdigest(),
      'tool_sha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
      'renderer':'Mitsuba '+mi.__version__,'variant':'llvm_ad_rgb','spp':args.spp,'render_seed':42,
      'camera_origin_m':origin.tolist(),'camera_target_m':target.tolist(),'fov':fov,'far_clip_m':far,
      'triangles':len(faces),'vertices':len(pos),'elapsed_seconds':time.monotonic()-started,
      'normal_model':'Geometric face normals for offline lighting; exported runtime smooth normals remain in source data.',
      'note':'Actual implementation mesh, CPU lighting and illustrative atmospheric display; NOT Unity or FPS.'}
    (OUT/(output.stem+'.report.json')).write_text(json.dumps(report,indent=2)+'\n')
    print('Saved',output,'in',round(report['elapsed_seconds'],2),'seconds',flush=True)

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('name');p.add_argument('--lod',action='store_true')
    p.add_argument('--width',type=int,default=1400);p.add_argument('--height',type=int,default=900)
    p.add_argument('--spp',type=int,default=64);p.add_argument('--threads',type=int,default=4);p.add_argument('--fov',type=float)
    render(p.parse_args())
if __name__=='__main__':main()
