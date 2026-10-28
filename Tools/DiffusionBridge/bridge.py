#!/usr/bin/env python3
"""Pinned InfiniteDiffusion provider: immutable jobs, bounded HTTP regions and AVWF baking.

Real execution imports the upstream WorldPipeline and pretrained models. The
NumPy-only contract tests use a named fake backend and never claim neural output.
"""
from __future__ import annotations
import argparse,concurrent.futures,hashlib,http.server,importlib.util,importlib.metadata,json,math,os
from pathlib import Path
import struct,subprocess,sys,threading,zlib
from collections import OrderedDict
from datetime import datetime,timezone
import numpy as np

ROOT=Path(__file__).resolve().parents[2]
UPSTREAM_COMMIT='e8dcb4b1a834ab2f6b1a6f5256ed7c9f2f3e8230'
MODEL='xandergos/terrain-diffusion-30m'
MODEL_REVISION='9ef8030cb805b433b98ec25c5dddefbac07a9e26'
CHANNELS=('temperature_c','temperature_std_c','precipitation_mm','precipitation_cv_percent','beta')
CLIMATE_NATIVE='upstream-native-v1'
CLIMATE_PHYSICAL='physical-si-v2'
COND_UNITS=('m','degC','degC','mm/year','percent')
MAGIC=b'ATDR0001'
MAX_JSON=65536
MAX_CONDITION_SAMPLES=4194304

def canonical(value):return json.dumps(value,sort_keys=True,separators=(',',':'),ensure_ascii=False,allow_nan=False).encode('utf-8')
def climate_encoding(value):
    encoding=value.get('climate_encoding',CLIMATE_NATIVE)
    if encoding not in (CLIMATE_NATIVE,CLIMATE_PHYSICAL):raise ValueError('Unknown climate encoding')
    return encoding

def climate_units(encoding):
    if encoding not in (CLIMATE_NATIVE,CLIMATE_PHYSICAL):raise ValueError('Unknown climate encoding')
    return ['degC','degC' if encoding==CLIMATE_PHYSICAL else 'centi_degC','mm/year','percent','degC/m']

def normalize_upstream_climate(climate,encoding):
    """Convert only the upstream seasonality scale, without mutating model tensors.

    Upstream returns surface-adjusted temperature already; no second lapse-rate
    adjustment belongs here. Legacy jobs preserve historical native bytes.
    """
    climate_encoding({'climate_encoding':encoding})
    value=np.array(climate,dtype=np.float32,copy=True)
    if value.ndim!=3 or value.shape[0]!=5 or not np.isfinite(value).all():raise ValueError('Invalid upstream climate planes')
    if encoding==CLIMATE_PHYSICAL:value[1]*=.01
    return value

def physical_climate(climate,encoding):
    """Decode an archived/wire climate array to the public physical-unit contract."""
    value=normalize_upstream_climate(climate,CLIMATE_NATIVE)
    climate_encoding({'climate_encoding':encoding})
    if encoding==CLIMATE_NATIVE:value[1]*=.01
    return value

def validate_climate_values(climate,encoding):
    climate_encoding({'climate_encoding':encoding})
    bounds=[(-150,100),(0,150 if encoding==CLIMATE_PHYSICAL else 15000),(0,100000),(0,10000),(-.1,.1)]
    for channel,(low,high) in enumerate(bounds):
        if np.any(climate[channel]<low) or np.any(climate[channel]>high):raise ValueError('Climate plane outside physical contract: '+str(channel))
def runtime_versions():
    versions={'python':'.'.join(str(v) for v in sys.version_info[:3])}
    for name in ('torch','torchvision','diffusers','infinite-tensor','numpy','scipy','scikit-image','rasterio','h5py','safetensors'):
        try:versions[name]=importlib.metadata.version(name)
        except importlib.metadata.PackageNotFoundError:versions[name]=None
    return versions

def validate_runtime(job):
    if job['spec'].get('runtime_versions')!=runtime_versions():
        raise RuntimeError('Inference package/Python versions differ from prepared job; prepare a new job in this inference environment')
def digest(path):
    value=hashlib.sha256()
    with Path(path).open('rb') as stream:
        for block in iter(lambda:stream.read(4*1024**2),b''):value.update(block)
    return value.hexdigest()
def integer(value,name,low,high):
    if type(value) is not int or value<low or value>high:raise ValueError(f'{name} must be an integer in {low}..{high}')
    return value
def finite(value,name,low=None,high=None):
    if isinstance(value,bool) or not isinstance(value,(int,float)) or not math.isfinite(value):raise ValueError(name+' must be finite')
    if low is not None and value<low or high is not None and value>high:raise ValueError(name+' outside supported range')
    return float(value)

