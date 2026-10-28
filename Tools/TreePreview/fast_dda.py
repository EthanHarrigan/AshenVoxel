"""Optional Numba acceleration for the exact-cell offline preview traversal.
No Unity dependency. The NumPy implementation remains the default fallback.
"""
import os
import numpy as np
from numba import njit,prange,set_num_threads
set_num_threads(min(8,os.cpu_count() or 1))

@njit(parallel=True,cache=True)
def _trace(vol,o,d,maxdist):
    count=len(d);sx=vol.shape[2];sy=vol.shape[1];sz=vol.shape[0]
    hit=np.zeros(count,np.bool_);cell=np.zeros((count,3),np.int32)
    normal=np.zeros((count,3),np.float32);dist=np.zeros(count,np.float32)
    for i in prange(count):
        enter=0.0;leave=float(maxdist);entry_axis=-1;valid=True
        for axis in range(3):
            limit=sx if axis==0 else sy if axis==1 else sz
            dv=d[i,axis];ov=o[i,axis]
            if abs(dv)<1e-8:
                if ov<0 or ov>=limit:valid=False;break
            else:
                a=-ov/dv;b=(limit-ov)/dv
                near=min(a,b);far=max(a,b)
                if near>enter:enter=near;entry_axis=axis
                leave=min(leave,far)
        if not valid or enter>leave:continue
        x=int(np.floor(o[i,0]+d[i,0]*(enter+.001)))
        y=int(np.floor(o[i,1]+d[i,1]*(enter+.001)))
        z=int(np.floor(o[i,2]+d[i,2]*(enter+.001)))
        x=min(max(x,0),sx-1);y=min(max(y,0),sy-1);z=min(max(z,0),sz-1)
        stepx=1 if d[i,0]>0 else -1 if d[i,0]<0 else 0
        stepy=1 if d[i,1]>0 else -1 if d[i,1]<0 else 0
        stepz=1 if d[i,2]>0 else -1 if d[i,2]<0 else 0
        invx=1/d[i,0] if abs(d[i,0])>1e-8 else 1e20
        invy=1/d[i,1] if abs(d[i,1])>1e-8 else 1e20
        invz=1/d[i,2] if abs(d[i,2])>1e-8 else 1e20
        tx=((x+(stepx>0))-o[i,0])*invx if stepx else 1e20
        ty=((y+(stepy>0))-o[i,1])*invy if stepy else 1e20
        tz=((z+(stepz>0))-o[i,2])*invz if stepz else 1e20
        nx=0.0;ny=0.0;nz=0.0
        if entry_axis==0:nx=-stepx
        if entry_axis==1:ny=-stepy
        if entry_axis==2:nz=-stepz
        distance=enter
        for iteration in range(sx+sy+sz+5):
            if vol[z,y,x]>0:
                hit[i]=True;cell[i,0]=x;cell[i,1]=y;cell[i,2]=z
                normal[i,0]=nx;normal[i,1]=ny;normal[i,2]=nz;dist[i]=distance
                break
            distance=min(tx,min(ty,tz))
            if tx<=distance+1e-6:nx=-stepx;ny=0;nz=0
            elif ty<=distance+1e-6:nx=0;ny=-stepy;nz=0
            else:nx=0;ny=0;nz=-stepz
            if tx<=distance+1e-6:tx+=abs(invx);x+=stepx
            if ty<=distance+1e-6:ty+=abs(invy);y+=stepy
            if tz<=distance+1e-6:tz+=abs(invz);z+=stepz
            if distance>leave or x<0 or y<0 or z<0 or x>=sx or y>=sy or z>=sz:break
    return hit,cell,normal,dist

def trace(vol,origins,directions,maxdist=3000):
    d=np.ascontiguousarray(directions,dtype=np.float32)
    o=np.ascontiguousarray(np.broadcast_to(np.asarray(origins,dtype=np.float32),(len(d),3)))
    hit,cell,n,dist=_trace(vol,o,d,float(maxdist))
    return hit,cell,n,o+d*dist[:,None],dist
