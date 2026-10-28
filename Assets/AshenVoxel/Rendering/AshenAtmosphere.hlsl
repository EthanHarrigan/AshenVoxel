#ifndef ASHEN_VOXEL_ATMOSPHERE_INCLUDED
#define ASHEN_VOXEL_ATMOSPHERE_INCLUDED

// URP Core.hlsl must be included upstream. Global uniforms, not material state.
// Colors are linear HDR. Density is extinction/metre; falloff is 1/metre.
float _AshenAtmosphereEnabled;
float _AshenFogOriginY;
float _AshenFogDensity;
float _AshenFogBaseHeight;
float _AshenFogHeightFalloff;
float _AshenFogStart;
float _AshenFogMaxOpacity;
float4 _AshenFogColor;
float4 _AshenFogSunColor;
float4 _AshenFogSunDirection;
float _AshenFogSunScatter;

// Average of exp(x) along a segment whose exponent endpoints are a and b:
// exp(max(a,b)) * (1-exp(-abs(b-a))) / abs(b-a).
// Evaluate its logarithm, then clamp final optical depth, not world altitude.
float AshenFogLogMeanCorrection(float delta)
{
    float d = abs(delta);
    if (d < 0.01)
    {
        // log((1-exp(-d))/d) = -d/2 + d^2/24 + O(d^4).
        return -0.5 * d + d * d * (1.0 / 24.0);
    }
    return log(max(1.0 - exp(-min(d, 80.0)), 1e-30)) - log(d);
}

float AshenFogOpticalDepth(float3 cameraWS, float3 positionWS)
{
    float3 ray = positionWS - cameraWS;
    float distanceToSurface = length(ray);
    float start = max(_AshenFogStart, 0.0);
    float density = max(_AshenFogDensity, 0.0);
    if (distanceToSurface <= start || distanceToSurface < 1e-5 || density <= 0)
        return 0;

    float segmentLength = distanceToSurface - start;
    float startFraction = start / distanceToSurface;
    float falloff = max(_AshenFogHeightFalloff, 0.0);
    // Subtract absolute base from origin before adding local elevations.
    // This retains local precision better than forming two absolute heights.
    float relativeStartHeight = (_AshenFogOriginY - _AshenFogBaseHeight)
        + cameraWS.y + ray.y * startFraction;
    float a = clamp(-falloff * relativeStartHeight, -1e20, 1e20);
    float delta = clamp(-falloff * ray.y * (1.0 - startFraction), -1e20, 1e20);
    float b = clamp(a + delta, -1e20, 1e20);
    float logTau = log(density) + log(segmentLength)
        + max(a, b) + AshenFogLogMeanCorrection(b - a);
    // tau >= 80 already gives effectively zero transmission. At the low end,
    // exp(-80) is negligible. No fixed height ceiling or premature exp overflow.
    return exp(clamp(logTau, -80.0, 4.382026635)); // log(80)
}

half3 AshenApplyFog(half3 color, float legacyFogFactor, float3 positionWS)
{
    if (_AshenAtmosphereEnabled < 0.5)
        return MixFog(color, legacyFogFactor);

    float tau = AshenFogOpticalDepth(_WorldSpaceCameraPos, positionWS);
    float opacity = min(saturate(_AshenFogMaxOpacity), 1.0 - exp(-tau));
    float3 ray = positionWS - _WorldSpaceCameraPos;
    float3 view = ray * rsqrt(max(dot(ray, ray), 1e-12));
    float3 sun = _AshenFogSunDirection.xyz;
    sun *= rsqrt(max(dot(sun, sun), 1e-12));
    float forward = pow(saturate(dot(view, sun)), 8.0);
    float3 fogColor = max(_AshenFogColor.rgb, 0.0)
        + max(_AshenFogSunColor.rgb, 0.0)
        * (saturate(_AshenFogSunScatter) * forward);
    // Replaces legacy fog completely in this branch. No gamma conversion.
    return (half3)min(lerp((float3)color, fogColor, opacity), 60000.0);
}
#endif
