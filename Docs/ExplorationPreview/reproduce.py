#!/usr/bin/env python3
"""Reproduce current rock-pigment previews with existing exact-cell tools, not Unity."""
from pathlib import Path
import datetime
import hashlib
import importlib.util
import json
import os
import re
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
OUT = Path(__file__).resolve().parent
BUILD = ROOT / 'Tools/GroundCoverPreview/bin/exploration-appearance-v2'
NAMES = ['shape-angularboulder', 'shape-slab', 'shape-wedge',
         'shape-spire', 'shape-splitoutcrop', 'shape-cluster']


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def export():
    BUILD.mkdir(parents=True, exist_ok=True)
    candidates = [Path(os.environ['DOTNET_ROOT']) / 'dotnet' if os.environ.get('DOTNET_ROOT') else None,
                  Path(shutil.which('dotnet')) if shutil.which('dotnet') else None,
                  ROOT.parent / '.dotnet/dotnet']
    dotnet = next(path.resolve() for path in candidates if path and path.is_file())
    version = lambda path: tuple(int(part) if part.isdigit() else 0 for part in path.name.split('.'))
    sdk = sorted((dotnet.parent / 'sdk').glob('8.*'), key=version)[-1]
    refs = sorted((dotnet.parent / 'packs/Microsoft.NETCore.App.Ref').glob('8.*'), key=version)[-1] / 'ref/net8.0'
    files = sorted((ROOT / 'Assets/AshenVoxel/Core').rglob('*.cs')) + [ROOT / 'Tools/GroundCoverPreview/ExportGroundCover.cs']
    hashes = {str(path.relative_to(ROOT)): sha(path) for path in files}
    snapshots = []
    for path in files:
        destination = BUILD / 'source' / path.relative_to(ROOT)
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(path.read_bytes())
        snapshots.append(destination)
    assert hashes == {str(path.relative_to(ROOT)): sha(path) for path in files}, 'Source changed during snapshot'
    assembly = BUILD / 'ExportGroundCover.dll'
    quote = lambda path: '"' + str(path) + '"'
    options = ['-nologo', '-warnaserror+', '-langversion:9.0', '-optimize+', '-target:exe', '-out:' + quote(assembly)]
    options += ['-r:' + quote(path) for path in sorted(refs.glob('*.dll'))] + [quote(path) for path in snapshots]
    response = BUILD / 'compile.rsp'
    response.write_text('\n'.join(options))
    assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions': {'tfm': 'net8.0', 'framework': {'name': 'Microsoft.NETCore.App', 'version': '8.0.0'}}}))
    subprocess.run([str(dotnet), str(sdk / 'Roslyn/bincore/csc.dll'), '@' + str(response)], check=True)
    subprocess.run([str(dotnet), str(assembly), str(BUILD), 'shapes'], check=True)
    source = (BUILD / 'source/Assets/AshenVoxel/Core/Rocks/RockGenerator.cs').read_text()
    geometry = int(re.search(r'const int Version\s*=\s*(\d+)', source)[1])
    appearance = int(re.search(r'const int AppearanceVersion\s*=\s*(\d+)', source)[1])
    assert (geometry, appearance) == (2, 2), 'This folder describes geometry2/appearance2; create a new folder for later versions'
    for name in NAMES:
        path = BUILD / (name + '.json')
        meta = json.loads(path.read_text())
        assert all('/appearance-v2' in item['profile']['Identity'] for item in meta['scene']['specimens'])
        meta['generation_version']['rock_appearance'] = appearance
        meta['appearance_metadata_annotation'] = 'AppearanceVersion read from the compiled source snapshot; original exporter does not emit this separate field.'
        path.write_text(json.dumps(meta, indent=2) + '\n')
        shutil.copyfile(path, OUT / path.name)
    proof = {'created_utc': datetime.datetime.now(datetime.timezone.utc).isoformat(), 'sdk': sdk.name,
             'source_sha256': hashes, 'geometry_version': geometry, 'appearance_version': appearance, 'mode': 'shapes',
             'note': 'Current actual C# occupied10cm voxels and stored colors. Presentation pads are authored fixtures. Offline preview, not Unity or reference-color calibration.',
             'exports': {path.name: sha(path) for path in sorted(BUILD.glob('shape-*')) if path.suffix in {'.bin', '.json'}}}
    (BUILD / 'source-provenance.json').write_text(json.dumps(proof, indent=2) + '\n')
    (OUT / 'source-provenance.json').write_text(json.dumps(proof, indent=2) + '\n')
    return proof


