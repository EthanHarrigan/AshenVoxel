#!/usr/bin/env python3
"""Convert explicit Azgaar JSON or meter-valued elevation TIFF into AVWF0001.

This is an offline control-atlas converter, not a diffusion model or voxelizer.
Only NumPy is required for JSON. TIFF additionally requires tifffile or rasterio.
"""
from pathlib import Path
import argparse,hashlib,json,math,os,struct,zlib
import numpy as np

MAGIC=b'AVWF0001'
MAX_SAMPLES=4_194_304
MAX_INPUT_BYTES=64*1024*1024
MAX_HEIGHT=1_000_000
MAPPING_VERSION='azgaar-control-atlas-v2'
RECORD=np.dtype([('height','<f4'),('biome','u1'),('land','u1')])

def finite(value,name,positive=False):
    if isinstance(value,bool) or not isinstance(value,(int,float)) or not math.isfinite(value):
        raise ValueError(name+' must be a finite number')
    if positive and value<=0:raise ValueError(name+' must be positive')
    return float(value)

def integer(value,name,minimum=0,maximum=2**31-1):
    if isinstance(value,bool) or not isinstance(value,int) or value<minimum or value>maximum:
        raise ValueError(f'{name} must be an integer in {minimum}..{maximum}')
    return value

def dimensions(cols,rows):
    if isinstance(cols,bool) or isinstance(rows,bool) or not isinstance(cols,int) or not isinstance(rows,int):
        raise ValueError('Atlas dimensions must be integers')
    if cols<2 or rows<2 or cols*rows>MAX_SAMPLES:
        raise ValueError('Atlas dimensions must be >=2 and total samples <=4,194,304')

def write_avwf(path,heights,biomes,land,origin_x,origin_z,extent_x,extent_z,source_identity):
    """Write atomic AVWF0001; all channels are2D [rows,columns], heights in meters."""
    h=np.asarray(heights);b=np.asarray(biomes);l=np.asarray(land)
    if h.ndim!=2 or b.shape!=h.shape or l.shape!=h.shape:raise ValueError('Atlas channels must have equal two-dimensional shapes')
    rows,cols=h.shape;dimensions(cols,rows)
    if h.dtype.kind not in 'fiu' or not np.isfinite(h).all() or np.any(np.abs(h.astype(np.float64))>MAX_HEIGHT):
        raise ValueError('Atlas heights must be finite meters within +/-1,000,000')
    if b.dtype.kind not in 'iu' or np.any(b<0) or np.any(b>255):raise ValueError('Source biomes must be integer bytes')
    if l.dtype.kind not in 'biu' or np.any((l!=0)&(l!=1)):raise ValueError('Land flags must be boolean or integer0/1')
    ox=finite(origin_x,'origin_x');oz=finite(origin_z,'origin_z')
    ex=finite(extent_x,'extent_x',True);ez=finite(extent_z,'extent_z',True)
    identity=source_identity if isinstance(source_identity,str) else json.dumps(source_identity,sort_keys=True,separators=(',',':'),ensure_ascii=False,allow_nan=False)
    if not identity.strip():raise ValueError('Source identity cannot be blank')
    identity_bytes=identity.encode('utf-8')
    if len(identity_bytes)>4096:raise ValueError('Source identity exceeds4,096 UTF-8 bytes')
    records=np.empty(h.shape,dtype=RECORD);records['height']=h;records['biome']=b;records['land']=l
    raw=MAGIC+struct.pack('<ii4dH',cols,rows,ox,oz,ex,ez,len(identity_bytes))+identity_bytes+records.tobytes(order='C')
    raw+=struct.pack('<I',zlib.crc32(raw)&0xffffffff)
    path=Path(path);path.parent.mkdir(parents=True,exist_ok=True);temporary=path.with_name(path.name+'.tmp')
    try:
        with temporary.open('wb') as f:f.write(raw);f.flush();os.fsync(f.fileno())
        os.replace(temporary,path)
    finally:
        if temporary.exists():temporary.unlink()
    return hashlib.sha256(raw).hexdigest()

