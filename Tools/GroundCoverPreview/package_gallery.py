#!/usr/bin/env python3
"""Verify plant/rock data provenance and compose actual-data contact sheets."""
from pathlib import Path
import hashlib,json
from PIL import Image,ImageDraw,ImageFont
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'Docs/GroundCoverGallery';BUILD=ROOT/'Tools/GroundCoverPreview/bin'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def font(n):
 try:return ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf',n)
 except OSError:return ImageFont.load_default()
def sheet(names,name,title,subtitle,columns=2):
 thumb_width=900;images=[]
 for n in names:
  im=Image.open(OUT/(n+'.png')).convert('RGB');im.thumbnail((thumb_width,550),Image.Resampling.LANCZOS);images.append(im)
 row_height=max(im.height for im in images)+14;rows=(len(images)+columns-1)//columns
 board=Image.new('RGB',(columns*(thumb_width+16)+16,108+row_height*rows),(13,20,25));d=ImageDraw.Draw(board)
 d.text((24,17),title,font=font(28),fill=(231,236,222));d.text((24,57),subtitle,font=font(16),fill=(167,189,181))
 for i,im in enumerate(images):board.paste(im,(16+(i%columns)*(thumb_width+16),99+(i//columns)*row_height))
 board.save(OUT/(name+'.png'));return {'image':name+'.png','sha256':sha(OUT/(name+'.png')),'inputs':names}
def main():
 provenance=json.loads((OUT/'provenance.json').read_text());reports=[]
 for name,h in provenance['source_sha256'].items():assert sha(ROOT/name)==h,'Source changed after gallery export: '+name
 for name,h in provenance['exports'].items():assert sha(BUILD/name)==h,'Export changed: '+name
 for p in sorted(OUT.glob('*.report.json')):
  r=json.loads(p.read_text());assert r['spp']>=32 and sha(ROOT/r['image'])==r['image_sha256']
  assert sha(BUILD/(p.name.replace('.report.json','.bin')))==r['input_sha256']
  assert sha(ROOT/'Tools/GroundCoverPreview/render_ground_cover.py')==r['tool_sha256']
  assert sha(ROOT/'Tools/TreePathtrace/render_pathtrace.py')==r['geometry_tool_sha256'];reports.append(r)
 plants=['plant-fern','plant-shrub','plant-grasstuft','plant-reed','plant-broadleafherb','plant-flower','plant-mushroom','plant-cactus','plant-succulent','plant-cushion']
 rocks=['rock-granite','rock-limestone','rock-sandstone','rock-shale','rock-stone']
 sheets=[sheet(plants,'plants-comparison','PROCEDURAL GROUND PLANTS / THREE SEEDS EACH','Actual 10 cm matter / shared Leaves, Wood, Fruit; separate Fungus and Petal / offline lighting / not Unity'),
  sheet(rocks,'rocks-comparison','WEATHERED VOXEL ROCKS / THREE SEEDS EACH','Actual 10 cm occupied faces / coherent six-shade pigments / static procedural shapes / offline lighting / not Unity')]
 sheets.append(sheet(['plant-fern','plant-shrub','plant-mushroom','plant-succulent','rock-granite','rock-sandstone'],'groundcover-comparison','GROUND COVER + WEATHERED ROCKS / ACTUAL 10 CM VOXELS','Seeded C# generators / exact occupied faces / offline CPU render / not Unity'))
 (OUT/'images-manifest.json').write_text(json.dumps({'reports':reports,'sheets':sheets},indent=2)+'\n')
 (OUT/'README.md').write_text('''# Procedural ground-cover and rock mockups

![Selected actual-data examples](groundcover-comparison.png)

These images render the exact occupied 10 cm cells and saved opaque material
colors emitted by the actual C# PlantGenerator and RockGenerator. Each species
and rock habit is shown with three recorded seeds. The procedural plant shapes
are not hand-painted sprites or replacement polygon models.

- [Ten plant kinds, three seeds each](plants-comparison.png)
- [Five weathered rock habits, three seeds each](rocks-comparison.png)
- [Six explicit rock shape controls and exact footprints](../RockShapeGallery/README.md)
- [One-cell mushroom, herb and flower examples](micro-plants.png)
- [Optional berry shrub using the same shrub generator](berry-shrub.png)
- [Forest-floor composition](forest-floor.png)
- [Dry-ground composition](dry-floor.png)

Ferns, grass, reeds, herbs, shrubs, cactus and succulents reuse the same Leaves
material as trees; woody shrub axes use Wood, berries use Fruit. Mushroom tissue
uses distinct Fungus. Flower petals use Petal. Individual plants use a bounded
small palette. Rocks use coherent restrained shades of their parent rock.
The 10 cm grid represents aggregate small leaves/needles and stylized mushroom
features, not anatomically resolved submillimeter plant parts.

Rock version2 selects genuinely different polygon/plane assemblies rather than
variations of an oval cap. The five material panels use Auto shape selection;
the separate six-shape study fixes all specimens to Granite to isolate shape.

The two floor views are authored arrangements of actual procedural specimens
on a controlled voxel floor, not an evaluation of world ecology or neural
terrain. Weathering is static shape construction, not erosion simulation.

Mitsuba CPU path tracing supplies only the camera, sky and area light. These
are not Unity screenshots, URP shader validation or player performance evidence.
No image generator, painted geometry or hidden high-resolution plant mesh was
used. Each dataset, profile, source snapshot, image and render tool has a hash.

Reproduce from project root: run Tools/GroundCoverPreview/export_ground_cover.py,
then render_ground_cover.py with 32 or 64 spp, then package_gallery.py. C# uses
the installed .NET 8 SDK without restore; rendering additionally uses Mitsuba,
NumPy and Pillow. Exported binaries and compiled assemblies are temporary under
Tools/GroundCoverPreview/bin and are excluded from project delivery archives.
''')
 print('PASS actual-data gallery provenance,',len(reports),'renders and',len(sheets),'contact sheets')
if __name__=='__main__':main()
