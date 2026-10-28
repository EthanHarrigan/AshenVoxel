#!/usr/bin/env python3
"""Plot actual AVWF neural output; no Unity or synthesized replacement terrain."""
from pathlib import Path
import argparse,hashlib,json,sys
import numpy as np
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'Tools/WorldScaleImport'))
from convert_world import read_avwf

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('atlas',type=Path);p.add_argument('output',type=Path);a=p.parse_args()
    d=read_avwf(a.atlas);h=d['heights'];identity=json.loads(d['source_identity'])
    if not identity.get('backend','').startswith('upstream-pretrained'):raise ValueError('Expected actual pretrained output identity')
    import matplotlib
    matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    from matplotlib.colors import LightSource
    plt.style.use('dark_background')
    x=np.linspace(0,d['extent_x']/1000,d['columns']);z=np.linspace(0,d['extent_z']/1000,d['rows']);xx,zz=np.meshgrid(x,z)
    fig=plt.figure(figsize=(12,6.5),facecolor='#10171b');ax=fig.add_subplot(121,projection='3d');ax.set_facecolor('#10171b')
    rgb=LightSource(azdeg=315,altdeg=40).shade(h,cmap=plt.get_cmap('gist_earth'),vert_exag=1,
        dx=d['extent_x']/(d['columns']-1),dy=d['extent_z']/(d['rows']-1),blend_mode='soft')
    ax.plot_surface(xx,zz,h/1000,facecolors=rgb,linewidth=0,antialiased=True,shade=False)
    ax.set(xlabel='x / km',ylabel='z / km',zlabel='Elevation / km');ax.set_title('Learned terrain / true physical proportions',fontsize=12)
    ax.set_box_aspect((d['extent_x']/1000,d['extent_z']/1000,max(.1,float(np.ptp(h))/1000)))
    ax.view_init(32,-60)
    bx=fig.add_subplot(122);bx.set_facecolor('#10171b');im=bx.imshow(h,extent=(0,d['extent_x']/1000,d['extent_z']/1000,0),cmap='gist_earth')
    bx.set(xlabel='x / km',ylabel='z / km',title='Actual float-meter output / native30 m samples');fig.colorbar(im,ax=bx,shrink=.65,label='Elevation / m')
    fig.suptitle('CETEVILES → CONDITIONING → INFINITEDIFFUSION',fontsize=17,y=.97)
    fig.text(.5,.06,f"{h.shape[1]}×{h.shape[0]} samples / {float(h.min()):,.1f}–{float(h.max()):,.1f} m / seed {identity['seed']}",ha='center',fontsize=11)
    fig.text(.5,.02,'ACTUAL PRETRAINED OUTPUT / CPU DATA VISUALIZATION / NOT A UNITY SCREENSHOT',ha='center',fontsize=10,color='#d7bd89')
    fig.tight_layout(rect=(0,.09,1,.93));a.output.parent.mkdir(parents=True,exist_ok=True);fig.savefig(a.output,dpi=150,facecolor=fig.get_facecolor());plt.close(fig)
    a.output.with_suffix('.json').write_text(json.dumps({'input_sha256':hashlib.sha256(a.atlas.read_bytes()).hexdigest(),
        'image_sha256':hashlib.sha256(a.output.read_bytes()).hexdigest(),'tool_sha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        'identity':identity,'note':'Actual neural AVWF diagnostic; not Unity or terrain appearance benchmark.'},indent=2)+'\n')
    print(a.output)
if __name__=='__main__':main()
