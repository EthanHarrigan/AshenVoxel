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
        [Range(0,2),Tooltip("0: clear sky; 1: layered clouds; 2: detailed clouds.")]
        public int CloudQuality=2;
        [Range(0,1)] public float CloudCoverage=.42f;
        [Range(0,2)] public float CloudDensity=.8f;
        [Range(.25f,4)] public float CloudScale=1;
        [Range(0,360)] public float CloudWindAzimuth=70;
        [Range(0,.1f)] public float CloudSpeed=.012f;
        public Color CloudLightColor=new Color(.98f,.96f,.91f,1);
        public Color CloudShadowColor=new Color(.47f,.56f,.68f,1);
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
        public static float Finite(float value,float fallback,float min,float max)=>
            float.IsNaN(value)||float.IsInfinity(value)?fallback:Mathf.Clamp(value,min,max);
        public static Color FiniteColor(Color value,Color fallback)=>new Color(
            Finite(value.r,fallback.r,0,1),Finite(value.g,fallback.g,0,1),Finite(value.b,fallback.b,0,1),1);
        public void Sanitize()
        {
            SkyExposure=Finite(SkyExposure,1,.1f,4);Haze=Finite(Haze,.35f,0,1);
            SunAngularDiameter=Finite(SunAngularDiameter,.53f,.1f,3);SunRadiance=Finite(SunRadiance,12,0,32);
            HorizonColor=FiniteColor(HorizonColor,new Color(.68f,.77f,.84f,1));
            CloudQuality=Mathf.Clamp(CloudQuality,0,2);CloudCoverage=Finite(CloudCoverage,.42f,0,1);
            CloudDensity=Finite(CloudDensity,.8f,0,2);CloudScale=Finite(CloudScale,1,.25f,4);
            CloudWindAzimuth=Finite(CloudWindAzimuth,70,0,360);CloudSpeed=Finite(CloudSpeed,.012f,0,.1f);
            CloudLightColor=FiniteColor(CloudLightColor,new Color(.98f,.96f,.91f,1));
            CloudShadowColor=FiniteColor(CloudShadowColor,new Color(.47f,.56f,.68f,1));
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
                s.SunElevation=4;s.SunAzimuth=265;s.SunIntensity=1.2f;s.SunColor=new Color(1,.65f,.37f,1);
                s.SkyColor=new Color(.35f,.39f,.61f,1);s.FogColor=new Color(.68f,.48f,.42f,1);
                s.AmbientSky=new Color(.42f,.46f,.66f,1);s.AmbientEquator=new Color(.63f,.43f,.40f,1);
                s.AmbientGround=new Color(.27f,.25f,.29f,1);s.AmbientFillStrength=1.2f;
                a.HorizonColor=new Color(.81f,.57f,.44f,1);a.Haze=.55f;a.CloudCoverage=.36f;
                a.CloudLightColor=new Color(1,.73f,.50f,1);a.CloudShadowColor=new Color(.32f,.34f,.49f,1);
                a.PostExposure=.25f;a.Temperature=5;a.BloomIntensity=.16f;
            }
            else if(preset==ExplorationAtmospherePreset.Overcast)
            {
                s.SunElevation=24;s.SunIntensity=.6f;s.SunColor=new Color(.83f,.88f,.94f,1);
                s.SkyColor=new Color(.43f,.49f,.57f,1);s.FogColor=new Color(.48f,.55f,.59f,1);
                s.FogDensity=.000085f;s.AmbientFillStrength=1.3f;
                s.AmbientSky=new Color(.64f,.69f,.75f,1);s.AmbientEquator=new Color(.54f,.61f,.65f,1);
                a.HorizonColor=new Color(.65f,.70f,.74f,1);a.Haze=.85f;a.CloudCoverage=.92f;a.CloudDensity=1.8f;
                a.CloudLightColor=new Color(.78f,.83f,.87f,1);a.CloudShadowColor=new Color(.32f,.38f,.46f,1);
                a.SunRadiance=4;a.PostExposure=.1f;a.Contrast=9;a.Saturation=-10;a.BloomIntensity=.04f;
            }
        }
    }

    /// <summary>Versioned options snapshot. Old option files keep the authored new controls.</summary>
    [Serializable]
    public sealed class ExplorationEnvironmentOptions
    {
        public int version=1;
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
            if(version!=1||atmosphere==null)return;
            s.Atmosphere=atmosphere.Copy();s.FogEnabled=fogEnabled;s.FogMode=(FogMode)fogMode;s.FogDensity=fogDensity;
            s.SunElevation=sunElevation;s.SunAzimuth=sunAzimuth;s.SunIntensity=sunIntensity;s.AmbientFillStrength=ambientFill;
            s.SkyColor=sky;s.FogColor=fog;s.SunColor=sun;s.AmbientSky=ambientSky;s.AmbientEquator=ambientEquator;s.AmbientGround=ambientGround;
            ExplorationAtmosphere.SanitizeLighting(s);
        }
    }
}
