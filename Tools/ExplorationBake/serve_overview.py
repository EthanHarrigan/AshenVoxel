"""Opt-in loopback 160/80/40 m learned overview service (separate from the 5 m provider).

No service is started by import. A pinned 320 bake job is accepted unchanged;
coarse/base weights are loaded lazily, and no fine decoder is created/called.
160/80 m windows use overview_provider (unchanged identity); 40 m windows use
overview_provider40 with its own identity. Both share one loaded model.
"""
from pathlib import Path
import argparse
import gc
import os
import http.server
import json
import subprocess
import sys
import threading
import urllib.parse
import urllib.request
from coarse_overview import b,load_height_pipeline,model_query
from overview_provider import OverviewProvider,Busy,canonical

class LearnedQuery:
    def __init__(self,job):self.pipeline,_=load_height_pipeline(job);self.query=model_query(self.pipeline)
    def __call__(self,*window):return self.query(*window)
    def close(self):
        self.query=None;self.pipeline=None;gc.collect()
        import torch
        if torch.cuda.is_available():torch.cuda.empty_cache()

class SharedQuery:
    """One model for several providers: serialised calls, released when the last user closes."""
    def __init__(self,job):self.job=job;self.lock=threading.Lock();self.users=0;self.inner=None
    def factory(self):
        with self.lock:
            if self.inner is None:self.inner=LearnedQuery(self.job)
            self.users+=1
        return _Handle(self)
class _Handle:
    def __init__(self,shared):self.shared=shared;self.open=True
    def __call__(self,*window):
        with self.shared.lock:return self.shared.inner(*window)
    def close(self):
        with self.shared.lock:
            if not self.open:return
            self.open=False;self.shared.users-=1
            if self.shared.users==0 and self.shared.inner is not None:self.shared.inner.close();self.shared.inner=None

def fine_idle(url):
    try:
        with urllib.request.urlopen(url+'/status',timeout=2) as response:state=json.load(response)
    except OSError:
        return True  # No 5 m service running: nothing has priority over far terrain.
    return state.get('ready') is True and state.get('in_flight')==0 and state.get('queued')==0

def handler_for(provider,provider40=None):
    def pick_spacing(spacing):return provider40 if provider40 is not None and spacing==40 else provider
    def pick_key(key):
        if provider40 is not None:
            try:provider40.result(key);return provider40
            except KeyError:pass
        return provider
    class Handler(http.server.BaseHTTPRequestHandler):
        def log_message(self,*args):pass
        def reply(self,code,value):
            raw=value if isinstance(value,bytes) else canonical(value)
            self.send_response(code);self.send_header('Content-Length',str(len(raw)))
            self.send_header('Content-Type','application/vnd.ashen.overview-v1' if isinstance(value,bytes) else 'application/json');self.end_headers();self.wfile.write(raw)
        def do_GET(self):
            url=urllib.parse.urlsplit(self.path)
            if url.path=='/status':
                query=urllib.parse.parse_qs(url.query);spacing=int(query.get('spacing',['160'])[0]) if query.get('spacing',['160'])[0].isdigit() else 160
                self.reply(200,pick_spacing(spacing).status());return
            if not url.path.startswith('/overview/'):self.reply(404,{'error':'Not found'});return
            key=url.path.split('/')[-1]
            try:
                value=pick_key(key).result(key);self.reply(200 if isinstance(value,bytes) else 202,value)
            except KeyError as error:self.reply(404,{'error':str(error)})
        def do_POST(self):
            if urllib.parse.urlsplit(self.path).path!='/overview':self.reply(404,{'error':'Not found'});return
            try:
                length=int(self.headers.get('Content-Length','0'))
                if not 2<=length<=4096:raise ValueError('JSON body must be2..4096bytes')
                value=json.loads(self.rfile.read(length));target=pick_spacing(value.get('spacing_m'))
                key=target.submit(value);self.reply(202,{'request_id':key,'source_id':target.source_id})
            except (ValueError,TypeError,KeyError,AttributeError) as error:self.reply(400,{'error':str(error)})
            except Busy as error:self.reply(429,{'error':str(error)})
        def do_DELETE(self):
            url=urllib.parse.urlsplit(self.path)
            if not url.path.startswith('/overview/'):self.reply(404,{'error':'Not found'});return
            key=url.path.split('/')[-1];self.reply(200,{'cancel_requested':pick_key(key).cancel(key)})
    return Handler

def below_normal_priority():
    if os.name=='nt':
        import ctypes
        kernel=ctypes.windll.kernel32;kernel.GetCurrentProcess.restype=ctypes.c_void_p
        kernel.SetPriorityClass.argtypes=(ctypes.c_void_p,ctypes.c_uint32)
        if not kernel.SetPriorityClass(kernel.GetCurrentProcess(),0x4000):raise OSError('Cannot set overview helper BelowNormal priority')
    else:os.nice(5)

def run_queued_prebake():
    """Bake-Terrain-80m.cmd starts this service when its 160/80 m pre-bake ends. If a further
    pre-bake stage is enabled (.terrain-cache/prebake-overview/stage40.enabled), finish it first."""
    here=Path(__file__).resolve().parent;state=here.parents[1]/'.terrain-cache/prebake-overview'
    if not (state/'stage40.enabled').exists():return
    progress=state/'progress40.json'
    if progress.exists():
        try:
            if json.loads(progress.read_text()).get('status')=='PASS':return
        except ValueError:pass
    print('Running the queued 40 m pre-bake before serving...',flush=True)
    run=subprocess.run([sys.executable,'-X','utf8',str(here/'prebake_overview_cache.py'),'--spacings','40','--status',str(progress)])
    if run.returncode:raise SystemExit('40 m pre-bake stopped; the launcher will retry')

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--job',required=True,type=Path);p.add_argument('--cache-dir',required=True,type=Path)
    p.add_argument('--port',type=int,default=8788);p.add_argument('--fine-url',default='http://127.0.0.1:8787');p.add_argument('--strip-delay',type=float,default=.25)
    p.add_argument('--idle-grace',type=float,default=20);p.add_argument('--min-commit-mib',type=int,default=2048);p.add_argument('--load-commit-mib',type=int,default=4096)
    p.add_argument('--disk-limit-mib',type=int,default=1024);p.add_argument('--no-queued-prebake',action='store_true');a=p.parse_args()
    if not 0<=a.strip_delay<=5 or not 0<=a.idle_grace<=20 or a.min_commit_mib<1024 or a.load_commit_mib<a.min_commit_mib:raise ValueError('Unsafe overview admission bounds')
    if not a.no_queued_prebake:run_queued_prebake()
    below_normal_priority();job=b.read_job(a.job);shared=SharedQuery(job)
    common=dict(fine_idle=lambda:fine_idle(a.fine_url),min_commit=a.min_commit_mib*1024**2,load_commit=a.load_commit_mib*1024**2,
                strip_delay=a.strip_delay,idle_grace=a.idle_grace,query_kind='pinned-coarse-base-model',disk_limit=a.disk_limit_mib*1024**2)
    provider=OverviewProvider(job,a.cache_dir,shared.factory,**common)
    try:
        from overview_provider40 import OverviewProvider40
        provider40=OverviewProvider40(job,a.cache_dir,shared.factory,**common)
    except ImportError:provider40=None
    server=http.server.ThreadingHTTPServer(('127.0.0.1',a.port),handler_for(provider,provider40));server.daemon_threads=True
    print(json.dumps({'listening':a.port,**provider.status()}),flush=True)
    try:server.serve_forever()
    finally:
        server.server_close();provider.close()
        if provider40 is not None:provider40.close()
if __name__=='__main__':main()
