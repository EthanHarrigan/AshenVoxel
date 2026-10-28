#ifndef ASHEN_MATERIAL_OPTICS
#define ASHEN_MATERIAL_OPTICS
float4 _VoxelMaterialOptics[32];
float4 _VoxelAmbientSky, _VoxelAmbientEquator, _VoxelAmbientGround;
half2 MaterialOptics(uint material)
{
    float4 value=_VoxelMaterialOptics[min(material,31u)];
    return value.z>.5?value.xy:half2(1,0);
}
// Decode at the vertex, before interpolation: material IDs must never blend.
uint VertexMaterialId(half encoded)
{
    return (uint)round(encoded*255.0)>>2;
}
half3 VertexOptics(half encoded)
{
    uint bits=(uint)round(encoded*255.0);
    return half3(MaterialOptics(bits>>2),(64.0+(bits&3u)*(191.0/3.0))/255.0);
}
half2 GroundOptics(float encoded)
{
    uint bits=(uint)round(encoded*255.0);
    return MaterialOptics(bits>=128u&&bits<153u?bits-128u:31u);
}
half MaterialViewOpacity(half opacity,uint material,float3 positionWS)
{
    // A small close-eye accent only: distant leaves have no coverage holes at
    // the opaque default. Manual opacity remains effective at every distance.
    // Use the same deformed position in forward/depth/normals/motion passes.
    if(material==7u)
    {
        float nearEye=1.0-smoothstep(.15,.8,distance(positionWS,_WorldSpaceCameraPos));
        opacity*=1.0-.06*nearEye;
    }
    return saturate(opacity);
}
half MaterialShadowCoverage(half opacity,half transmission)
{
    // Transmission primarily adds diffuse backlighting. Keep only a restrained
    // shadow accent, independent of the eye and shadow-map/cascade pixel grid.
    return saturate(opacity)*(1.0-.08*saturate(transmission));
}
void ClipMaterialStable(half opacity,float3 coveragePosition)
{
    // Unanimated 1cm cells attached to undeformed object/prototype coordinates.
    // All passes must supply the same coordinates, not SV_POSITION or the
    // camera-relative/rebased world position. Integer hashing avoids a visible
    // repeating Bayer grid and changes neither with the camera nor cascade.
    if(opacity>=.9999)return;
    if(opacity<=0){clip(-1);return;}
    uint3 cell=(uint3)(int3)floor(coveragePosition*100.0);
    uint hash=cell.x*0x8da6b343u ^ cell.y*0xd8163841u ^ cell.z*0xcb1ab31fu;
    hash^=hash>>16;hash*=0x7feb352du;hash^=hash>>15;
    hash*=0x846ca68bu;hash^=hash>>16;
    float threshold=(hash&0x00ffffffu)*(1.0/16777216.0);
    clip(opacity-threshold);
}
#ifdef UNIVERSAL_LIGHTING_INCLUDED
half3 MaterialAmbient(half3 normal)
{
    if(_VoxelAmbientSky.w<.5)return SampleSH(normal);
    return lerp(_VoxelAmbientEquator.rgb,normal.y>=0?_VoxelAmbientSky.rgb:_VoxelAmbientGround.rgb,abs(normal.y));
}
half3 MaterialTransmission(half3 albedo,half3 normal,Light light,half strength)
{
    half back=saturate((dot(-normal,light.direction)+.35h)/1.35h);
    return albedo*light.color*(back*.65h*strength)*light.distanceAttenuation*light.shadowAttenuation;
}
#endif
#endif
