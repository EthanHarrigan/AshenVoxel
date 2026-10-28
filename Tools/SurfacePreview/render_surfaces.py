#!/usr/bin/env python3
"""Render exact controlled10cm material fixtures, with explicit offline water study."""
from pathlib import Path
import argparse,hashlib,json,math,sys,time
import numpy as np
from PIL import Image,ImageDraw,ImageFont
ROOT=Path(__file__).resolve().parents[2];BUILD=ROOT/'Tools/SurfacePreview/bin';OUT=ROOT/'Docs/SurfaceGallery'
sys.path.insert(0,str(ROOT/'Tools/TreePathtrace'))
from render_pathtrace import load_export,exposed_geometry
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def font(n):
    try:return ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf',n)
    except OSError:return ImageFont.load_default()
def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('name');p.add_argument('--width',type=int,default=1100);p.add_argument('--height',type=int,default=900);p.add_argument('--spp',type=int,default=64);p.add_argument('--threads',type=int,default=2);a=p.parse_args()
    import mitsuba as mi
    import drjit as dr
    mi.set_variant('llvm_ad_rgb');dr.set_thread_count(a.threads);started=time.monotonic()
    meta,vol,colors,scale,input_sha,provenance=load_export(a.name,BUILD)
    vertices,rgb,faces,quads=exposed_geometry(vol,colors,scale)
    props=mi.Properties();props['bsdf']=mi.load_dict({'type':'diffuse','reflectance':{'type':'mesh_attribute','name':'vertex_color'}})
    mesh=mi.Mesh('actual10cm surface matter',len(vertices),len(faces),props)
    params=mi.traverse(mesh);params['vertex_positions']=vertices.ravel();params['faces']=faces.ravel();params.update();mesh.add_attribute('vertex_color',3,rgb.ravel())
    low=np.array(meta['bounds_min_m']);high=np.array(meta['bounds_max_m']);span=high-low
    center=(low+high)*.5;center[1]=high[1]*.43
    direction=np.array([1.1,.63,-1.4]);direction/=np.linalg.norm(direction)
    if a.name in ('forest','arid'):direction=np.array([.78,.38,-1.1]);direction/=np.linalg.norm(direction)
    distance=max(43,float(span[1])*1.8);origin=center+direction*distance;fov=39
    theta=(np.arange(128,dtype=np.float32)+.5)/128*np.pi;up=np.cos(theta)
    horizon=np.array([.48,.56,.68]);zenith=np.array([.22,.34,.52]);lower=np.array([.045,.055,.065])
    sky=np.where(up[:,None]>=0,horizon[None,:]*(1-up[:,None])+zenith[None,:]*up[:,None],horizon[None,:]*(1+up[:,None])-lower[None,:]*up[:,None])
    sky=np.repeat(sky[:,None,:],256,axis=1).astype(np.float32)
    T=mi.ScalarTransform4f;light=center+np.array([-22,32,-25])
    scene={'type':'scene','integrator':{'type':'path','max_depth':6,'rr_depth':3},
      'sensor':{'type':'perspective','fov':fov,'to_world':T.look_at(origin=origin.tolist(),target=center.tolist(),up=[0,1,0]),
        'sampler':{'type':'independent','sample_count':a.spp},'film':{'type':'hdrfilm','width':a.width,'height':a.height,'pixel_format':'rgb','rfilter':{'type':'tent'}}},
      'terrain':mesh,'sky':{'type':'envmap','bitmap':mi.Bitmap(sky)},
      'sun':{'type':'rectangle','to_world':T.look_at(origin=light.tolist(),target=center.tolist(),up=[0,0,1])@T.scale([6,6,1]),
        'emitter':{'type':'area','radiance':{'type':'rgb','value':[42,34,25]}}}}
    if meta['water_plane']:
        scene['illustrative_water']={'type':'rectangle','to_world':T.translate([span[0]*.5,meta['water_local_height_m'],span[2]*.5])@T.rotate([1,0,0],-90)@T.scale([span[0]*.5,span[2]*.5,1]),
          'bsdf':{'type':'dielectric','int_ior':1.333,'ext_ior':1.0,'specular_transmittance':{'type':'rgb','value':[.73,.88,.91]}}}
    print(a.name,'actual',len(vertices),'vertices',len(faces),'triangles; rendering...',flush=True)
    values=np.asarray(mi.render(mi.load_dict(scene),spp=a.spp,seed=42),dtype=np.float32)
    assert np.isfinite(values).all(),'Nonfinite offline radiance'
    values=1-np.exp(-np.maximum(values,0)*1.25);values=np.where(values<=.0031308,values*12.92,1.055*values**(1/2.4)-.055)
    pic=Image.fromarray(np.uint8(np.clip(values*255+.5,0,255)))
    titles={'alpine':'ALPINE / PATCHY SNOW + EXPOSED GRANITE','forest':'FOREST SOIL / GRASS → LITTER → DIRT → BEDROCK',
      'pale-shore':'PALE SHORE / SAND, GRAVEL + LIMESTONE','black-shore':'VOLCANIC SHORE / BLACK SAND + BASALT','arid':'ARID CUTAWAY / SAND + SANDSTONE'}
    board=Image.new('RGB',(a.width,a.height+110),(13,20,25));board.paste(pic,(0,64));draw=ImageDraw.Draw(board)
    draw.text((20,12),titles[a.name],font=font(21),fill=(232,235,225));draw.text((20,40),'Controlled field / actual shared sampler and10cm voxel matter / stored material colors',font=font(12),fill=(165,187,185))
    footer='CPU PATH TRACE / NOT UNITY / '+('WATER IS AN OFFLINE OPTICAL STUDY' if meta['water_plane'] else 'NO CONCEPT ART OR REPLACEMENT TERRAIN')
    draw.text((20,board.height-30),footer,font=font(13),fill=(215,181,118));output=OUT/(a.name+'.png');board.save(output)
    report={'image':str(output.relative_to(ROOT)),'image_sha256':sha(output),'input_sha256':input_sha,'source_provenance':provenance,
      'tool_sha256':sha(Path(__file__)),'spp':a.spp,'width':a.width,'height':a.height,'threads':a.threads,'render_seed':42,'renderer':'Mitsuba '+mi.__version__,
      'triangles':len(faces),'exposed_quads':quads,'camera_origin_m':origin.tolist(),'camera_target_m':center.tolist(),'fov':fov,
      'elapsed_seconds':time.monotonic()-started,'meta':meta,
      'note':'Exact generated10cm matter from a controlled synthetic field/profile. Offline CPU light, not Unity. Water is a separate illustrative dielectric plane, not the URP shader or fluid simulation.'}
    (OUT/(a.name+'.report.json')).write_text(json.dumps(report,indent=2)+'\n');print('Saved',output,'in',round(report['elapsed_seconds'],2),'s',flush=True)
if __name__=='__main__':main()
