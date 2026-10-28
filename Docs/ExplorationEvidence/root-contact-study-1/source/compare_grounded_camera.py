"""Isolated actual-data shape study. No production geometry is written."""
from pathlib import Path
import collections,hashlib,json,math,sys,time
import numpy as np
from PIL import Image,ImageDraw,ImageFont
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[1]
sys.path.insert(0,str(ROOT/'Tools/TreePreview'))
from render_trees import trace,display_curve
DT=np.dtype([('x','<i2'),('y','<i2'),('z','<i2'),('color','<u4')])
TD=np.dtype([('top','<i8'),('bare','<i8'),('color','<u4'),('deep','<u4')])
SCALE=.1;SEAM=30;MAX_Y=80

def wood(id,kind):
    p=HERE/f'wood-{id}-{kind}.bin';data=np.fromfile(p,dtype=DT,offset=4)
    assert len(data)==int.from_bytes(p.read_bytes()[:4],'little')
    return {(int(v['x']),int(v['y']),int(v['z'])):int(v['color']) for v in data}

def terrain(id):
    p=HERE/f'terrain-{id}.bin'
    with p.open('rb') as f:
        gx,gz=np.fromfile(f,'<i8',2);side=int(np.fromfile(f,'<i4',1)[0]);a=np.fromfile(f,TD).reshape(side,side)
    low=int(a['top'].min())-5;high=int(a['top'].max())+2
    vol=np.zeros((side,high-low,side),np.uint8);colors=np.zeros(vol.shape,np.uint32)
    for z in range(side):
        for x in range(side):
            top=int(a['top'][z,x])-low
            vol[z,:top+1,x]=1;colors[z,:top+1,x]=a['deep'][z,x];colors[z,top,x]=a['color'][z,x]
    return a,np.array([gx,low,gz],float)*.1,vol,colors

def overlap_sampler(site,a,origin):
    angle=math.radians(site['yaw']);co,si=math.cos(angle),math.sin(angle)
    base=np.array([site['x'],site['z']])*10-origin[[0,2]]*10
    cache={}
    def sample(x,z):
        key=(x,z)
        if key in cache:return cache[key]
        # Exact positive-area overlap of a yawed local cell square and global
        # terrain squares using all four separating axes. Touching edges alone
        # do not count. This replaces the earlier corner-only approximation.
        center=base+np.array([co*(x+.5)+si*(z+.5),-si*(x+.5)+co*(z+.5)])
        radius=(abs(co)+abs(si))*.5;bare=[];top=[]
        for gz in range(math.floor(center[1]-radius),math.ceil(center[1]+radius)):
            for gx in range(math.floor(center[0]-radius),math.ceil(center[0]+radius)):
                dx,dz=gx+.5-center[0],gz+.5-center[1];limit=.5+radius-1e-9
                if abs(dx)>=limit or abs(dz)>=limit or abs(co*dx-si*dz)>=limit or abs(si*dx+co*dz)>=limit:continue
                assert 0<=gx<a.shape[1] and 0<=gz<a.shape[0]
                bare.append((int(a['bare'][gz,gx])+1)*.1);top.append((int(a['top'][gz,gx])+1)*.1)
        assert bare
        result=(min(bare),max(bare),min(top),max(top));cache[key]=result;return result
    return sample

def columns(cells):
    result=collections.defaultdict(list)
    for (x,y,z),color in cells.items():result[x,z].append((y,color))
    for values in result.values():values.sort()
    return result

def metrics(cells,original,sample,anchor):
    cols=columns(cells);gaps=[];burial=[]
    for (x,z),c in cols.items():
        y=c[0][0]
        if y>=SEAM:continue
        low,high,_,_=sample(x,z);bottom=anchor+y*.1
        gaps.append(max(0,bottom-low));burial.append(max(0,high-bottom))
    lower={p for p in cells if p[1]<=SEAM};seen={p for p in lower if p[1]==SEAM};queue=collections.deque(seen)
    while queue:
        x,y,z=queue.popleft()
        for dx,dy,dz in [(1,0,0),(-1,0,0),(0,1,0),(0,-1,0),(0,0,1),(0,0,-1)]:
            p=x+dx,y+dy,z+dz
            if p in lower and p not in seen:seen.add(p);queue.append(p)
    return dict(wood_cells=len(cells),added_cells=len(cells.keys()-original.keys()),removed_cells=len(original.keys()-cells.keys()),
        lower_cells=len(lower),disconnected_lower_cells=len(lower-seen),max_basal_gap_m=max(gaps),max_basal_burial_m=max(burial),
        gap_columns=sum(g>1e-6 for g in gaps),burial_quantiles_m=np.quantile(burial,[0,.5,.95,1]).tolist(),
        upper_geometry_and_tint_unchanged={p:c for p,c in cells.items() if p[1]>=SEAM}=={p:c for p,c in original.items() if p[1]>=SEAM})

