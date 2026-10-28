"""Engine-free coordinate/filter contract checks; not model quality evidence."""
from pathlib import Path
import argparse
import json
import tempfile
import numpy as np
from coarse_overview import (atomic_json, latent_coordinate, sample_overview,
                             VERSION, sha)
from convert_world import read_avwf, write_avwf


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--output',type=Path,required=True)
    a=p.parse_args()
    checks=[]
    def check(name, condition):
        if not condition: raise AssertionError(name)
        checks.append(name)
    def provider(function):
        def query(r0,r1,c0,c1):
            x,z=np.meshgrid(np.arange(c0,c1)*40+17.5,np.arange(r0,r1)*40+17.5)
            h=function(x,z)
            return np.sign(h)*np.sqrt(np.abs(h))
        return query
    check('upstream half-pixel registration at signed coordinates',
          np.array_equal(latent_coordinate(np.array([-320.,0.,320.])),[-8.4375,-.4375,7.5625]))
    xs=np.arange(-10,11)*320.;zs=np.arange(-7,8)*320.
    linear=sample_overview(provider(lambda x,z: 2000+.4*x+.3*z),xs,zs)
    expected=2000+.4*xs[None,:]+.3*zs[:,None]
    check('physical-meter affine field survives filter including negative heights',np.max(np.abs(linear-expected))<.001)
    q=provider(lambda x,z: 1000+300*np.sin(x/700)+120*np.cos(z/830))
    whole=sample_overview(q,xs,zs)
    partition=np.concatenate([sample_overview(q,xs[:6],zs),sample_overview(q,xs[6:13],zs),sample_overview(q,xs[13:],zs)],axis=1)
    check('finite halo output independent of column partition',np.array_equal(whole,partition))
    rows=np.concatenate([sample_overview(q,xs,zs[:4]),sample_overview(q,xs,zs[4:])],axis=0)
    check('finite halo output independent of row partition',np.array_equal(whole,rows))
    for bad in (5.,79.,81.,1281.):
        try: sample_overview(q,xs,zs,bad)
        except ValueError: checks.append('reject unsupported spacing '+str(bad))
        else: raise AssertionError('accepted invalid spacing')
    with tempfile.TemporaryDirectory() as d:
        path=Path(d)/'overview.bytes';biomes=np.ones_like(whole,dtype='u1')
        write_avwf(path,whole,biomes,whole>=0,float(xs[0]),float(zs[0]),float(xs[-1]-xs[0]),float(zs[-1]-zs[0]),{'generator':VERSION})
        back=read_avwf(path)
        check('existing atlas format round-trip and extents',np.array_equal(back['heights'],whole) and back['extent_x']==xs[-1]-xs[0])
        raw=path.read_bytes();path.write_bytes(raw[:-7])
        try: read_avwf(path)
        except ValueError: checks.append('truncated atlas rejected')
        else: raise AssertionError('truncated atlas accepted')
        damaged=bytearray(raw);damaged[-12]^=1;path.write_bytes(damaged)
        try: read_avwf(path)
        except ValueError: checks.append('corrupt atlas rejected')
        else: raise AssertionError('corrupt atlas accepted')
    a.output.parent.mkdir(parents=True,exist_ok=True)
    atomic_json(a.output,{'status':'PASS','scope':__doc__,'checks':checks,
                'source_sha256':{p.name:sha(p) for p in (Path(__file__),Path(__file__).with_name('coarse_overview.py'))}})
    print('PASS',len(checks),'overview coordinate/filter/format checks')


if __name__=='__main__':main()
