#ifndef ASHEN_VOXEL_CLOUD_VOLUME_INCLUDED
#define ASHEN_VOXEL_CLOUD_VOLUME_INCLUDED

// Include URP Core.hlsl first. Shared material layout for sky and volume mesh.
TEXTURE3D(_CloudNoiseTex);
SAMPLER(sampler_CloudNoiseTex);

CBUFFER_START(UnityPerMaterial)
    float4 _SkyTint,_GroundColor,_SunDirection,_HorizonColor,_SunColor;
    float4 _CloudWind,_CloudLightColor,_CloudShadowColor,_CloudCameraData;
    float _Exposure,_SunAngularDiameter,_SunRadiance,_Haze;
    float _CloudCoverage,_CloudDensity,_CloudScale,_CloudSpeed,_CloudQuality;
    float _CloudNoiseReady,_CloudNoiseWorldSize,_CloudThickness,_CloudVerticalScale;
    float _CloudExtinction,_CloudErosion,_CloudMaxDistance,_CloudViewSteps,_CloudLightSteps;
    float _CloudAnisotropy,_CloudAmbientStrength,_CloudSunStrength,_CloudAerialDensity;
    float _CloudVolumeExternal,_CloudDepthReady,_CloudDepthBias;
CBUFFER_END

float3 AVCloudSafeDirection(float3 v)
{
    float l2=dot(v,v);
    return l2>1e-12 ? v*rsqrt(l2) : float3(0,1,0);
}

float2 AVCloudWindPhase()
{
    float2 w=_CloudWind.xy;
    w*=rsqrt(max(dot(w,w),1e-12));
    return frac(_Time.y*clamp(_CloudSpeed,0,.1)*w/64.0);
}

bool AVCloudLayerInterval(float height,float dy,float thickness,float maxDistance,
                         out float entry,out float exit)
{
    entry=0;
    exit=maxDistance;

    if(abs(dy)<1e-6)
        return height>=0 && height<=thickness;

    float a=-height/dy;
    float b=(thickness-height)/dy;
    entry=max(0,min(a,b));
    exit=min(maxDistance,max(a,b));
    return exit>entry;
}

float3 AVCloudVolumeUV(float3 displacement,float period,float2 wind)
{
    float3 uv=float3(_CloudCameraData.x,
        _CloudCameraData.y/period,_CloudCameraData.z)+displacement/period;
    uv.y*=clamp(_CloudVerticalScale,.5,8);
    uv.xz+=wind;
    return uv;
}

float AVCloudCoarseDensity(float3 displacement,float period,float2 wind,float lod,
                          out float3 uv,out float4 shapeData)
{
    float h=(_CloudCameraData.y+displacement.y)
        /clamp(_CloudThickness,100,3000);

    uv=AVCloudVolumeUV(displacement,period,wind);
    shapeData=0;
    if(h<=0 || h>=1)
        return 0;

    float4 n=SAMPLE_TEXTURE3D_LOD(
        _CloudNoiseTex,sampler_CloudNoiseTex,uv,lod);

    // Expand broad A variation and combine it with 3D R/G lobes.
    float cap=saturate((n.a-.28)*2.2);
    float lobe=saturate((n.r-.22)*1.8);
    float top=clamp(
        .38+.30*cap+.30*lobe+.32*(n.g-.5),
        .38,.98);
    float bottom=.025+.18*smoothstep(.15,.85,n.g);

    float coreProfile=smoothstep(bottom,bottom+.08,h)
        *(1-smoothstep(top-.16,top,h));

    // A weak, broader envelope carries translucent wisps around the body.
    // Both ends still reach zero smoothly at the layer's physical bounds.
    float fringeProfile=smoothstep(max(0,bottom-.06),bottom+.04,h)
        *(1-smoothstep(top-.04,min(1,top+.12),h));

    float profile=lerp(coreProfile,fringeProfile,.18);

    float c=saturate(_CloudCoverage);
    float weather=smoothstep(1-c-.18,1-c+.18,n.b)
        *smoothstep(0,.025,c);

    // Smooth positive compression retains density variation in filled cores.
    float signedBody=n.r-(.12+(1-weather)*.58);
    const float margin=.08;
    float x=(signedBody<=-margin?0:(signedBody>=margin?signedBody:
        (signedBody+margin)*(signedBody+margin)/(4*margin)))*2.2;
    float body=1.2*x/(.4+x);

    float amount=body*sqrt(weather)*clamp(_CloudDensity,0,2);
    shapeData=float4(bottom,top,amount,h);
    return amount*profile;
}

