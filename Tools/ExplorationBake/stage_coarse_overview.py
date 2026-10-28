"""Prepare an existing decoded world with a separately identified baked overview.

Writes a staging folder only. Installation keeps existing Unity asset GUIDs.
"""
from pathlib import Path
import argparse
import copy
import json
import shutil
from coarse_overview import ROOT, atomic_json, sha
from bake_coarse_overview import read_climate
from convert_world import read_avwf


def portable(value):
    if isinstance(value, dict):
        return {k: portable(v) for k, v in value.items() if k != 'path'}
    if isinstance(value, list):
        return [portable(v) for v in value]
    return value


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--bake', type=Path, required=True)
    p.add_argument('--source', type=Path, default=ROOT/'Assets/AshenVoxel/Exploration/Samples')
    p.add_argument('--output', type=Path, required=True)
    a = p.parse_args()
    if a.output.exists():
        raise FileExistsError('Use a fresh staging folder')
    identity = json.loads((a.bake/'identity.json').read_text())
    result = json.loads((a.bake/'result.json').read_text())
    old = json.loads((a.source/'exploration-world.json').read_text(encoding='utf-8-sig'))
    assert result['status'] == 'PASS' and result['bake_id'] == identity['bake_id']
    assert old['world_id'] == identity['baseline_world_id']
    assert sha(a.source/'ExplorationMacro.bytes') == identity['baseline_height_sha256']
    assert sha(a.source/'ExplorationMacro.climate.bytes') == identity['baseline_climate_sha256']
    for name, file in result['files'].items():
        assert sha(a.bake/name) == file['sha256']
    height = read_avwf(a.bake/'Coarse320.bytes')
    climate = read_climate(a.bake/'Coarse320.climate.bytes', a.bake/'Coarse320.bytes')
    job = json.loads((a.bake/'job.json').read_text())
    metadata = copy.deepcopy(old)
    metadata.update(world_id=identity['bake_id'], detail_world_id=old['world_id'],
        generator_version='learned-lowfreq-overview-v1',
        composition_policy='overview-with-original-decoded-detail-v1',
        detail_native_model_world_id=old['native_model_world_id'],
        native_model_world_id=identity['native_job_world_id'],
        detail_native_job_spec=old['native_job_spec'], native_job_spec=portable(job['spec']),
        detail_producer_sha256=old['producer_sha256'],
        producer_sha256={**identity['source_sha256'], 'stage_coarse_overview.py': sha(Path(__file__))},
        elapsed_seconds=result['sum_chunk_generation_seconds'],
        timing_scope='Cumulative overview generation-call time, excludes reloads and memory waits; retained fine data was not regenerated',
        scope='Learned 320m low-frequency overview plus unchanged original CPU decoded 5m patch and matching optional detail service',
        derived_publication='Overview bytes copied exactly from the validated bake; fine and biome payloads retain their original identities. No LOD or renderer changes.')
    metadata['macro'] = dict(height_file='ExplorationMacro.bytes', climate_file='ExplorationMacro.climate.bytes',
        height_sha256=sha(a.bake/'Coarse320.bytes'), climate_sha256=sha(a.bake/'Coarse320.climate.bytes'),
        columns=height['columns'], rows=height['rows'], origin_x_m=height['origin_x'], origin_z_m=height['origin_z'],
        extent_x_m=height['extent_x'], extent_z_m=height['extent_z'], spacing_m=320.,
        height_range_m=result['height_range_m'], source_identity=json.loads(height['source_identity']),
        climate_ranges=[[float(v.min()), float(v.max())] for v in climate], climate_source_spacing_m=1280.,
        baseline_height_sha256=identity['baseline_height_sha256'], baseline_climate_sha256=identity['baseline_climate_sha256'])
    a.output.mkdir(parents=True)
    for file in a.source.iterdir():
        if file.suffix in ('.bytes', '.json'):
            shutil.copyfile(file, a.output/file.name)
    shutil.copyfile(a.bake/'Coarse320.bytes', a.output/'ExplorationMacro.bytes')
    shutil.copyfile(a.bake/'Coarse320.climate.bytes', a.output/'ExplorationMacro.climate.bytes')
    atomic_json(a.output/'exploration-world.json', metadata)
    print('PASS staged320m overview with original decoded/biome identity:', a.output)


if __name__ == '__main__':
    main()
