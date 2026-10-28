#!/usr/bin/env python3
"""Offline CPU ray casting of the project's actual generated voxel data.

This is an inspection preview, NOT a Unity screenshot, shader validation or target
frame-rate demonstration. Requires .NET8 SDK, numpy, Pillow. No external assets.
Usage: python3 Tools/Preview/render_preview.py [--width 1000]
"""
from pathlib import Path
import argparse, json, subprocess
import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT=Path(__file__).resolve().parents[2]
BUILD=ROOT/'Tools/Preview/bin'
OUT=ROOT/'Docs/Preview'
PALETTE=np.array([[0,0,0],[111,117,124],[84,71,55],[103,79,54],[80,66,39],[81,101,52],[103,79,56],[65,91,65],[158,143,110],[234,112,42],[176,61,45]],dtype=np.float32)/255

def compile_export():
    dotnet=ROOT.parent/'.dotnet/dotnet'
    sdk=sorted((dotnet.parent/'sdk').glob('8.*'))[-1]
    refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'))[-1]/'ref/net8.0'
    BUILD.mkdir(parents=True,exist_ok=True); OUT.mkdir(parents=True,exist_ok=True)
    assembly=BUILD/'ExportPreview.dll'
    quote=lambda p:'"'+str(p)+'"'
    options=['-nologo','-target:exe','-out:'+quote(assembly)]
    options+=['-r:'+quote(p) for p in refs.glob('*.dll')]
    options+=[quote(p) for p in sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))]
    options+=[quote(ROOT/'Tools/Preview/ExportPreview.cs')]
    rsp=BUILD/'compile.rsp';rsp.write_text('\n'.join(options))
    (BUILD/'ExportPreview.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
    subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
    subprocess.run([str(dotnet),str(assembly),str(BUILD)],check=True)

def trace(volume,origins,directions,maxdist=1000):
    n=len(directions); dims=np.array([256,128,256],dtype=np.int32)
    d=directions.astype(np.float32); o=np.broadcast_to(np.asarray(origins,dtype=np.float32),(n,3))
    inv=np.where(np.abs(d)>1e-9,1/np.where(d==0,1e-20,d),1e20)
    a=(0-o)*inv;b=(dims-o)*inv
    enter=np.maximum(np.minimum(a,b).max(axis=1),0)
    leave=np.minimum(np.maximum(a,b).min(axis=1),maxdist)
    active=(leave>=enter)
    pos=np.floor(o+d*(enter[:,None]+1e-3)).astype(np.int32)
    pos=np.clip(pos,0,dims-1)
    step=np.sign(d).astype(np.int32)
    delta=np.abs(inv)
    tnext=((pos+(step>0))-o)*inv
    normal=np.zeros((n,3),dtype=np.float32)
    hit=np.zeros(n,dtype=bool); mat=np.zeros(n,dtype=np.uint8); dist=enter.copy()
    for iteration in range(650):
        ids=np.flatnonzero(active)
        if not len(ids):break
        p=pos[ids]; m=volume[p[:,2],p[:,1],p[:,0]]
        found=m>0
        hits=ids[found];hit[hits]=True;mat[hits]=m[found];active[hits]=False
        ids=ids[~found]
        if not len(ids):continue
        axis=tnext[ids].argmin(axis=1)
        t=tnext[ids,axis]
        dist[ids]=t
        pos[ids,axis]+=step[ids,axis]
        tnext[ids,axis]+=delta[ids,axis]
        normal[ids]=0; normal[ids,axis]=-step[ids,axis]
        active[ids]&=(pos[ids]>=0).all(axis=1)&(pos[ids]<dims).all(axis=1)&(t<=leave[ids])
    points=o+d*dist[:,None]
    return hit,mat,normal,points,dist

def render(raw,name,camera,target,width,subtitle):
    vol=np.fromfile(raw,dtype=np.uint8).reshape(256,128,256)
    rgb=np.fromfile(raw.with_suffix(".rgb.raw"),dtype="<u4").reshape(256,128,256)
    height=int(width*.65)
    o=np.array(camera)*10;target=np.array(target)*10
    f=target-o;f/=np.linalg.norm(f)
    right=np.cross(f,[0,1,0]);right/=np.linalg.norm(right)
    up=np.cross(right,f)
    xx,yy=np.meshgrid((np.arange(width)+.5-width/2)/height,(height/2-np.arange(height)-.5)/height)
    d=f+right*xx[...,None]*1.12+up*yy[...,None]*1.12
    d=d.reshape(-1,3);d/=np.linalg.norm(d,axis=1)[:,None]
    hit,mat,n,p,t=trace(vol,o,d)
    sky=np.array([.11,.155,.18],dtype=np.float32)
    color=np.tile(sky,(len(d),1))
    ids=np.flatnonzero(hit)
    sun=np.array([-.6,.78,-.35]);sun/=np.linalg.norm(sun)
    # Exact DDA secondary rays avoid light leaking through single-cell features.
    shadow,*_=trace(vol,p[ids]+n[ids]*.02,np.tile(sun,(len(ids),1)),maxdist=600)
    diffuse=np.maximum(n[ids]@sun,0)*(~shadow)
    coords=np.floor(p[ids]).astype(int)
    grain=((coords[:,0]*17+coords[:,1]*31+coords[:,2]*13)%19)/19
    ambient=.24+.12*np.maximum(n[ids,1],0)
    lit=ambient[:,None]*np.array([.75,.90,1.04])+diffuse[:,None]*np.array([1.13,.88,.57])*.82
    sample=np.clip(np.floor(p[ids]-n[ids]*.002).astype(int),0,np.array([255,127,255]))
    packed=rgb[sample[:,2],sample[:,1],sample[:,0]]
    albedo=np.stack([(packed>>16)&255,(packed>>8)&255,packed&255],axis=1).astype(np.float32)/255
    albedo=np.where(albedo<=.04045,albedo/12.92,((albedo+.055)/1.055)**2.4)
    color[ids]=albedo*lit*(.98+.04*grain[:,None])*2.6
    fog=1-np.exp(-(t[ids]*.0017)**2)
    color[ids]=color[ids]*(1-fog[:,None])+sky*fog[:,None]
    # Simple display curve; not Unity's ACES implementation.
    color=np.clip((color*(2.51*color+.03))/(color*(2.43*color+.59)+.14),0,1)
    color=np.where(color<=.0031308,color*12.92,1.055*np.maximum(color,0)**(1/2.4)-.055)
    image=Image.fromarray((color.reshape(height,width,3)*255).astype(np.uint8))
    draw=ImageDraw.Draw(image)
    draw.rectangle((0,0,width,66),fill=(13,19,22))
    try:
        font=ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf',16)
        small=ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf',12)
    except OSError:font=small=ImageFont.load_default()
    draw.text((20,12),'ASHEN / 10 CM VOXEL FOUNDATION',font=font,fill=(225,227,217))
    draw.text((20,39),subtitle,font=small,fill=(164,181,186))
    draw.rectangle((0,height-34,width,height),fill=(13,19,22))
    draw.text((18,height-24),'OFFLINE CPU PREVIEW OF GENERATED DATA — Unity rendering and performance unverified',font=small,fill=(211,172,110))
    image.save(OUT/name)
    print('Wrote',OUT/name,flush=True)

def section(raw):
    vol=np.fromfile(raw,dtype=np.uint8).reshape(256,128,256)
    rgb=(PALETTE[vol[128,::-1,:]]*255).astype(np.uint8)
    rgb[vol[128,::-1,:]==0]=[28,39,43]
    content=Image.fromarray(rgb).resize((1024,512),resample=Image.Resampling.NEAREST)
    image=Image.new('RGB',(1064,620),(13,19,22));image.paste(content,(20,60))
    draw=ImageDraw.Draw(image)
    try: font=ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf',14)
    except OSError: font=ImageFont.load_default()
    draw.text((20,16),'ACTUAL GENERATED MATERIAL SECTION / z = 12.8 m / 10 cm cells',font=font,fill=(225,227,217))
    draw.text((20,584),'Basalt > mud / soil > litter > grass; procedural wood / leaves above • data view, not Unity',font=font,fill=(211,172,110))
    image.save(OUT/'material-section.png')
    print('Wrote',OUT/'material-section.png',flush=True)

def main():
    p=argparse.ArgumentParser();p.add_argument('--width',type=int,default=1000);args=p.parse_args()
    compile_export()
    section(BUILD/'forest.raw')
    render(BUILD/'forest.raw','forest-overview.png',(31,20,-13),(12.8,3.6,12.8),args.width,'Seed 714103 • 25.6 × 12.8 × 25.6 m • actual generator v'+(BUILD/'generator-version.txt').read_text().strip()+' / unedited forest')
    render(BUILD/'edited.raw','terrain-edit.png',(12.8,7.5,10.0),(12.8,2.2,13.0),args.width,'Actual core carve/place operations • soil strata and gravity after 16 budgeted steps')
if __name__=='__main__':main()
