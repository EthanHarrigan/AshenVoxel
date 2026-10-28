"""Bounded regional learned coarse/base overview provider; no fine-decoder calls.

The injected query contract returns denormalized signed-sqrt learned elevation.
Tests may inject synthetic queries; only load_height_pipeline/model_query executes
actual pinned model predictions. This module never starts a server or inference.
"""
from pathlib import Path
import concurrent.futures
import hashlib
import json
import math
import os
import struct
import threading
import time
import zlib
import numpy as np
from coarse_overview import VERSION, sample_overview, sha, available_commit

SCHEMA='ashen-regional-learned-overview-v1'
FILTER='meter Gaussian sigma=spacing/2 truncate3; bilinear global lattice; 40m learned low frequency'
MAGIC=b'AVOV0001'

def canonical(value):return json.dumps(value,sort_keys=True,separators=(',',':'),allow_nan=False).encode()

class Busy(RuntimeError):pass
class Cancelled(RuntimeError):pass

class OverviewProvider:
    def __init__(self,job,cache,query_factory,*,fine_idle,commit=available_commit,min_commit=2*1024**3,load_commit=4*1024**3,strip_delay=.25,query_kind='injected-test-query',idle_grace=20.,disk_limit=256*1024**2,wait_seconds=60):
        self.job=job;self.cache=Path(cache);self.cache.mkdir(parents=True,exist_ok=True)
        self.query_kind=query_kind;self.factory=query_factory;self.fine_idle=fine_idle;self.commit=commit;self.min_commit=min_commit
        self.idle_grace=idle_grace;self.idle_since=time.monotonic();self.cleanup_pending=False;self.stop_monitor=threading.Event();self.load_commit=load_commit;self.strip_delay=strip_delay;self.disk_limit=disk_limit;self.wait_seconds=wait_seconds;self.lock=threading.RLock()
        self.pool=concurrent.futures.ThreadPoolExecutor(max_workers=1,thread_name_prefix='overview-lowfreq')
        self.active=None;self.last=None;self.closed=False;self.query=None;self.hits=0;self.model_queries=0
        self.identity={'schema':SCHEMA,'native_model_world_id':job['world_id'],'coarse_version':VERSION,'filter':FILTER,
                       'query_kind':query_kind,'source_sha256':{'coarse_overview.py':sha(Path(__file__).with_name('coarse_overview.py')),'overview_provider.py':sha(__file__)}}
        self.source_id=hashlib.sha256(canonical(self.identity)).hexdigest()
        self.monitor=threading.Thread(target=self._monitor_idle,name='overview-idle-admission',daemon=True);self.monitor.start()
    def validate(self,request):
        if request.get('native_model_world_id')!=self.job['world_id']:raise ValueError('Pinned native model job identity mismatch')
        names=('row0','col0','rows','cols','spacing_m')
        if any(type(request.get(k)) is not int for k in names):raise ValueError('Integer lattice/window fields required')
        r,c,h,w,s=(request[k] for k in names)
        if s not in (160,80) or not 1<=h<=65 or not 1<=w<=65 or abs(r)>100000 or abs(c)>100000:raise ValueError('Bounded160/80m window required')
        return {k:request[k] for k in names}
    def _key(self,window):return hashlib.sha256(canonical({'source_id':self.source_id,**window})).hexdigest()
    def submit(self,request):
        window=self.validate(request);key=self._key(window)
        with self.lock:
            if self.closed:raise Busy('Provider closed')
            if self.active is not None:
                if self.active['request_id']==key:return key
                raise Busy('One overview job admitted; retry after existing job')
            if self.last is not None and self.last['request_id']==key and self.last['state']=='READY':return key
            record={'request_id':key,'window':window,'state':'QUEUED','cancel':threading.Event(),'completed_rows':0,'error':None,'raw':None}
            self.active=record;record['future']=self.pool.submit(self._run,record);return key
    def _wait_admission(self,record):
        began=time.monotonic()
        while True:
            if record['cancel'].is_set() or self.closed:raise Cancelled('Cancelled before next model query')
            headroom=self.commit()
            try:idle=bool(self.fine_idle())
            except Exception:idle=False
            required=self.load_commit if self.query is None else self.min_commit
            memory=headroom is not None and headroom>=required
            if (not idle or not memory) and self.query is not None:
                close=getattr(self.query,'close',None)
                if close is not None:close()
                self.query=None
            with self.lock:record['state']='RUNNING' if idle and memory else 'WAIT_FINE' if not idle else 'WAIT_MEMORY'
            if idle and memory:return
            if time.monotonic()-began>=self.wait_seconds:raise Busy('Fine-priority or memory admission timed out; retry later')
            record['cancel'].wait(.1)
    def _run(self,record):
        try:
            key=record['request_id'];path=self.cache/(key+'.avov')
            if path.exists():
                if path.stat().st_size>65536:raise ValueError('Oversized overview cache')
                raw=path.read_bytes();meta,_=decode(raw)
                if meta['source_id']!=self.source_id or meta['window']!=record['window']:raise ValueError('Overview cache identity mismatch')
                with self.lock:self.hits+=1
                os.utime(path,None)
            else:
                w=record['window'];xs=np.arange(w['col0'],w['col0']+w['cols'],dtype='f8')*w['spacing_m'];zs=np.arange(w['row0'],w['row0']+w['rows'],dtype='f8')*w['spacing_m'];height=np.empty((w['rows'],w['cols']),dtype='f4')
                for row in range(0,w['rows'],4):
                    for col in range(0,w['cols'],17):
                        self._wait_admission(record)
                        def guarded_query(r0,r1,c0,c1):
                            self._wait_admission(record)
                            while self.query is None:
                                with self.lock:record['state']='LOADING'
                                self.query=self.factory()
                                self._wait_admission(record)
                            with self.lock:self.model_queries+=1
                            return self.query(r0,r1,c0,c1)
                        height[row:row+4,col:col+17]=sample_overview(guarded_query,xs[col:col+17],zs[row:row+4],w['spacing_m'])
                        if record['cancel'].wait(self.strip_delay):raise Cancelled('Cancelled between overview strips')
                    with self.lock:record['completed_rows']=min(row+4,w['rows'])
                if record['cancel'].is_set():raise Cancelled('Cancelled; output not published')
                meta={'schema':SCHEMA,'source_id':self.source_id,'native_model_world_id':self.job['world_id'],'window':w,**w,
                      'origin_x_m':float(xs[0]),'origin_z_m':float(zs[0]),'height_units':'meters','filter':FILTER,'query_kind':self.query_kind,'native_model_spacing_m':30.,'world_meters_per_native_sample':5.,'latent_spacing_world_m':40.,
                      'generator_scope':'Pinned learned coarse/base low-frequency output; fine residual decoder omitted',
                      'height_sha256':hashlib.sha256(height.astype('<f4').tobytes()).hexdigest(),'identity':self.identity}
                raw=encode(meta,height);temporary=path.with_suffix('.tmp');temporary.write_bytes(raw);os.replace(temporary,path)
                files=sorted(self.cache.glob('*.avov'),key=lambda p:p.stat().st_mtime_ns);total=sum(p.stat().st_size for p in files)
                for old in files:
                    if total<=self.disk_limit:break
                    total-=old.stat().st_size;old.unlink()
            with self.lock:
                if record['cancel'].is_set():raise Cancelled('Cancelled before response publication')
                record['raw']=raw;record['state']='RELEASING'
        except Exception as error:
            with self.lock:record['state']='CANCELLED' if isinstance(error,Cancelled) else 'FAILED';record['error']=type(error).__name__+': '+str(error)
        finally:
            with self.lock:
                if record['state']=='RELEASING':
                    if record['cancel'].is_set():record['state']='CANCELLED';record['raw']=None
                    else:record['state']='READY'
                self.last=record;self.active=None;self.idle_since=time.monotonic()
    def _monitor_idle(self):
        while not self.stop_monitor.wait(.1):
            with self.lock:
                if self.closed or self.active is not None or self.query is None or self.cleanup_pending:continue
            try:fine_busy=not bool(self.fine_idle())
            except Exception:fine_busy=True
            with self.lock:
                if self.closed or self.active is not None or self.query is None or self.cleanup_pending:continue
                if fine_busy or time.monotonic()-self.idle_since>=self.idle_grace:
                    self.cleanup_pending=True;self.pool.submit(self._idle_cleanup,fine_busy)
    def _idle_cleanup(self,fine_busy):
        # Runs on the SAME single inference executor. A newly reserved job wins
        # the recheck; no cleanup can race a model call or close its pipeline.
        with self.lock:
            try:
                if self.active is not None or self.query is None:return
                if not fine_busy and time.monotonic()-self.idle_since<self.idle_grace:return
                query=self.query;self.query=None;close=getattr(query,'close',None)
                if close is not None:close()
            finally:self.cleanup_pending=False
    def result(self,key):
        with self.lock:
            r=self.active if self.active is not None and self.active['request_id']==key else self.last
            if r is None or r['request_id']!=key:raise KeyError('Unknown or expired overview request')
            if r['state']=='READY':return r['raw']
            return {k:r[k] for k in ('request_id','window','state','completed_rows','error')}
    def cancel(self,key):
        with self.lock:
            if self.active is not None and self.active['request_id']==key:self.active['cancel'].set();return True
            return False
    def status(self):
        with self.lock:
            return {'ready':not self.closed,'accepting_requests':self.active is None,'model_loaded':self.query is not None,'available_commit_bytes':self.commit(),'source_id':self.source_id,'native_model_world_id':self.job['world_id'],'spacing_m':[160,80],
                    'active':None if self.active is None else {k:self.active[k] for k in ('request_id','state','completed_rows')},
                    'cache_hits':self.hits,'model_queries':self.model_queries,'queued_capacity':0,'max_window_side':65,'max_model_strip_nodes':[4,17],'strip_delay_seconds':self.strip_delay,
                    'model_residency':'One reusable coarse/base pipeline; released by serial executor after idle grace or fine-job arrival','idle_grace_seconds':self.idle_grace,'priority':'Fine5m jobs checked before each bounded coarse/base query; an in-flight model call is non-preemptible'}
    def close(self):
        with self.lock:
            self.closed=True
            if self.active is not None:self.active['cancel'].set()
        self.stop_monitor.set();self.monitor.join(timeout=3)
        self.pool.shutdown(wait=True,cancel_futures=True)
        if self.query is not None:
            close=getattr(self.query,'close',None)
            if close is not None:close()
        self.query=None

def encode(meta,height):
    metadata=canonical(meta);payload=height.astype('<f4').tobytes();raw=MAGIC+struct.pack('<I',len(metadata))+metadata+payload
    return raw+struct.pack('<I',zlib.crc32(raw)&0xffffffff)

def decode(raw):
    if len(raw)<16 or raw[:8]!=MAGIC or zlib.crc32(raw[:-4])&0xffffffff!=struct.unpack_from('<I',raw,len(raw)-4)[0]:raise ValueError('Overview magic/checksum mismatch')
    n=struct.unpack_from('<I',raw,8)[0]
    if n>16384 or n+16>len(raw):raise ValueError('Overview metadata bounds')
    meta=json.loads(raw[12:12+n]);w=meta['window'];h,c=w['rows'],w['cols']
    if not 1<=h<=65 or not 1<=c<=65 or len(raw)!=16+n+h*c*4:raise ValueError('Overview dimensions/length')
    data=np.frombuffer(raw,dtype='<f4',offset=12+n,count=h*c).reshape(h,c)
    if not np.isfinite(data).all() or hashlib.sha256(data.tobytes()).hexdigest()!=meta['height_sha256']:raise ValueError('Overview value/hash mismatch')
    return meta,data
