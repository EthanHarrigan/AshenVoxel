#!/usr/bin/env python3
"""Compose v2 same-material shape photographs and exact above-ground footprints."""
from pathlib import Path
import argparse,hashlib,json,math,sys
import numpy as np
from PIL import Image,ImageDraw,ImageFont
ROOT=Path(__file__).resolve().parents[2];BUILD=ROOT/'Tools/GroundCoverPreview/bin/shapes';OUT=ROOT/'Docs/RockShapeGallery'
sys.path.insert(0,str(ROOT/'Tools/TreePathtrace'))
from render_pathtrace import load_export
NAMES=['shape-angularboulder','shape-slab','shape-wedge','shape-spire','shape-splitoutcrop','shape-cluster']
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def font(n):
 try:return ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf',n)
 except OSError:return ImageFont.load_default()
def hull_area(mask):
 z,x=np.nonzero(mask);points=set()
 for a,b in zip(x,z):points.update(((int(a),int(b)),(int(a+1),int(b)),(int(a),int(b+1)),(int(a+1),int(b+1))))
 points=sorted(points)
 if len(points)<3:return 0
 def cross(o,a,b):return (a[0]-o[0])*(b[1]-o[1])-(a[1]-o[1])*(b[0]-o[0])
 lo=[];hi=[]
 for p in points:
  while len(lo)>=2 and cross(lo[-2],lo[-1],p)<=0:lo.pop()
  lo.append(p)
 for p in reversed(points):
  while len(hi)>=2 and cross(hi[-2],hi[-1],p)<=0:hi.pop()
  hi.append(p)
 poly=lo[:-1]+hi[:-1]
 return abs(sum(poly[i][0]*poly[(i+1)%len(poly)][1]-poly[(i+1)%len(poly)][0]*poly[i][1] for i in range(len(poly))))*.5
