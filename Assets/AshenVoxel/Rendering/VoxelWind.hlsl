#ifndef ASHEN_VOXEL_WIND_INCLUDED
#define ASHEN_VOXEL_WIND_INCLUDED
// Rendering only. All quantities are metres/radians. Time angles and origin
// phase are reduced on the CPU in double precision, never from camera position.
float4 _VoxelWindTime, _VoxelWindPreviousTime;
float4 _VoxelWindState, _VoxelWindPreviousState; // strength, origin phase, unused
float4 _VoxelWindOriginDelta; // current origin - previous origin, metres

bool VoxelWindMaterial(uint material)
{
    return material==6u || material==7u || material==10u || material==23u;
}

// Shared continuous branch field keeps wood, attached leaves and fruit moving
// together. Local leaf flutter is at most 2.5cm at strength1. No voxel hashes.
// Displacement norm <= strength*(.65+.16+.025) <= 1.67m at strength2.
void VoxelWindField(float3 relative, float3 rootWS, uint material,
    float4 angles, float4 state, out float3 displacement,
    out float3 derivativeX, out float3 derivativeY, out float3 derivativeZ)
{
    displacement=derivativeX=derivativeY=derivativeZ=0;
    if(state.x<=0 || !VoxelWindMaterial(material))return;
    float h=max(0,relative.y);
    float heightMask=h/(h+6.0);
    float branchMask=h/(h+1.5);
    float heightDerivative=relative.y>0 ? 6.0/((h+6.0)*(h+6.0)) : 0;
    float branchDerivative=relative.y>0 ? 1.5/((h+1.5)*(h+1.5)) : 0;
    float phase=dot(rootWS.xz,float2(.037,.023))+state.y;
    float gust=.72+.28*sin(angles.y+phase*2.0);
    float sway=sin(angles.x+phase);
    float3 along=float3(.93969262,0,.34202014);
    float3 across=float3(-.34202014,0,.93969262);
    float branchAngle=angles.y+phase+dot(relative,float3(.19,.075,.13));
    float branchSin=sin(branchAngle),branchCos=cos(branchAngle);
    displacement=along*(.65*heightMask*gust*sway)+across*(.16*branchMask*branchSin);
    derivativeX=across*(.16*branchMask*branchCos*.19);
    derivativeY=along*(.65*heightDerivative*gust*sway)+across*(.16*(branchDerivative*branchSin+branchMask*branchCos*.075));
    derivativeZ=across*(.16*branchMask*branchCos*.13);
    if(material==7u || material==23u)
    {
        // Long enough spatial wavelength to keep adjacent 10cm faces coherent.
        float flutterAngle=angles.z+phase+dot(relative,float3(1.7,.7,1.3));
        float flutterSin=sin(flutterAngle),flutterCos=cos(flutterAngle);
        float3 flutterAxis=float3(.28,.90,-.33346664); // unit length
        displacement+=flutterAxis*(.025*branchMask*flutterSin);
        derivativeX+=flutterAxis*(.025*branchMask*flutterCos*1.7);
        derivativeY+=flutterAxis*(.025*(branchDerivative*flutterSin+branchMask*flutterCos*.7));
        derivativeZ+=flutterAxis*(.025*branchMask*flutterCos*1.3);
    }
    displacement*=state.x;derivativeX*=state.x;derivativeY*=state.x;derivativeZ*=state.x;
}

float3 VoxelWindPosition(float3 positionWS,float3 rootWS,uint material,float4 angles,float4 state)
{
    float3 d,dx,dy,dz;VoxelWindField(positionWS-rootWS,rootWS,material,angles,state,d,dx,dy,dz);
    return positionWS+d;
}

void VoxelWindVertex(float3 positionOS,float3 normalOS,uint material,out float3 positionWS,out float3 normalWS)
{
    positionWS=TransformObjectToWorld(positionOS);
    normalWS=TransformObjectToWorldNormal(normalOS);
    float3 rootWS=TransformObjectToWorld(float3(0,0,0));
    float3 d,dx,dy,dz;
    VoxelWindField(positionWS-rootWS,rootWS,material,_VoxelWindTime,_VoxelWindState,d,dx,dy,dz);
    positionWS+=d;
    // Cofactor(J)*normal == inverse-transpose(J)*normal up to determinant.
    float3 jx=float3(1,0,0)+dx,jy=float3(0,1,0)+dy,jz=float3(0,0,1)+dz;
    normalWS=normalize(normalWS.x*cross(jy,jz)+normalWS.y*cross(jz,jx)+normalWS.z*cross(jx,jy));
}
#endif
