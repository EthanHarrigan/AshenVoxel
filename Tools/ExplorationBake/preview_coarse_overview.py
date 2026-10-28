"""Render actual source height comparisons; these are not Unity screenshots."""
from pathlib import Path
import argparse
import sys
import numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.colors import LightSource, Normalize
from scipy.ndimage import map_coordinates
from coarse_overview import ROOT
from convert_world import read_avwf


def baseline_at(atlas, xs, zs):
    coords = np.meshgrid((zs-atlas['origin_z'])/atlas['extent_z']*(atlas['rows']-1),
                         (xs-atlas['origin_x'])/atlas['extent_x']*(atlas['columns']-1), indexing='ij')
    return map_coordinates(atlas['heights'], coords, order=1, prefilter=False, mode='nearest')


def compare(xs, zs, candidate, baseline, output, title):
    arrays = [baseline, candidate]
    lo, hi = min(float(h.min()) for h in arrays), max(float(h.max()) for h in arrays)
    norm = Normalize(vmin=lo, vmax=hi)
    light = LightSource(azdeg=315, altdeg=45)
    fig, axes = plt.subplots(1, 2, figsize=(14, 7), constrained_layout=True, facecolor='#101722')
    fig.suptitle(title, color='white', fontsize=19)
    for ax, h, label in zip(axes, arrays, ['Current 1,280 m source', 'Candidate 320 m learned overview']):
        rgba = light.shade(h, cmap=plt.get_cmap('terrain'), norm=norm,
                           dx=float(xs[1]-xs[0]), dy=float(zs[1]-zs[0]), vert_exag=1., blend_mode='soft')
        rgba[h<0,:3] = np.array([.07,.19,.29])
        ax.imshow(rgba, origin='lower', extent=[xs[0]/1000,xs[-1]/1000,zs[0]/1000,zs[-1]/1000],
                  interpolation='nearest', aspect='equal')
        ax.set_title(label, color='white', fontsize=14, pad=12)
        ax.set_xlabel('World X (km)', color='#cbd5e1')
        ax.set_ylabel('World Z (km)', color='#cbd5e1')
        ax.tick_params(colors='#cbd5e1')
        ax.set_facecolor('#101722')
    fig.supxlabel('Same area, elevation colors and lighting. Offline source-data preview; no renderer or LOD changes.',
                  color='#cbd5e1', fontsize=10)
    fig.savefig(output, dpi=150, facecolor=fig.get_facecolor())
    plt.close(fig)


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--input', type=Path, required=True)
    p.add_argument('--baseline', type=Path, default=ROOT/'Assets/AshenVoxel/Exploration/Samples/ExplorationMacro.bytes')
    p.add_argument('--output', type=Path, required=True)
    p.add_argument('--title', default='Learned terrain: 320 m overview test')
    a = p.parse_args()
    data = np.load(a.input, allow_pickle=False)
    xs, zs, h = data['x'], data['z'], data['height']
    a.output.parent.mkdir(parents=True, exist_ok=True)
    compare(xs, zs, h, baseline_at(read_avwf(a.baseline),xs,zs), a.output, a.title)


if __name__ == '__main__':
    main()