float AVCloudFineDensity(float coarse,float3 uv,float lod,float4 shapeData)
{
    float4 detail=SAMPLE_TEXTURE3D_LOD(
        _CloudNoiseTex,sampler_CloudNoiseTex,
        uv*4+float3(.17,.31,.53),min(lod+2,6));

    // Two detail scales from one fetch. Multiplicative erosion preserves faint
    // irregular margins instead of subtracting them into crisp empty cutouts.
    float erosion=.65*detail.g+.35*detail.r;
    // Fine billows move the boundary in three dimensions, rather than only
    // changing opacity on an otherwise smooth surface. Same single detail fetch.
    float warp=clamp(((detail.r-.45)*.45+(detail.g-.40)*.30)*saturate(_CloudErosion/.22),-.12,.12);
    float h=shapeData.w+warp,bottom=shapeData.x,top=shapeData.y;
    float core=smoothstep(bottom,bottom+.08,h)*(1-smoothstep(top-.16,top,h));
    float fringe=smoothstep(max(0,bottom-.06),bottom+.04,h)
        *(1-smoothstep(top-.04,min(1,top+.12),h));
    coarse=shapeData.z*lerp(core,fringe,.18)
        *smoothstep(0,.03,shapeData.w)*(1-smoothstep(.96,1,shapeData.w));
    float amount=clamp(_CloudDensity,0,2);
    float edge=1-saturate(coarse/max(amount,1e-5));

    float retention=exp2(
        -clamp(_CloudErosion,0,.6)*10*(1-erosion)*edge*edge);
    float fine=coarse*(.90+.20*detail.r)*retention;

    return clamp(fine,0,amount);
}

float AVCloudSunOpticalDepth(float3 displacement,float3 sun,float period,
                            float2 wind,float viewLod,out float nearTau)
{
    nearTau=0;

    float thickness=clamp(_CloudThickness,100,3000);
    float entry,exit;

    // Local, capped shadow approximation.
    float lightReach=min(thickness*3,4000);
    if(!AVCloudLayerInterval(
        _CloudCameraData.y+displacement.y,sun.y,
        thickness,lightReach,entry,exit))
        return 0;

    int count=clamp((int)floor(_CloudLightSteps+.5),3,5);
    float rayMetric=length(
        sun*float3(1,clamp(_CloudVerticalScale,.5,8),1));

    float tau=0;

    [loop] for(int j=0;j<5;++j)
    {
        if(j>=count)
            break;

        // Resolve nearby ridge occlusion first, preserving interval weights.
        float a=(float)j/count;
        float b=(float)(j+1)/count;
        float begin=entry+(exit-entry)*a*a;
        float end=entry+(exit-entry)*b*b;
        float stepLength=end-begin;

        float footprint=stepLength*.20*rayMetric;
        float lod=max(
            min(viewLod,1),
            clamp(log2(max(footprint*64/period,1)),0,6));

        float3 uv;
        float4 shapeData;
        float density=AVCloudCoarseDensity(
            displacement+sun*((begin+end)*.5),
            period,wind,lod,uv,shapeData);
        // Resolve the visible fine boundary for the nearest shadow sample.
        // Distant shadow probes retain the documented coarse approximation.
        if(j==0&&shapeData.z>1e-5)density=AVCloudFineDensity(density,uv,lod,shapeData);

        tau+=density*stepLength*clamp(_CloudExtinction,.001,.08);
        if(j==0)
            nearTau=tau;
    }

    return min(tau,80);
}