def footprints(name,out,input_dir=BUILD,output_prefix=''):
 meta,vol,colors,scale,input_sha,provenance=load_export(name,input_dir)
 # The specimen base is packed at y=2. Exclude the buried connecting root and
 # presentation pad; this is the exact union of occupied visible rock columns.
 mask=(vol[:,2:,:]==11).any(axis=1);board=Image.new('RGB',(1050,435),(20,28,34));d=ImageDraw.Draw(board)
 d.text((16,10),meta['scene']['label'].upper()+' / V'+str(meta['generation_version']['rock'])+' / ABOVE-GROUND FOOTPRINT',font=font(19),fill=(227,233,217))
 stats=[]
 for i,s in enumerate(meta['scene']['specimens']):
  offset=s['packed_offset_cells'];bounds=s['result'];minimum=bounds['BoundsMinCellInclusive'];maximum=bounds['BoundsMaxCellInclusive']
  x0=minimum['x']+offset['x'];x1=maximum['x']+offset['x']+1;z0=minimum['z']+offset['z'];z1=maximum['z']+offset['z']+1
  piece=mask[z0:z1,x0:x1];hull=hull_area(piece);area=int(piece.sum());ys,xs=np.nonzero(piece)
  assert area>0
  crop=piece[ys.min():ys.max()+1,xs.min():xs.max()+1]
  factor=max(1,min(14,270//max(crop.shape)));im=Image.fromarray(np.uint8(crop)*255,'L').resize((crop.shape[1]*factor,crop.shape[0]*factor),Image.Resampling.NEAREST)
  tile=Image.new('RGB',im.size,(190,210,173));board.paste(tile,(i*350+(350-im.width)//2,65+(270-im.height)//2),im)
  d.text((i*350+18,350),'Seed '+str(s['seed'])+' / '+str(area)+' columns',font=font(14),fill=(214,223,206))
  d.text((i*350+18,375),f'{area*.01:.2f} m² / convex fill {area/hull:.2f}',font=font(13),fill=(159,185,173))
  stats.append({'seed':s['seed'],'above_ground_columns':area,'area_m2':area*.01,'convex_hull_area_m2':hull*.01,'convex_fill_ratio':area/hull,
   'extent_m':[crop.shape[1]*.1,crop.shape[0]*.1],'note':'Grid union above y=2 presentation surface; buried root excluded. Convex fill is a geometric descriptor, not a realism score.'})
 d.text((16,411),'EXACT 10 CM CELLS / NO CAMERA OR LIGHTING IN THIS DIAGRAM / ALL GRANITE',font=font(12),fill=(214,183,120))
 path=out/(output_prefix+name+'-footprint.png');board.save(path)
 return {'name':name,'image':str(path.relative_to(ROOT)),'image_sha256':sha(path),'input_sha256':input_sha,'specimens':stats}
def sheet(paths,path,title,subtitle):
 images=[]
 for p in paths:
  im=Image.open(p).convert('RGB');im.thumbnail((900,540),Image.Resampling.LANCZOS);images.append(im)
 height=max(im.height for im in images)+14;rows=(len(images)+1)//2;board=Image.new('RGB',(1848,110+rows*height),(13,20,25));d=ImageDraw.Draw(board)
 d.text((24,16),title,font=font(27),fill=(230,234,221));d.text((24,57),subtitle,font=font(16),fill=(164,189,179))
 for i,im in enumerate(images):board.paste(im,(16+(i%2)*916,100+(i//2)*height))
 board.save(path);return {'image':str(path.relative_to(ROOT)),'image_sha256':sha(path),'inputs':[str(p.relative_to(ROOT)) for p in paths]}
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--draft',action='store_true');p.add_argument('--historical-input-dir',type=Path);a=p.parse_args();out=BUILD/'draft' if a.draft else OUT;out.mkdir(parents=True,exist_ok=True)
 provenance=json.loads((BUILD/'source-provenance.json').read_text());reports=[]
 if not a.draft:
  for name,h in provenance['source_sha256'].items():assert sha(ROOT/name)==h,'Current source changed: '+name
  for name,h in provenance['exports'].items():assert sha(BUILD/name)==h,'Data changed: '+name
 for name in NAMES:
  report=json.loads((out/(name+'.report.json')).read_text());assert sha(ROOT/report['image'])==report['image_sha256']
  assert sha(BUILD/(name+'.bin'))==report['input_sha256']
  assert sha(ROOT/'Tools/GroundCoverPreview/render_ground_cover.py')==report['tool_sha256']
  if not a.draft:assert report['spp']>=32
  reports.append(report)
 diagrams=[footprints(n,out) for n in NAMES]
 sheets=[sheet([out/(n+'.png') for n in NAMES],out/'shapes-comparison.png','SIX DISTINCT ROCK SHAPES / SAME GRANITE / THREE SEEDS','Actual version2 C# voxel matter / higher oblique view / offline CPU light / not Unity'),
  sheet([out/(n+'-footprint.png') for n in NAMES],out/'footprints-comparison.png','ROCK SHAPE FOOTPRINTS / EXACT ABOVE-GROUND CELLS','Ten-centimeter occupancy / buried connecting roots excluded / same Granite material')]
 historical=None
 if not a.draft:
  before=OUT/'before-v1-rock-granite.png';after=OUT/'after-v2-rock-granite.png'
  if before.exists() and after.exists():
   for image in [before,after]:
    r=json.loads(image.with_suffix('.report.json').read_text());assert sha(image)==r['image_sha256'];assert sha(ROOT/'Tools/GroundCoverPreview/render_ground_cover.py')==r['tool_sha256']
   sheets.append(sheet([before,after],out/'before-after.png','BEFORE V1 / AFTER V2 / SAME GRANITE AND THREE SEEDS','Actual historical v1 and current v2 cells / matched camera rules / offline CPU light / not Unity'))
  if a.historical_input_dir:
   old=a.historical_input_dir.resolve();old_proof=json.loads((old/'source-provenance.json').read_text())
   for name,h in old_proof['source_sha256'].items():assert sha(old/'source'/name)==h,'Historical snapshot changed: '+name
   before_report=json.loads((OUT/'before-v1-rock-granite.report.json').read_text());assert sha(old/'rock-granite.bin')==before_report['input_sha256']
   assert before_report['metadata']['generation_version']['rock']==1
   historical={'status':'PASS','note':'Frozen v1 source hashes and original voxel data verified as historical inputs, not checked against or relabelled as current v2 sources.',
    'input_sha256':sha(old/'rock-granite.bin'),'source_provenance':old_proof,
    'before_footprint':footprints('rock-granite',OUT,old,'before-v1-'),
    'after_footprint':footprints('rock-granite',OUT,ROOT/'Tools/GroundCoverPreview/bin','after-v2-')}
   sheets.append(sheet([OUT/'before-v1-rock-granite-footprint.png',OUT/'after-v2-rock-granite-footprint.png'],out/'before-after-footprints.png','BEFORE V1 / AFTER V2 / EXACT ABOVE-GROUND FOOTPRINTS','Camera-independent occupied columns / same material and seeds / buried roots excluded'))
  (out/'shape-gallery-validation.json').write_text(json.dumps({'status':'PASS','source_provenance':provenance,'shape_reports':reports,'footprints':diagrams,'sheets':sheets,
   'historical_before_comparison':historical,'tool_sha256':sha(Path(__file__)),'note':'Photographs use exact occupied voxel faces. Footprints are exact above-ground column unions, not reconstructed artistic outlines. Geometry tests and native Unity are separate evidence.'},indent=2)+'\n')
 print('PASS' if not a.draft else 'DRAFT','six shape panels and exact footprint diagrams saved:',out)
if __name__=='__main__':main()
