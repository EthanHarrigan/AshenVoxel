#!/usr/bin/env python3
"""Render exact occupied10cm plant/rock cells with offline CPU lighting."""
from pathlib import Path
import argparse,datetime,hashlib,json,math,sys,time
import numpy as np
from PIL import Image,ImageDraw,ImageFont
ROOT=Path(__file__).resolve().parents[2];BUILD=ROOT/'Tools/GroundCoverPreview/bin';OUT=ROOT/'Docs/GroundCoverGallery'
sys.path.insert(0,str(ROOT/'Tools/TreePathtrace'))
from render_pathtrace import load_export,exposed_geometry
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def font(n):
 try:return ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf',n)
 except OSError:return ImageFont.load_default()
def render(mi,dr,name,args):
 started=time.monotonic();meta,vol,colors,scale,input_sha,provenance=load_export(name,args.input_dir)
 vertices,rgb,faces,quads=exposed_geometry(vol,colors,scale)
 props=mi.Properties();props['bsdf']=mi.load_dict({'type':'diffuse','reflectance':{'type':'mesh_attribute','name':'vertex_color'}})
 mesh=mi.Mesh('exact occupied10cm plant and rock faces',len(vertices),len(faces),props)
 params=mi.traverse(mesh);params['vertex_positions']=vertices.ravel();params['faces']=faces.ravel();params.update();mesh.add_attribute('vertex_color',3,rgb.ravel())
 low=np.array(meta['bounds_min_m']);high=np.array(meta['bounds_max_m']);size=high-low;center=(low+high)*.5;center[1]=low[1]+size[1]*.43
 arranged=meta['scene']['category']=='arrangement';width,height=(1100,800) if arranged else (1050,430)
 if args.draft:width,height=int(width*.65),int(height*.65)
 is_rock=meta['scene']['category'].startswith('rock')
 direction=np.array([1.0,.9,-1.4] if arranged else [.26,1.15,-1.25] if is_rock else [.15,.95,-1.35] if name=='plant-fern' else [.15,.63,-1.65]);direction/=np.linalg.norm(direction)
 fov=39;tan=math.tan(math.radians(fov*.5));distance=max(size[0]*.5/tan,size[1]*.5/(tan*height/width))*1.16+size[2]*.4
 if arranged:distance=max(size[0],size[2])*.5/tan*1.42
 origin=center+direction*distance
 T=mi.ScalarTransform4f;light_distance=max(8,float(max(size))*2.4);ld=np.array([-.6,.95,-.8]);ld/=np.linalg.norm(ld);light=center+ld*light_distance
 theta=(np.arange(64,dtype=np.float32)+.5)/64*np.pi;up=np.cos(theta)
 horizon=np.array([.32,.36,.40]);zenith=np.array([.18,.24,.32]);lower=np.array([.06,.06,.055])
 sky=np.where(up[:,None]>=0,horizon[None,:]*(1-up[:,None])+zenith[None,:]*up[:,None],horizon[None,:]*(1+up[:,None])-lower[None,:]*up[:,None]);sky=np.repeat(sky[:,None,:],128,axis=1).astype(np.float32)
 scene={'type':'scene','integrator':{'type':'path','max_depth':5,'rr_depth':3},
  'sensor':{'type':'perspective','fov':fov,'to_world':T.look_at(origin=origin.tolist(),target=center.tolist(),up=[0,1,0]),
   'sampler':{'type':'independent','sample_count':args.spp},'film':{'type':'hdrfilm','width':width,'height':height,'pixel_format':'rgb','rfilter':{'type':'tent'}}},
  'voxels':mesh,'sky':{'type':'envmap','bitmap':mi.Bitmap(sky)},
  'key':{'type':'rectangle','to_world':T.look_at(origin=light.tolist(),target=center.tolist(),up=[0,0,1])@T.scale([light_distance*.15,light_distance*.15,1]),
   'emitter':{'type':'area','radiance':{'type':'rgb','value':[34,30,24]}}}}
 print(name,meta['occupied_cells'],'cells;',len(faces),'triangles; rendering',args.spp,'spp',flush=True)
 values=np.asarray(mi.render(mi.load_dict(scene),spp=args.spp,seed=42),dtype=np.float32);assert np.isfinite(values).all()
 values=1-np.exp(-np.maximum(values,0)*1.2);values=np.where(values<=.0031308,values*12.92,1.055*values**(1/2.4)-.055)
 pic=Image.fromarray(np.uint8(np.clip(values*255+.5,0,255)))
 board=Image.new('RGB',(width,height+99),(13,20,25));board.paste(pic,(0,59));d=ImageDraw.Draw(board)
 title=meta['scene']['label'].upper()
 if is_rock:title+=' / V'+str(meta['generation_version']['rock'])
 d.text((15,9),title,font=font(20 if not args.draft else 15),fill=(229,234,221))
 if not arranged:
  specimens=meta['scene']['specimens'];counts=', '.join(str(s['generated_cells']) for s in specimens)
  subtitle=('ONE 10 CM CELL EACH / Fungus, Leaves, Petal' if name=='micro-plants' else 'SEEDS 714103 / 271828 / 161803')+'    |    '+counts+' generated cells'
  if meta['scene']['category']=='rock' and all(s.get('resolved_shape') for s in specimens):
   display={'AngularBoulder':'Angular boulder','SplitOutcrop':'Split outcrop'}
   subtitle+='  |  '+' / '.join(display.get(s['resolved_shape'],s['resolved_shape']) for s in specimens)
 else:subtitle='Authored arrangement of actual procedural specimens / controlled voxel floor'
 d.text((15,35),subtitle,font=font(12 if not args.draft else 9),fill=(158,184,177))
 d.text((15,board.height-27),'EXACT 10 CM VOXEL DATA / OFFLINE CPU LIGHTING / NOT UNITY',font=font(12 if not args.draft else 9),fill=(215,182,119))
 output=(args.input_dir/'draft' if args.draft else args.output_dir)/(args.output_prefix+name+'.png');output.parent.mkdir(parents=True,exist_ok=True);board.save(output)
 report={'created_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'image':str(output.relative_to(ROOT)),'image_sha256':sha(output),
  'export_name':name,'input_sha256':input_sha,'source_provenance':provenance,'tool_sha256':sha(Path(__file__)),'geometry_tool_sha256':sha(ROOT/'Tools/TreePathtrace/render_pathtrace.py'),
  'renderer':'Mitsuba '+mi.__version__,'drjit':dr.__version__,'threads':args.threads,'spp':args.spp,'render_seed':42,'voxel_m':scale,
  'vertices':len(vertices),'triangles':len(faces),'exposed_quads':quads,'camera_origin_m':origin.tolist(),'camera_target_m':center.tolist(),'fov':fov,
  'elapsed_seconds':round(time.monotonic()-started,3),'metadata':meta,
  'note':'Exact actual generated occupied voxel faces and stored sRGB pigments. Camera, sky and key are offline lighting fixtures. Not Unity appearance, GPU evidence, neural terrain or ecological scatter.'}
 output.with_suffix('.report.json').write_text(json.dumps(report,indent=2)+'\n');print('Saved',output,'in',report['elapsed_seconds'],'seconds',flush=True)
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('names',nargs='*');p.add_argument('--spp',type=int,default=64);p.add_argument('--threads',type=int,default=4);p.add_argument('--draft',action='store_true')
 p.add_argument('--input-dir',type=Path,default=BUILD);p.add_argument('--output-dir',type=Path,default=OUT);p.add_argument('--output-prefix',default='');a=p.parse_args()
 a.input_dir=a.input_dir.resolve();a.output_dir=a.output_dir.resolve()
 import mitsuba as mi,drjit as dr
 mi.set_variant('llvm_ad_rgb');dr.set_thread_count(a.threads)
 names=a.names or sorted(p.stem for p in a.input_dir.glob('*.bin'))
 for name in names:render(mi,dr,name,a)
if __name__=='__main__':main()
