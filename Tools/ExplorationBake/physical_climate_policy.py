"""Versioned physical publication policy; unchanged original model is the source.

This adapter is placed BEFORE canonical owner encoding. Original upstream
climate can contain negative physical seasonality/precipitation/CV, which are
finite model predictions but cannot satisfy our public physical contract.
Only those negative values become zero; every other value remains bit exact.
"""
from pathlib import Path
import copy,hashlib,json,os,threading
import numpy as np

POLICY_ID='physical-nonnegative-v1'
POLICY={'schema':'ashen-physical-climate-policy-v1','policy_id':POLICY_ID,
        'input_encoding':'physical-si-v2','negative_to_zero_channels':
        ['temperature_std_c','precipitation_mm','precipitation_cv_percent'],
        'unchanged':'height,temperature_c,beta,all nonnegative climate values,biomes,land',
        'nonfinite_and_upper_bounds':'reject; never silently saturate',
        'cache_namespace':POLICY_ID}

def rectify(climate):
    values=np.array(climate,dtype=np.float32,copy=True)
    if values.ndim!=3 or values.shape[0]!=5 or not np.isfinite(values).all():
        raise ValueError('Physical climate policy requires five finite planes')
    counts={}
    for index,name in ((1,'temperature_std_c'),(2,'precipitation_mm'),(3,'precipitation_cv_percent')):
        negative=values[index]<0;counts[name]=int(negative.sum());values[index][negative]=0
    return values,counts

class PhysicalClimateBackend:
    def __init__(self,backend,encoding,record_path=None):
        if encoding!=POLICY['input_encoding']:
            raise ValueError('Versioned physical climate policy requires physical-si-v2 source')
        self.backend=backend;self.kind=backend.kind+'/'+POLICY_ID
        self.record_path=None if record_path is None else Path(record_path);self._lock=threading.Lock()
        self._summary={'policy':copy.deepcopy(POLICY),'counter_scope':'current backend process; persisted summary records its last writing session','query_count':0,
                       'rectified_values':{name:0 for name in POLICY['negative_to_zero_channels']},
                       'note':'Derived publication of original model predictions; raw hash records refer to pre-policy physical arrays, not a new model run.'}
    def query(self,window):
        height,climate,biomes,land=self.backend.query(window)
        normalized,counts=rectify(climate)
        with self._lock:
            self._summary['query_count']+=1
            for key,value in counts.items():self._summary['rectified_values'][key]+=value
            self._summary['last_query']={'native_window':list(window),'rectified_values':counts,
                'raw_physical_climate_sha256':hashlib.sha256(np.asarray(climate,dtype='<f4').tobytes()).hexdigest(),
                'published_physical_climate_sha256':hashlib.sha256(normalized.astype('<f4',copy=False).tobytes()).hexdigest(),
                'height_sha256':hashlib.sha256(np.asarray(height,dtype='<f4').tobytes()).hexdigest()}
            if self.record_path is not None:
                self.record_path.parent.mkdir(parents=True,exist_ok=True)
                temporary=self.record_path.with_suffix('.tmp')
                temporary.write_text(json.dumps(self._summary,indent=2)+'\n');os.replace(temporary,self.record_path)
        return height,normalized,biomes,land
    def summary(self):
        with self._lock:return copy.deepcopy(self._summary)

def canonical_backend(b,job,backend,cache_dir):
    """A distinct policy cache; valid historical owners migrate without inference.

    The old native cache is untouched. Its strict old decoder guarantees it
    contains no negative physical planes; array-preserving migration therefore
    has exactly zero corrections. Policy kind and directory prevent old/new
    contracts from sharing canonical response identities.
    """
    cache_dir=Path(cache_dir);policy_root=cache_dir/POLICY_ID
    physical=PhysicalClimateBackend(backend,b.climate_encoding(job['spec']),
        policy_root/job['world_id']/'physical-climate-provenance.json')
    canonical=b.CanonicalBackend(job,physical,policy_root)
    legacy=cache_dir/job['world_id'];migrated=0
    if legacy.is_dir():
        for source in legacy.glob('*.atdr'):
            destination=canonical.directory/source.name
            if destination.exists():continue
            if source.stat().st_size>canonical.size*canonical.size*26+b.MAX_JSON+16:
                raise ValueError('Oversized legacy canonical owner')
            meta,*arrays=b.decode_region(source.read_bytes(),job['world_id']);win=meta['window']
            if meta['backend']!=backend.kind+'/canonical-owner-v1' or b.climate_encoding(meta)!=b.CLIMATE_PHYSICAL:
                raise ValueError('Legacy owner cannot migrate from a different source/backend contract')
            if win['rows']!=canonical.size or win['cols']!=canonical.size or win['row0']%canonical.size or win['col0']%canonical.size:
                raise ValueError('Legacy canonical owner lattice mismatch')
            expected=f"{win['row0']//canonical.size}_{win['col0']//canonical.size}.atdr"
            if source.name!=expected:raise ValueError('Legacy owner filename does not match signed lattice')
            normalized,counts=rectify(arrays[1])
            if any(counts.values()) or not np.array_equal(normalized,arrays[1]):
                raise ValueError('Strict historical owner unexpectedly requires climate correction')
            raw=b.encode_region(job,(win['row0'],win['col0'],win['rows'],win['cols']),arrays,canonical.kind)
            temporary=destination.with_suffix('.tmp');temporary.write_bytes(raw);os.replace(temporary,destination);migrated+=1
    canonical.physical_policy=physical
    canonical.migrated_legacy_owners=migrated
    return canonical
