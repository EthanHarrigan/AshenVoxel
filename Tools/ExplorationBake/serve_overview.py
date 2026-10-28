"""Opt-in loopback160/80m learned overview service (separate from5m provider).

No service is started by import. A pinned320 bake job is accepted unchanged;
coarse/base weights are loaded lazily, and no fine decoder is created/called.
"""
from pathlib import Path
import argparse
import gc
import os
import http.server
import json
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

def fine_idle(url):
    with urllib.request.urlopen(url+'/status',timeout=2) as response:state=json.load(response)
    return state.get('ready') is True and state.get('in_flight')==0 and state.get('queued')==0

def handler_for(provider):
    class Handler(http.server.BaseHTTPRequestHandler):
        def reply(self,code,value):
            raw=value if isinstance(value,bytes) else canonical(value)
            self.send_response(code);self.send_header('Content-Length',str(len(raw)))
            self.send_header('Content-Type','application/vnd.ashen.overview-v1' if isinstance(value,bytes) else 'application/json');self.end_headers();self.wfile.write(raw)
        def do_GET(self):
            if self.path=='/status':self.reply(200,provider.status());return
            if not self.path.startswith('/overview/'):self.reply(404,{'error':'Not found'});return
            try:
                value=provider.result(self.path.split('/')[-1]);self.reply(200 if isinstance(value,bytes) else 202,value)
            except KeyError as error:self.reply(404,{'error':str(error)})
        def do_POST(self):
            if self.path!='/overview':self.reply(404,{'error':'Not found'});return
            try:
                length=int(self.headers.get('Content-Length','0'))
                if not 2<=length<=4096:raise ValueError('JSON body must be2..4096bytes')
                value=json.loads(self.rfile.read(length));key=provider.submit(value);self.reply(202,{'request_id':key,'source_id':provider.source_id})
            except (ValueError,TypeError,KeyError) as error:self.reply(400,{'error':str(error)})
            except Busy as error:self.reply(429,{'error':str(error)})
        def do_DELETE(self):
            if not self.path.startswith('/overview/'):self.reply(404,{'error':'Not found'});return
            self.reply(200,{'cancel_requested':provider.cancel(self.path.split('/')[-1])})
    return Handler

def below_normal_priority():
    if os.name=='nt':
        import ctypes
        kernel=ctypes.windll.kernel32;kernel.GetCurrentProcess.restype=ctypes.c_void_p
        kernel.SetPriorityClass.argtypes=(ctypes.c_void_p,ctypes.c_uint32)
        if not kernel.SetPriorityClass(kernel.GetCurrentProcess(),0x4000):raise OSError('Cannot set overview helper BelowNormal priority')
    else:os.nice(5)

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--job',required=True,type=Path);p.add_argument('--cache-dir',required=True,type=Path)
    p.add_argument('--port',type=int,default=8788);p.add_argument('--fine-url',default='http://127.0.0.1:8787');p.add_argument('--strip-delay',type=float,default=.25)
    p.add_argument('--idle-grace',type=float,default=20);p.add_argument('--min-commit-mib',type=int,default=2048);p.add_argument('--load-commit-mib',type=int,default=4096);a=p.parse_args()
    if not 0<=a.strip_delay<=5 or not 0<=a.idle_grace<=20 or a.min_commit_mib<1024 or a.load_commit_mib<a.min_commit_mib:raise ValueError('Unsafe overview admission bounds')
    below_normal_priority();job=b.read_job(a.job);provider=OverviewProvider(job,a.cache_dir,lambda:LearnedQuery(job),fine_idle=lambda:fine_idle(a.fine_url),
        min_commit=a.min_commit_mib*1024**2,load_commit=a.load_commit_mib*1024**2,strip_delay=a.strip_delay,idle_grace=a.idle_grace,query_kind='pinned-coarse-base-model')
    server=http.server.ThreadingHTTPServer(('127.0.0.1',a.port),handler_for(provider));server.daemon_threads=True
    print(json.dumps({'listening':a.port,**provider.status()}),flush=True)
    try:server.serve_forever()
    finally:server.server_close();provider.close()
if __name__=='__main__':main()
