using UnityEngine;

namespace AshenVoxel.Exploration
{
    public sealed partial class ExplorationBootstrap
    {
        bool environmentOpen=true,environmentAdvanced,environmentColors;
        void DrawEnvironmentSettings()
        {
            environmentOpen=GUILayout.Toggle(environmentOpen,"Sky, atmosphere and post processing (live)",GUI.skin.button);
            if(!environmentOpen)return;
            if(ActiveSettings.Atmosphere==null)ActiveSettings.Atmosphere=new ExplorationAtmosphere();
            bool changedBefore=GUI.changed;GUI.changed=false;
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("Clear day")){ExplorationAtmosphere.ApplyPreset(ActiveSettings,ExplorationAtmospherePreset.ClearDay);GUI.changed=true;}
            if(GUILayout.Button("Warm dusk")){ExplorationAtmosphere.ApplyPreset(ActiveSettings,ExplorationAtmospherePreset.WarmDusk);GUI.changed=true;}
            if(GUILayout.Button("Overcast")){ExplorationAtmosphere.ApplyPreset(ActiveSettings,ExplorationAtmospherePreset.Overcast);GUI.changed=true;}
            if(GUILayout.Button("Reset")){ExplorationEnvironmentOptions.Capture(Settings).Apply(ActiveSettings);GUI.changed=true;}
            GUILayout.EndHorizontal();
            var s=ActiveSettings;var a=s.Atmosphere;
            s.SunElevation=Slider("Sun elevation",s.SunElevation,-12,89,"°");
            s.SunAzimuth=Slider("Sun azimuth",s.SunAzimuth,0,360,"°");
            s.SunIntensity=Slider("Sunlight",s.SunIntensity,0,4,"×");
            s.AmbientFillStrength=Slider("Shadow fill",s.AmbientFillStrength,0,2,"×");
            a.CloudCoverage=Slider("Cloud coverage",a.CloudCoverage,0,1,"");
            a.CloudDensity=Slider("Cloud density",a.CloudDensity,0,2,"");
            string quality=a.CloudQuality==0?"Off":a.CloudQuality==1?"Layered":"Detailed";
            if(GUILayout.Button("Cloud detail: "+quality)){a.CloudQuality=(a.CloudQuality+1)%3;GUI.changed=true;}
            a.Haze=Slider("Sky haze",a.Haze,0,1,"");
            s.FogEnabled=GUILayout.Toggle(s.FogEnabled,"Atmospheric fog");
            a.HeightFog=GUILayout.Toggle(a.HeightFog,"Height fog");
            s.FogDensity=Slider("Fog density per km",s.FogDensity*1000,0,2,"")*.001f;
            a.PostProcessing=GUILayout.Toggle(a.PostProcessing,"Post processing");
            a.PostExposure=Slider("Exposure",a.PostExposure,-3,3," EV");
            a.Contrast=Slider("Contrast",a.Contrast,-50,50,"");
            a.Saturation=Slider("Saturation",a.Saturation,-100,100,"");
            a.BloomIntensity=Slider("Bloom",a.BloomIntensity,0,1,"");
            if(GUILayout.Button("Tone mapping: "+a.ToneMapping)){a.ToneMapping=(ExplorationToneMapping)(((int)a.ToneMapping+1)%3);GUI.changed=true;}
            environmentAdvanced=GUILayout.Toggle(environmentAdvanced,"More atmosphere controls");
            if(environmentAdvanced)
            {
                a.SkyExposure=Slider("Sky brightness",a.SkyExposure,.1f,4,"×");
                a.SunAngularDiameter=Slider("Sun diameter",a.SunAngularDiameter,.1f,3,"°");
                a.SunRadiance=Slider("Sun disc brightness",a.SunRadiance,0,32,"");
                a.CloudScale=Slider("Cloud scale",a.CloudScale,.25f,4,"");
                a.CloudWindAzimuth=Slider("Cloud wind direction",a.CloudWindAzimuth,0,360,"°");
                a.CloudSpeed=Slider("Cloud speed",a.CloudSpeed*1000,0,100,"")*.001f;
                a.FogBaseHeight=Slider("Fog reference altitude",a.FogBaseHeight,-1000,8000," m");
                a.FogHeightFalloff=Slider("Fog falloff per km",a.FogHeightFalloff*1000,0,10,"")*.001f;
                a.FogStart=Slider("Fog clear distance",a.FogStart,0,2000," m");
                a.FogMaxOpacity=Slider("Fog maximum opacity",a.FogMaxOpacity,0,1,"");
                a.FogSunScatter=Slider("Sunward fog glow",a.FogSunScatter,0,1,"");
                a.MatchFogToHorizon=GUILayout.Toggle(a.MatchFogToHorizon,"Match fog to sky horizon");
                a.Temperature=Slider("Warmth",a.Temperature,-100,100,"");
                a.Tint=Slider("Tint",a.Tint,-100,100,"");
                a.BloomThreshold=Slider("Bloom threshold",a.BloomThreshold,.1f,4,"");
                a.BloomScatter=Slider("Bloom spread",a.BloomScatter,0,1,"");
                a.Vignette=Slider("Vignette",a.Vignette,0,.5f,"");
            }
            environmentColors=GUILayout.Toggle(environmentColors,"Edit sky and lighting colours");
            if(environmentColors)
            {
                s.SkyColor=EnvironmentColor("Zenith",s.SkyColor);a.HorizonColor=EnvironmentColor("Horizon",a.HorizonColor);
                s.SunColor=EnvironmentColor("Sun",s.SunColor);
                a.CloudLightColor=EnvironmentColor("Cloud light",a.CloudLightColor);a.CloudShadowColor=EnvironmentColor("Cloud shadow",a.CloudShadowColor);
                if(!a.MatchFogToHorizon)s.FogColor=EnvironmentColor("Fog",s.FogColor);
                s.AmbientSky=EnvironmentColor("Ambient sky",s.AmbientSky);s.AmbientEquator=EnvironmentColor("Ambient horizon",s.AmbientEquator);s.AmbientGround=EnvironmentColor("Ambient ground",s.AmbientGround);
                a.ColorFilter=EnvironmentColor("Colour filter",a.ColorFilter);
            }
            if(GUI.changed){ExplorationAtmosphere.SanitizeLighting(s);ApplyLighting();}
            GUI.changed|=changedBefore;
            GUILayout.Label("Changes are live. Save these options to keep them; Reset restores the scene's authored appearance.",text);
        }
        Color EnvironmentColor(string label,Color color)
        {
            GUILayout.Label(label,text);
            color.r=Slider("Red",color.r,0,1,"");color.g=Slider("Green",color.g,0,1,"");color.b=Slider("Blue",color.b,0,1,"");color.a=1;
            return color;
        }
    }
}
