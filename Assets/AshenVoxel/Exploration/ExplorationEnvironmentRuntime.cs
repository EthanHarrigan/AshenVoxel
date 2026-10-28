#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace AshenVoxel.Exploration
{
    /// <summary>Owns environment globals. No textures, render passes or per-frame allocations.</summary>
    public sealed class ExplorationEnvironmentRuntime : IDisposable
    {
        static readonly string[] FloatNames={"_AshenAtmosphereEnabled","_AshenFogDensity","_AshenFogBaseHeight","_AshenFogHeightFalloff","_AshenFogStart","_AshenFogMaxOpacity","_AshenFogSunScatter","_AshenFogOriginY"};
        static readonly string[] VectorNames={"_AshenFogColor","_AshenFogSunColor","_AshenFogSunDirection","_VoxelAmbientSky","_VoxelAmbientEquator","_VoxelAmbientGround"};
        static readonly int[] FloatIds=Ids(FloatNames),VectorIds=Ids(VectorNames);
        readonly float[] oldFloats=new float[FloatNames.Length];
        readonly Vector4[] oldVectors=new Vector4[VectorNames.Length];
        readonly bool oldFog=RenderSettings.fog;
        readonly FogMode oldFogMode=RenderSettings.fogMode;
        readonly float oldFogDensity=RenderSettings.fogDensity,oldFogStart=RenderSettings.fogStartDistance,oldFogEnd=RenderSettings.fogEndDistance;
        readonly Color oldFogColor=RenderSettings.fogColor,oldSky=RenderSettings.ambientSkyColor,oldEquator=RenderSettings.ambientEquatorColor,oldGround=RenderSettings.ambientGroundColor;
        readonly AmbientMode oldAmbientMode=RenderSettings.ambientMode;
        readonly Light oldSun=RenderSettings.sun;
        Light ownerSun;
        bool disposed;
        float lastOriginY=float.NaN;
        static int[] Ids(string[] names){var ids=new int[names.Length];for(int i=0;i<ids.Length;i++)ids[i]=Shader.PropertyToID(names[i]);return ids;}
        static Color Linear(Color color)=>QualitySettings.activeColorSpace==ColorSpace.Linear?color.linear:color;
        static Color Authored(Color color)=>QualitySettings.activeColorSpace==ColorSpace.Linear?color.gamma:color;

        public ExplorationEnvironmentRuntime()
        {
            for(int i=0;i<FloatIds.Length;i++)oldFloats[i]=Shader.GetGlobalFloat(FloatIds[i]);
            for(int i=0;i<VectorIds.Length;i++)oldVectors[i]=Shader.GetGlobalVector(VectorIds[i]);
        }
        public void SetOriginY(float originY)
        {
            if(originY==lastOriginY)return;
            Shader.SetGlobalFloat(FloatIds[7],originY);lastOriginY=originY;
        }
        public void Apply(ExplorationSettings s,Material sky,Light sun)
        {
            ExplorationAtmosphere.SanitizeLighting(s);var a=s.Atmosphere;ownerSun=sun;
            float daylight=Mathf.SmoothStep(0,1,Mathf.InverseLerp(-12,3,s.SunElevation));
            float directDaylight=Mathf.SmoothStep(0,1,Mathf.InverseLerp(-3,1,s.SunElevation));
            float twilight=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,16,s.SunElevation));
            Color horizon=Color.Lerp(a.HorizonColor,Color.Lerp(a.HorizonColor,s.SunColor,.3f),twilight);
            // Shared linear horizon is also the asymptote of surface fog; post exposure affects both.
            Color horizonLinear=Linear(horizon)*Mathf.Lerp(.035f,1,daylight);
            Color fogLinear=(a.MatchFogToHorizon?horizonLinear:Linear(s.FogColor))*a.SkyExposure;
            RenderSettings.fog=s.FogEnabled;RenderSettings.fogMode=s.FogMode;RenderSettings.fogDensity=s.FogDensity;
            RenderSettings.fogColor=Authored(fogLinear);
            RenderSettings.fogStartDistance=s.ViewDistanceKm*500;RenderSettings.fogEndDistance=s.ViewDistanceKm*1000;
            Color ambientSky=s.AmbientSky* Mathf.Lerp(.08f,1,daylight);
            Color ambientEquator=s.AmbientEquator*Mathf.Lerp(.08f,1,daylight);
            Color ambientGround=s.AmbientGround*Mathf.Lerp(.08f,1,daylight);
            RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=ambientSky;
            RenderSettings.ambientEquatorColor=ambientEquator;RenderSettings.ambientGroundColor=ambientGround;
            AshenVoxel.Rendering.MaterialOpticsProfile.SetAmbient(ambientSky,ambientEquator,ambientGround,s.AmbientFillStrength);
            Vector3 direction=-(Quaternion.Euler(s.SunElevation,s.SunAzimuth,0)*Vector3.forward);
            if(sun!=null){sun.transform.rotation=Quaternion.Euler(s.SunElevation,s.SunAzimuth,0);sun.color=s.SunColor;sun.intensity=s.SunIntensity*directDaylight;}
            Shader.SetGlobalFloat(FloatIds[0],s.FogEnabled&&a.HeightFog?1:0);
            Shader.SetGlobalFloat(FloatIds[1],s.FogDensity);Shader.SetGlobalFloat(FloatIds[2],a.FogBaseHeight);
            Shader.SetGlobalFloat(FloatIds[3],a.FogHeightFalloff);Shader.SetGlobalFloat(FloatIds[4],a.FogStart);
            Shader.SetGlobalFloat(FloatIds[5],a.FogMaxOpacity);Shader.SetGlobalFloat(FloatIds[6],a.FogSunScatter*directDaylight);
            Shader.SetGlobalVector(VectorIds[0],fogLinear);Shader.SetGlobalVector(VectorIds[1],Linear(s.SunColor)*a.SkyExposure);
            Shader.SetGlobalVector(VectorIds[2],new Vector4(direction.x,direction.y,direction.z,0));
            if(sky==null)return;
            sky.SetFloat("_Exposure",a.SkyExposure);
            if(sky.shader.name!="AshenVoxel/ExplorationSky")
            {sky.SetColor("_SkyTint",s.SkyColor);sky.SetColor("_GroundColor",s.AmbientGround);return;}
            // Raw vectors avoid SetColor's attribute-dependent HDR/Gamma conversion.
            sky.SetVector("_SkyTint",Linear(s.SkyColor));sky.SetVector("_GroundColor",Linear(s.AmbientGround));
            sky.SetVector("_SunDirection",new Vector4(direction.x,direction.y,direction.z,0));
            sky.SetVector("_HorizonColor",horizonLinear);sky.SetVector("_SunColor",Linear(s.SunColor));
            sky.SetFloat("_SunAngularDiameter",a.SunAngularDiameter);sky.SetFloat("_SunRadiance",a.SunRadiance*directDaylight);
            sky.SetFloat("_Haze",a.Haze);sky.SetFloat("_CloudCoverage",a.CloudCoverage);sky.SetFloat("_CloudDensity",a.CloudDensity);
            sky.SetFloat("_CloudScale",a.CloudScale);sky.SetFloat("_CloudSpeed",a.CloudSpeed);sky.SetFloat("_CloudQuality",a.CloudQuality);
            float wind=a.CloudWindAzimuth*Mathf.Deg2Rad;
            sky.SetVector("_CloudWind",new Vector4(Mathf.Cos(wind),Mathf.Sin(wind),0,0));
            sky.SetVector("_CloudLightColor",Linear(a.CloudLightColor));sky.SetVector("_CloudShadowColor",Linear(a.CloudShadowColor));
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            // A different environment may have taken over the scene in the meantime.
            if(ReferenceEquals(ownerSun,null)||(RenderSettings.sun!=ownerSun&&RenderSettings.sun!=null))return;
            RenderSettings.sun=oldSun;RenderSettings.fog=oldFog;RenderSettings.fogMode=oldFogMode;
            RenderSettings.fogDensity=oldFogDensity;RenderSettings.fogStartDistance=oldFogStart;RenderSettings.fogEndDistance=oldFogEnd;RenderSettings.fogColor=oldFogColor;
            RenderSettings.ambientMode=oldAmbientMode;RenderSettings.ambientSkyColor=oldSky;
            RenderSettings.ambientEquatorColor=oldEquator;RenderSettings.ambientGroundColor=oldGround;
            for(int i=0;i<FloatIds.Length;i++)Shader.SetGlobalFloat(FloatIds[i],oldFloats[i]);
            for(int i=0;i<VectorIds.Length;i++)Shader.SetGlobalVector(VectorIds[i],oldVectors[i]);
        }
    }
}
#endif
