#!/usr/bin/env python3
"""Compose actual-data full-height and basal-bark renders with scale captions."""
from pathlib import Path
import json
from PIL import Image,ImageDraw,ImageFont

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Docs/TreeGallery'
FONT='/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf'

def font(size):
    try:return ImageFont.truetype(FONT,size)
    except OSError:return ImageFont.load_default()

def main():
    meta=json.loads((ROOT/'Tools/TreePathtrace/inputs/redwood-0.json').read_text())
    h=meta['bounds_max_m'][1]-meta['center_m'][1]
    canvas=Image.new('RGB',(1600,1120),(14,20,24));draw=ImageDraw.Draw(canvas)
    draw.text((30,20),'COAST REDWOOD / MATURE PROPORTIONS',font=font(29),fill=(225,232,223))
    draw.text((30,64),f'Generated height {h:.1f} m / 10 cm material cells / actual stored geometry and colors',font=font(18),fill=(158,181,180))
    for image_name,box in [('pathtraced-redwood.png',(20,108,600,990)),('pathtraced-redwood-base.png',(620,108,1580,990))]:
        image=Image.open(OUT/image_name).convert('RGB')
        image=image.crop((0,0,image.width,image.height-36))
        if image_name=='pathtraced-redwood.png':
            # The tall specimen occupies the middle of the normal portrait.
            # Crop only empty side background so both panels can use their height.
            margin=int(image.width*.2);image=image.crop((margin,0,image.width-margin,image.height))
        image.thumbnail((box[2]-box[0],box[3]-box[1]),Image.Resampling.LANCZOS)
        x=box[0]+(box[2]-box[0]-image.width)//2;y=box[1]+(box[3]-box[1]-image.height)//2
        canvas.paste(image,(x,y))
    draw.text((30,1008),'Dominant leader / tall lifted crown',font=font(18),fill=(211,223,210))
    draw.text((645,1008),'Fluted buttresses / irregular vertical bark ridges',font=font(18),fill=(211,223,210))
    draw.text((30,1067),'OFFLINE CPU PATH TRACE / NOT UNITY / NO REPLACEMENT TREE MESH OR PAINTED BARK',font=font(16),fill=(199,168,116))
    canvas.save(OUT/'redwood-study.png')
    print(OUT/'redwood-study.png')

if __name__=='__main__':main()
