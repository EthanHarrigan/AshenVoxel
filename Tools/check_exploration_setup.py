#!/usr/bin/env python3
"""Validate bundled exploration references/data; this does not launch Unity."""
from pathlib import Path
import datetime
import hashlib
import json
import math
import re
import struct

ROOT = Path(__file__).resolve().parents[1]
SETTINGS = ROOT / 'Assets/AshenVoxel/Settings/ExplorationSettings.asset'
SCENE = ROOT / 'Assets/Scenes/Exploration.unity'
SAMPLES = ROOT / 'Assets/AshenVoxel/Exploration/Samples'
PROTOTYPES = ROOT / 'Assets/AshenVoxel/Exploration/Prototypes/ExplorationPrototypes.bytes'


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def guid(path):
    return re.search(r'^guid: ([0-9a-f]{32})$',
                     Path(str(path) + '.meta').read_text(), re.M)[1]


def main():
    passed = []

    def check(condition, message):
        if not condition:
            raise AssertionError(message)
        passed.append(message)

    settings = SETTINGS.read_text()
    scene = SCENE.read_text()
    metadata_path = SAMPLES / 'exploration-world.json'
    metadata = json.loads(metadata_path.read_text())
    fields = dict(re.findall(r'^  (\w+): (.+)$', settings, re.M))
    settings_script = ROOT / 'Assets/AshenVoxel/Exploration/ExplorationSettings.cs'
    bootstrap = ROOT / 'Assets/AshenVoxel/Exploration/ExplorationBootstrap.cs'
    check(guid(settings_script) in fields['m_Script'], 'settings reference their actual ScriptableObject script')
    check(guid(bootstrap) in scene, 'scene references its actual exploration bootstrap')
    check(guid(SETTINGS) in scene, 'scene references the bundled settings asset')
    check('SceneRoots:' in scene and '- {fileID: 1001}' in scene,
          'scene has its bootstrap transform in the root list')
    bindings = {
        'MacroWorld': SAMPLES / 'ExplorationMacro.bytes',
        'MacroClimate': SAMPLES / 'ExplorationMacro.climate.bytes',
        'FineWorld': SAMPLES / 'ExplorationFine.bytes',
        'FineClimate': SAMPLES / 'ExplorationFine.climate.bytes',
        'SourceBiomes': SAMPLES / 'ExplorationBiomes.bytes',
        'WorldMetadata': metadata_path,
        'PrototypePack': PROTOTYPES,
    }
    for field, path in bindings.items():
        check(path.is_file() and path.stat().st_size > 0 and guid(path) in fields[field]
              and 'fileID: 4900000' in fields[field], field + ' resolves to a nonempty bundled TextAsset')
    for stage in ('macro', 'fine'):
        data = metadata[stage]
        for kind in ('height', 'climate'):
            path = SAMPLES / data[kind + '_file']
            check(sha(path) == data[kind + '_sha256'], stage + ' ' + kind + ' data match metadata SHA-256')
        check(data['source_identity']['world_id'] == metadata['world_id'], stage + ' shares the declared world identity')
    check(sha(SAMPLES / metadata['source_biomes']['file']) == metadata['source_biomes']['sha256'],
          'source biome data match metadata SHA-256')
    check(sha(SAMPLES / metadata['authored_map']['file']) == metadata['authored_map']['sha256'],
          'authored Azgaar source matches metadata SHA-256')
    check(float(fields['WorldWidthMeters']) == metadata['world_width_m']
          and float(fields['WorldDepthMeters']) == metadata['world_depth_m'],
          'default settings use the imported world dimensions')
    check(fields['UseImportedDimensions'] == '1' and fields['UseRecommendedSpawn'] == '1',
          'initial launch uses imported dimensions and the validated recommended spawn')
    check(fields['EnableLiveRefinement'] == '0', 'initial launch does not require the optional Python service')
    check(20 <= float(fields['ViewDistanceKm']) <= 40, 'initial terrain LOD view is between 20 and 40 km')
    check(float(fields['MinimumTerrainTileMeters']) / int(fields['NearTerrainQuads']) <= 1,
          'nearest terrain grid stays at or below one meter')
    fine = metadata['fine']
    spawn = metadata['recommended_spawn']
    inset = float(fields['FineBlendMeters'])
    check(all(math.isfinite(float(spawn[name])) for name in ('x_m', 'y_m', 'z_m'))
          and fine['origin_x_m'] + inset < spawn['x_m'] < fine['origin_x_m'] + fine['extent_x_m'] - inset
          and fine['origin_z_m'] + inset < spawn['z_m'] < fine['origin_z_m'] + fine['extent_z_m'] - inset,
          'recommended spawn lies inside the decoded fine region beyond its blend border')
    check(abs(spawn['x_m']) < metadata['world_width_m'] / 2
          and abs(spawn['z_m']) < metadata['world_depth_m'] / 2,
          'recommended spawn lies within the full imported map')
    header = PROTOTYPES.read_bytes()[:12]
    check(header[:8] == b'AVPM0004' and struct.unpack('<I', header[8:12])[0] == 99,
          'bundled prototype pack has the final exact-quad format and all 99 models')
    editor = ROOT / 'Assets/AshenVoxel/Exploration/Editor/ExplorationProjectSetup.cs'
    legacy = ROOT / 'Assets/AshenVoxel/Editor/ProjectSetup.cs'
    check('File.Exists("Assets/Scenes/Exploration.unity")' in legacy.read_text(),
          'legacy automatic setup defers to the new bundled scene')
    referenced = [SETTINGS, SCENE, settings_script, bootstrap, editor, legacy,
                  *bindings.values(), SAMPLES / metadata['authored_map']['file']]
    report = {
        'status': 'PASS', 'checks': len(passed),
        'generated_utc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'scope': 'Static scene/settings GUID bindings, imported metadata and data integrity; no native Unity asset deserialization or playtest',
        'passed': passed,
        'world_dimensions_m': [metadata['world_width_m'], metadata['world_depth_m']],
        'default_view_distance_km': float(fields['ViewDistanceKm']),
        'prototype_count': 99,
        'source_sha256': {str(path.relative_to(ROOT)): sha(path) for path in dict.fromkeys(referenced)},
    }
    (ROOT / 'Docs/exploration-setup-validation.json').write_text(json.dumps(report, indent=2) + '\n')
    print('PASS', len(passed), 'exploration asset/data checks; native Editor startup remains unverified')


if __name__ == '__main__':
    main()
