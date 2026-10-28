#!/usr/bin/env python3
"""Cross-check optional compiled preview traversal against NumPy and boundary fixtures."""
import numpy as np
import render_trees as r

def main():
    v=np.zeros((7,9,8),dtype=np.uint8);v[1:4,2:6,2:5]=1
    rng=np.random.default_rng(517)
    origins=rng.uniform([-3,-3,-3],[10,12,10],(10000,3)).astype(np.float32)
    rays=rng.normal(size=(10000,3)).astype(np.float32);rays/=np.linalg.norm(rays,axis=1)[:,None]
    a=r._numpy_trace(v,origins,rays);b=r.trace(v,origins,rays)
    assert np.array_equal(a[0],b[0]),'Hit masks differ'
    ids=a[0]
    assert np.array_equal(a[1][ids],b[1][ids]),'Hit cells differ'
    assert np.array_equal(a[2][ids],b[2][ids]),'Normals differ'
    assert np.allclose(a[4][ids],b[4][ids],atol=1e-4),'Distances differ'
    # An exact diagonal passes through the corner and never enters a side-contact cell.
    for trace in [r._numpy_trace,r.trace]:
        side=np.zeros((4,4,4),dtype=np.uint8);side[0,0,1]=1
        hit,*_=trace(side,[.5,.5,.5],np.array([[1,1,1]],dtype=np.float32)/np.sqrt(3))
        assert not hit[0],'Corner contact incorrectly counted as entering a cell'
        side[1,1,1]=1
        hit,cell,n,p,t=trace(side,[.5,.5,.5],np.array([[1,1,1]],dtype=np.float32)/np.sqrt(3))
        assert hit[0] and np.array_equal(cell[0],[1,1,1]),'Diagonal cell missed'
        hit,*_=trace(v,[1.5,1.5,-1],np.array([[0,0,1.]],dtype=np.float32),maxdist=1)
        assert not hit[0],'Maximum reach ignored'
    print(f'PASS:10,000 random noncubic rays ({ids.sum()} hits), hit cells/normals/distances agree.')
    print('PASS: exact tied-axis corner traversal and maximum reach in both backends.')
    print('Backend:', 'Numba parallel' if r.trace is not r._numpy_trace else 'NumPy fallback (Numba not installed).')
if __name__=='__main__':main()