def read_avwf(path):
    """Strict independent offline reader, useful for conversion validation."""
    path=Path(path)
    if path.stat().st_size>MAX_SAMPLES*6+4200:raise ValueError('Atlas exceeds bounded payload')
    raw=path.read_bytes()
    if len(raw)<54 or len(raw)>MAX_SAMPLES*6+4200:raise ValueError('Invalid atlas size')
    if raw[:8]!=MAGIC:raise ValueError('Unsupported atlas magic/version')
    if zlib.crc32(raw[:-4])&0xffffffff!=struct.unpack_from('<I',raw,len(raw)-4)[0]:raise ValueError('Atlas checksum mismatch')
    cols,rows,ox,oz,ex,ez,size=struct.unpack_from('<ii4dH',raw,8);dimensions(cols,rows)
    if size<1 or size>4096 or len(raw)!=54+size+cols*rows*6:raise ValueError('Atlas size does not match dimensions')
    finite(ox,'origin_x');finite(oz,'origin_z');finite(ex,'extent_x',True);finite(ez,'extent_z',True)
    identity=raw[50:50+size].decode('utf-8',errors='strict')
    if not identity.strip():raise ValueError('Empty atlas identity')
    records=np.frombuffer(raw,dtype=RECORD,offset=50+size,count=cols*rows).reshape(rows,cols)
    h=records['height'];land=records['land']
    if not np.isfinite(h).all() or np.any(np.abs(h)>MAX_HEIGHT) or np.any(land>1):raise ValueError('Invalid atlas channel values')
    return {'columns':cols,'rows':rows,'origin_x':ox,'origin_z':oz,'extent_x':ex,'extent_z':ez,
        'source_identity':identity,'heights':h.copy(),'biomes':records['biome'].copy(),'land':land.astype(bool)}

def _constant(value):raise ValueError('Nonfinite JSON literal is unsupported: '+value)

def parse_azgaar(path,sampling='polygon'):
    path=Path(path)
    if path.suffix.lower()=='.map':raise ValueError('Native Azgaar .map requires graph reconstruction. Open it in Azgaar and export Full JSON or Pack Cells JSON.')
    with path.open('rb') as f:raw=f.read(MAX_INPUT_BYTES+1)
    if len(raw)>MAX_INPUT_BYTES:raise ValueError('Input exceeds64 MiB conversion limit')
    try:data=json.loads(raw.decode('utf-8-sig'),parse_constant=_constant)
    except (UnicodeError,json.JSONDecodeError) as e:raise ValueError('Expected Azgaar Full JSON or Pack Cells JSON; .map/GeoJSON need explicit conversion') from e
    if not isinstance(data,dict) or not isinstance(data.get('info'),dict):raise ValueError('Missing Azgaar info object')
    info=data['info'];width=finite(info.get('width'),'info.width',True);height=finite(info.get('height'),'info.height',True)
    if width>1e9 or height>1e9:raise ValueError('Source canvas dimensions exceed supported range')
    pack=data.get('pack',data.get('cells'))
    if not isinstance(pack,dict) or not isinstance(pack.get('cells'),list):raise ValueError('Expected pack.cells or cells.cells array; Minimal/Grid-only exports do not carry packed biomes')
    source_cells=pack['cells']
    if not 1<=len(source_cells)<=200_000:raise ValueError('Expected1..200,000 packed cells')
    vertices={};source_vertices=pack.get('vertices',[])
    if not isinstance(source_vertices,list) or len(source_vertices)>600_000:raise ValueError('Expected at most600,000 explicit vertices')
    for v in source_vertices:
        if not isinstance(v,dict):raise ValueError('Invalid vertex record')
        i=integer(v.get('i'),'vertex.i')
        if i in vertices:raise ValueError('Duplicate vertex ID')
        vertices[i]=_point(v.get('p'),'vertex.p')
        if max(abs(a) for a in vertices[i])>max(width,height)*4:raise ValueError('Vertex coordinates exceed supported canvas bounds')
    biome_ids=set()
    for biome in pack.get('biomes',[]):
        if not isinstance(biome,dict):raise ValueError('Unsupported legacy biome table; re-export with a current Azgaar version')
        i=integer(biome.get('i'),'biome.i',0,255)
        if i in biome_ids:raise ValueError('Duplicate biome ID')
        biome_ids.add(i)
    cells=[];seen=set()
    for c in source_cells:
        if not isinstance(c,dict):raise ValueError('Invalid packed-cell record')
        i=integer(c.get('i'),'cell.i')
        if i in seen:raise ValueError('Duplicate packed-cell ID')
        seen.add(i);point=_point(c.get('p'),'cell.p')
        if not 0<=point[0]<=width or not 0<=point[1]<=height:raise ValueError('Cell center lies outside source canvas')
        h=finite(c.get('h'),'cell.h')
        if not 0<=h<=100:raise ValueError('Raw Azgaar heights must be in0..100')
        biome=integer(c.get('biome'),'cell.biome',0,255)
        if biome_ids and biome not in biome_ids:raise ValueError('Cell refers to missing biome')
        polygon=None
        if sampling=='polygon':
            ids=c.get('v')
            if not isinstance(ids,list) or not 3<=len(ids)<=128:raise ValueError('Polygon sampling needs3..128 cell vertex IDs; use explicit --sampling nearest for center-only data')
            ids=[integer(v,'cell vertex ID') for v in ids]
            if len(set(ids))!=len(ids):raise ValueError('Cell polygon repeats a vertex ID')
            try:polygon=np.array([vertices[integer(v,'cell vertex ID')] for v in ids],dtype=np.float64)
            except KeyError as e:raise ValueError('Cell refers to missing polygon vertex') from e
            area=abs(np.sum(polygon[:,0]*np.roll(polygon[:,1],-1)-polygon[:,1]*np.roll(polygon[:,0],-1)))
            if area<=width*height*1e-14:raise ValueError('Degenerate cell polygon')
        cells.append({'id':i,'point':point,'h':h,'biome':biome,'polygon':polygon})
    settings=data.get('settings',{})
    if not isinstance(settings,dict):raise ValueError('Invalid settings object')
    exponent=finite(settings.get('heightExponent',2),'settings.heightExponent',True)
    if exponent>6:raise ValueError('Height exponent exceeds supported0..6 range')
    return {'raw':raw,'data':data,'info':info,'width':width,'height':height,'cells':sorted(cells,key=lambda c:c['id']),
        'height_exponent':exponent,'height_exponent_defaulted':'heightExponent' not in settings,'source_biomes':pack.get('biomes',[])}

