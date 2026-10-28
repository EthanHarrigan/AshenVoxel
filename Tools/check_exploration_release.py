#!/usr/bin/env python3
"""Audit current exploration evidence and exact official-API source coverage.

This does not rerun behavior tests or native Unity. It rejects stale evidence
before packaging and writes an inventory of the completed checks and limits.
"""
from pathlib import Path
import datetime
import hashlib
import json
import re

ROOT = Path(__file__).resolve().parents[1]
REPORTS = [
    'Docs/exploration-field-validation.json',
    'Docs/exploration-population-validation.json',
    'Docs/exploration-streaming-validation.json',
    'Docs/exploration-movement-validation.json',
    'Docs/exploration-smoke-validation.json',
    'Docs/exploration-live-validation.json',
    'Docs/world-explorer-validation.json',
    'Docs/exploration-setup-validation.json',
    'Docs/shading-validation.json',
    'Docs/exploration-world-survey.json',
    'Docs/ExplorationEvidence/real-unity-api-validation.json',
    'Docs/ExplorationEvidence/real-unity-editor-api-validation.json',
]
HEX = re.compile(r'^[0-9a-f]{64}$')
PREFIXES = ('Assets/', 'Tools/', 'ProjectSettings/', 'Packages/', 'Docs/')


def current_hash(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def recorded_files(value):
    if isinstance(value, dict):
        for name, item in value.items():
            if isinstance(item, str) and HEX.fullmatch(item) and name.startswith(PREFIXES):
                yield name, item
            else:
                yield from recorded_files(item)
    elif isinstance(value, list):
        for item in value:
            yield from recorded_files(item)


def main():
    inventory = []
    audited = 0
    api_sources = set()
    for name in REPORTS:
        report = json.loads((ROOT / name).read_text())
        if report.get('status') != 'PASS':
            raise RuntimeError('Evidence report is not PASS: ' + name)
        count = 0
        for relative, expected in recorded_files(report):
            path = ROOT / relative
            if not path.is_file() or current_hash(path) != expected:
                raise RuntimeError('Stale/missing evidence: ' + name + ' -> ' + relative)
            count += 1
            if 'real-unity-' in name and relative.endswith('.cs'):
                api_sources.add(relative)
        audited += count
        inventory.append({'path': name, 'sha256': current_hash(ROOT / name),
                          'checks': report.get('checks'), 'audited_file_entries': count,
                          'scope': report.get('scope')})
    actual_sources = {p.relative_to(ROOT).as_posix() for p in (ROOT / 'Assets').rglob('*.cs')}
    if api_sources != actual_sources:
        raise RuntimeError('Official-API source coverage differs: ' +
                           repr(sorted(actual_sources.symmetric_difference(api_sources))))
    result = {
        'status': 'PASS',
        'generated_utc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'scope': 'Release evidence freshness/coverage audit, not a new behavior test or native Unity run',
        'audited_file_hash_entries': audited,
        'official_api_current_source_files': len(api_sources),
        'reports': inventory,
        'native_editor_import': 'NOT EXECUTED',
        'native_shader_compilation': 'NOT EXECUTED',
        'native_play_mode_and_gpu_performance': 'NOT EXECUTED',
        'windows_launcher': 'NOT EXECUTED',
        'terrain_fidelity': 'Actual1280m learned coarse whole world;5m decoded5115m starter;optional guarded live5m;near0.5-1m height proxies;10cm props/local matter',
    }
    output = ROOT / 'Docs/exploration-release-validation.json'
    output.write_text(json.dumps(result, indent=2) + '\n')
    print(f'PASS release audit: {audited} current file-hash entries; all{len(api_sources)} project C# sources covered by official API compilation')


if __name__ == '__main__':
    main()