def load_conditioning_manifest(path):
    """Describe files now; read arrays only when the provider is started."""
    if path is None:return None
    path=Path(path).resolve()
    if path.stat().st_size>MAX_JSON:raise ValueError('Conditioning manifest too large')
    doc=json.loads(path.read_text())
    if doc.get('schema')!='ashen-conditioning-v1':raise ValueError('Unknown conditioning schema')
    spacing=finite(doc.get('coarse_spacing_m'),'coarse_spacing_m',1,1000000)
    origin_i=integer(doc.get('origin_i',0),'origin_i',-2**40,2**40)
    origin_j=integer(doc.get('origin_j',0),'origin_j',-2**40,2**40)
    periodic=doc.get('periodic',False)
    if type(periodic) is not bool:raise ValueError('periodic must be boolean')
    channels=[]
    for key,entry in doc.get('channels',{}).items():
        if key not in ('0','1','2','3','4'):raise ValueError('Conditioning channel must be0..4')
        channel=int(key)
        if entry.get('units')!=COND_UNITS[channel]:raise ValueError('Conditioning unit mismatch for channel '+key)
        filename=(path.parent/entry['path']).resolve()
        if not filename.is_file():raise ValueError('Missing conditioning file: '+str(filename))
        channels.append({'channel':channel,'path':str(filename),'sha256':digest(filename),'units':entry['units']})
    channels.sort(key=lambda v:v['channel'])
    if not channels:raise ValueError('Conditioning manifest contains no channels')
    result={'coarse_spacing_m':spacing,'origin_i':origin_i,'origin_j':origin_j,
        'periodic':periodic,'padding_cells':0 if periodic else 64,'channels':channels}
    if 'provenance' in doc:
        canonical(doc['provenance']) # Require finite JSON metadata before it enters identity.
        result['provenance']=doc['provenance']
    if 'source_biome' in doc:
        filename=(path.parent/doc['source_biome']).resolve()
        result['source_biome']={'path':str(filename),'sha256':digest(filename)}
    return result

def identity_spec(spec):
    # Paths are machine-local locators; bytes and units establish world identity.
    result=json.loads(json.dumps(spec))
    if result['conditioning']:
        for entry in result['conditioning']['channels']:entry.pop('path',None)
        if 'source_biome' in result['conditioning']:result['conditioning']['source_biome'].pop('path',None)
    if result.get('statistics'):result['statistics'].pop('path',None)
    return result

def make_job(upstream_source,seed=714103,conditioning=None,device='cpu',cache_mib=256,
             response_cache_mib=128,max_side=1024,max_samples=262144,queue_capacity=8,snr=None,
             stats_file=None,model_dir=None,torch_threads=4,T=2):
    integer(seed,'seed',0,2**64-1)
    integer(cache_mib,'cache_mib',16,4096);integer(response_cache_mib,'response_cache_mib',16,4096)
    integer(max_side,'max_side',2,2048);integer(max_samples,'max_samples',4,4194304)
    integer(queue_capacity,'queue_capacity',1,64)
    integer(torch_threads,'torch_threads',1,32);integer(T,'T',1,2)
    if device not in ('cpu','cuda','mps'):raise ValueError('device must be cpu, cuda or mps')
    snr=[.2,.2,1.,.2,1.] if snr is None else snr
    if len(snr)!=5:raise ValueError('Exactly five conditioning SNR values required')
    snr=[finite(v,'snr',.000001,1000) for v in snr]
    cond=load_conditioning_manifest(conditioning)
    if cond and abs(cond['coarse_spacing_m']-7680)>1e-6:
        raise ValueError('30m model requires7680m coarse conditioning cells; resample the authored map explicitly')
    spec={'schema':'ashen-diffusion-job-v1','upstream_commit':UPSTREAM_COMMIT,
        'model':MODEL,'model_revision':MODEL_REVISION,'seed':seed,'spacing_m':30.,
        'precision':'fp32','device':device,'pipeline_cache_bytes':cache_mib*1024**2,
        'runtime_versions':runtime_versions(),
        'sampling_policy':'canonical-owner-v1','owner_samples':256,'owner_halo':32,
        'owner_disk_cache_bytes':512*1024**2,
        'torch_threads':torch_threads,'T':T,'latents_batch_size':[1],
        'statistics':None if stats_file is None else {'path':str(Path(stats_file).resolve()),'sha256':digest(stats_file)},
        'response_cache_bytes':response_cache_mib*1024**2,'max_region_side':max_side,
        'max_cached_regions':256,
        'max_region_samples':max_samples,'queue_capacity':queue_capacity,
        'conditioning_snr':snr,'conditioning':cond,
        'height_units':'meters','row_axis':'global_z','column_axis':'global_x',
        'climate_channels':list(CHANNELS),'climate_encoding':CLIMATE_PHYSICAL,'biome_policy':'preserve_imported_or_unclassified_zero',
        'land_policy':'generated_height_at_or_above_zero','coast_policy':'soft_model_conditioning'}
    world_id=hashlib.sha256(canonical(identity_spec(spec))).hexdigest()
    return {'spec':spec,'world_id':world_id,'upstream_source':str(Path(upstream_source).resolve()),
        'model_dir':str(Path(model_dir).resolve()) if model_dir is not None else None,
        'note':'Prepared inference job; this manifest is not generated terrain or evidence of executed weights.'}

