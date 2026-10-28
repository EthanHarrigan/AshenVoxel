"""Package a validated standalone source map; never installs it into Unity."""
from pathlib import Path
import argparse
import hashlib
import json
import zipfile

from coarse_overview import atomic_json, sha


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input', type=Path, required=True)
    parser.add_argument('--evidence', type=Path, required=True)
    args = parser.parse_args()
    source, evidence = args.input, args.evidence
    result = json.loads((source / 'result.json').read_text())
    evaluation = json.loads((evidence / 'evaluation.json').read_text())
    consumer = json.loads((evidence / 'consumer.json').read_text())
    assert result['status'] == evaluation['status'] == consumer['status'] == 'PASS'
    assert result['bake_id'] == evaluation['bake_id']
    assert result['files']['Coarse320.bytes']['sha256'] == consumer['heightSha256']
    names = ['Coarse320.bytes', 'Coarse320.climate.bytes', 'Coarse320-height.tif']
    for name in names:
        assert sha(source / name) == result['files'][name]['sha256']
    readme = f'''320 M BAKED COARSE MAP - learned-lowfreq-overview-v1

937 columns x 469 rows, covering 299520 x 149760 world meters.
Node spacing: 320 meters. Height values: meters, sea level = 0.
Origin: X=-149760, Z=-74880. Maximum: X=149760, Z=74880.
Bake ID: {result['bake_id']}

Coarse320.bytes: AVWF0001 height, source biome and land-mask atlas.
Coarse320.climate.bytes: AVCL0001 climate paired to the exact height SHA256.
Coarse320-height.tif: float32 elevation. Local X/Z meters; no geographic CRS.
TIFF row zero is maximum Z; AVWF row zero is minimum Z.
identity.json / result.json: source identities, hashes, size and bake timings.
evaluation.json / consumer.json: actual-data and project C# reader checks.

This is learned intermediate elevation from the pinned InfiniteDiffusion
coarse/base stages, filtered to 320m. The fine residual decoder is omitted.
It is not an upscaled copy of the old 1280m map. Climate retains 1280m source
fidelity; temperature follows the new elevation using the original lapse rate.
The GPU/batch-derived identity differs from the current playable CPU world.

These files are standalone source data. Reading them requires no model,
GPU inference or LOD bake. They do not replace your current project assets.
Import/in-game integration, fine-patch blending and performance acceptance
are separate work. Do not rename this as the existing CPU world identity.
'''
    payload = {name: (source / name).read_bytes() for name in names}
    payload.update({name: (source / name).read_bytes() for name in ('identity.json', 'result.json')})
    payload.update({name: (evidence / name).read_bytes() for name in ('evaluation.json', 'consumer.json')})
    payload['README.txt'] = readme.encode('utf-8')
    path = evidence / 'Coarse320Map.zip'
    temporary = path.with_suffix('.tmp.zip')
    with zipfile.ZipFile(temporary, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for name, content in payload.items():
            archive.writestr(name, content)
    with zipfile.ZipFile(temporary) as archive:
        assert archive.testzip() is None
        assert set(archive.namelist()) == set(payload)
        for name, content in payload.items():
            assert archive.read(name) == content
    temporary.replace(path)
    atomic_json(evidence / 'download.json', {'status': 'PASS', 'bake_id': result['bake_id'],
        'archive': path.name, 'bytes': path.stat().st_size, 'sha256': sha(path),
        'members': {name: {'bytes': len(content), 'sha256': hashlib.sha256(content).hexdigest()}
                    for name, content in payload.items()}})
    print('PASS standalone map package:', path, path.stat().st_size, 'bytes')


if __name__ == '__main__':
    main()
