#!/usr/bin/env python3
"""Export/render five actual seeded specimens of each shared tree preset.

Keeps the existing frozen gallery intact. No Unity or AI image generation.
"""
from pathlib import Path
import hashlib,json,subprocess,sys,zipfile
from datetime import datetime,timezone
from PIL import Image,ImageDraw,ImageFont

ROOT=Path(__file__).resolve().parents[2]
HERE=Path(__file__).resolve().parent
BUILD=ROOT/'Tools/TreePreview/bin/seed-study'
OUT=ROOT/'Docs/TreeSeeds'
SPECIES=('birch','oak','redwood','pine')
FONT='/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf'

def font(size):
    try:return ImageFont.truetype(FONT,size)
    except OSError:return ImageFont.load_default()

def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()

def main():
    OUT.mkdir(parents=True,exist_ok=True)
    sys.path.insert(0,str(ROOT/'Tools/TreePreview'))
    import render_trees as exporter
    exporter.BUILD=BUILD;exporter.OUT=OUT
    exporter.compile_export(None,variations=5)
    (BUILD/'source-provenance.json').write_bytes((OUT/'provenance.json').read_bytes())
    samples=[]
    for species in SPECIES:
        for variation in range(5):
            name=f'{species}-{variation}'
            meta=json.loads((BUILD/(name+'.json')).read_text())
            if meta['truncated']:raise RuntimeError(name+': '+meta['truncation_reason'])
            if meta['seed']!=714103+variation*9173:raise RuntimeError('Unexpected seed')
            output=OUT/(name+'.png')
            subprocess.run([sys.executable,str(HERE/'render_pathtrace.py'),
                '--input-dir',str(BUILD),'--name',name,'--width','800','--height','1000',
                '--spp','64','--threads','6','--output',str(output)],check=True)
            report=HERE/'reports'/(name+'.json')
            saved_report=OUT/(name+'.json')
            saved_report.write_bytes(report.read_bytes())
            report.unlink()
            # Captions belong on the comparison sheets. Individual captures remain
            # exactly as hashed by the path-trace renderer.
            meta['export_sha256']=sha(BUILD/(name+'.bin'))
            meta['image_sha256']=sha(output)
            meta['render_report']=saved_report.name
            samples.append(meta)
    sheet_w=2500;tile_w=500;tile_h=680
    combined=Image.new('RGB',(sheet_w,tile_h*4),(14,20,24))
    for row,species in enumerate(SPECIES):
        sheet=Image.new('RGB',(sheet_w,tile_h),(14,20,24));draw=ImageDraw.Draw(sheet)
        draw.text((18,12),species.upper()+' / FIVE PROCEDURAL SEEDS / 10 CM VOXELS',font=font(23),fill=(232,235,224))
        for variation in range(5):
            name=f'{species}-{variation}';meta=next(m for m in samples if m['name']==name)
            im=Image.open(OUT/(name+'.png')).convert('RGB')
            im=im.crop((0,0,im.width,im.height-36));im.thumbnail((480,568),Image.Resampling.LANCZOS)
            x=variation*tile_w+(tile_w-im.width)//2
            sheet.paste(im,(x,52))
            height=meta['bounds_max_m'][1]-meta['center_m'][1]
            draw.text((variation*tile_w+20,628),f'Seed {meta["seed"]} / {height:.1f} m',font=font(18),fill=(206,219,210))
        draw.text((18,657),'ACTUAL GENERATED GEOMETRY / CPU PATH TRACE / NOT UNITY / PANELS INDIVIDUALLY FRAMED',font=font(13),fill=(191,165,112))
        sheet.save(OUT/(species+'-five-seeds.png'))
        combined.paste(sheet,(0,row*tile_h))
    combined.save(OUT/'all-trees-five-seeds.jpg',quality=94,subsampling=0)
    manifest={'created_utc':datetime.now(timezone.utc).isoformat(),
        'note':'Actual unchanged species presets, five different seeds each, 10 cm voxels; CPU path trace, not Unity.',
        'seeds':[714103+i*9173 for i in range(5)],'samples':samples,
        'tool_sha256':sha(Path(__file__))}
    (OUT/'samples.json').write_text(json.dumps(manifest,indent=2)+'\n')
    (OUT/'README.md').write_text('''# Five seeds of each procedural tree

Twenty individual800×1000 renders: five birches, five oaks, five mature coast
redwoods and five pines, using the same species presets and only changing seed.
Each species sheet labels seed and height. Panels are individually framed;
a redwood is much larger than a birch in real-world scale.

These images trace actual generated10 cm voxel geometry and stored colors with
Mitsuba CPU lighting. They are not Unity screenshots or AI-generated images.
All20 exports completed without truncation. `samples.json`, source provenance
and individual render reports record parameters, hashes and rendering settings.

Reproduce: `python3 Tools/TreePathtrace/render_seed_study.py` from project root.
Requires the dependencies listed in the TreePathtrace tool. Existing frozen
TreeGallery inputs and images are preserved.
''')
    # Verify the exact delivered images/reports before archiving.
    for meta in samples:
        report=json.loads((OUT/meta['render_report']).read_text())
        assert report['export_sha256']==meta['export_sha256']
        assert report['image_sha256']==sha(OUT/(meta['name']+'.png'))
        assert report['spp']==64 and Image.open(OUT/(meta['name']+'.png')).size==(800,1000)
        assert report['source_provenance']==json.loads((OUT/'provenance.json').read_text())
    archive=ROOT.parent/'AshenVoxel-Tree-Seeds.zip'
    with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED) as z:
        for path in sorted(OUT.iterdir()):
            if path.is_file():z.write(path,'TreeSeeds/'+path.name)
    with zipfile.ZipFile(archive) as z:assert z.testzip() is None
    print('PASS:20 untruncated actual specimens,20 render/image hashes, four species sheets; '+str(archive),flush=True)

if __name__=='__main__':main()