def validate_job(job):
    spec=job['spec']
    if spec.get('schema')!='ashen-diffusion-job-v1':raise ValueError('Unsupported job schema')
    if spec.get('upstream_commit')!=UPSTREAM_COMMIT or spec.get('model')!=MODEL or spec.get('model_revision')!=MODEL_REVISION:
        raise ValueError('This adapter supports only its inspected pinned source/model')
    if spec.get('spacing_m')!=30 or spec.get('precision')!='fp32':raise ValueError('Unsupported spacing/precision')
    if not isinstance(spec.get('runtime_versions'),dict):raise ValueError('Missing execution package version fingerprint')
    if list(spec.get('climate_channels',[]))!=list(CHANNELS):raise ValueError('Unknown climate layout')
    climate_encoding(spec)
    integer(spec['seed'],'seed',0,2**64-1)
    integer(spec['pipeline_cache_bytes'],'pipeline_cache_bytes',16*1024**2,4096*1024**2)
    integer(spec['response_cache_bytes'],'response_cache_bytes',16*1024**2,4096*1024**2)
    integer(spec['max_region_side'],'max_region_side',2,2048)
    integer(spec['max_region_samples'],'max_region_samples',4,4194304)
    integer(spec['queue_capacity'],'queue_capacity',1,64)
    integer(spec['max_cached_regions'],'max_cached_regions',1,256)
    integer(spec['torch_threads'],'torch_threads',1,32);integer(spec['T'],'T',1,2)
    if hashlib.sha256(canonical(identity_spec(spec))).hexdigest()!=job['world_id']:raise ValueError('Job identity mismatch')
    if spec.get('sampling_policy')!='canonical-owner-v1' or spec.get('owner_samples')!=256 or spec.get('owner_halo')!=32:
        raise ValueError('Unknown canonical sampling policy')
    integer(spec['owner_disk_cache_bytes'],'owner_disk_cache_bytes',16*1024**2,4096*1024**2)
    for entry in (spec.get('conditioning') or {}).get('channels',[]):
        if digest(entry['path'])!=entry['sha256']:raise ValueError('Conditioning bytes changed: '+entry['path'])
    biome=(spec.get('conditioning') or {}).get('source_biome')
    if biome and digest(biome['path'])!=biome['sha256']:raise ValueError('Source biome bytes changed')
    stats=spec.get('statistics')
    if stats and digest(stats['path'])!=stats['sha256']:raise ValueError('Conditioning statistics bytes changed')
    return job

def read_job(path):
    path=Path(path)
    if path.stat().st_size>MAX_JSON:raise ValueError('Job manifest too large')
    return validate_job(json.loads(path.read_text()))

def read_array(path):
    path=Path(path)
    if path.suffix.lower()=='.npy':arr=np.load(path,allow_pickle=False,mmap_mode='r')
    elif path.suffix.lower() in ('.tif','.tiff'):
        try:import rasterio
        except ImportError as e:raise RuntimeError('TIFF conditioning requires rasterio') from e
        with rasterio.open(path) as ds:
            if ds.count!=1 or ds.width*ds.height>MAX_CONDITION_SAMPLES:raise ValueError('Conditioning TIFF must be one bounded band')
            arr=ds.read(1)
    else:raise ValueError('Conditioning files must be.npy or.tif/.tiff')
    if arr.ndim!=2 or arr.size>MAX_CONDITION_SAMPLES or min(arr.shape)<1:raise ValueError('Conditioning shape exceeds limits')
    if arr.dtype.kind not in 'fiu' or not np.isfinite(arr).all():raise ValueError('Conditioning must contain finite numeric samples')
    return np.array(arr,dtype=np.float32,copy=True)

