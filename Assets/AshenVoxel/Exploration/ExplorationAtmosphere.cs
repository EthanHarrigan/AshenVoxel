using System;
using UnityEngine;

namespace AshenVoxel.Exploration
{
    public enum ExplorationAtmospherePreset { ClearDay, WarmDusk, Overcast }
    public enum ExplorationToneMapping { ACES, Neutral, None }

    /// <summary>Artist controls shared by the runtime sky, surface fog and owned URP volume.</summary>
    [Serializable]
    public sealed class ExplorationAtmosphere
    {
        [Header("Sky and sun")]
        [Range(.1f,4)] public float SkyExposure=1;
        public Color HorizonColor=new Color(.68f,.77f,.84f,1);
        public bool MatchFogToHorizon=true;
        [Range(.1f,3)] public float SunAngularDiameter=.53f;
        [Range(0,32)] public float SunRadiance=12;
        [Range(0,1)] public float Haze=.35f;
        [Header("Clouds")]
        [Range(0,2),Tooltip("0: clear sky; 1: layered clouds; 2: volumetric clouds.")]
        public int CloudQuality=2;
        [Range(0,1)] public float CloudCoverage=.58f;
        [Range(0,2)] public float CloudDensity=.8f;
        [Range(.25f,4)] public float CloudScale=1.6f;
        [Range(0,360)] public float CloudWindAzimuth=70;
        [Range(0,.1f)] public float CloudSpeed=.012f;
        public Color CloudLightColor=new Color(.98f,.98f,.97f,1);
        public Color CloudShadowColor=new Color(.60f,.64f,.69f,1);
        [Header("Volumetric clouds (metres)")]
        [Range(-1000,12000)] public float CloudBaseHeight=2200;
        [Range(100,3000)] public float CloudThickness=700;
        [Range(1000,20000)] public float CloudNoiseWorldSize=6400;
        [Range(.5f,8)] public float CloudVerticalScale=3;
        [Range(.001f,.08f)] public float CloudExtinction=.018f;
        [Range(0,.6f)] public float CloudErosion=.22f;
        [Range(3000,80000)] public float CloudMaxDistance=30000;
        [Range(24,64)] public int CloudViewSteps=64;
        [Range(3,5)] public int CloudLightSteps=4;
        [Range(0,.75f)] public float CloudAnisotropy=.45f;
        [Range(0,2)] public float CloudAmbientStrength=1;
        [Range(0,4)] public float CloudSunStrength=1;
        [Range(0,.0002f)] public float CloudAerialDensity=.000035f;
        [Header("Height fog (metres)")]
        public bool HeightFog=true;
        [Range(-1000,8000)] public float FogBaseHeight;
        [Range(0,.01f),Tooltip("Inverse metres. Zero gives uniform distance fog.")]
        public float FogHeightFalloff=.00035f;
        [Range(0,2000)] public float FogStart=80;
        [Range(0,1)] public float FogMaxOpacity=.97f;
        [Range(0,1)] public float FogSunScatter=.12f;
        [Header("Post processing")]
        public bool PostProcessing=true;
        public ExplorationToneMapping ToneMapping=ExplorationToneMapping.ACES;
        [Range(-3,3)] public float PostExposure=.2f;
        [Range(-50,50)] public float Contrast=6;
        [Range(-100,100)] public float Saturation=3;
        [Range(-100,100)] public float Temperature;
        [Range(-100,100)] public float Tint;
        public Color ColorFilter=Color.white;
        [Range(0,1)] public float BloomIntensity=.1f;
        [Range(.1f,4)] public float BloomThreshold=1.2f;
        [Range(0,1)] public float BloomScatter=.55f;
        [Range(0,.5f)] public float Vignette;

