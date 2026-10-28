#!/usr/bin/env python3
"""Draw actual exported C# LOD leaves; no independent reimplementation of selection."""
from pathlib import Path
from collections import Counter
import hashlib,json,math
from PIL import Image,ImageDraw,ImageFont
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'Docs/WorldViews'
PALETTE=['#5ce6d8','#3dd6be','#5fdf74','#9bd34c','#d8c645','#f4a24a','#eb7f49','#d55d75','#aa6cbd','#8774d7','#5b83d1','#4670b5','#385991']
FONT='/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf'
def font(n):
 try:return ImageFont.truetype(FONT,n)
 except OSError:return ImageFont.load_default()
def text(draw,p,s,n=18,fill='#dce6df'):draw.text(p,s,font=font(n),fill=fill)
def plot(canvas,box,meta,radius,title):
 x0,y0,x1,y1=box;w,h=x1-x0,y1-y0;layer=Image.new('RGB',(w,h),'#18262c');draw=ImageDraw.Draw(layer)
 cx,cz=meta['global_camera'][0],meta['global_camera'][2]
 def xy(x,z):return ((x-cx+radius)/(2*radius)*w,(z-cz+radius)/(2*radius)*h)
 for t in sorted(meta['tiles'],key=lambda t:-t['level']):
  a,b=xy(t['ox'],t['oz']);c,d=xy(t['ox']+t['size'],t['oz']+t['size'])
  if c<0 or d<0 or a>w or b>h:continue
  a,b,c,d=max(0,a),max(0,b),min(w,c),min(h,d)
  if c<a or d<b:continue
  draw.rectangle((a,b,c,d),fill=PALETTE[t['level']],outline='#17252d',width=1)
 # Distance rings aid physical-scale reading; every rectangle is a real leaf.
 for fraction in (.25,.5,.75,1):
  rx=fraction*w*.5;ry=fraction*h*.5
  draw.ellipse((w/2-rx,h/2-ry,w/2+rx,h/2+ry),outline='#cfdfd5',width=1)
 draw.line((w/2-13,h/2,w/2+13,h/2),fill='white',width=3);draw.line((w/2,h/2-13,w/2,h/2+13),fill='white',width=3)
 draw.ellipse((w/2-5,h/2-5,w/2+5,h/2+5),fill='#f2f8ef',outline='#071013',width=2)
 canvas.paste(layer,(x0,y0));draw=ImageDraw.Draw(canvas)
 text(draw,(x0,y0-35),title,22)
 km=radius/1000
 for fraction,label in [(0,f'{-km:g}'),(.5,'0'),(1,f'+{km:g}')]:
  px=x0+fraction*w
  text(draw,(px-15,y1+8),label,15)
 text(draw,(x1-90,y1+31),'x / km',14,'#9ab0b3')
 text(draw,(x0+12,y0+12),'z = -'+f'{km:g}'+' km',15,'#f5faf3');text(draw,(x0+12,y1-30),'z = +'+f'{km:g}'+' km',15,'#f5faf3')
def legend(canvas,y):
 draw=ImageDraw.Draw(canvas);x=30
 for level,color in enumerate(PALETTE):
  spacing=2*(2**level);label=f'L{level} / '+(f'{spacing/1000:g} km' if spacing>=1000 else f'{spacing} m')
  width=round((canvas.width-60)/len(PALETTE));draw.rectangle((x,y,x+width-9,y+20),fill=color)
  text(draw,(x,y+28),label,13);x+=width
 text(draw,(30,y+62),'Colors show actual height-proxy sample spacing. Finest proxy is 2 m; editable matter remains 10 cm.',17,'#d6c08c')
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 low=json.loads((OUT/'analytic-observer.json').read_text());high=json.loads((OUT/'analytic-high.json').read_text())
 # Schema/binary checks also guard against silently drawing invented leaves.
 for m in (low,high):
  assert len(m['tiles'])<=m['tile_cap'] and m['pending_splits']>=0
  assert all(t['sample_spacing_m']==t['size']/32 for t in m['tiles'])
 (OUT/'lod-palette.json').write_text(json.dumps({'rgb_hex':PALETTE,'spacing_m':[2*2**i for i in range(13)]},indent=2)+'\n')
 image=Image.new('RGB',(1800,1110),'#0e171c');draw=ImageDraw.Draw(image)
 text(draw,(30,20),'ACTUAL TERRAIN LOD / OBSERVER NEAR THE GROUND',30)
 text(draw,(30,64),f"Camera {low['global_camera'][1]:,.1f} m elevation / {low['camera_height_above_ground_m']:g} m above terrain / {len(low['tiles'])} of {low['tile_cap']} tiles / {low['pending_splits']} refinements waiting",20,'#b1c5c5')
 plot(image,(45,152,855,912),low,32000,'64 km overview / camera at center')
 plot(image,(945,152,1755,912),low,1000,'2 km close view / same selected leaves')
 legend(image,983);text(ImageDraw.Draw(image),(30,1080),'ACTUAL C# PLANNER OUTPUT / ANALYTIC FALLBACK, NOT DIFFUSION / OFFLINE DIAGRAM, NOT UNITY',15,'#9fb5b7')
 image.save(OUT/'lod-layout.png')
 image=Image.new('RGB',(1800,1110),'#0e171c');draw=ImageDraw.Draw(image)
 text(draw,(30,20),'ALTITUDE CHANGES THE SELECTED TERRAIN DETAIL',30)
 text(draw,(30,64),'Same field, position, tile cap and 32 km range; only camera altitude changes.',20,'#b1c5c5')
 plot(image,(45,152,855,912),low,16000,f"{low['camera_height_above_ground_m']:g} m above ground")
 plot(image,(945,152,1755,912),high,16000,f"{high['camera_height_above_ground_m']:g} m above ground")
 legend(image,983);text(ImageDraw.Draw(image),(30,1080),'ACTUAL C# PLANNER OUTPUT / 2:1 BALANCED LEAVES / OFFLINE DIAGRAM, NOT A UNITY CAPTURE',15,'#9fb5b7')
 image.save(OUT/'lod-altitude-comparison.png')
 report={'tool_sha256':sha(Path(__file__)),'inputs':{p:sha(OUT/p) for p in ['analytic-observer.json','analytic-high.json']},'images':{p:sha(OUT/p) for p in ['lod-layout.png','lod-altitude-comparison.png']},'counts':{'near_levels':dict(Counter(t['level'] for t in low['tiles'])),'high_levels':dict(Counter(t['level'] for t in high['tiles']))},'note':'Exact exported C# leaf rectangles; simple distance-ring labels added for inspection.'}
 (OUT/'lod-diagram-provenance.json').write_text(json.dumps(report,indent=2)+'\n')
 print('Wrote exact-leaf LOD layout and altitude comparison')
if __name__=='__main__':main()