def install_conditioning(pipeline,cond):
    if cond is None:return None
    common=None
    for entry in cond['channels']:
        arr=read_array(entry['path'])
        if common is None:common=arr.shape
        elif arr.shape!=common:raise ValueError('All conditioning rasters must share shape and lattice')
        channel=entry['channel']
        if channel==2:arr*=100. # TIFF/NPY seasonality Celsius → upstream Celsius×100.
        pad=cond['padding_cells']
        if pad:arr=np.pad(arr,pad,mode='edge')
        if cond['periodic']:
            # Pinned upstream periodic lookup ignores import origins and wraps raw
            # global indices. Roll the values so a nonzero authored origin retains
            # its documented phase. Keep the original period (no edge padding).
            arr=np.roll(arr,(cond['origin_i'],cond['origin_j']),axis=(0,1))
        pipeline.set_custom_conditioning_import(channel,arr,cond['origin_i']-pad,
            cond['origin_j']-pad,default_value=-1000. if channel==0 else None,
            periodic=cond['periodic'],rebuild=False)
    pipeline.rebuild()
    if 'source_biome' in cond:
        arr=read_array(cond['source_biome']['path'])
        if arr.shape!=common or np.any(arr!=np.floor(arr)) or np.any(arr<0) or np.any(arr>255):
            raise ValueError('Source biome must be byte IDs on the conditioning lattice')
        return arr.astype(np.uint8)
    return None

def dependencies():
    names=('torch','torchvision','diffusers','huggingface_hub','safetensors','infinite_tensor',
        'h5py','scipy','skimage','matplotlib','rasterio','pyfastnoiselite')
    return {name:importlib.util.find_spec(name) is not None for name in names}

class RealBackend:
    """One owner loads unchanged upstream code and selected pretrained weights."""
    kind='upstream-pretrained'
    def __init__(self,job,allow_download=False):
        validate_job(job);validate_runtime(job);self.job=job
        source=Path(job['upstream_source'])
        actual=subprocess.check_output(['git','-C',str(source),'rev-parse','HEAD'],text=True).strip()
        if actual!=UPSTREAM_COMMIT:raise RuntimeError('Upstream checkout does not match pinned commit')
        if subprocess.run(['git','-C',str(source),'diff','--quiet','HEAD','--','terrain_diffusion']).returncode:
            raise RuntimeError('Upstream terrain_diffusion source has uncommitted changes')
        missing=[k for k,v in dependencies().items() if not v]
        if missing:raise RuntimeError('Missing inference dependencies: '+', '.join(missing))
        sys.path.insert(0,str(source))
        from huggingface_hub import snapshot_download
        import torch
        import terrain_diffusion.inference.synthetic_map as synthetic_map
        from terrain_diffusion.inference.world_pipeline import WorldPipeline
        spec=job['spec']
        if not spec.get('statistics'):raise RuntimeError('Pass --stats-file when preparing the job; unattended inference needs the explicit verified synthetic-map statistics')
        synthetic_map.STATS_CACHE_PATH=spec['statistics']['path']
        torch.set_num_threads(spec['torch_threads'])
        snapshot=job.get('model_dir') or snapshot_download(spec['model'],revision=spec['model_revision'],local_files_only=not allow_download)
        pinned=json.loads((Path(__file__).parent/'model-manifest.json').read_text())
        for entry in pinned['files']:
            if digest(Path(snapshot)/entry['file'])!=entry['sha256']:raise RuntimeError('Pinned model file hash mismatch: '+entry['file'])
        # Upstream may request WorldClim data interactively. Fail clearly rather than
        # hanging an unattended Unity provider or answering the prompt automatically.
        import builtins
        original_input=builtins.input
        def no_prompt(prompt=''):raise RuntimeError('Upstream requested interactive data setup: '+prompt+' Prepare its WorldClim/cache dependencies first.')
        builtins.input=no_prompt
        try:
            self.pipeline=WorldPipeline.from_pretrained(snapshot,seed=spec['seed'],
                latents_batch_size=spec['latents_batch_size'],T=spec['T'],torch_compile=False,dtype=None,
                caching_strategy='direct',cache_limit=spec['pipeline_cache_bytes'])
            self.pipeline.to(spec['device']);self.pipeline.set_cond_snr(spec['conditioning_snr'])
            self.pipeline.bind()
            self.biomes=install_conditioning(self.pipeline,spec['conditioning'])
        finally:builtins.input=original_input
        native=float(self.pipeline.native_resolution)
        if native!=spec['spacing_m']:raise RuntimeError('Loaded model native spacing differs from manifest')
    def query(self,window):
        r,c,h,w=window
        with self.pipeline:result=self.pipeline.get(r,c,r+h,c+w,with_climate=True)
        def cpu(x):return np.asarray(x.detach().cpu().numpy(),dtype=np.float32)
        heights=cpu(result['elev']);climate=normalize_upstream_climate(cpu(result['climate']),climate_encoding(self.job['spec']))
        source_biomes=np.zeros((h,w),dtype=np.uint8)
        cond=self.job['spec']['conditioning']
        if self.biomes is not None:
            ri=np.floor_divide(np.arange(r,r+h,dtype=np.int64),256)-cond['origin_i']
            ci=np.floor_divide(np.arange(c,c+w,dtype=np.int64),256)-cond['origin_j']
            if cond['periodic']:
                source_biomes=self.biomes[ri[:,None]%self.biomes.shape[0],ci[None,:]%self.biomes.shape[1]]
            else:
                mask=(ri[:,None]>=0)&(ri[:,None]<self.biomes.shape[0])&(ci[None,:]>=0)&(ci[None,:]<self.biomes.shape[1])
                values=self.biomes[np.clip(ri,0,self.biomes.shape[0]-1)[:,None],np.clip(ci,0,self.biomes.shape[1]-1)[None,:]]
                source_biomes=np.where(mask,values,0).astype(np.uint8)
        return heights,climate,source_biomes,(heights>=0).astype(np.uint8)

