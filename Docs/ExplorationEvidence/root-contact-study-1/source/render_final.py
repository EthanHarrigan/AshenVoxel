from pathlib import Path
import hashlib,json,math,time
import numpy as np
from PIL import Image,ImageDraw,ImageFont
import compare as original_renderer
from seam_check import build,transition_metrics

HERE=Path(__file__).resolve().parent;OUT=HERE/'continuous-shaft';OUT.mkdir(exist_ok=True)
# Preserve prior comparison source while it is still rendering. Reuse the
# exact existing renderer with an explicit camera direction as its sole change.
source=(HERE/'compare.py').read_text(encoding='utf-8')
old='horizontal=np.array([-.75,0,-.66]) if view==0 else np.array([.74,0,.67])'
assert source.count(old)==1
target=HERE/'compare_camera.py';target.write_text(source.replace(old,'horizontal=np.asarray(view,dtype=float)'),encoding='utf-8')
import compare_camera as c

def main():
    panels=[];rows=[];started=time.perf_counter();font=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',16)
    for site in json.loads((HERE/'sites.json').read_text()):
        terr=c.terrain(site['id']);sample=c.overlap_sampler(site,terr[0],terr[1]);old,base,result,extra=build(site,sample)
        # The actual four-metre terrain gradient determines downhill; a side
        # camera independently inspects the root silhouette and upper splice.
        a=terr[0];gx=(float(a['bare'][128,148])-float(a['bare'][128,108]))/40
        gz=(float(a['bare'][148,128])-float(a['bare'][108,128]))/40
        downhill=np.array([-gx,0,-gz]);downhill/=np.linalg.norm(downhill)
        side=np.array([-downhill[2],0,downhill[0]])
        row=dict(site_id=site['id'],slope=site['slope'],**extra,**c.metrics(result,old,sample,site['original_y']))
        row['baseline']=c.metrics(old,old,sample,site['original_y']);rows.append(row)
        for view,name in [(downhill,'downhill'),(side,'side'),(-downhill,'uphill')]:
            joined=Image.new('RGB',(960,450),(17,23,26));draw=ImageDraw.Draw(joined)
            draw.text((10,4),f"SITE {site['id']} / {site['slope']:.2f} DEG / {name.upper()} VIEW / CPU ACTUAL-DATA STUDY",font=font,fill='white')
            for i,(title,cells,data) in enumerate([('Original mature base',old,row['baseline']),('Continuous trunk + terrain-following base',result,row)]):
                img=c.render(site,cells,site['original_y'],terr,view);joined.paste(img,(i*480,90))
                draw.text((i*480+10,34),title,font=font,fill='white')
                draw.text((i*480+10,59),f"gap {data['max_basal_gap_m']:.2f}m / burial {data['max_basal_burial_m']:.2f}m",font=font,fill=(216,188,145))
            joined.save(OUT/f"site-{site['id']}-{name}.png");panels.append(joined)
            print('Final comparison',site['id'],name,round(time.perf_counter()-started,1),'seconds',flush=True)
        (OUT/'metrics.json').write_text(json.dumps(rows,indent=2),encoding='utf-8')
    sheet=Image.new('RGB',(960,450*len(panels)))
    for i,p in enumerate(panels):sheet.paste(p,(0,i*450))
    sheet.save(OUT/'comparison.png')
    paths=[Path(__file__),HERE/'seam_check.py',HERE/'compare_camera.py',HERE/'compare.py',HERE/'Export.cs',HERE/'export.py',HERE/'provenance.json']
    report=dict(scope='CPU actual-data appearance/geometry diagnostic only. Actual globally quantized terrain and wood, no foliage/native runtime/collision adoption. Canonical upper geometry/pigment exact, below3m continuous original central shaft, narrowed root flare and per-column terrain-following taper. Different matter volume is explicitly measured; not conserved physics. Root input world and old artifacts untouched.',
        source_sha256={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in paths},
        input_sha256={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in list(HERE.glob('wood-*.bin'))+list(HERE.glob('terrain-*.bin'))+[HERE/'sites.json']},rows=rows)
    (OUT/'manifest.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
if __name__=='__main__':main()
