"""Second CPU comparison: retain canonical pigments when narrowing the base."""
import collections,hashlib,json,math,time
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import compare as c

OUT=c.HERE/'retained-pigments';OUT.mkdir(exist_ok=True)

def corrected(site,sample):
    start=time.perf_counter();original=c.wood(site['id'],'baseline');narrow=c.wood(site['id'],'narrow')
    base={p:color for p,color in original.items() if p[1]>=c.SEAM}
    reused=0;new=0
    for p,color in narrow.items():
        if p[1]>=c.SEAM:continue
        if p in original:color=original[p];reused+=1
        else:new+=1
        base[p]=color
    anchor=site['original_y'];result={p:color for p,color in base.items() if p[1]>=c.SEAM}
    maximum_displacement=0
    for (x,z),col in c.columns(base).items():
        bottom=col[0][0]
        if bottom>=c.SEAM:continue
        low,*_=sample(x,z);target=math.floor((low-anchor)/.1+1e-7)
        assert target<c.SEAM-3
        maximum_displacement=max(maximum_displacement,abs(target-bottom)*.1)
        ratio=(c.SEAM-target)/(c.SEAM-bottom)
        for y,color in col:
            if y>=c.SEAM:continue
            lo=math.floor(target+(y-bottom)*ratio+1e-7);hi=math.ceil(target+(y+1-bottom)*ratio-1e-7)
            for ny in range(lo,min(c.SEAM,hi)):result[x,ny,z]=color
    return original,base,result,dict(canonical_lower_pigments_reused=reused,new_narrow_only_cells=new,max_base_displacement_m=maximum_displacement,
        python_shape_seconds_including_loading=time.perf_counter()-start,
        caveat='Python full dictionaries are offline diagnostics, not a runtime cost estimate. A future implementation must cache shared base metadata and bound per-instance work.')

def main():
    rows=[];panels=[];started=time.perf_counter()
    font=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',16)
    for site in json.loads((c.HERE/'sites.json').read_text()):
        terr=c.terrain(site['id']);sample=c.overlap_sampler(site,terr[0],terr[1]);original,base,result,extra=corrected(site,sample)
        variants=[('original',original),('narrow-retained-pigment',base),('taper-retained-pigment',result)]
        for name,cells in variants:
            row=dict(site_id=site['id'],slope=site['slope'],variant=name,**c.metrics(cells,original,sample,site['original_y']),**extra)
            rows.append(row)
        (OUT/'metrics.json').write_text(json.dumps(rows,indent=2),encoding='utf-8')
        for view in [0,1]:
            joined=Image.new('RGB',(1440,450),(17,23,26));draw=ImageDraw.Draw(joined)
            draw.text((10,5),f"NATURAL {site['slope']:.2f} DEGREE SITE / ACTUAL 10cm WOOD / CPU SHAPE STUDY, NOT UNITY",font=font,fill='white')
            for index,(name,cells) in enumerate(variants):
                image=c.render(site,cells,site['original_y'],terr,view);joined.paste(image,(index*480,90))
                row=rows[-3+index]
                draw.text((index*480+10,35),name.replace('-',' '),font=font,fill='white')
                draw.text((index*480+10,60),f"gap {row['max_basal_gap_m']:.2f}m / burial {row['max_basal_burial_m']:.2f}m",font=font,fill=(216,188,145))
            path=OUT/f"site-{site['id']}-view-{view}.png";joined.save(path);panels.append(joined)
            print('Retained-pigment comparison',path.name,round(time.perf_counter()-started,1),'seconds',flush=True)
    sheet=Image.new('RGB',(1440,len(panels)*450))
    for i,p in enumerate(panels):sheet.paste(p,(0,i*450))
    sheet.save(OUT/'comparison.png')
    manifest={'scope':'Offline geometry preview, not Unity. Two actual natural mature placements. Unchanged canonical wood and pigments above3m; foliage not exported or rendered. Exact positive-area rotated cell overlap, but no runtime collision/material mutation implementation.',
        'source_sha256':{str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in [Path(__file__),c.HERE/'compare.py',c.HERE/'Export.cs',c.HERE/'export.py',c.HERE/'provenance.json']},
        'input_sha256':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in list(c.HERE.glob('wood-*.bin'))+list(c.HERE.glob('terrain-*.bin'))+[c.HERE/'sites.json']},'rows':rows}
    (OUT/'manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
if __name__=='__main__':main()