def validate_window(spec,request):
    if request.get('world_id') is None:raise ValueError('world_id is required')
    r=integer(request.get('row0'),'row0',-2**40,2**40)
    c=integer(request.get('col0'),'col0',-2**40,2**40)
    h=integer(request.get('rows'),'rows',2,spec['max_region_side'])
    w=integer(request.get('cols'),'cols',2,spec['max_region_side'])
    if h*w>spec['max_region_samples']:raise ValueError('Region exceeds sample budget')
    return r,c,h,w

def encode_region(job,window,channels,backend_kind):
    r,c,h,w=window;height,climate,biomes,land=channels
    height=np.asarray(height,dtype='<f4');climate=np.asarray(climate,dtype='<f4')
    biomes=np.asarray(biomes);land=np.asarray(land)
    if height.shape!=(h,w) or climate.shape!=(5,h,w) or biomes.shape!=(h,w) or land.shape!=(h,w):raise ValueError('Backend response shape mismatch')
    if not np.isfinite(height).all() or not np.isfinite(climate).all():raise ValueError('Backend response contains nonfinite values')
    validate_climate_values(climate,climate_encoding(job['spec']))
    if np.any(np.abs(height)>1000000) or biomes.dtype.kind not in 'ui' or np.any(biomes<0) or np.any(biomes>255):raise ValueError('Backend elevation/biome values outside supported range')
    if land.dtype.kind not in 'biu' or np.any((land!=0)&(land!=1)):raise ValueError('Backend land flags must be0/1')
    spacing=job['spec']['spacing_m']
    meta={'schema':'ashen-terrain-region-v1','world_id':job['world_id'],
        'window':{'row0':r,'col0':c,'rows':h,'cols':w},'spacing_m':spacing,
        'origin_x_m':c*spacing,'origin_z_m':r*spacing,'height_units':'meters',
        'climate_channels':list(CHANNELS),'climate_encoding':climate_encoding(job['spec']),
        'climate_value_units':climate_units(climate_encoding(job['spec'])),'backend':backend_kind,
        'layout':'height_f32_planar,climate5_f32_planar,source_biome_u8,land_u8'}
    header=canonical(meta)
    raw=MAGIC+struct.pack('<I',len(header))+header+height.tobytes()+climate.tobytes()+biomes.astype('u1').tobytes()+land.astype('u1').tobytes()
    return raw+struct.pack('<I',zlib.crc32(raw)&0xffffffff)

def decode_region(raw,expected_world_id=None):
    if len(raw)<16 or len(raw)>120*1024**2 or raw[:8]!=MAGIC:raise ValueError('Invalid region header/size')
    if zlib.crc32(raw[:-4])&0xffffffff!=struct.unpack_from('<I',raw,len(raw)-4)[0]:raise ValueError('Region checksum mismatch')
    n=struct.unpack_from('<I',raw,8)[0]
    if n>MAX_JSON or n<2 or len(raw)<16+n:raise ValueError('Invalid metadata length')
    meta=json.loads(raw[12:12+n].decode('utf-8'));win=meta['window']
    if meta.get('schema')!='ashen-terrain-region-v1' or meta.get('height_units')!='meters' or meta.get('climate_channels')!=list(CHANNELS):raise ValueError('Unknown region schema/units/channels')
    encoding=climate_encoding(meta)
    if 'climate_value_units' in meta and meta['climate_value_units']!=climate_units(encoding):raise ValueError('Climate value units do not match encoding')
    if meta.get('layout')!='height_f32_planar,climate5_f32_planar,source_biome_u8,land_u8':raise ValueError('Unknown region layout')
    finite(meta['spacing_m'],'spacing_m',.001,1000000)
    integer(win['row0'],'row0',-2**40,2**40);integer(win['col0'],'col0',-2**40,2**40)
    h=integer(win['rows'],'rows',2,2048);w=integer(win['cols'],'cols',2,2048)
    count=h*w
    if count>4194304 or len(raw)!=16+n+count*26:raise ValueError('Region payload size mismatch')
    if expected_world_id is not None and meta['world_id']!=expected_world_id:raise ValueError('Stale world identity')
    off=12+n
    heights=np.frombuffer(raw,dtype='<f4',count=count,offset=off).reshape(h,w);off+=count*4
    climate=np.frombuffer(raw,dtype='<f4',count=count*5,offset=off).reshape(5,h,w);off+=count*20
    biomes=np.frombuffer(raw,dtype='u1',count=count,offset=off).reshape(h,w);off+=count
    land=np.frombuffer(raw,dtype='u1',count=count,offset=off).reshape(h,w)
    if not np.isfinite(heights).all() or not np.isfinite(climate).all() or np.any(land>1):raise ValueError('Invalid region values')
    validate_climate_values(climate,encoding)
    return meta,heights,climate,biomes,land