float AVCloudSpatialSeed(float3 d)
{
    // Fixed raster-space jitter: no frame/time term or extra texture.
    float3 viewRay=mul((float3x3)UNITY_MATRIX_V,d);
    float4 clip=mul(UNITY_MATRIX_P,float4(viewRay,0));
    float2 pixel=floor(
        (clip.xy/max(abs(clip.w),1e-5)*.5+.5)*_ScaledScreenParams.xy);

    float3 p=frac(float3(pixel.x,pixel.y,pixel.x)*.1031);
    p+=dot(p,p.yzx+33.33);
    return frac((p.x+p.y)*p.z);
}

void AVCloudIntegrateClouds(float3 d,float3 sun,float day,float angularWidth,
                           float2 wind,float3 originOffset,float distanceLimit,
                           out float3 radiance,out float transmission)
{
    radiance=0;
    transmission=1;

    if(_CloudCoverage<=0 || _CloudDensity<=0 || distanceLimit<=0)
        return;

    float thickness=clamp(_CloudThickness,100,3000);
    float maxDistance=min(
        clamp(_CloudMaxDistance,3000,80000),distanceLimit);
    float entry,exit;

    if(!AVCloudLayerInterval(
        _CloudCameraData.y+originOffset.y,d.y,
        thickness,maxDistance,entry,exit))
        return;

    int count=clamp((int)floor(_CloudViewSteps+.5),24,64);
    float ds=(exit-entry)/count;
    float period=clamp(_CloudNoiseWorldSize,1000,20000)
        /clamp(_CloudScale,.25,4);
    float extinction=clamp(_CloudExtinction,.001,.08);
    float verticalScale=max(1,clamp(_CloudVerticalScale,.5,8));
    float rayMetric=length(
        d*float3(1,clamp(_CloudVerticalScale,.5,8),1));
    float seed=AVCloudSpatialSeed(d);

    float g=clamp(_CloudAnisotropy,0,.75);
    float mu=clamp(dot(d,sun),-1,1);
    float hg=(1-g*g)/pow(max(1+g*g-2*g*mu,.05),1.5);
    float phase=lerp(.7,min(hg,3),.35);

    [loop] for(int k=0;k<64;++k)
    {
        if(k>=count || transmission<.005)
            break;

        // Uniform cells retain resolution after the empty top of the physical
        // slab; concentrating cells at the slab entry wasted them in clear air.
        float jitter=.35+.30*frac(seed+k*.61803398875);
        float t=entry+(k+jitter)*ds;
        float3 displacement=originOffset+d*t;

        float footprint=max(
            ds*.20*rayMetric,
            t*angularWidth*verticalScale*.5);
        float lod=clamp(log2(max(footprint*64/period,1)),0,6);

        float3 uv;
        float4 shapeData;
        float density=AVCloudCoarseDensity(
            displacement,period,wind,lod,uv,shapeData);
        if(shapeData.z<=1e-5)
            continue;

        density=AVCloudFineDensity(density,uv,lod,shapeData);
        if(density<=1e-5)
            continue;

        float tau=min(density*extinction*ds,80);
        float alpha=1-exp(-tau);

        float nearTau;
        float sunTau=AVCloudSunOpticalDepth(
            displacement,sun,period,wind,lod,nearTau);

        float h=saturate(
            (_CloudCameraData.y+displacement.y)/thickness);
        float3 ambient=max(_CloudShadowColor.rgb,0)
            *clamp(_CloudAmbientStrength,0,2)
            *lerp(.70,1,h)*lerp(.06,1,day);

        // Restrained local openness cue, not hemispherical AO.
        ambient*=.75+.35*exp(-nearTau*.7);

        // Artistic approximation to secondary scattering.
        float illumination=exp(-sunTau)+.12*exp(-sunTau*.25);
        float3 direct=max(_CloudLightColor.rgb,0)*max(_SunColor.rgb,0)
            *clamp(_CloudSunStrength,0,4)*day*phase*illumination;

        float aerial=1-exp(-max(_CloudAerialDensity,0)*t);
        float3 source=lerp(
            ambient+direct,max(_HorizonColor.rgb,0),aerial);

        radiance+=transmission*alpha*source;
        transmission*=1-alpha;
    }

    // Premultiplied in-scattering plus remaining transmission.
    // Caller composites once; no surface fog or horizon fade here.
}

#endif
