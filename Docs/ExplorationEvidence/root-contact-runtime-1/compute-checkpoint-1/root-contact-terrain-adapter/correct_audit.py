"""Dated correction; retain the earlier audit byte-for-byte as historical evidence."""
from pathlib import Path
import hashlib,json,datetime
HERE=Path(__file__).resolve().parent
AUDIT=HERE.parent/'root-contact-integration-audit'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
originals={}
for name in ['README.md','report.json','audit.py']:
    path=AUDIT/name;backup=AUDIT/(path.stem+'-before-terrain-correction'+path.suffix)
    if not backup.exists():backup.write_bytes(path.read_bytes())
    originals[str(backup)]=sha(backup)
now=datetime.datetime.now(datetime.timezone.utc).isoformat()
correction={
 'utc':now,
 'supersedes':'Prior audit description of a24m sampler halo and root use of full EvaluateColumn.',
 'facts':['Full SurfaceMaterialSampler.EvaluateColumn samples wind upstream/downstream at32m (SurfaceProfile.SampleRadiusMax=32), beyond the24m curvature samples.',
 'GroundHeightMeters is exactly BaseField.Sample(x,z).HeightMeters before material/snow derivation. RootTerrainSnapshot uses this bare height directly at global10cm centers plus existing SurfaceTop quantization.',
 'Preserve TerrainDetailField; remove outer SurfaceWorldField before snapshot capture. Bare adapter needs no external material-sampler halo. Provider-internal dependency footprints remain part of the bare-field revision contract.',
 'Immutable snapshot consistency does not certify current live source, template/context, resident owner or edit revision. The existing cache Current/final transaction gate must check all of them.'],
 'adapter_report':str(HERE/'report.json'),'adapter_report_sha256':sha(HERE/'report.json'),
 'earlier_audit_preserved_sha256':originals,
 'scope':'Correction only. Historical main source hashes/date remain those of the original audit; no claim of a refreshed current-main audit or installed adapter.'}
report=json.loads((AUDIT/'report-before-terrain-correction.json').read_text())
report['terrain_adapter_correction']=correction
report['proposed_data_interfaces']['RootTerrainSnapshot']='Use isolated RootTerrainSnapshot.TryCapture: unwrap outer SurfaceWorldField, capture bare IWorldFieldSnapshotProvider while preserving TerrainDetailField. Sample bare HeightMeters once at global10cm cell centers and apply existing VoxelTerrainPage.SurfaceTop. No material/snow evaluation or external32m sampler halo. Bounded rectangle/queries/identity; full live/template/context/owner current gate remains mandatory.'
report['freshness_and_edits']['global_revision_caveat']='ExplorationRefinementField.SourceIdentity increments globally when a distant patch publishes. Do not make active near collision disappear merely because an unrelated far revision changed. Cancel/retry uncommitted jobs globally while retaining old committed render+collision safely, or use region epochs covering the complete bare-provider dependency footprint. Full EvaluateColumn would require32m external halo; the direct bare-height adapter does not call it.'
(AUDIT/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
text=(AUDIT/'README-before-terrain-correction.md').read_text()
text=text.replace('Any later regional epoch scheme must cover the sampler\'s complete footprint, including relief queries up to 24m beyond root samples.',
 'Any later regional epoch scheme must cover the bare provider\'s complete dependency footprint. Full material-column sampling reaches 32m; the new direct bare-height adapter avoids that external halo.')
note='> Correction '+now+': the full material sampler reaches **32m**, not 24m. Roots need only its initial bare-height sample. The isolated `../root-contact-terrain-adapter` now verifies one exact global10cm-center query plus `SurfaceTop`, preserving terrain relief and excluding snow. See `report.json.terrain_adapter_correction`; earlier audit files are retained with `-before-terrain-correction` names. Original main hashes below remain a dated audit, not a new current-state verification.\n\n'
text=text.replace('# Root-contact production integration audit\n\n','# Root-contact production integration audit\n\n'+note)
(AUDIT/'README.md').write_text(text,encoding='utf-8')
source=(AUDIT/'audit-before-terrain-correction.py').read_text()
source=source.replace('Capture IWorldFieldSnapshotProvider when available (TerrainDetailField forwards it), wrap the captured bare field and unchanged SurfaceProfile. Sample global10cm cell centers; return VoxelTerrainPage.SurfaceTop(column.GroundHeightMeters), not renderedLOD/interpolated height or snow top.',report['proposed_data_interfaces']['RootTerrainSnapshot'])
source=source.replace('That footprint includes SurfaceMaterialSampler relief/climate queries (up to24m beyond sampled columns), not only the root X/Z box.','With full EvaluateColumn that footprint includes32m wind samples. The direct RootTerrainSnapshot bare-height adapter avoids the external material-sampler halo; preserved bare-provider dependencies still need correct revision coverage.')
(AUDIT/'audit.py').write_text(source,encoding='utf-8')
(HERE/'audit-correction.json').write_text(json.dumps(correction,indent=2),encoding='utf-8')
print(json.dumps({'corrected':str(AUDIT),'preserved_files':len(originals),'utc':now}))