class QueueFull(RuntimeError):pass
class IdentityMismatch(ValueError):pass

class CanonicalBackend:
    """Every global pixel has one fixed model-query owner, independent of callers.

    Upstream cropped queries can differ slightly because reconstruction work is
    shaped by the requested rectangle. Fixed owner windows avoid transferring
    that partition dependency to0.1m voxel quantization. The disk cache freezes
    owner bytes while resident; it is regenerable and not a world-save format.
    """
    def __init__(self,job,backend,cache_dir):
        self.job=job;self.backend=backend;self.kind=backend.kind+'/canonical-owner-v1'
        self.directory=Path(cache_dir)/job['world_id'];self.directory.mkdir(parents=True,exist_ok=True)
        self.size=job['spec']['owner_samples'];self.halo=job['spec']['owner_halo']
        self.limit=job['spec']['owner_disk_cache_bytes']
    def _owner(self,tile_r,tile_c):
        path=self.directory/f'{tile_r}_{tile_c}.atdr'
        if path.exists():
            if path.stat().st_size>self.size*self.size*26+MAX_JSON+16:raise ValueError('Oversized canonical owner file')
            raw=path.read_bytes();meta,*arrays=decode_region(raw,self.job['world_id'])
            expected={'row0':tile_r*self.size,'col0':tile_c*self.size,'rows':self.size,'cols':self.size}
            if meta['window']!=expected or meta['backend']!=self.kind:raise ValueError('Canonical owner identity/layout mismatch')
            if climate_encoding(meta)!=climate_encoding(self.job['spec']):raise ValueError('Canonical owner climate encoding mismatch')
            os.utime(path,None);return arrays
        r,c=tile_r*self.size,tile_c*self.size;p=self.halo
        values=self.backend.query((r-p,c-p,self.size+2*p,self.size+2*p))
        cropped=(values[0][p:p+self.size,p:p+self.size],values[1][:,p:p+self.size,p:p+self.size],
            values[2][p:p+self.size,p:p+self.size],values[3][p:p+self.size,p:p+self.size])
        raw=encode_region(self.job,(r,c,self.size,self.size),cropped,self.kind)
        temporary=path.with_suffix('.tmp')
        with temporary.open('wb') as stream:stream.write(raw);stream.flush();os.fsync(stream.fileno())
        os.replace(temporary,path)
        files=sorted(self.directory.glob('*.atdr'),key=lambda f:f.stat().st_mtime_ns)
        total=sum(f.stat().st_size for f in files)
        for old in files:
            if total<=self.limit:break
            total-=old.stat().st_size;old.unlink()
        return cropped
    def query(self,window):
        r,c,h,w=window
        height=np.empty((h,w),dtype=np.float32);climate=np.empty((5,h,w),dtype=np.float32)
        biomes=np.empty((h,w),dtype=np.uint8);land=np.empty((h,w),dtype=np.uint8)
        for tr in range(r//self.size,(r+h-1)//self.size+1):
            for tc in range(c//self.size,(c+w-1)//self.size+1):
                values=self._owner(tr,tc);orow,ocol=tr*self.size,tc*self.size
                r0,r1=max(r,orow),min(r+h,orow+self.size);c0,c1=max(c,ocol),min(c+w,ocol+self.size)
                dest=(slice(r0-r,r1-r),slice(c0-c,c1-c));src=(slice(r0-orow,r1-orow),slice(c0-ocol,c1-ocol))
                height[dest]=values[0][src];climate[(slice(None),)+dest]=values[1][(slice(None),)+src]
                biomes[dest]=values[2][src];land[dest]=values[3][src]
        return height,climate,biomes,land

class RegionProvider:
    """A single inference executor with bounded queued work and response residency."""
    def __init__(self,job,backend):
        self.job=validate_job(json.loads(json.dumps(job)));self.backend=backend;self.lock=threading.Lock()
        self.executor=concurrent.futures.ThreadPoolExecutor(max_workers=1,thread_name_prefix='terrain-inference')
        self.pending={};self.cache=OrderedDict();self.cache_bytes=0;self.in_flight=0;self.failure=None
    def request(self,request):
        if request.get('world_id')!=self.job['world_id']:raise IdentityMismatch('world_id does not match this immutable provider')
        window=validate_window(self.job['spec'],request)
        with self.lock:
            if self.failure is not None:raise RuntimeError('Provider inference failed and is latched; restart after correction: '+self.failure)
            if window in self.cache:
                value=self.cache.pop(window);self.cache[window]=value
                future=concurrent.futures.Future();future.set_result(value);return future
            if window in self.pending:return self.pending[window]
            if len(self.pending)>=self.job['spec']['queue_capacity']+1:raise QueueFull('Inference queue full; retry after backoff')
            future=self.executor.submit(self._run,window);self.pending[window]=future
            future.add_done_callback(lambda result,key=window:self._done(key,result))
            return future
    def _run(self,window):
        with self.lock:
            if self.failure is not None:raise RuntimeError('Provider failure already latched: '+self.failure)
            self.in_flight=1
        try:return encode_region(self.job,window,self.backend.query(window),self.backend.kind)
        except Exception as e:
            with self.lock:self.failure=type(e).__name__+': '+str(e)
            raise
        finally:
            with self.lock:self.in_flight=0
    def _done(self,window,result):
        with self.lock:
            self.pending.pop(window,None)
            if result.cancelled() or result.exception() is not None:return
            value=result.result();limit=self.job['spec']['response_cache_bytes']
            if len(value)>limit:return
            while self.cache and (self.cache_bytes+len(value)>limit or len(self.cache)>=self.job['spec']['max_cached_regions']):
                _,old=self.cache.popitem(last=False);self.cache_bytes-=len(old)
            self.cache[window]=value;self.cache_bytes+=len(value)
    def status(self):
        with self.lock:
            return {'ready':self.failure is None,'failure':self.failure,'world_id':self.job['world_id'],'spacing_m':self.job['spec']['spacing_m'],
                'max_region_side':self.job['spec']['max_region_side'],'max_region_samples':self.job['spec']['max_region_samples'],
                'queue_capacity':self.job['spec']['queue_capacity'],'queued':max(0,len(self.pending)-self.in_flight),
                'in_flight':self.in_flight,'resident_regions':len(self.cache),'resident_bytes':self.cache_bytes,
                'backend':self.backend.kind,'note':'Immutable model/configuration; no seed-changing endpoint.'}
    def close(self):self.executor.shutdown(wait=True,cancel_futures=True)

def handler_for(provider):
    class Handler(http.server.BaseHTTPRequestHandler):
        def reply(self,status,body,content_type='application/json'):
            self.send_response(status);self.send_header('Content-Type',content_type)
            self.send_header('Content-Length',str(len(body)));self.end_headers();self.wfile.write(body)
        def do_GET(self):
            if self.path!='/status':self.reply(404,canonical({'error':'Not found'}));return
            self.reply(200,canonical(provider.status()))
        def do_POST(self):
            if self.path!='/region':self.reply(404,canonical({'error':'Not found'}));return
            try:
                length=int(self.headers.get('Content-Length','0'))
                if length<2 or length>4096:raise ValueError('Request body size must be2..4096 bytes')
                request=json.loads(self.rfile.read(length).decode('utf-8'))
                if not isinstance(request,dict):raise ValueError('Request must be an object')
                result=provider.request(request).result()
                self.reply(200,result,'application/vnd.ashen.terrain-region-v1')
            except IdentityMismatch as e:self.reply(409,canonical({'error':str(e)}))
            except QueueFull as e:self.reply(429,canonical({'error':str(e),'retry_after_seconds':1}))
            except (ValueError,KeyError,TypeError) as e:self.reply(400,canonical({'error':str(e)}))
            except Exception as e:self.reply(500,canonical({'error':type(e).__name__+': '+str(e)}))
    return Handler

def bake(provider,window,output):
    r,c,h,w=window
    raw=provider.request({'world_id':provider.job['world_id'],'row0':r,'col0':c,'rows':h,'cols':w}).result()
    meta,heights,climate,biomes,land=decode_region(raw,provider.job['world_id'])
    sys.path.insert(0,str(ROOT/'Tools/WorldScaleImport'))
    from convert_world import write_avwf
    identity={'provider':'InfiniteDiffusion','world_id':provider.job['world_id'],'window':meta['window'],
        'model':provider.job['spec']['model'],'model_revision':provider.job['spec']['model_revision'],
        'seed':provider.job['spec']['seed'],'spacing_m':meta['spacing_m'],'height_units':'meters','backend':meta['backend']}
    sha=write_avwf(output,heights,biomes,land,meta['origin_x_m'],meta['origin_z_m'],
        (w-1)*meta['spacing_m'],(h-1)*meta['spacing_m'],identity)
    # Preserve all climate channels alongside AVWF; the Unity atlas currently samples
    # elevation/biomes while this sidecar keeps future biome climate classification possible.
    output=Path(output);np.savez_compressed(output.with_suffix('.climate.npz'),climate=climate)
    report={'created_utc':datetime.now(timezone.utc).isoformat(),'job':provider.job,'region_metadata':meta,
        'avwf_sha256':sha,'climate_sha256':digest(output.with_suffix('.climate.npz')),
        'note':'Actual provider output. Inspect backend field; fake test backends are not neural inference.'}
    output.with_suffix('.report.json').write_text(json.dumps(report,indent=2)+'\n')
    return report

def main():
    p=argparse.ArgumentParser(description=__doc__);sub=p.add_subparsers(dest='command',required=True)
    plan=sub.add_parser('plan');plan.add_argument('--upstream-source',required=True,type=Path);plan.add_argument('--output',required=True,type=Path)
    plan.add_argument('--seed',type=int,default=714103);plan.add_argument('--conditioning',type=Path)
    plan.add_argument('--device',choices=('cpu','cuda','mps'),default='cpu');plan.add_argument('--cache-mib',type=int,default=256)
    plan.add_argument('--response-cache-mib',type=int,default=128);plan.add_argument('--max-side',type=int,default=1024)
    plan.add_argument('--max-samples',type=int,default=262144);plan.add_argument('--queue-capacity',type=int,default=8)
    plan.add_argument('--snr',default='.2,.2,1,.2,1')
    plan.add_argument('--stats-file',type=Path);plan.add_argument('--model-dir',type=Path)
    plan.add_argument('--torch-threads',type=int,default=4);plan.add_argument('--T',type=int,choices=(1,2),default=2)
    sub.add_parser('check-dependencies')
    for command in ('serve','bake'):
        parser=sub.add_parser(command);parser.add_argument('--job',required=True,type=Path);parser.add_argument('--allow-download',action='store_true')
        parser.add_argument('--owner-cache-dir',type=Path,default=ROOT/'Tools/DiffusionBridge/bin/cache')
        if command=='serve':parser.add_argument('--host',default='127.0.0.1');parser.add_argument('--port',type=int,default=8765)
        else:
            parser.add_argument('--row0',type=int,default=0);parser.add_argument('--col0',type=int,default=0)
            parser.add_argument('--rows',type=int,default=64);parser.add_argument('--cols',type=int,default=64)
            parser.add_argument('--output',required=True,type=Path)
    args=p.parse_args()
    if args.command=='plan':
        job=make_job(args.upstream_source,args.seed,args.conditioning,args.device,args.cache_mib,args.response_cache_mib,
            args.max_side,args.max_samples,args.queue_capacity,[float(v) for v in args.snr.split(',')],
            args.stats_file,args.model_dir,args.torch_threads,args.T)
        args.output.parent.mkdir(parents=True,exist_ok=True);args.output.write_text(json.dumps(job,indent=2)+'\n')
        print('Prepared immutable job '+job['world_id']+' (no model executed)');return
    if args.command=='check-dependencies':
        found=dependencies();print(json.dumps(found,indent=2));sys.exit(0 if all(found.values()) else 2)
    job=read_job(args.job);backend=RealBackend(job,args.allow_download)
    backend=CanonicalBackend(job,backend,args.owner_cache_dir);provider=RegionProvider(job,backend)
    try:
        if args.command=='bake':
            print(json.dumps(bake(provider,(args.row0,args.col0,args.rows,args.cols),args.output),indent=2));return
        server=http.server.ThreadingHTTPServer((args.host,args.port),handler_for(provider));server.daemon_threads=True
        print(f'Ready: http://{args.host}:{args.port}/status / world {job["world_id"]}',flush=True)
        try:server.serve_forever()
        finally:server.server_close()
    finally:provider.close()

if __name__=='__main__':
    try:main()
    except (ValueError,RuntimeError,FileNotFoundError) as e:print('ERROR: '+str(e),file=sys.stderr);sys.exit(2)