        public ExplorationAtmosphere Copy()=>(ExplorationAtmosphere)MemberwiseClone();
        public void CopyVolumeFrom(ExplorationAtmosphere source)
        {
            CloudBaseHeight=source.CloudBaseHeight;CloudThickness=source.CloudThickness;CloudNoiseWorldSize=source.CloudNoiseWorldSize;
            CloudVerticalScale=source.CloudVerticalScale;CloudExtinction=source.CloudExtinction;CloudErosion=source.CloudErosion;
            CloudMaxDistance=source.CloudMaxDistance;CloudViewSteps=source.CloudViewSteps;CloudLightSteps=source.CloudLightSteps;
            CloudAnisotropy=source.CloudAnisotropy;CloudAmbientStrength=source.CloudAmbientStrength;
            CloudSunStrength=source.CloudSunStrength;CloudAerialDensity=source.CloudAerialDensity;
        }
        public static float Finite(float value,float fallback,float min,float max)=>
            float.IsNaN(value)||float.IsInfinity(value)?fallback:Mathf.Clamp(value,min,max);
        public static Color FiniteColor(Color value,Color fallback)=>new Color(
            Finite(value.r,fallback.r,0,1),Finite(value.g,fallback.g,0,1),Finite(value.b,fallback.b,0,1),1);
        public void Sanitize()
        {
            SkyExposure=Finite(SkyExposure,1,.1f,4);Haze=Finite(Haze,.35f,0,1);
            SunAngularDiameter=Finite(SunAngularDiameter,.53f,.1f,3);SunRadiance=Finite(SunRadiance,12,0,32);
            HorizonColor=FiniteColor(HorizonColor,new Color(.68f,.77f,.84f,1));
            CloudQuality=Mathf.Clamp(CloudQuality,0,2);CloudCoverage=Finite(CloudCoverage,.58f,0,1);
            CloudDensity=Finite(CloudDensity,.8f,0,2);CloudScale=Finite(CloudScale,1.6f,.25f,4);
            CloudWindAzimuth=Finite(CloudWindAzimuth,70,0,360);CloudSpeed=Finite(CloudSpeed,.012f,0,.1f);
            CloudLightColor=FiniteColor(CloudLightColor,new Color(.98f,.98f,.97f,1));
            CloudShadowColor=FiniteColor(CloudShadowColor,new Color(.60f,.64f,.69f,1));
            CloudBaseHeight=Finite(CloudBaseHeight,2200,-1000,12000);CloudThickness=Finite(CloudThickness,700,100,3000);
            CloudNoiseWorldSize=Finite(CloudNoiseWorldSize,6400,1000,20000);CloudVerticalScale=Finite(CloudVerticalScale,3,.5f,8);
            CloudExtinction=Finite(CloudExtinction,.018f,.001f,.08f);CloudErosion=Finite(CloudErosion,.22f,0,.6f);
            CloudMaxDistance=Finite(CloudMaxDistance,30000,3000,80000);CloudViewSteps=Mathf.Clamp(CloudViewSteps,24,64);CloudLightSteps=Mathf.Clamp(CloudLightSteps,3,5);
            CloudAnisotropy=Finite(CloudAnisotropy,.45f,0,.75f);CloudAmbientStrength=Finite(CloudAmbientStrength,1,0,2);
            CloudSunStrength=Finite(CloudSunStrength,1,0,4);CloudAerialDensity=Finite(CloudAerialDensity,.000035f,0,.0002f);
            FogBaseHeight=Finite(FogBaseHeight,0,-1000,8000);FogHeightFalloff=Finite(FogHeightFalloff,.00035f,0,.01f);
            FogStart=Finite(FogStart,80,0,2000);FogMaxOpacity=Finite(FogMaxOpacity,.97f,0,1);FogSunScatter=Finite(FogSunScatter,.12f,0,1);
            ToneMapping=(ExplorationToneMapping)Mathf.Clamp((int)ToneMapping,0,2);
            PostExposure=Finite(PostExposure,.2f,-3,3);Contrast=Finite(Contrast,6,-50,50);Saturation=Finite(Saturation,3,-100,100);
            Temperature=Finite(Temperature,0,-100,100);Tint=Finite(Tint,0,-100,100);ColorFilter=FiniteColor(ColorFilter,Color.white);
            BloomIntensity=Finite(BloomIntensity,.1f,0,1);BloomThreshold=Finite(BloomThreshold,1.2f,.1f,4);
            BloomScatter=Finite(BloomScatter,.55f,0,1);Vignette=Finite(Vignette,0,0,.5f);
        }

        public static void SanitizeLighting(ExplorationSettings s)
        {
            if(s.Atmosphere==null)s.Atmosphere=new ExplorationAtmosphere();
            s.Atmosphere.Sanitize();
            s.FogDensity=Finite(s.FogDensity,.00004f,0,.002f);
            s.SunElevation=Finite(s.SunElevation,36,-12,89);s.SunAzimuth=Finite(s.SunAzimuth,325,0,360);
            s.SunIntensity=Finite(s.SunIntensity,2.1f,0,4);s.AmbientFillStrength=Finite(s.AmbientFillStrength,1.5f,0,2);
            s.FogMode=(FogMode)Mathf.Clamp((int)s.FogMode,1,3);
            s.SkyColor=FiniteColor(s.SkyColor,new Color(.42f,.53f,.65f,1));s.FogColor=FiniteColor(s.FogColor,new Color(.49f,.59f,.68f,1));
            s.SunColor=FiniteColor(s.SunColor,new Color(1,.92f,.81f,1));
            s.AmbientSky=FiniteColor(s.AmbientSky,new Color(.72f,.78f,.85f,1));
            s.AmbientEquator=FiniteColor(s.AmbientEquator,new Color(.58f,.66f,.73f,1));
            s.AmbientGround=FiniteColor(s.AmbientGround,new Color(.36f,.39f,.32f,1));
        }

