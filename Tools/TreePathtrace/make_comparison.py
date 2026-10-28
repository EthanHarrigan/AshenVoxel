#!/usr/bin/env python3
"""Compose four real CPU path-traced samples; this does not generate or modify tree geometry."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
ROOT=Path(__file__).resolve().parents[2];out=ROOT/'Docs/TreeGallery'
fontpath='/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf'
try:
 title=ImageFont.truetype(fontpath,24);small=ImageFont.truetype(fontpath,15)
except OSError:title=small=ImageFont.load_default()
canvas=Image.new('RGB',(1280,1500),(15,20,25));draw=ImageDraw.Draw(canvas)
draw.text((26,17),'GENERATED VOXEL TREES / CPU PATH TRACE / NOT UNITY',font=title,fill=(222,229,227))
draw.text((26,50),'Actual stored 10 cm voxel geometry and colors / 64 samples per pixel / multibounce lighting study',font=small,fill=(152,170,176))
for i,name in enumerate(('birch','oak','redwood','pine')):
 image=Image.open(out/f'pathtraced-{name}.png').convert('RGB')
 # Remove the per-image footer before fitting; this sheet carries its own explicit label.
 image=image.crop((0,0,image.width,image.height-36));image.thumbnail((620,650),Image.Resampling.LANCZOS)
 x=20+(i%2)*640;y=102+(i//2)*690
 canvas.paste(image,(x+(620-image.width)//2,y));draw.text((x+12,y+654),name.upper(),font=title,fill=(213,222,218))
canvas.save(out/'pathtraced-comparison.png');print(out/'pathtraced-comparison.png')
