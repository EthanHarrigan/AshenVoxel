import collections,json,math
import compare as c

def transition_metrics(cells):
    lower={(x,z) for x,y,z in cells if y==c.SEAM-1}
    upper={(x,z) for x,y,z in cells if y==c.SEAM}
    missing=upper-lower
    distance=max((min(abs(x-xx)+abs(z-zz) for xx,zz in lower) for x,z in missing),default=0)
    return dict(seam_upper_cells=len(upper),seam_lower_cells=len(lower),unsupported_upper_cells=len(missing),max_unsupported_horizontal_cells=distance)

def build(site,sample):
    original=c.wood(site['id'],'baseline');narrow=c.wood(site['id'],'narrow')
    base={p:color for p,color in original.items() if p[1]>=c.SEAM}
    base.update({p:original.get(p,color) for p,color in narrow.items() if p[1]<c.SEAM})
    before=transition_metrics(base)
    core={(x,z) for x,y,z in original if y==c.SEAM}
    added=0
    # Preserve the original continuous central shaft under the unchanged seam.
    # Only cells that already exist in the canonical tree may be restored;
    # no invented scaffold or top-layer overhang is used to hide the splice.
    for p,color in original.items():
        if p[1]<c.SEAM and (p[0],p[2]) in core:
            if p not in base:added+=1
            base[p]=color
    result={p:color for p,color in base.items() if p[1]>=c.SEAM};anchor=site['original_y'];max_displacement=0
    for (x,z),col in c.columns(base).items():
        bottom=col[0][0]
        if bottom>=c.SEAM:continue
        low,*_=sample(x,z);target=math.floor((low-anchor)/.1+1e-7)
        assert target<c.SEAM-3
        max_displacement=max(max_displacement,abs(target-bottom)*.1);ratio=(c.SEAM-target)/(c.SEAM-bottom)
        for y,color in col:
            if y>=c.SEAM:continue
            lo=math.floor(target+(y-bottom)*ratio+1e-7);hi=math.ceil(target+(y+1-bottom)*ratio-1e-7)
            for ny in range(lo,min(c.SEAM,hi)):result[x,ny,z]=color
    return original,base,result,dict(before_splice=before,after_base_splice=transition_metrics(base),after_conforming_splice=transition_metrics(result),canonical_shaft_cells_restored=added,max_base_displacement_m=max_displacement)

if __name__=='__main__':
    rows=[]
    for site in json.loads((c.HERE/'sites.json').read_text()):
        terr=c.terrain(site['id']);sample=c.overlap_sampler(site,terr[0],terr[1]);original,base,result,extra=build(site,sample)
        rows.append(dict(site_id=site['id'],original_seam=transition_metrics(original),**extra,**c.metrics(result,original,sample,site['original_y'])))
    (c.HERE/'seam-check.json').write_text(json.dumps(rows,indent=2),encoding='utf-8');print(json.dumps(rows,indent=2))
