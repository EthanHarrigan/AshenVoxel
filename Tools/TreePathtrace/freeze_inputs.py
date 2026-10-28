#!/usr/bin/env python3
"""Freeze the final actual-data exports so a lighting study survives later generator changes."""
from pathlib import Path
import hashlib,json
from datetime import datetime,timezone
ROOT=Path(__file__).resolve().parents[2]
source=ROOT/'Tools/TreePreview/bin';target=Path(__file__).resolve().parent/'inputs'
target.mkdir(parents=True,exist_ok=True)
provenance=(ROOT/'Docs/TreeGallery/provenance.json').read_bytes()
capture=json.loads(provenance)
if capture.get('export_species','all')!='all' or capture.get('export_variations',3)!=3:
    raise RuntimeError('Refusing to freeze a partial tree export. Complete the unfiltered gallery export first.')
files={}
for name in ('birch-0','oak-fruit-0','redwood-0','pine-0'):
    for ext in ('.bin','.json'):
        data=(source/(name+ext)).read_bytes()
        (target/(name+ext)).write_bytes(data)
        files[name+ext]=hashlib.sha256(data).hexdigest()
if provenance!=(ROOT/'Docs/TreeGallery/provenance.json').read_bytes():
    raise RuntimeError('Export provenance changed during capture; retry after export completes')
(target/'source-provenance.json').write_bytes(provenance)
(target/'manifest.json').write_text(json.dumps({'captured_utc':datetime.now(timezone.utc).isoformat(),'note':'Immutable actual generated voxel snapshots for CPU lighting study; use explicit --input-dir to render these historical inputs.','sha256':files,'source_provenance_sha256':hashlib.sha256(provenance).hexdigest()},indent=2))
print('Frozen final actual-data samples:',', '.join(files))
