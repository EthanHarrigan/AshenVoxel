"""Offline view of exact exported voxel faces; not a Unity screenshot."""
from pathlib import Path
import hashlib,json
import numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from mpl_toolkits.mplot3d.art3d import Poly3DCollection

root=Path(__file__).resolve().parents[2];out=root/'Docs/VoxelTerrainEvidence'
fig=plt.figure(figsize=(12,5),facecolor='#161b20')
for index,(name,title) in enumerate([('actual-spawn','Actual learned terrain near spawn'),('layered-cliff-fixture','Controlled cliff fixture: exposed strata')]):
    path=out/(name+'.json');data=json.loads(path.read_text());polygons=[];colors=[]
    for face in data['faces']:
        axis=face['Axis'];p=np.array([face['X'],face['Y'],face['Z']],float)
        if face['Positive']:p[axis]+=1
        u=np.eye(3)[(axis+1)%3];v=np.eye(3)[(axis+2)%3]
        polygons.append(np.array([p,p+u,p+u+v,p+v])[:,[0,2,1]]*.1)
        tint=face['Tint'];rgb=np.array([(tint>>16)&255,(tint>>8)&255,tint&255])/255
        n=np.eye(3)[axis]*(1 if face['Positive'] else -1);light=np.array([-.35,.85,-.4]);light/=np.linalg.norm(light)
        ao=sum(face['AO'+str(i)] for i in range(4))/(4*255)
        colors.append(np.clip(rgb*(.45+.55*max(0,n@light))*(.5+.5*ao),0,1))
    ax=fig.add_subplot(1,2,index+1,projection='3d',facecolor='#161b20')
    ax.add_collection3d(Poly3DCollection(polygons,facecolors=colors,edgecolors='none'))
    points=np.concatenate(polygons);lo=points.min(axis=0);hi=points.max(axis=0)
    ax.set_xlim(lo[0],hi[0]);ax.set_ylim(lo[1],hi[1]);ax.set_zlim(lo[2],hi[2]);ax.set_box_aspect(np.maximum(hi-lo,.2))
    ax.view_init(elev=28,azim=-55);ax.set_axis_off();ax.set_title(title,color='#e6e9ed',fontsize=11,pad=10)
fig.suptitle('10 cm material voxels — offline render of exact conversion output',color='white',fontsize=15)
fig.text(.5,.025,'Existing material pigments • cubic geometry • preview lighting only • not native Unity',ha='center',color='#b6c0c9',fontsize=10)
fig.subplots_adjust(left=0,right=1,bottom=.08,top=.85,wspace=0)
destination=out/'preview.png';fig.savefig(destination,dpi=150,facecolor=fig.get_facecolor());plt.close(fig)
provenance={'scope':'Offline exact-face preview, not Unity, actual learned spawn page versus explicitly labelled cliff fixture',
            'inputs':{name:hashlib.sha256((out/(name+'.json')).read_bytes()).hexdigest() for name in ['actual-spawn','layered-cliff-fixture']},
            'renderer_sha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),'image_sha256':hashlib.sha256(destination.read_bytes()).hexdigest()}
(out/'preview-provenance.json').write_text(json.dumps(provenance,indent=2)+'\n')
print(destination)
