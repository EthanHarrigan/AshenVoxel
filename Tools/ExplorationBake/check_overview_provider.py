"""Fast injected-query transport/cache/priority checks; executes NO model inference."""
from pathlib import Path
import hashlib
import http.server
import json
import tempfile
import threading
import time
import unittest
import urllib.request
import numpy as np
from coarse_overview import sample_overview
from overview_provider import OverviewProvider,Busy,decode
from serve_overview import handler_for

class Checks(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.calls=[];self.idle=True;self.memory=10*1024**3
        def query(r0,r1,c0,c1):
            self.calls.append((r0,r1,c0,c1));x,z=np.meshgrid(np.arange(c0,c1)*40+17.5,np.arange(r0,r1)*40+17.5)
            h=1000+100*np.sin(x/470)+50*np.cos(z/330);return np.sign(h)*np.sqrt(abs(h))
        self.query=query;self.job={'world_id':'a'*64};self.p=self.make()
    def make(self):return OverviewProvider(self.job,self.temp.name,lambda:self.query,fine_idle=lambda:self.idle,commit=lambda:self.memory,strip_delay=0,wait_seconds=1)
    def tearDown(self):self.p.close();self.temp.cleanup()
    def request(self,**kwargs):return {'native_model_world_id':self.job['world_id'],'row0':-20,'col0':-17,'rows':41,'cols':41,'spacing_m':80,**kwargs}
    def done(self,key):
        end=time.monotonic()+3
        while time.monotonic()<end:
            value=self.p.result(key)
            if isinstance(value,bytes) or value['state'] in ('FAILED','CANCELLED'):return value
            time.sleep(.005)
        self.fail('job did not finish')
    def test_partition_and_actual_new_spacing(self):
        key=self.p.submit(self.request());raw=self.done(key);meta,height=decode(raw)
        xs=np.arange(-17,24)*80.;zs=np.arange(-20,21)*80.;whole=sample_overview(self.query,xs,zs,80)
        self.assertTrue(np.array_equal(height,whole));self.assertEqual(height.shape,(41,41));self.assertEqual(meta['native_model_world_id'],self.job['world_id'])
        self.assertEqual(meta['spacing_m'],80);self.assertTrue(len(self.calls)>1)
        # These are injected query outputs, not interpolated320m samples.
        self.assertGreater(np.ptp(height),0)
    def test_cache_and_spacing_identity(self):
        key=self.p.submit(self.request(rows=3,cols=3,spacing_m=160));raw=self.done(key);calls=len(self.calls)
        self.p.close();self.p=self.make();self.assertEqual(self.p.submit(self.request(rows=3,cols=3,spacing_m=160)),key)
        self.assertEqual(self.done(key),raw);self.assertEqual(len(self.calls),calls);self.assertEqual(self.p.hits,1)
        other=self.p.submit(self.request(rows=3,cols=3,spacing_m=80));self.assertNotEqual(key,other);self.assertIsInstance(self.done(other),bytes)
    def test_checksum_corruption_rejected(self):
        key=self.p.submit(self.request(rows=2,cols=2));raw=self.done(key);bad=bytearray(raw);bad[-8]^=1
        with self.assertRaises(ValueError):decode(bytes(bad))
        (Path(self.temp.name)/(key+'.avov')).write_bytes(bad);self.p.close();self.p=self.make();self.p.submit(self.request(rows=2,cols=2));self.assertEqual(self.done(key)['state'],'FAILED')
    def test_bounds(self):
        for extra in ({'rows':66},{'cols':0},{'spacing_m':320},{'row0':1.5},{'native_model_world_id':'b'*64},{'col0':100001}):
            with self.assertRaises(ValueError):self.p.submit(self.request(**extra))
        key=self.p.submit(self.request(rows=65,cols=65));self.assertEqual(decode(self.done(key))[1].shape,(65,65))
    def test_fine_priority_one_admission_cancel(self):
        self.idle=False;key=self.p.submit(self.request());time.sleep(.03)
        self.assertEqual(len(self.calls),0)
        with self.assertRaises(Busy):self.p.submit(self.request(col0=0))
        self.assertTrue(self.p.cancel(key));self.assertEqual(self.done(key)['state'],'CANCELLED');self.assertEqual(len(self.calls),0)
    def test_memory_wait_does_not_execute(self):
        self.memory=1;key=self.p.submit(self.request());time.sleep(.03);self.assertEqual(len(self.calls),0);self.assertEqual(self.p.result(key)['state'],'WAIT_MEMORY')
        self.memory=10*1024**3;self.assertIsInstance(self.done(key),bytes)
    def test_fine_arrival_between_strips(self):
        original=self.query
        def query(*args):
            value=original(*args);self.idle=False;return value
        self.p.factory=lambda:query;key=self.p.submit(self.request());time.sleep(.04);self.assertEqual(len(self.calls),1)
        self.p.cancel(key);self.assertEqual(self.done(key)['state'],'CANCELLED')
    def test_serial_idle_cleanup_and_fine_arrival(self):
        closed=[];threads=[];query=self.query
        class Tracked:
            def __call__(self,*args):threads.append(threading.get_ident());return query(*args)
            def close(self):closed.append(threading.get_ident())
        self.p.factory=Tracked;self.p.idle_grace=.05
        key=self.p.submit(self.request(rows=2,cols=2));self.assertIsInstance(self.done(key),bytes)
        deadline=time.monotonic()+1
        while not closed and time.monotonic()<deadline:time.sleep(.01)
        self.assertEqual(closed,[threads[0]]);self.assertIsNone(self.p.query)
        self.p.idle_grace=20;self.p.submit(self.request(rows=2,cols=2,col0=0));self.done(self.p._key(self.p.validate(self.request(rows=2,cols=2,col0=0))));self.idle=False
        deadline=time.monotonic()+1
        while len(closed)<2 and time.monotonic()<deadline:time.sleep(.01)
        self.assertEqual(len(closed),2)
    def test_new_reserved_job_prevents_queued_cleanup(self):
        closed=[];loaded=[];query=self.query
        class Tracked:
            def __init__(self):loaded.append(1)
            def __call__(self,*args):return query(*args)
            def close(self):closed.append(1)
        self.p.factory=Tracked;key=self.p.submit(self.request(rows=2,cols=2));self.done(key)
        release=threading.Event();block=self.p.pool.submit(lambda:release.wait(1));self.p.pool.submit(self.p._idle_cleanup,True)
        key=self.p.submit(self.request(rows=2,cols=2,col0=0));release.set();self.assertIsInstance(self.done(key),bytes)
        self.assertEqual(len(loaded),1);self.assertEqual(closed,[])
    def test_http_binary_status_cancel(self):
        server=http.server.ThreadingHTTPServer(('127.0.0.1',0),handler_for(self.p));thread=threading.Thread(target=server.serve_forever,daemon=True);thread.start();url='http://127.0.0.1:'+str(server.server_port)
        try:
            status=json.load(urllib.request.urlopen(url+'/status'));self.assertEqual(status['spacing_m'],[160,80]);self.assertEqual(status['max_window_side'],65)
            data=json.dumps(self.request(rows=2,cols=2)).encode();req=urllib.request.Request(url+'/overview',data=data,headers={'Content-Type':'application/json'})
            key=json.load(urllib.request.urlopen(req))['request_id'];self.done(key)
            with urllib.request.urlopen(url+'/overview/'+key) as response:self.assertEqual(decode(response.read())[1].shape,(2,2))
        finally:server.shutdown();server.server_close();thread.join()

if __name__=='__main__':
    import datetime
    suite=unittest.defaultTestLoader.loadTestsFromTestCase(Checks);result=unittest.TextTestRunner(verbosity=2).run(suite)
    root=Path(__file__).resolve().parents[2];out=root/'.terrain-cache/regional-overview-checks';out.mkdir(parents=True,exist_ok=True)
    report=out/(datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%d-%H%M%S-%f')+'.json')
    files=['coarse_overview.py','overview_provider.py','serve_overview.py','check_overview_provider.py']
    report.write_text(json.dumps({'status':'PASS' if result.wasSuccessful() else 'FAIL','tests':result.testsRun,
        'scope':'Injected deterministic query contract/cache/transport tests only; no actual model inference, learned quality or throughput claim',
        'source_sha256':{name:hashlib.sha256(Path(__file__).with_name(name).read_bytes()).hexdigest() for name in files}},indent=2)+'\n')
    print(report);raise SystemExit(0 if result.wasSuccessful() else 1)