def variants(site,sample):
    original=wood(site['id'],'baseline');narrow=wood(site['id'],'narrow')
    # Base-only splice. The canonical wood (including every original color)
    # above 3m is copied, never taken from the regenerated narrow variant.
    base={p:c for p,c in original.items() if p[1]>=SEAM}
    base.update({p:c for p,c in narrow.items() if p[1]<SEAM})
    anchor=site['original_y'];curtain=base.copy();conform={p:c for p,c in base.items() if p[1]>=SEAM}
    for (x,z),col in columns(base).items():
        bottom=col[0][0]
        if bottom>=SEAM:continue
        low,*_=sample(x,z);target=math.floor((low-anchor)/.1+1e-7)
        for y in range(target,bottom):curtain[x,y,z]=col[0][1]
        # Terrain-following taper: pin the 3m seam; map the authored lowest
        # occupied cell to the exact overlapping terrain minimum. Rasterize
        # every source cell's mapped interval, retaining 10cm cubic voxels.
        # This is a visual geometry prototype, NOT authoritative runtime matter.
        assert target<SEAM-3,'Site needs explicit rejection or alternate base'
        ratio=(SEAM-target)/(SEAM-bottom)
        for y,color in col:
            if y>=SEAM:continue
            lo=math.floor(target+(y-bottom)*ratio+1e-7)
            hi=math.ceil(target+(y+1-bottom)*ratio-1e-7)
            for ny in range(lo,min(SEAM,hi)):conform[x,ny,z]=color
    return [('original',original,anchor),('narrow',base,anchor),('skirt',curtain,anchor),('taper',conform,anchor)],original

def volume(cells):
    filtered={p:c for p,c in cells.items() if p[1]<MAX_Y};coords=np.array(list(filtered));lo=coords.min(0);hi=coords.max(0)+1;size=hi-lo
    vol=np.zeros(tuple(size[[2,1,0]]),np.uint8);colors=np.zeros(vol.shape,np.uint32)
    q=coords-lo;ix=(q[:,2],q[:,1],q[:,0]);vol[ix]=1;colors[ix]=list(filtered.values())
    return vol,colors,lo*.1

def render(site,cells,anchor,terr,view,width=480,height=360):
    a,to,tv,tc=terr;rv,rc,ro=volume(cells);yaw=math.radians(site['yaw']);co,si=math.cos(yaw),math.sin(yaw)
    # Local tree -> global rotation matches PopulationInstance yaw convention.
    rot=np.array([[co,0,si],[0,1,0],[-si,0,co]])
    tree=np.array([site['x'],anchor,site['z']]);target=tree+np.array([0,1.8,0])
    horizontal=np.asarray(view,dtype=float)
    camera=target+horizontal*12+np.array([0,1.2,0])
    ix=int(math.floor((camera[0]-to[0])/.1));iz=int(math.floor((camera[2]-to[2])/.1))
    assert 0<=ix<a.shape[1] and 0<=iz<a.shape[0]
    camera[1]=max(camera[1],(int(a["top"][iz,ix])+1)*.1+1.7)
    forward=target-camera;forward/=np.linalg.norm(forward);right=np.cross(forward,[0,1,0]);right/=np.linalg.norm(right);up=np.cross(right,forward)
    xx,yy=np.meshgrid((np.arange(width)+.5-width/2)/height,(height/2-np.arange(height)-.5)/height)
    d=(forward+right*xx[...,None]*.9+up*yy[...,None]*.9).reshape(-1,3);d/=np.linalg.norm(d,axis=1)[:,None]
    def trace_both(origins,directions):
        th,tq,tn,tp,tt=trace(tv,(origins-to)/.1,directions,maxdist=600)
        rh,rq,rn,rp,rt=trace(rv,((origins-tree)@rot-ro)/.1,directions@rot,maxdist=600)
        use_root=rh&(~th|(rt<tt));hit=th|rh;dist=np.where(use_root,rt,tt)*.1
        normals=np.where(use_root[:,None],rn@rot.T,tn);packed=np.zeros(len(hit),np.uint32)
        ids=np.flatnonzero(th&~use_root);q=tq[ids];packed[ids]=tc[q[:,2],q[:,1],q[:,0]]
        ids=np.flatnonzero(use_root);q=rq[ids];packed[ids]=rc[q[:,2],q[:,1],q[:,0]]
        return hit,normals,dist,packed
    hit,normals,dist,packed=trace_both(camera,d);ids=np.flatnonzero(hit)
    color=np.tile([.14,.20,.24],(len(hit),1));sun=np.array([-.62,.72,-.31]);sun/=np.linalg.norm(sun)
    points=camera+d[ids]*dist[ids,None]+normals[ids]*.004
    blocked,*_=trace_both(points,np.broadcast_to(sun,points.shape))
    diffuse=np.maximum(normals[ids]@sun,0)*(~blocked)
    pigments=packed[ids];base=np.stack([(pigments>>16)&255,(pigments>>8)&255,pigments&255],1)/255
    base=np.where(base<=.04045,base/12.92,((base+.055)/1.055)**2.4)
    ambient=.32+.18*np.maximum(normals[ids,1],0)
    color[ids]=base*(ambient[:,None]*[.72,.87,1.05]+diffuse[:,None]*[1.5,1.28,.92])
    return Image.fromarray((display_curve(color).reshape(height,width,3)*255).astype(np.uint8))

