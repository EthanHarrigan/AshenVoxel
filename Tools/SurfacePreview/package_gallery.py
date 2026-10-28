#!/usr/bin/env python3
"""Verify and compose actual-data surface examples for review."""
from pathlib import Path
import hashlib,json
from PIL import Image,ImageDraw,ImageFont
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'Docs/SurfaceGallery'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def font(n):
    try:return ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf',n)
    except OSError:return ImageFont.load_default()
def main():
    names=['alpine','forest','pale-shore','black-shore','arid'];sources=json.loads((OUT/'provenance.json').read_text());reports=[]
    for n,h in sources['source_sha256'].items():assert sha(ROOT/n)==h,n+' changed after export'
    for name in names:
        r=json.loads((OUT/(name+'.report.json')).read_text());assert r['spp']==64 and sha(ROOT/r['image'])==r['image_sha256']
        assert sha(ROOT/'Tools/SurfacePreview/bin'/(name+'.bin'))==r['input_sha256']
        assert r['tool_sha256']==sha(ROOT/'Tools/SurfacePreview/render_surfaces.py')
        reports.append(r)
    canvas=Image.new('RGB',(1800,1480),(13,20,25));draw=ImageDraw.Draw(canvas)
    draw.text((25,16),'SURFACE MATERIALS / ACTUAL10CM VOXEL DATA',font=font(27),fill=(234,237,226))
    draw.text((25,54),'Controlled terrain fixtures of the real sampler / offline CPU renders / not Unity',font=font(17),fill=(166,188,185))
    for i,name in enumerate(names):
        im=Image.open(OUT/(name+'.png')).convert('RGB');im.thumbnail((585,665),Image.Resampling.LANCZOS)
        x=(i%3)*600+(600-im.width)//2;y=100+(i//3)*680;canvas.paste(im,(x,y))
    draw.text((1220,900),'REAL GENERATED MATTER',font=font(22),fill=(226,232,217))
    draw.text((1220,950),'Snow thickness changes geometry.',font=font(17),fill=(178,198,190))
    draw.text((1220,990),'Biomes and geology control layers.',font=font(17),fill=(178,198,190))
    draw.text((1220,1030),'Shore water is an optical study.',font=font(17),fill=(178,198,190))
    canvas.save(OUT/'surface-comparison.png')
    (OUT/'images-manifest.json').write_text(json.dumps({'reports':reports,'comparison_sha256':sha(OUT/'surface-comparison.png')},indent=2)+'\n')
    (OUT/'README.md').write_text('''# Actual surface-material examples

![Five generated material examples](surface-comparison.png)

Full images: [alpine snow](alpine.png), [forest cutaway](forest.png),
[pale shore](pale-shore.png), [volcanic shore](black-shore.png),
[arid sandstone](arid.png).

These are controlled synthetic terrain fields and artist profiles passed through
the actual shared C# SurfaceMaterialSampler, then materialized as10cm cells with
VoxelWorld.Set. They are not Ceteviles or neural terrain outputs. No concept-art
generator, replacement mountains, vegetation or painted texture is used.

The five fixtures show alpine snow and exposed granite, a forest soil cutaway,
pale sediment and gravel on limestone shores, black volcanic sand and basalt,
and an arid sandstone cutaway. Snow depth is actual positive-volume Snow cells.
Forest cutaways expose grass/litter/soil/bedrock from the real layer order.

The renderer traces exact exposed cell faces and saved opaque material colors.
Mitsuba CPU path tracing, sky and light are presentation fixtures. Shore images
add a separate flat dielectric surface at mean sea level as an optical study;
this is not the Unity water shader or fluid simulation. The actual URP shader,
underwater effect and player performance remain separate native verification.

Each image and dataset has recorded hashes, physical dimensions, material
counts, snow depth/coverage statistics, camera and source/profile provenance.
Temporary datasets/assemblies live underTools/SurfacePreview/bin and are not
project assets. Run export_surfaces.py, then render_surfaces.py for each named
fixture, and finally package_gallery.py to reproduce from project root.
''')
    print('PASS:5 actual-data images, source/bin/image hashes; comparison saved',OUT/'surface-comparison.png')
if __name__=='__main__':main()
