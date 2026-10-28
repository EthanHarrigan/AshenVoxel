#!/usr/bin/env python3
"""Prepare all five native conditioning rasters using the pinned upstream converter.

No neural weights are loaded. Physical map bounds are preserved by rasterizing
onto the global7680m conditioning lattice, not by stretching the map to rounded
whole-cell dimensions. Climate conversion assumptions remain explicit provenance.
"""
from pathlib import Path
import argparse,copy,json,math,subprocess,sys
import numpy as np
import bridge as b

def prepare(source,output,upstream_source,width_km=750,depth_km=None,origin_x=None,origin_z=None,
            elevation_noise_ratio=.35,climate_noise_ratio=.2,elevation_scale=1.,peak_meters=None):
    source=Path(source);output=Path(output);upstream_source=Path(upstream_source)
    if source.stat().st_size>64*1024**2:raise ValueError('Azgaar JSON exceeds64MiB')
    raw=json.loads(source.read_text());info=raw.get('info',{})
    width=b.finite(info.get('width'),'info.width',.001,1000000)
    height=b.finite(info.get('height'),'info.height',.001,1000000)
    physical_width=b.finite(width_km,'width_km',.001,200000)*1000
    physical_depth=physical_width*height/width if depth_km is None else b.finite(depth_km,'depth_km',.001,200000)*1000
    origin_x=-physical_width*.5 if origin_x is None else b.finite(origin_x,'origin_x')
    origin_z=-physical_depth*.5 if origin_z is None else b.finite(origin_z,'origin_z')
    elevation_noise_ratio=b.finite(elevation_noise_ratio,'elevation_noise_ratio',.000001,1000)
    climate_noise_ratio=b.finite(climate_noise_ratio,'climate_noise_ratio',.000001,1000)
    elevation_scale=b.finite(elevation_scale,'elevation_scale',.000001,100)
    if peak_meters is not None:
        peak_meters=b.finite(peak_meters,'peak_meters',.001,1000000)
        if elevation_scale!=1:raise ValueError('Choose elevation_scale or peak_meters, not both')
    if 'grid' not in raw or 'pack' not in raw or 'mapCoordinates' not in raw:
        raise ValueError('Neural conditioning requires Azgaar Full JSON including original grid, packed cells and mapCoordinates')
    for key in ('grid','pack'):
        if not isinstance(raw[key].get('cells'),list) or not isinstance(raw[key].get('vertices'),list):raise ValueError(key+' requires explicit cells and vertices')
        known=set()
        for vertex in raw[key]['vertices']:
            vertex_id=b.integer(vertex.get('i'),key+' vertex ID',0,2**31-1)
            if vertex_id in known:raise ValueError('Duplicate '+key+' vertex ID')
            known.add(vertex_id)
            if not isinstance(vertex.get('p'),list) or len(vertex['p'])!=2:raise ValueError('Vertex requires a two-dimensional point')
            for axis in vertex['p']:b.finite(axis,'vertex coordinate')
        for cell in raw[key]['cells']:
            if 'h' in cell:b.finite(cell['h'],'raw cell height',0,100)
            if 'biome' in cell:b.integer(cell['biome'],'source biome',0,255)
            if 'temp' in cell:b.finite(cell['temp'],'source temperature')
            if 'prec' in cell:b.finite(cell['prec'],'source precipitation',0)
            if not isinstance(cell.get('v'),list) or len(cell['v'])<3 or any(v not in known for v in cell['v']):
                raise ValueError('Every '+key+' cell requires a valid polygon')
    if not any('temp' in cell for cell in raw['grid']['cells']) or not any('prec' in cell for cell in raw['grid']['cells']):
        raise ValueError('Full JSON grid must include actual temperature and precipitation fields')
    if not any(1<=cell.get('biome',0)<=12 for cell in raw['pack']['cells']):
        raise ValueError('At least one supported land biome is needed for upstream variability heuristics')
    actual=subprocess.check_output(['git','-C',str(upstream_source),'rev-parse','HEAD'],text=True).strip()
    if actual!=b.UPSTREAM_COMMIT:raise ValueError('Upstream converter checkout differs from pinned version')
    if subprocess.run(['git','-C',str(upstream_source),'diff','--quiet','HEAD','--','terrain_diffusion']).returncode:
        raise ValueError('Upstream converter source has uncommitted modifications')
    spacing=7680.;oi=math.floor(origin_z/spacing);oj=math.floor(origin_x/spacing)
    cols=max(2,math.ceil((origin_x+physical_width)/spacing)-oj)
    rows=max(2,math.ceil((origin_z+physical_depth)/spacing)-oi)
    if rows*cols>b.MAX_CONDITION_SAMPLES:raise ValueError('Physical map requires more than4,194,304 conditioning cells')
    sys.path.insert(0,str(upstream_source))
    from terrain_diffusion.inference.utils.azgaar_to_tiff import load_map,rasterize_conditioning_layers,rasterize_layer
    data=load_map(source)
    # Extend the raster canvas at the original map's meters-per-canvas-unit scale.
    # This keeps arbitrary750km worlds exactly750km instead of752.64km after ceil.
    scale_x=physical_width/width;scale_z=physical_depth/height
    shift_x=(origin_x-oj*spacing)/scale_x;shift_z=(origin_z-oi*spacing)/scale_z
    data['pack_verts']={i:[p[0]+shift_x,p[1]+shift_z] for i,p in data['pack_verts'].items()}
    data['grid_verts']={i:[p[0]+shift_x,p[1]+shift_z] for i,p in data['grid_verts'].items()}
    data['map_w']=cols*spacing/scale_x;data['map_h']=rows*spacing/scale_z
    layers=rasterize_conditioning_layers(data,out_width=cols,out_height=rows)
    fields=(('elevation_m','m'),('temperature_c','degC'),('temperature_std_c','degC'),
        ('precipitation_mm','mm/year'),('precipitation_cv_pct','percent'))
    # Pixel centers outside the authored physical box are explicit water context;
    # climate retains the converter's nearest valid context instead of fake data.
    x=(oj+np.arange(cols)+.5)*spacing;z=(oi+np.arange(rows)+.5)*spacing
    outside=(x[None,:]<origin_x)|(x[None,:]>=origin_x+physical_width)|(z[:,None]<origin_z)|(z[:,None]>=origin_z+physical_depth)
    layers['elevation_m']=np.where(outside,-1000.,layers['elevation_m']).astype(np.float32)
    elevation_profile=None
    if elevation_scale!=1 or peak_meters is not None:
        native_peak=float(np.max(layers['elevation_m']))
        if native_peak<=0:raise ValueError('Vertical land controls require positive land elevation')
        factor=elevation_scale if peak_meters is None else peak_meters/native_peak
        native_height=layers['elevation_m'].astype(np.float64)
        layers['elevation_m']=np.where(native_height>0,native_height*factor,native_height).astype(np.float32)
        elevation_profile={'mode':'coarse_land_peak' if peak_meters is not None else 'land_scale',
            'requested_peak_meters':peak_meters,'requested_elevation_scale':elevation_scale,
            'native_sampled_peak_meters':native_peak,'effective_positive_land_scale':factor,
            'sea_level_and_ocean':'unchanged','meaning':'conditioning target, not guaranteed neural maximum'}
    biomes=rasterize_layer(data['pack_cells'],data['pack_verts'],cols/data['map_w'],rows/data['map_h'],
        (rows,cols),lambda cell:int(cell.get('biome',0)),'uint8',0)
    biomes=np.where(outside,0,biomes).astype(np.uint8)
    output.mkdir(parents=True,exist_ok=True);channels={}
    for channel,(name,units) in enumerate(fields):
        array=layers[name]
        if array.shape!=(rows,cols) or not np.isfinite(array).all():raise ValueError('Upstream conversion produced missing/nonfinite '+name)
        if channel==0 and np.any(np.abs(array)>1000000):raise ValueError('Conditioned heights exceed supported +/-1,000,000m')
        if channel in (2,3,4) and np.any(array<0):raise ValueError('Upstream conversion produced invalid negative '+name)
        filename=name+'.npy';np.save(output/filename,np.asarray(array,dtype=np.float32))
        channels[str(channel)]={'path':filename,'units':units}
    np.save(output/'source_biome.npy',biomes)
    manifest={'schema':'ashen-conditioning-v1','coarse_spacing_m':spacing,'origin_i':oi,'origin_j':oj,
        'periodic':False,'channels':channels,'source_biome':'source_biome.npy',
        'provenance':{'producer':'ashen-azgaar-native-lattice-v1','upstream_commit':actual,
            'source_sha256':b.digest(source),'source_file':source.name,
            'physical_origin_x_m':origin_x,'physical_origin_z_m':origin_z,
            'physical_width_m':physical_width,'physical_depth_m':physical_depth,
            'coverage_origin_x_m':oj*spacing,'coverage_origin_z_m':oi*spacing,
            'coverage_width_m':cols*spacing,'coverage_depth_m':rows*spacing,
            'conditioning_pixel_centers':'global coarse cell index plus0.5, rowZ/columnX',
            'native_query_origin':'row_i0/col_j0 are global native pixels;64-cell padding is placed before original origin, not an added world offset',
            'climate_assumptions':'grid.prec*100 annualmm; seasonality/CV are upstream biome heuristics',
            'recommended_fork_snr':[elevation_noise_ratio]+[climate_noise_ratio]*4,
            'authored_summit_or_coast_postprocess':'not applied; this is soft upstream conditioning'}}
    if elevation_profile is not None:manifest['provenance']['elevation_profile']=elevation_profile
    path=output/'conditioning.json';path.write_text(json.dumps(manifest,indent=2)+'\n')
    # Validate the exact produced input contract without importing a model.
    b.load_conditioning_manifest(path)
    return manifest

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('source',type=Path);p.add_argument('output',type=Path)
    p.add_argument('--upstream-source',type=Path,required=True);p.add_argument('--width-km',type=float,default=750)
    p.add_argument('--depth-km',type=float);p.add_argument('--origin-x-m',type=float);p.add_argument('--origin-z-m',type=float)
    p.add_argument('--elevation-noise-ratio',type=float,default=.35);p.add_argument('--climate-noise-ratio',type=float,default=.2)
    elevation=p.add_mutually_exclusive_group()
    elevation.add_argument('--elevation-scale',type=float,default=1.)
    elevation.add_argument('--peak-meters',type=float)
    args=p.parse_args();result=prepare(args.source,args.output,args.upstream_source,args.width_km,args.depth_km,
        args.origin_x_m,args.origin_z_m,args.elevation_noise_ratio,args.climate_noise_ratio,args.elevation_scale,args.peak_meters)
    print(json.dumps(result,indent=2))

if __name__=='__main__':
    try:main()
    except (ValueError,RuntimeError,KeyError) as error:print('ERROR: '+str(error),file=sys.stderr);sys.exit(2)