def main():
    sites=json.loads((HERE/'sites.json').read_text());reports=[];panels=[];start=time.monotonic()
    try:font=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',16)
    except OSError:font=ImageFont.load_default()
    titles={'original':'Original mature base','narrow':'Narrow base / upper tree preserved','skirt':'Downward fill / skirt control','taper':'Terrain-following tapered base'}
    for site in sites:
        terr=terrain(site['id']);sample=overlap_sampler(site,terr[0],terr[1]);shapes,original=variants(site,sample)
        for view in [0,1]:
            row=[]
            for name,cells,anchor in shapes:
                data=metrics(cells,original,sample,anchor)
                if view==0:reports.append(dict(site_id=site['id'],slope=site['slope'],variant=name,**data))
                img=render(site,cells,anchor,terr,view);panel=Image.new('RGB',(480,418),(17,23,26));panel.paste(img,(0,58));draw=ImageDraw.Draw(panel)
                draw.text((10,4),titles[name],font=font,fill='white')
                draw.text((10,26),f"gap {data['max_basal_gap_m']:.2f}m / burial {data['max_basal_burial_m']:.2f}m",font=font,fill=(216,188,145))
                panel.save(HERE/f"site-{site['id']}-view-{view}-{name}.png");row.append(panel)
                print('Rendered',site['id'],view,name,'seconds',round(time.monotonic()-start,1),flush=True)
            joined=Image.new('RGB',(1920,450),(17,23,26))
            for i,panel in enumerate(row):joined.paste(panel,(i*480,32))
            ImageDraw.Draw(joined).text((12,4),f"ACTUAL 10cm WOOD / natural site {site['id']}, slope {site['slope']:.2f} degrees / CPU SHAPE STUDY, NOT UNITY",font=font,fill=(210,220,220))
            joined.save(HERE/f"site-{site['id']}-view-{view}.png");panels.append(joined)
    sheet=Image.new('RGB',(1920,450*len(panels)))
    for i,p in enumerate(panels):sheet.paste(p,(0,i*450))
    sheet.save(HERE/'comparison.png')
    result={'scope':'Offline actual-data CPU shape preview. Terrain height/top pigment exact; subsurface pigment representative at 0.5m. Foliage omitted. Canonical wood above3m preserved exactly; no complete live pack or collision replacement. Exact positive-area rotated-cell/global-cell overlap sampling. No Unity/FPS or aesthetic acceptance implied.',
        'generation_seconds_including_python_render':time.monotonic()-start,'rows':reports,'tools_sha256':{str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in [Path(__file__),ROOT/'Tools/TreePreview/render_trees.py',ROOT/'Tools/TreePreview/fast_dda.py']}}
    (HERE/'comparison.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
if __name__=='__main__':main()