def _point(value,name):
    if not isinstance(value,list) or len(value)!=2:raise ValueError(name+' must be[x,y]')
    return (finite(value[0],name+'.x'),finite(value[1],name+'.y'))

def _inside_polygon(x,y,polygon,epsilon):
    inside=np.zeros(np.broadcast_shapes(x.shape,y.shape),dtype=bool);boundary=inside.copy()
    for a,b in zip(polygon,np.roll(polygon,-1,axis=0)):
        dx=b[0]-a[0];dy=b[1]-a[1]
        boundary|=(np.abs((x-a[0])*dy-(y-a[1])*dx)<=epsilon*max(1,abs(dx)+abs(dy)))&(x>=min(a[0],b[0])-epsilon)&(x<=max(a[0],b[0])+epsilon)&(y>=min(a[1],b[1])-epsilon)&(y<=max(a[1],b[1])+epsilon)
        if abs(dy)>1e-30:inside^=((a[1]>y)!=(b[1]>y))&(x<(b[0]-a[0])*(y-a[1])/dy+a[0])
    return inside|boundary

def _fill_grid_water(parsed,xs,ys,missing,raw_height,biomes):
    """Fill absent packed samples classified as original-grid water.

    Prefer explicit original-grid water polygons, then Azgaar's nearest original
    grid-center semantics for omitted boundary facets. Nearest packed land must
    never be extended across this absent ocean region.
    """
    grid=parsed['data'].get('grid')
    if not isinstance(grid,dict) or not isinstance(grid.get('cells'),list) or not isinstance(grid.get('vertices'),list):return 0
    if len(grid['cells'])>200_000 or len(grid['vertices'])>600_000:raise ValueError('Original grid exceeds converter graph limits')
    vertices={}
    for vertex in grid['vertices']:
        if not isinstance(vertex,dict):raise ValueError('Invalid grid vertex')
        i=integer(vertex.get('i'),'grid vertex.i')
        if i in vertices:raise ValueError('Duplicate grid vertex ID')
        vertices[i]=_point(vertex.get('p'),'grid vertex.p')
    seen=set();filled=np.zeros(missing.shape,dtype=bool);epsilon=max(parsed['width'],parsed['height'])*1e-10;candidates=0
    cells=sorted(grid['cells'],key=lambda c:integer(c.get('i'),'grid cell.i'))
    for cell in cells:
        i=integer(cell.get('i'),'grid cell.i')
        if i in seen:raise ValueError('Duplicate grid cell ID')
        seen.add(i);h=finite(cell.get('h'),'grid cell.h')
        if not 0<=h<=100:raise ValueError('Original grid heights must be in0..100')
        if h>=20:continue
        ids=cell.get('v')
        if not isinstance(ids,list) or not 3<=len(ids)<=128:raise ValueError('Grid water cell needs3..128 polygon vertices')
        try:poly=np.array([vertices[integer(v,'grid cell vertex ID')] for v in ids],dtype=np.float64)
        except KeyError as e:raise ValueError('Missing original grid polygon vertex') from e
        px0,py0=poly.min(axis=0);px1,py1=poly.max(axis=0)
        ix=np.flatnonzero((xs>=px0-epsilon)&(xs<=px1+epsilon));iy=np.flatnonzero((ys>=py0-epsilon)&(ys<=py1+epsilon))
        if not len(ix) or not len(iy):continue
        x0,x1=ix[0],ix[-1]+1;y0,y1=iy[0],iy[-1]+1
        needed=missing[y0:y1,x0:x1]&~filled[y0:y1,x0:x1]
        if not needed.any():continue
        candidates+=(x1-x0)*(y1-y0)
        if candidates>100_000_000:raise ValueError('Grid water coverage exceeds bounded candidate limit')
        mask=needed&_inside_polygon(xs[None,x0:x1],ys[y0:y1,None],poly,epsilon)
        raw_height[y0:y1,x0:x1][mask]=h;biomes[y0:y1,x0:x1][mask]=0;filled[y0:y1,x0:x1][mask]=True
    # Azgaar's own Grid.findCell performs nearest-center lookup. A small number
    # of exported grid polygons stop before the canvas edge because virtual
    # boundary cells are omitted. Retain the source lookup semantics there:
    # only original-grid water can fill a gap; original-grid land still fails.
    z,x=np.where(missing&~filled);nearest_filled=0
    if len(x):
        points=grid.get('points');valid_points=[];heights=[]
        for cell in cells:
            i=cell['i'];point=cell.get('p')
            if point is None:
                if not isinstance(points,list) or i>=len(points):raise ValueError('Original grid missing point coordinates for coverage validation')
                point=points[i]
            valid_points.append(_point(point,'grid point'));heights.append(float(cell['h']))
        valid_points=np.asarray(valid_points);query=np.column_stack((xs[x],ys[z]));heights=np.asarray(heights)
        try:
            from scipy.spatial import cKDTree
            _,indices=cKDTree(valid_points).query(query,k=1,workers=1)
        except ImportError:
            if len(query)*len(valid_points)>100_000_000:raise ValueError('Large original-grid coverage lookup needs optional SciPy or lower output resolution')
            indices=np.empty(len(query),np.int32);step=max(1,1_000_000//len(valid_points))
            for start in range(0,len(query),step):indices[start:start+step]=np.argmin(np.sum((query[start:start+step,None,:]-valid_points[None,:,:])**2,axis=2),axis=1)
        water=heights[indices]<20;zz=z[water];xx=x[water]
        raw_height[zz,xx]=heights[indices[water]];biomes[zz,xx]=0;filled[zz,xx]=True;nearest_filled=len(xx)
    parsed['grid_water_nearest_samples']=nearest_filled
    return filled

def rasterize(parsed,cols,rows,sampling='polygon',landmass_scale=1,uncovered='grid-water'):
    dimensions(cols,rows);scale=finite(landmass_scale,'landmass_scale',True)
    if scale>100 or scale<.01:raise ValueError('Landmass scale must be0.01..100')
    width,height=parsed['width'],parsed['height']
    xs=((np.linspace(0,1,cols)-.5)/scale+.5)*width
    ys=((np.linspace(0,1,rows)-.5)/scale+.5)*height
    owner=np.full((rows,cols),-1,dtype=np.int32)
    active=(ys[:,None]>=0)&(ys[:,None]<=height)&(xs[None,:]>=0)&(xs[None,:]<=width)
    if sampling=='nearest':
        points=np.asarray([c['point'] for c in parsed['cells']]);query=np.column_stack((np.broadcast_to(xs,(rows,cols))[active],np.broadcast_to(ys[:,None],(rows,cols))[active]))
        # Optional compiled spatial search for large maps; small fixtures retain
        # a deterministic bounded-memory NumPy fallback.
        try:
            from scipy.spatial import cKDTree
            tree=cKDTree(points);_,indices=tree.query(query,k=1,workers=1)
            # Equidistant ownership is explicitly lowest source ID.
            distances,_=tree.query(query,k=min(2,len(points)),workers=1)
            if len(points)>1:
                ties=np.isclose(distances[:,0],distances[:,1],rtol=0,atol=max(width,height)*1e-12)
                for j in np.flatnonzero(ties):
                    candidates=sorted(tree.query_ball_point(query[j],distances[j,0]+max(width,height)*1e-12))
                    ds=np.sum((points[candidates]-query[j])**2,axis=1);indices[j]=candidates[int(np.argmin(ds))]
        except ImportError:
            if len(query)*len(points)>100_000_000:raise ValueError('Large nearest-center conversion requires optional SciPy; polygon sampling avoids this dependency')
            indices=np.empty(len(query),dtype=np.int32)
            for start in range(0,len(query),max(1,1_000_000//len(points))):
                q=query[start:start+max(1,1_000_000//len(points))]
                indices[start:start+len(q)]=np.argmin(np.sum((q[:,None,:]-points[None,:,:])**2,axis=2),axis=1)
        owner[active]=indices
    elif sampling=='polygon':
        epsilon=max(width,height)*1e-10;candidates=0
        for i,c in enumerate(parsed['cells']):
            poly=c['polygon'];px0,py0=poly.min(axis=0);px1,py1=poly.max(axis=0)
            ix=np.flatnonzero((xs>=px0-epsilon)&(xs<=px1+epsilon));iy=np.flatnonzero((ys>=py0-epsilon)&(ys<=py1+epsilon))
            if not len(ix) or not len(iy):continue
            x0,x1=ix[0],ix[-1]+1;y0,y1=iy[0],iy[-1]+1
            candidates+=(x1-x0)*(y1-y0)
            if candidates>100_000_000:raise ValueError('Polygon rasterization exceeds100 million candidate limit; reduce atlas resolution or repair overlapping geometry')
            region=owner[y0:y1,x0:x1]
            mask=_inside_polygon(xs[None,x0:x1],ys[y0:y1,None],poly,epsilon)&active[y0:y1,x0:x1]&(region<0)
            region[mask]=i
    else:raise ValueError('Unsupported sampling mode')
    raw_height=np.zeros((rows,cols),dtype=np.float64);biomes=np.zeros((rows,cols),dtype=np.uint8)
    selected=owner>=0
    hs=np.array([c['h'] for c in parsed['cells']]);bs=np.array([c['biome'] for c in parsed['cells']],dtype=np.uint8)
    raw_height[selected]=hs[owner[selected]];biomes[selected]=bs[owner[selected]]
    missing=active&(owner<0);filled=0
    if missing.any() and sampling=='polygon':
        if uncovered=='grid-water':
            restored=_fill_grid_water(parsed,xs,ys,missing,raw_height,biomes)
            if isinstance(restored,np.ndarray):filled=int(restored.sum());missing=missing&~restored
        elif uncovered!='error':raise ValueError('Unsupported uncovered-coverage policy')
        if missing.any():
            zi,xi=np.where(missing)
            raise ValueError(f'Cell polygons leave {len(xi):,} atlas samples uncovered after policy {uncovered}; first at source({xs[xi[0]]:.3f},{ys[zi[0]]:.3f}). Original grid can restore only provable water; repair missing land or choose explicit --sampling nearest.')
    parsed['coverage_report']={'packed_samples':int(selected.sum()),'original_grid_water_samples':filled,
        'original_grid_nearest_water_samples':parsed.get('grid_water_nearest_samples',0),'outside_source_samples':int((~active).sum()),'policy':uncovered}
    return raw_height,biomes,selected&(raw_height>=20)

def height_meters(raw_height,exponent=2,peak_meters=None,sea_level_meters=0):
    h=np.asarray(raw_height,dtype=np.float64);land=h>=20
    if not np.isfinite(h).all() or np.any((h<0)|(h>100)):raise ValueError('Raw heights must be finite values in0..100')
    exponent=finite(exponent,'height_exponent',True)
    if exponent>6:raise ValueError('Height exponent must be <=6')
    result=np.full(h.shape,-990.0)
    water=(h>0)&~land;result[water]=(h[water]-20)/h[water]*50
    if peak_meters is None:result[land]=np.power(h[land]-18,exponent)
    else:
        peak=finite(peak_meters,'peak_meters',True)
        result[land]=peak*np.power((h[land]-20)/80,exponent)
    result+=finite(sea_level_meters,'sea_level_meters')
    if not np.isfinite(result).all() or np.any(np.abs(result)>MAX_HEIGHT):raise ValueError('Converted heights exceed finite +/-1,000,000 meter limits')
    return result.astype('<f4')

def apply_summit_anchors(parsed,heights,land,extent_x,extent_z,landmass_scale,sea_level_meters,enabled):
    extension=parsed['data'].get('terrainDiffusion')
    if extension is None:return heights,{'present':False,'applied':False,'anchors':[],'warnings':[]}
    if not isinstance(extension,dict):raise ValueError('terrainDiffusion extension must be an object')
    warnings=[];anchors=extension.get('summitAnchors',[])
    if not isinstance(anchors,list) or len(anchors)>16:raise ValueError('Expected at most16 summit anchors')
    planned=[];rows,cols=heights.shape
    for a in anchors:
        if not isinstance(a,dict):raise ValueError('Invalid summit anchor')
        u=finite(a.get('u'),'anchor.u');v=finite(a.get('v'),'anchor.v');boost=finite(a.get('boostMetres'),'anchor.boostMetres');radius=finite(a.get('radiusNativeAt750'),'anchor.radiusNativeAt750')
        if not 0<=u<=1 or not 0<=v<=1 or not 0<=boost<=15000 or not 100<=radius<=5000:raise ValueError('Summit anchor exceeds handed-off compiler limits')
        # The supplied compiler scales reference radii by physicalWidthKm/750.
        # Its native terrain pixels represent30 meters, independently of the
        # Minecraft blocks-per-pixel scale. Keep Unity coordinates in meters.
        radius_m=radius*(extent_x/1000/750)*30*landmass_scale
        center_x=(u-.5)*extent_x*landmass_scale;center_z=(v-.5)*extent_z*landmass_scale
        planned.append({'u':u,'v':v,'center_x_m':center_x,'center_z_m':center_z,'radius_m':radius_m,'boost_m':boost})
    if anchors and not enabled:warnings.append('Authored summit anchors are preserved but not applied; select --apply-summits to apply the documented Unity envelope profile.')
    if enabled and extension.get('formatVersion')!=2:raise ValueError('Applying summit anchors supports handed-off terrainDiffusion formatVersion2 only')
    if enabled:
        xx=np.linspace(-extent_x/2,extent_x/2,cols)[None,:];zz=np.linspace(-extent_z/2,extent_z/2,rows)[:,None]
        extra=np.zeros(heights.shape,dtype=np.float64)
        for anchor in planned:
            r=np.sqrt((xx-anchor['center_x_m'])**2+(zz-anchor['center_z_m'])**2)/anchor['radius_m']
            t=np.clip(1-r,0,1);shoulder=t*t*(3-2*t);core=t**4
            extra=np.maximum(extra,anchor['boost_m']*(.65*shoulder+.35*core))
        gate=np.clip((heights.astype(float)-sea_level_meters-700)/2800,0,1);gate=gate*gate*(3-2*gate)
        heights=(heights+np.where(land,extra*gate,0)).astype('<f4')
        warnings.append('Unity summit-envelope-v1 applies authored anchors with V13 shoulder/core and altitude gating. It does not reproduce V13 noise, valley-detail modulation, anchor resnapping, or Minecraft build-height ceiling; this is not bit-exact mod parity.')
    if extension.get('biomeProfiles'):warnings.append('Authored biome profiles are preserved as metadata/source IDs; Minecraft biome mapping, desert dune ecology and other mod systems are not executed by this atlas converter.')
    return heights,{'present':True,'applied':bool(enabled),'profile':'unity-summit-envelope-v1' if enabled else 'metadata-only',
        'anchors':planned,'extension_sha256':hashlib.sha256(json.dumps(extension,sort_keys=True,separators=(',',':')).encode()).hexdigest(),'warnings':warnings}

def convert_azgaar(source,output,*,cols=257,rows=None,width_km=100,depth_km=None,peak_meters=None,sea_level_meters=0,landmass_scale=1,sampling='polygon',height_exponent=None,uncovered='grid-water',apply_summits=False):
    parsed=parse_azgaar(source,sampling)
    if rows is None:rows=max(2,int(round((cols-1)*parsed['height']/parsed['width']))+1)
    raw,biomes,land=rasterize(parsed,cols,rows,sampling,landmass_scale,uncovered)
    ex=finite(width_km,'width_km',True)*1000
    ez=finite(depth_km,'depth_km',True)*1000 if depth_km is not None else ex*parsed['height']/parsed['width']
    exponent=parsed['height_exponent'] if height_exponent is None else height_exponent
    heights=height_meters(raw,exponent,peak_meters,sea_level_meters)
    heights,summits=apply_summit_anchors(parsed,heights,land,ex,ez,landmass_scale,sea_level_meters,apply_summits)
    identity={'mapping':MAPPING_VERSION,'source_sha256':hashlib.sha256(parsed['raw']).hexdigest(),
        'source_version':str(parsed['info'].get('version','unknown')),'source_map_name':str(parsed['info'].get('mapName',''))[:160],
        'columns':cols,'rows':rows,'extent_x':ex,'extent_z':ez,'height_exponent':exponent,
        'height_exponent_defaulted':parsed['height_exponent_defaulted'] and height_exponent is None,
        'peak_meters':peak_meters,'sea_level_meters':sea_level_meters,'landmass_scale':landmass_scale,'sampling':sampling,
        'uncovered':uncovered,'summits_applied':bool(apply_summits),'summit_profile':summits.get('profile'),
        'extension_sha256':summits.get('extension_sha256')}
    digest=write_avwf(output,heights,biomes,land,-ex/2,-ez/2,ex,ez,identity)
    report={'atlas_sha256':digest,'identity':identity,'source_biomes':parsed['source_biomes'],
        'source_cell_count':len(parsed['cells']),'land_samples':int(land.sum()),'water_samples':int((~land).sum()),
        'height_min_m':float(heights.min()),'height_max_m':float(heights.max()),
        'coverage':parsed['coverage_report'],'summits':summits,'source_terrain_diffusion':parsed['data'].get('terrainDiffusion'),
        'note':'Offline Azgaar control map; source biomes preserved. Runtime forest override is separate. Not diffusion-generated or a voxel volume.'}
    Path(output).with_suffix(Path(output).suffix+'.json').write_text(json.dumps(report,indent=2,allow_nan=False)+'\n')
    return report

def convert_tiff(source,output,*,elevation_units,origin_x,origin_z,extent_x,extent_z,sea_level_meters=0,source_biome=0):
    if elevation_units!='meters':raise ValueError('TIFF elevations require explicit --elevation-units meters; normalized16-bit images are not accepted as physical heights')
    source=Path(source)
    if source.stat().st_size>MAX_INPUT_BYTES:raise ValueError('TIFF exceeds64 MiB input limit')
    try:
        import rasterio
        with rasterio.open(source) as dataset:
            dimensions(dataset.width,dataset.height)
            if dataset.count!=1:raise ValueError('Expected one elevation band')
            heights=dataset.read(1,masked=True)
            if np.ma.getmaskarray(heights).any():raise ValueError('TIFF contains NoData; resolve missing elevations explicitly before import')
            heights=np.asarray(heights)
    except ImportError:
        try:import tifffile
        except ImportError as e:raise ValueError('Optional TIFF import requires rasterio or tifffile') from e
        with tifffile.TiffFile(source) as tif:
            if len(tif.pages)!=1 or len(tif.pages[0].shape)!=2:raise ValueError('Expected one2D elevation TIFF page')
            dimensions(tif.pages[0].shape[1],tif.pages[0].shape[0]);heights=tif.asarray()
            nodata=tif.pages[0].tags.get('GDAL_NODATA')
            if nodata is not None:
                marker=float(nodata.value)
                if np.any(np.isnan(heights)) if math.isnan(marker) else np.any(heights==marker):
                    raise ValueError('TIFF contains NoData; resolve missing elevations explicitly before import')
    if heights.dtype.kind!='f':raise ValueError('TIFF must store floating-point meter elevations; integer/normalized imagery needs an explicit external scale conversion')
    biome=integer(source_biome,'source_biome',0,255)
    land=np.asarray(heights)>=finite(sea_level_meters,'sea_level_meters')
    identity={'mapping':'float-tiff-meters-v1','source_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),
        'elevation_units':'meters','sea_level_meters':sea_level_meters,'source_biome':biome,
        'origin_x':origin_x,'origin_z':origin_z,'extent_x':extent_x,'extent_z':extent_z}
    return write_avwf(output,heights,np.full(heights.shape,biome,dtype=np.uint8),land,origin_x,origin_z,extent_x,extent_z,identity)

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source',type=Path);parser.add_argument('output',type=Path)
    parser.add_argument('--cols',type=int,default=257);parser.add_argument('--rows',type=int)
    parser.add_argument('--width-km',type=float,default=100);parser.add_argument('--depth-km',type=float)
    parser.add_argument('--peak-meters',type=float);parser.add_argument('--height-exponent',type=float)
    parser.add_argument('--sea-level-meters',type=float,default=0);parser.add_argument('--landmass-scale',type=float,default=1)
    parser.add_argument('--sampling',choices=('polygon','nearest'),default='polygon')
    parser.add_argument('--uncovered',choices=('grid-water','error'),default='grid-water')
    parser.add_argument('--apply-summits',action='store_true',help='Apply authored format2 summit anchors using the versioned Unity envelope profile; not bit-exact Minecraft V13.')
    parser.add_argument('--elevation-units',choices=('meters',))
    parser.add_argument('--origin-x-meters',type=float);parser.add_argument('--origin-z-meters',type=float)
    parser.add_argument('--source-biome',type=int,default=0)
    args=parser.parse_args()
    try:
        if args.source.suffix.lower() in ('.tif','.tiff'):
            ex=finite(args.width_km,'width_km',True)*1000
            if args.depth_km is None:raise ValueError('TIFF requires --depth-km as well as --width-km; georeferencing is not silently guessed')
            ez=finite(args.depth_km,'depth_km',True)*1000
            result=convert_tiff(args.source,args.output,elevation_units=args.elevation_units,
                origin_x=-ex/2 if args.origin_x_meters is None else args.origin_x_meters,
                origin_z=-ez/2 if args.origin_z_meters is None else args.origin_z_meters,
                extent_x=ex,extent_z=ez,sea_level_meters=args.sea_level_meters,source_biome=args.source_biome)
        else:
            result=convert_azgaar(args.source,args.output,cols=args.cols,rows=args.rows,width_km=args.width_km,depth_km=args.depth_km,
                peak_meters=args.peak_meters,sea_level_meters=args.sea_level_meters,landmass_scale=args.landmass_scale,
                sampling=args.sampling,height_exponent=args.height_exponent,uncovered=args.uncovered,apply_summits=args.apply_summits)
        print(json.dumps(result,indent=2))
    except (ValueError,OSError) as e:parser.exit(2,'Conversion failed: '+str(e)+'\n')

if __name__=='__main__':main()
