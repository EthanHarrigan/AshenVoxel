"""Validate a finished overview and compare actual source heights, without Unity."""
from pathlib import Path
import argparse
import json
import numpy as np
from scipy.ndimage import gaussian_filter
import rasterio

from coarse_overview import ROOT, atomic_json, sha
from convert_world import read_avwf
from preview_coarse_overview import baseline_at, compare


def error_metrics(candidate, reference):
    delta = candidate.astype('f8') - reference.astype('f8')
    return {'mae_m': float(np.mean(np.abs(delta))),
            'rmse_m': float(np.sqrt(np.mean(delta * delta))),
            'bias_m': float(np.mean(delta)),
            'max_absolute_m': float(np.max(np.abs(delta)))}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    folder, out = args.input, args.output
    out.mkdir(parents=True, exist_ok=True)
    result = json.loads((folder / 'result.json').read_text())
    identity = json.loads((folder / 'identity.json').read_text())
    assert result['status'] == 'PASS'
    for name, record in result['files'].items():
        assert sha(folder / name) == record['sha256'], name
    height = read_avwf(folder / 'Coarse320.bytes')
    source = ROOT / 'Assets/AshenVoxel/Exploration/Samples'
    baseline = read_avwf(source / 'ExplorationMacro.bytes')
    fine = read_avwf(source / 'ExplorationFine.bytes')
    assert sha(source / 'ExplorationMacro.bytes') == identity['baseline_height_sha256']
    with np.load(folder / 'overview.npz', allow_pickle=False) as data:
        xs, zs, h = data['x'], data['z'], data['height']
        assert np.array_equal(h, height['heights'])
        assert np.array_equal(data['land'], h >= 0)
    blocks, seconds = [], []
    for r in range(0, len(zs), identity['block_nodes']):
        for c in range(0, len(xs), identity['block_nodes']):
            path = folder / 'blocks' / f'{r}_{c}.npz'
            saved = json.loads(path.with_suffix('.json').read_text())
            assert saved['bake_id'] == identity['bake_id']
            assert saved['sha256'] == sha(path)
            with np.load(path, allow_pickle=False) as data:
                block = data['height']
                assert np.array_equal(h[r:r+block.shape[0], c:c+block.shape[1]], block)
            blocks.append(saved)
            seconds.append(saved['generation_seconds'])
    with rasterio.open(folder / 'Coarse320-height.tif') as tif:
        # GDAL can emit an unnamed LOCAL_CS when writing the Point raster key.
        # Only local world coordinates are allowed; never a geographic/map CRS.
        tiff_crs = tif.crs.to_wkt() if tif.crs else None
        assert tif.crs is None or (tiff_crs.startswith('LOCAL_CS[') and
                                   not tif.crs.is_geographic and not tif.crs.is_projected)
        assert tif.xy(0, 0) == (xs[0], zs[-1])
        assert tif.xy(len(zs)-1, len(xs)-1) == (xs[-1], zs[0])
        assert np.array_equal(np.flipud(tif.read(1)), h)
    old = baseline_at(baseline, xs, zs)
    compare(xs, zs, h, old, out / 'whole-world.png', 'Whole-world baked source: 299.52 x 149.76 km')
    # Fixed region used by the earlier probe. Read the completed bake independently.
    xi = (xs >= -51200) & (xs <= -31040)
    zi = (zs >= -25600) & (zs <= -5440)
    compare(xs[xi], zs[zi], h[np.ix_(zi, xi)], old[np.ix_(zi, xi)],
            out / 'valley-comparison.png', '320 m baked terrain: ridges and valleys')

    # Compare against the pre-existing fully decoded 5m patch at the same scale.
    # The 480m margin covers the finite Gaussian support; no padded edges enter.
    margin = 480.
    fx = xs[(xs >= fine['origin_x']+margin) &
            (xs <= fine['origin_x']+fine['extent_x']-margin)]
    fz = zs[(zs >= fine['origin_z']+margin) &
            (zs <= fine['origin_z']+fine['extent_z']-margin)]
    step_x = fine['extent_x']/(fine['columns']-1)
    step_z = fine['extent_z']/(fine['rows']-1)
    filtered_fine = dict(fine)
    filtered_fine['heights'] = gaussian_filter(fine['heights'].astype('f8'),
                                             sigma=(160/step_z, 160/step_x),
                                             truncate=3., mode='nearest')
    reference = baseline_at(filtered_fine, fx, fz)
    comparison = {
        'scope': 'One existing decoded 5m patch, Gaussian sigma160m then sampled at 320m; 480m edge margin',
        'caveat': 'Reference is the existing CPU/runtime world; candidate has a separate GPU/batch identity. This is not whole-world visual acceptance.',
        'reference_sha256': sha(source / 'ExplorationFine.bytes'),
        'sample_count': int(reference.size), 'columns': len(fx), 'rows': len(fz),
        'x_range_m': [float(fx[0]), float(fx[-1])],
        'z_range_m': [float(fz[0]), float(fz[-1])],
        'baseline_1280m': error_metrics(baseline_at(baseline, fx, fz), reference),
        'candidate_320m': error_metrics(baseline_at(height, fx, fz), reference)}
    comparison['rmse_reduction_fraction'] = 1 - comparison['candidate_320m']['rmse_m'] / comparison['baseline_1280m']['rmse_m']
    # Boundary statistics are descriptive; terrain slope is not a seam oracle.
    boundary = np.concatenate([np.abs(np.diff(h, axis=0))[63::64].ravel(),
                               np.abs(np.diff(h, axis=1))[:, 63::64].ravel()])
    all_steps = np.concatenate([np.abs(np.diff(h, axis=0)).ravel(), np.abs(np.diff(h, axis=1)).ravel()])
    data = {'status': 'PASS', 'scope': __doc__, 'bake_id': identity['bake_id'],
            'checks': ['published file hashes', 'all 120 completed blocks match final atlas',
                       'NPZ/atlas exact heights and land mask', 'TIFF exact heights and signed coordinates'],
            'blocks': len(blocks), 'tiff_local_crs_wkt': tiff_crs,
            'timing_seconds': {'total_generation': float(sum(seconds)),
                'median_block': float(np.median(seconds)), 'slowest_block': float(max(seconds)),
                'fastest_block': float(min(seconds))},
            'fine_patch_comparison': comparison,
            'whole_world': {'difference_from_baseline': error_metrics(h, old),
                'baseline_land_fraction': float(np.mean(old >= 0)),
                'candidate_land_fraction': float(np.mean(h >= 0)),
                'changed_land_water_fraction': float(np.mean((h >= 0) != (old >= 0))),
                'adjacent_height_step_median_m': float(np.median(all_steps)),
                'block_boundary_height_step_median_m': float(np.median(boundary)),
                'boundary_metric_scope': 'Descriptive only; synthetic filter partition invariance is checked separately'},
            'source_sha256': {name: sha(Path(__file__).with_name(name)) for name in
                ('evaluate_coarse_overview.py', 'preview_coarse_overview.py')}}
    atomic_json(out / 'evaluation.json', data)
    print(json.dumps(data, indent=2))


if __name__ == '__main__':
    main()
