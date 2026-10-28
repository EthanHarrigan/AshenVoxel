#!/usr/bin/env python3
"""Verify and package actual implementation visualizations for review."""
from pathlib import Path
import hashlib,json,zipfile,shutil
from PIL import Image,ImageDraw,ImageFont
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'Docs/WorldViews'
def font(n):
    try:return ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf',n)
    except OSError:return ImageFont.load_default()
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
    provenance=json.loads((OUT/'provenance.json').read_text())
    for n,h in provenance['source_sha256'].items():assert sha(ROOT/n)==h,n+' changed'
    for n,h in provenance['asset_sha256'].items():assert sha(ROOT/n)==h,n+' changed'
    names=['analytic-observer-view','analytic-observer-lod','analytic-high-view','ceteviles-observer-view','neural-study-view']
    reports=[]
    for src,name in [('Ceteviles-import-diagnostic.png','imported-map-and-biomes.png'),('Ceteviles-neural-region.png','learned-heightfield-data.png')]:
        shutil.copyfile(ROOT/'Docs/WorldScaleEvidence'/src,OUT/name)
    for n in names:
        report=json.loads((OUT/(n+'.report.json')).read_text())
        assert sha(ROOT/report['image'])==report['image_sha256']
        assert report['spp']==64
        data_name=n.removesuffix('-view').removesuffix('-lod')
        assert sha(OUT/(data_name+'.json'))==report['metadata_sha256']
        assert sha(ROOT/'Tools/WorldViews/bin'/(data_name+'.bin'))==report['binary_sha256']
        assert sha(ROOT/'Tools/WorldViews/render_views.py')==report['tool_sha256']
        reports.append(report)
    a=Image.open(OUT/'analytic-observer-view.png').convert('RGB');b=Image.open(OUT/'analytic-observer-lod.png').convert('RGB')
    board=Image.new('RGB',(a.width*2,a.height+108),(13,20,25));board.paste(a,(0,0));board.paste(b,(a.width,0));d=ImageDraw.Draw(board)
    palette=json.loads((OUT/'lod-palette.json').read_text())
    sw=board.width//13
    for level,color in enumerate(palette['rgb_hex']):
        x=level*sw;d.rectangle((x+12,a.height+12,x+sw-8,a.height+34),fill=color)
        spacing=palette['spacing_m'][level]
        d.text((x+12,a.height+43),f'L{level} / {spacing:g} m samples',font=font(14),fill=(213,223,215))
    d.text((22,a.height+80),'Same actual geometry and camera / colors at right identify LOD / offline lighting, not Unity',font=font(16),fill=(204,178,120))
    board.save(OUT/'terrain-lod-comparison.png')
    contact=Image.new('RGB',(2200,1600),(13,20,25))
    for i,n in enumerate(['analytic-observer-view','analytic-observer-lod','analytic-high-view','neural-study-view']):
        im=Image.open(OUT/(n+'.png')).convert('RGB');im.thumbnail((1080,780),Image.Resampling.LANCZOS)
        contact.paste(im,((i%2)*1100+(1100-im.width)//2,(i//2)*800+(800-im.height)//2))
    contact.save(OUT/'worldgen-and-lods.jpg',quality=94,subsampling=0)
    (OUT/'README.md').write_text('''# Actual world generation and LOD visuals

The five rendered views use exact meshes produced by the C# WorldExplorer
builder, with the real fields, planner, vertex colors and triangle topology.
They are offline CPU lighting renders, not Unity screenshots or GPU benchmarks.
No trees, decorations, replacement mountains or concept art were added.

- `terrain-lod-comparison.png`: the same mountain view with normal runtime
  colors and diagnostic LOD colors. It uses the analytic fallback, not diffusion.
- `lod-layout.png`: actual selected leaves over64 km and a2 km close view.
- `lod-altitude-comparison.png`: actual plans6 m and6000 m above the same summit.
- `analytic-high-view.png`: the higher observation view.
- `ceteviles-observer-view.png`: the imported authored macro field before
  neural refinement. Its large plateaus/low-detail appearance are real limits
  of that rough field, not final environment art.
- `neural-study-view.png`: the actual Ceteviles-conditioned33×33 learned
  region,960 m across at30 m native sampling. This finite top-surface study
  omits boundary skirts and is not a generated32 km neural landscape.
- `imported-map-and-biomes.png` and `learned-heightfield-data.png`: source-map
  and actual learned-field diagnostics from the executed integration.

Each height-proxy tile has32 quads per side. LOD0 samples every2 m; coarser
levels double that spacing. Authoritative editable matter remains10 cm; these
images do not demonstrate a complete100 m microvoxel ring. The planner cap is
384 tiles; captions expose the actual selected count and pending refinements.
Camera positions were chosen through the same sampler for clear observation,
rather than using the default coarse spawn search. They are reachable by the
implemented free-flight camera.

Offline illumination uses Mitsuba CPU path tracing, geometric face normals and
illustrative aerial perspective. Unity's exported smooth normals remain in the
data but are not used for this lighting study, so facets can be more visible.
It does not execute the URP shader or establish Unity appearance.
Source/binary/image hashes and camera settings are recorded alongside images.

Reproduce with the installed .NET8 SDK, NumPy/Pillow/Mitsuba dependencies:

```sh
python3 Tools/WorldViews/export_views.py
python3 Tools/WorldViews/lod_diagrams.py
python3 Tools/WorldViews/render_views.py analytic-observer
python3 Tools/WorldViews/render_views.py analytic-observer --lod
python3 Tools/WorldViews/render_views.py analytic-high
python3 Tools/WorldViews/render_views.py ceteviles-observer
python3 Tools/WorldViews/render_views.py neural-study
python3 Tools/WorldViews/package_views.py
```
''')
    manifest={'note':'Actual implementation geometry and planner; offline, not Unity.',
        'images':{p.name:sha(p) for p in OUT.iterdir() if p.suffix.lower() in {'.png','.jpg'}},
        'render_reports':reports,'source_provenance':provenance}
    (OUT/'images-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
    archive=ROOT.parent/'AshenVoxel-Worldgen-LOD-Images.zip'
    with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED) as z:
        for p in sorted(OUT.iterdir()):
            if p.is_file():z.write(p,'WorldViews/'+p.name)
        for p in sorted((ROOT/'Tools/WorldViews').iterdir()):
            if p.is_file() and p.suffix in {'.py','.cs'}:z.write(p,'Tools/WorldViews/'+p.name)
    with zipfile.ZipFile(archive) as z:assert z.testzip() is None
    print('PASS:5 actual mesh renders, source/asset/binary/image hashes; '+str(archive))
if __name__=='__main__':main()