        public static void ApplyPreset(ExplorationSettings s,ExplorationAtmospherePreset preset)
        {
            var a=new ExplorationAtmosphere();s.Atmosphere=a;
            s.FogEnabled=true;s.FogMode=FogMode.ExponentialSquared;s.FogDensity=.00004f;
            s.SunElevation=36;s.SunAzimuth=325;s.SunIntensity=2.1f;s.SunColor=new Color(1,.92f,.81f,1);
            s.SkyColor=new Color(.42f,.53f,.65f,1);s.FogColor=new Color(.49f,.59f,.68f,1);
            s.AmbientSky=new Color(.72f,.78f,.85f,1);s.AmbientEquator=new Color(.58f,.66f,.73f,1);
            s.AmbientGround=new Color(.36f,.39f,.32f,1);s.AmbientFillStrength=1.5f;
            if(preset==ExplorationAtmospherePreset.WarmDusk)
            {
                s.SunElevation=4;s.SunAzimuth=265;s.SunIntensity=1.2f;s.SunColor=new Color(1,.86f,.72f,1);
                s.SkyColor=new Color(.45f,.56f,.72f,1);s.FogColor=new Color(.78f,.65f,.59f,1);
                s.AmbientSky=new Color(.50f,.56f,.70f,1);s.AmbientEquator=new Color(.65f,.54f,.51f,1);
                s.AmbientGround=new Color(.31f,.30f,.35f,1);s.AmbientFillStrength=1.2f;
                a.HorizonColor=new Color(.93f,.78f,.72f,1);a.Haze=.45f;a.CloudCoverage=.60f;
                a.CloudLightColor=new Color(.98f,.97f,.96f,1);a.CloudShadowColor=new Color(.60f,.59f,.68f,1);
                a.PostExposure=.25f;a.Temperature=0;a.Contrast=4;a.Saturation=-8;a.BloomIntensity=.16f;
            }
            else if(preset==ExplorationAtmospherePreset.Overcast)
            {
                s.SunElevation=24;s.SunIntensity=.6f;s.SunColor=new Color(.83f,.88f,.94f,1);
                s.SkyColor=new Color(.43f,.49f,.57f,1);s.FogColor=new Color(.48f,.55f,.59f,1);
                s.FogDensity=.000085f;s.AmbientFillStrength=1.3f;
                s.AmbientSky=new Color(.64f,.69f,.75f,1);s.AmbientEquator=new Color(.54f,.61f,.65f,1);
                a.HorizonColor=new Color(.65f,.70f,.74f,1);a.Haze=.85f;a.CloudCoverage=.92f;a.CloudDensity=1.8f;
                a.CloudBaseHeight=1800;a.CloudThickness=900;
                a.CloudLightColor=new Color(.78f,.83f,.87f,1);a.CloudShadowColor=new Color(.32f,.38f,.46f,1);
                a.SunRadiance=4;a.PostExposure=.1f;a.Contrast=9;a.Saturation=-10;a.BloomIntensity=.04f;
            }
        }
    }

    /// <summary>Versioned options snapshot. Old option files keep the authored new controls.</summary>
    [Serializable]
    public sealed class ExplorationEnvironmentOptions
    {
        public int version=2;
        public ExplorationAtmosphere atmosphere;
        public bool fogEnabled;
        public int fogMode;
        public float fogDensity,sunElevation,sunAzimuth,sunIntensity,ambientFill;
        public Color sky,fog,sun,ambientSky,ambientEquator,ambientGround;
        public static ExplorationEnvironmentOptions Capture(ExplorationSettings s)=>new ExplorationEnvironmentOptions
        {
            atmosphere=(s.Atmosphere??new ExplorationAtmosphere()).Copy(),fogEnabled=s.FogEnabled,fogMode=(int)s.FogMode,
            fogDensity=s.FogDensity,sunElevation=s.SunElevation,sunAzimuth=s.SunAzimuth,sunIntensity=s.SunIntensity,ambientFill=s.AmbientFillStrength,
            sky=s.SkyColor,fog=s.FogColor,sun=s.SunColor,ambientSky=s.AmbientSky,ambientEquator=s.AmbientEquator,ambientGround=s.AmbientGround
        };
        public void Apply(ExplorationSettings s)
        {
            if((version!=1&&version!=2)||atmosphere==null)return;
            s.Atmosphere=atmosphere.Copy();
            if(version==1)s.Atmosphere.CopyVolumeFrom(new ExplorationAtmosphere());
            s.FogEnabled=fogEnabled;s.FogMode=(FogMode)fogMode;s.FogDensity=fogDensity;
            s.SunElevation=sunElevation;s.SunAzimuth=sunAzimuth;s.SunIntensity=sunIntensity;s.AmbientFillStrength=ambientFill;
            s.SkyColor=sky;s.FogColor=fog;s.SunColor=sun;s.AmbientSky=ambientSky;s.AmbientEquator=ambientEquator;s.AmbientGround=ambientGround;
            ExplorationAtmosphere.SanitizeLighting(s);
        }
    }
}