def compose(proof):
    from PIL import Image, ImageDraw, ImageFont
    source = ROOT / 'Tools/RockShapePreview/package_shapes.py'
    spec = importlib.util.spec_from_file_location('shape_packager', source)
    packager = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(packager)
    panels = [OUT / (name + '.png') for name in NAMES]
    sheet = packager.sheet(panels, OUT / 'rocks-appearance-v2-contact.png',
                          'CURRENT ROCKS / APPEARANCE 2 / GEOMETRY 2',
                          'OFFLINE CPU LIGHTING / NOT UNITY / same Granite, six shapes, three seeds / exact 10 cm cells')
    image = Image.open(panels[0]).convert('RGB')
    # An explicitly enlarged crop of the middle generated specimen, not new geometry.
    crop = image.crop((390, 170, 660, 415)).resize((540, 490), Image.Resampling.NEAREST)
    board = Image.new('RGB', (640, 690), (13, 20, 25))
    board.paste(crop, (50, 74))
    draw = ImageDraw.Draw(board)
    font_path = '/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf'
    try:
        font = ImageFont.truetype(font_path, 15)
    except OSError:
        font = ImageFont.load_default()
    draw.text((20, 12), 'APPEARANCE 2 / GRANITE / SEED 271828', font=font, fill=(229, 234, 221))
    draw.text((20, 38), 'OFFLINE / NOT UNITY / enlarged image crop', font=font, fill=(215, 182, 119))
    meta = json.loads((OUT / 'shape-angularboulder.json').read_text())
    palette = meta['scene']['specimens'][1]['palette']['Granite']
    assert len(palette) <= 6
    for index, color in enumerate(palette):
        x = 50 + index * 90
        draw.rectangle((x, 578, x + 89, 620), fill=((color >> 16) & 255, (color >> 8) & 255, color & 255))
    draw.text((20, 632), 'Stored sRGB palette above; lighting changes faces.', font=font, fill=(190, 205, 195))
    draw.text((20, 656), 'No claim of matching the inaccessible reference clip.', font=font, fill=(190, 205, 195))
    detail = OUT / 'granite-palette-detail.png'
    board.save(detail)
    reports = [json.loads(path.with_suffix('.report.json').read_text()) for path in panels]
    for path, report in zip(panels, reports):
        assert sha(path) == report['image_sha256']
        assert sha(BUILD / (report['export_name'] + '.bin')) == report['input_sha256']
        assert report['metadata']['generation_version']['rock_appearance'] == 2
        assert report['spp'] == 64 and report['threads'] == 2
    assert all(sha(ROOT / name) == value for name, value in proof['source_sha256'].items()), 'Source changed during rendering'
    manifest = {'status': 'PASS', 'generated_utc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
                'scope': 'Actual Core geometry2/appearance2 data, offline Mitsuba CPU lighting. Not native Unity, FPS or Avroid color calibration.',
                'voxel_m': .1, 'material': 'Granite', 'seeds': [714103, 271828, 161803],
                'source_provenance': proof, 'contact_sheet': sheet,
                'detail': {'image': str(detail.relative_to(ROOT)), 'image_sha256': sha(detail),
                           'source_image': str(panels[0].relative_to(ROOT)), 'crop_pixels_xyxy': [390, 170, 660, 415],
                           'display_scale': 2, 'stored_srgb_palette': ['#%06X' % (color & 0xffffff) for color in palette]},
                'render_reports': [str(path.with_suffix('.report.json').relative_to(ROOT)) for path in panels],
                'tool_sha256': {str(path.relative_to(ROOT)): sha(path) for path in
                                (Path(__file__), source, ROOT / 'Tools/GroundCoverPreview/render_ground_cover.py', ROOT / 'Tools/TreePathtrace/render_pathtrace.py')}}
    (OUT / 'preview-validation.json').write_text(json.dumps(manifest, indent=2) + '\n')
    print('PASS current appearance2/geometry2 actual-data rock preview:', OUT)


if __name__ == '__main__':
    proof = export()
    subprocess.run([sys.executable, str(ROOT / 'Tools/GroundCoverPreview/render_ground_cover.py'), *NAMES,
                    '--spp', '64', '--threads', '2', '--input-dir', str(BUILD), '--output-dir', str(OUT)], check=True)
    compose(proof)
