#!/usr/bin/env python3
"""Actual-data CPU inspection gallery for procedural 10cm voxel trees.

Needs .NET8 SDK, numpy and Pillow. No concept images, external art or Unity.
Renders use the generator's actual per-voxel colors, hard voxel geometry, exact
primary/sun DDA, local ambient occlusion and illustrative sky lighting.
"""
from pathlib import Path
import argparse,hashlib,json,os,shutil,subprocess
from datetime import datetime,timezone
import numpy as np
from PIL import Image,ImageDraw,ImageFont
ROOT=Path(__file__).resolve().parents[2]
BUILD=ROOT/'Tools/TreePreview/bin'
OUT=ROOT/'Docs/TreeGallery'

def version(p):return tuple(int(v) if v.isdigit() else 0 for v in p.name.split('.'))
def compile_export(dotnet_arg,export_species=None,variations=3):
    candidates=[dotnet_arg,Path(os.environ['DOTNET_ROOT'])/'dotnet' if os.environ.get('DOTNET_ROOT') else None,Path(shutil.which('dotnet')) if shutil.which('dotnet') else None,ROOT.parent/'.dotnet/dotnet']
    dotnet=next((v.resolve() for v in candidates if v and v.is_file()),None)
    if not dotnet:raise RuntimeError('Install .NET8 SDK; set DOTNET_ROOT or pass --dotnet')
    sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1]
    refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
    BUILD.mkdir(parents=True,exist_ok=True);OUT.mkdir(parents=True,exist_ok=True)
    assembly=BUILD/'ExportTrees.dll';quote=lambda p:'"'+str(p)+'"'
    core=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))
    hashes={str(v.relative_to(ROOT)):hashlib.sha256(v.read_bytes()).hexdigest() for v in core}
    snapshot=[]
    for source in core:
        target=BUILD/'source'/source.relative_to(ROOT/'Assets/AshenVoxel/Core')
        target.parent.mkdir(parents=True,exist_ok=True)
        target.write_bytes(source.read_bytes());snapshot.append(target)
    if hashes!={str(v.relative_to(ROOT)):hashlib.sha256(v.read_bytes()).hexdigest() for v in core}:
        raise RuntimeError('Core changed while copying source snapshot; retry')
    options=['-nologo','-optimize+','-target:exe','-out:'+quote(assembly)]
    options+=['-r:'+quote(v) for v in refs.glob('*.dll')]
    options+=[quote(v) for v in snapshot]+[quote(ROOT/'Tools/TreePreview/ExportTrees.cs')]
    rsp=BUILD/'compile.rsp';rsp.write_text('\n'.join(options))
    (BUILD/'ExportTrees.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
    subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
    command=[str(dotnet),str(assembly),str(BUILD)]
    if export_species or variations!=3:command.extend([export_species or 'all',str(variations)])
    subprocess.run(command,check=True)
    (OUT/'provenance.json').write_text(json.dumps({'source_sha256':hashes,'tool_sha256':{v.name:hashlib.sha256(v.read_bytes()).hexdigest() for v in [ROOT/'Tools/TreePreview/ExportTrees.cs',Path(__file__),ROOT/'Tools/TreePreview/fast_dda.py']},'exported_utc':datetime.now(timezone.utc).isoformat(),'sdk':sdk.name,'export_species':export_species or 'all','export_variations':variations,'note':'Actual generated data from immutable source snapshot; CPU inspection render, not Unity output.'},indent=2))

def read_world(name):
    meta=json.loads((BUILD/(name+'.json')).read_text())
    dtype=np.dtype([('x','<u2'),('y','<u2'),('z','<u2'),('id','u1'),('color','<u4')])
    with open(BUILD/(name+'.bin'),'rb') as f:
        dims=np.fromfile(f,dtype='<i4',count=3);scale=np.fromfile(f,dtype='<f4',count=1)[0];count=np.fromfile(f,dtype='<i4',count=1)[0]
        records=np.fromfile(f,dtype=dtype,count=int(count))
    if len(records)!=count:raise ValueError('Truncated exported world')
    shape=(int(dims[2]),int(dims[1]),int(dims[0]))
    vol=np.zeros(shape,dtype=np.uint8);colors=np.zeros(shape,dtype=np.uint32)
    ix=(records['z'],records['y'],records['x'])
    vol[ix]=records['id'];colors[ix]=records['color']
    return meta,vol,colors,float(scale)

def trace(vol,origins,directions,maxdist=3000):
    dims=np.array([vol.shape[2],vol.shape[1],vol.shape[0]],dtype=np.int32)
    d=np.asarray(directions,dtype=np.float32);n=len(d)
    o=np.broadcast_to(np.asarray(origins,dtype=np.float32),(n,3))
    inv=np.where(np.abs(d)>1e-8,1/np.where(d==0,1e-20,d),1e20)
    a=-o*inv;b=(dims-o)*inv
    near=np.minimum(a,b);enter=np.maximum(near.max(axis=1),0);leave=np.minimum(np.maximum(a,b).min(axis=1),maxdist)
    active=(leave>=enter)
    pos=np.floor(o+d*(enter[:,None]+1e-3)).astype(np.int32);pos=np.clip(pos,0,dims-1)
    step=np.sign(d).astype(np.int32);delta=np.abs(inv);tnext=((pos+(step>0))-o)*inv
    normals=np.zeros((n,3),dtype=np.float32)
    outside=(near.max(axis=1)>0);entry_axis=near.argmax(axis=1);ids=np.flatnonzero(outside)
    normals[ids,entry_axis[ids]]=-step[ids,entry_axis[ids]]
    hit=np.zeros(n,dtype=bool);cell=np.zeros((n,3),dtype=np.int32);dist=enter.copy()
    for iteration in range(int(dims.sum()+5)):
        ids=np.flatnonzero(active)
        if not len(ids):break
        p=pos[ids];found=vol[p[:,2],p[:,1],p[:,0]]>0
        hits=ids[found];hit[hits]=True;cell[hits]=pos[hits];active[hits]=False
        ids=ids[~found]
        if not len(ids):continue
        axis=tnext[ids].argmin(axis=1);t=tnext[ids,axis];dist[ids]=t
        ties=tnext[ids]<=t[:,None]+1e-6
        pos[ids]+=step[ids]*ties;tnext[ids]+=delta[ids]*ties
        normals[ids]=0;normals[ids,axis]=-step[ids,axis]
        active[ids]&=(pos[ids]>=0).all(axis=1)&(pos[ids]<dims).all(axis=1)&(t<=leave[ids])
    return hit,cell,normals,o+d*dist[:,None],dist

# Optional acceleration preserves the same exact-cell image model.
_numpy_trace=trace
try:
    from fast_dda import trace
except ImportError:
    pass

def occupied(vol,p):
    q=np.floor(p).astype(np.int32);dims=np.array([vol.shape[2],vol.shape[1],vol.shape[0]])
    inside=(q>=0).all(axis=1)&(q<dims).all(axis=1)
    result=np.zeros(len(p),dtype=np.float32);v=q[inside]
    result[inside]=(vol[v[:,2],v[:,1],v[:,0]]>0)
    return result

def display_curve(x):
    # Simple filmic display curve; illustrative, not the URP shader's response.
    a,b,c,d,e=2.51,.03,2.43,.59,.14
    linear=np.clip((x*(a*x+b))/(x*(c*x+d)+e),0,1)
    return np.where(linear<=.0031308,linear*12.92,1.055*linear**(1/2.4)-.055)

def render(name,width,height,detail=None):
    meta,vol,colors,scale=read_world(name)
    lo=np.array(meta['bounds_min_m']);hi=np.array(meta['bounds_max_m']);center=(lo+hi)/2
    treeheight=hi[1]-.3;span=max(hi[0]-lo[0],hi[2]-lo[2]);
    if detail=='bark':
        target=np.array(meta['center_m'],dtype=float)+np.array([0,min(treeheight*.14,2.8),0])
        # Camera offset adapts to thick redwood bark and thin birch trunks.
        radius=max(.45,float(meta['parameters']['TrunkRadius'])*1.8)
        camera=target+np.array([-radius*2.3,.35,-radius*3.3]);fov=.82
    elif detail=='foliage':
        fruit=np.argwhere(vol==10) if meta['fruit_enabled'] else np.zeros((0,3),dtype=int)
        if len(fruit):
            targets=(fruit[:,[2,1,0]]+.5)*scale
            choices=[]
            for angle in np.linspace(0,2*np.pi,8,endpoint=False):
                offset=np.array([np.sin(angle)*1.3,-.35,np.cos(angle)*1.3])
                origins=targets+offset
                rays=targets-origins;rays/=np.linalg.norm(rays,axis=1)[:,None]
                hits,cells,_,_,_=trace(vol,origins/scale,rays,maxdist=30)
                visible=np.zeros(len(hits),dtype=bool);q=cells[hits]
                visible[hits]=vol[q[:,2],q[:,1],q[:,0]]==10
                for index in np.flatnonzero(visible):
                    choices.append((float(offset@np.array([-.62,.72,-.31])),targets[index],origins[index]))
            if choices:
                _,target,camera=max(choices,key=lambda v:v[0])
            else:
                target=targets[np.argmin(fruit[:,0])];camera=target+np.array([0,-.5,-1.3])
            fov=.86
        else:
            target=center+np.array([span*.15,treeheight*.14,-span*.15])
            camera=target+np.array([span*.42,treeheight*.05,-span*.66]);fov=.86
    else:
        target=center+np.array([0,-treeheight*.03,0]);distance=max(treeheight*1.5,span*1.7)
        camera=target+np.array([distance*.47,distance*.22,-distance*.88]);fov=.82
    o=camera/scale;f=target-camera;f/=np.linalg.norm(f)
    right=np.cross(f,[0,1,0]);right/=np.linalg.norm(right);up=np.cross(right,f)
    x,y=np.meshgrid((np.arange(width)+.5-width/2)/height,(height/2-np.arange(height)-.5)/height)
    d=f+right*x[...,None]*fov+up*y[...,None]*fov;d=d.reshape(-1,3);d/=np.linalg.norm(d,axis=1)[:,None]
    hit,cell,n,p,t=trace(vol,o,d)
    sky_low=np.array([.12,.17,.19]);sky_high=np.array([.20,.29,.34])
    skymix=np.clip(d[:,1]*.7+.4,0,1)
    color=sky_low*(1-skymix[:,None])+sky_high*skymix[:,None]
    ids=np.flatnonzero(hit)
    sun=np.array([-.62,.72,-.31]);sun/=np.linalg.norm(sun)
    shadow,*_=trace(vol,p[ids]+n[ids]*.03,np.tile(sun,(len(ids),1)))
    diffuse=np.maximum(n[ids]@sun,0)*(~shadow)
    # Small local hemisphere samples give contact depth while retaining readable dark foliage.
    points=p[ids]+n[ids]*.04;ao=np.zeros(len(ids),dtype=np.float32)
    offsets=[(.4,.8,0),(-.4,.8,0),(0,.8,.4),(0,.8,-.4),(.9,.55,.9),(-.9,.55,.9),(.9,.55,-.9),(-.9,.55,-.9)]
    for offset in offsets:
        v=np.array(offset);v=v+n[ids]*.8
        ao+=occupied(vol,points+v)
    ao=1-ao/len(offsets)*.66
    q=cell[ids];packed=colors[q[:,2],q[:,1],q[:,0]]
    base=np.stack([(packed>>16)&255,(packed>>8)&255,packed&255],axis=1).astype(np.float32)/255
    base=np.where(base<=.04045,base/12.92,((base+.055)/1.055)**2.4)
    ambient=(.28+.17*np.maximum(n[ids,1],0))*ao
    lighting=ambient[:,None]*np.array([.72,.87,1.05])+diffuse[:,None]*np.array([1.5,1.28,.92])*1.05
    color[ids]=base*lighting
    # Very restrained atmospheric depth; inspect species color rather than hide it in fog.
    fog=1-np.exp(-(t[ids]*scale*.006)**2)
    color[ids]=color[ids]*(1-fog[:,None])+sky_low*fog[:,None]
    image=Image.fromarray((display_curve(color).reshape(height,width,3)*255).astype(np.uint8))
    return image,meta

def font(size):
    try:return ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf',size)
    except OSError:return ImageFont.load_default()
def label(image,title,subtitle,footer=True):
    result=Image.new('RGB',(image.width,image.height+96 if footer else image.height+66),(12,19,21));result.paste(image,(0,66))
    draw=ImageDraw.Draw(result);draw.text((18,10),title,font=font(22),fill=(230,232,220));draw.text((18,39),subtitle,font=font(12),fill=(166,186,186))
    if footer:draw.text((18,result.height-23),'ACTUAL 10 CM VOXELS / OFFLINE CPU INSPECTION / UNITY UNVERIFIED',font=font(11),fill=(218,176,103))
    return result

def gallery(args):
    if not args.skip_export:compile_export(args.dotnet,args.export_species,args.export_variations)
    if args.export_only:return
    OUT.mkdir(parents=True,exist_ok=True)
    species=args.species.split(',') if args.species else ['birch','oak','redwood','pine','oak-fruit']
    samples=[];allmeta=[]
    for s in species:
        name=s+'-0';image,meta=render(name,args.width,int(args.width*1.18))
        title=meta['species'].upper()+(' / FRUIT VARIANT' if meta['fruit_enabled'] else '')
        h=meta['bounds_max_m'][1]-.3
        sub=f"Seed {meta['seed']} • actual height {h:.1f} m • material colors stored per voxel"
        labeled=label(image,title,sub);labeled.save(OUT/(s+'.png'));samples.append((s,labeled));allmeta.append(meta)
        print('Rendered',s,flush=True)
        if not args.no_details:
            bark,_=render(name,args.width//2,args.width//2,'bark');foliage,_=render(name,args.width//2,args.width//2,'foliage')
            joined=Image.new('RGB',(bark.width*2,bark.height),(12,19,21));joined.paste(bark,(0,0));joined.paste(foliage,(bark.width,0))
            label(joined,title+' / MATERIAL DETAIL','Bark (left) • '+('acorn detail' if meta['fruit_enabled'] else 'foliage / branch structure')+' (right)').save(OUT/(s+'-detail.png'))
    thumb_width=500;thumbs=[]
    for s,img in samples:
        h=round(img.height*thumb_width/img.width);thumbs.append(img.resize((thumb_width,h),Image.Resampling.LANCZOS))
    canvas=Image.new('RGB',(thumb_width*3,max(i.height for i in thumbs)*2),(12,19,21))
    for i,img in enumerate(thumbs):canvas.paste(img,((i%3)*thumb_width,(i//3)*max(v.height for v in thumbs)))
    draw=ImageDraw.Draw(canvas);draw.text((1020,canvas.height//2+80),'PROCEDURAL TREE STUDY',font=font(20),fill=(225,230,219));draw.text((1020,canvas.height//2+120),'10 cm material voxels',font=font(17),fill=(163,181,183));draw.text((1020,canvas.height//2+160),'Individually framed; heights labelled',font=font(14),fill=(163,181,183));draw.text((1020,canvas.height//2+200),'Offline CPU render / Unity unverified',font=font(12),fill=(213,169,97))
    canvas.save(OUT/('selected-comparison.png' if args.species else 'species-comparison.png'))
    if not args.no_variations:
        tilew=400;tileh=540;canvas=Image.new('RGB',(tilew*3,tileh*4),(12,19,21))
        for row,s in enumerate([s for s in species if s!='oak-fruit'][:4]):
            for variation in range(3):
                img,meta=render(s+'-'+str(variation),tilew,tileh-66)
                if variation>0:allmeta.append(meta)
                img=label(img,s.upper(),f"Seed {meta['seed']} / same species parameters",False)
                canvas.paste(img,(variation*tilew,row*tileh))
                print('Variation',s,variation,flush=True)
        canvas.save(OUT/'seed-variations.png')
    (OUT/('generated-selected.json' if args.species else 'generated-samples.json')).write_text(json.dumps(allmeta,indent=2))
    provenance=json.loads((OUT/'provenance.json').read_text())
    provenance['render_tool_sha256']={v.name:hashlib.sha256(v.read_bytes()).hexdigest() for v in [Path(__file__),ROOT/'Tools/TreePreview/fast_dda.py']}
    provenance['rendered_utc']=datetime.now(timezone.utc).isoformat()
    provenance['render_backend']='Numba parallel exact-cell DDA' if trace is not _numpy_trace else 'NumPy exact-cell DDA'
    (OUT/'provenance.json').write_text(json.dumps(provenance,indent=2))

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--dotnet',type=Path);p.add_argument('--width',type=int,default=900);p.add_argument('--skip-export',action='store_true');p.add_argument('--no-details',action='store_true');p.add_argument('--no-variations',action='store_true');p.add_argument('--species',help='Comma-separated render subset, e.g. birch,oak');p.add_argument('--export-only',action='store_true');p.add_argument('--export-species',choices=('Birch','Oak','Redwood','Pine'),help='Export a single species for a provisional inspection; omit for the complete gallery');p.add_argument('--export-variations',type=int,choices=(1,2,3),default=3);args=p.parse_args();gallery(args)
if __name__=='__main__':main()
