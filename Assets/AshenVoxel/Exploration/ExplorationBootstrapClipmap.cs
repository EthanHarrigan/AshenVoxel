using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using AshenVoxel.WorldScale;
using AshenVoxel.WorldScaleRuntime;
using UnityEngine;

namespace AshenVoxel.Exploration
{
    public sealed partial class ExplorationBootstrap
    {
        [Tooltip("Explicit scene preview. Old saved preferences do not suppress it; a saved renderer choice or Roll back to Legacy does. Production settings remain Legacy.")]
        public bool PreviewClipmapOnNextPlay;
        public TerrainClipmap Clipmap{get;private set;}
        public BakedOverviewField StaticOverview{get;private set;}
        public string TerrainRendererStatus{get;private set;}="Legacy terrain";
        bool clipmapChoiceSaved;
        void ApplyClipmapPreviewRequest()
        {
            if((PreviewClipmapOnNextPlay||ActiveSettings.PreviewClipmapOnNextPlay)&&!clipmapChoiceSaved)
            {ActiveSettings.TerrainRenderer=TerrainRendererMode.Clipmap;ActiveSettings.ClipmapQuality=TerrainClipmapQuality.High;ActiveSettings.ClipmapFinestSpacing=.2f;}
        }
        IEnumerator ConfigureOverviewSource()
        {
            bool requested=ActiveSettings.UseStaticFarTerrain||ActiveSettings.TerrainRenderer==TerrainRendererMode.Clipmap;
            StaticOverview=null;Clipmap=null;
            if(requested)
            {
                TextAsset asset=ActiveSettings.StaticFarTerrain;
#if UNITY_EDITOR
                if(asset==null)asset=UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/AshenVoxel/Exploration/Samples/ExplorationFar40.bytes");
#endif
                if(asset!=null)
                {
                    byte[] bytes=asset.bytes;
                    var task=Task.Run(()=>{using(var input=new MemoryStream(bytes,false))return BakedFarTerrainIO.Read(input);});
                    while(!task.IsCompleted){Status="Checking static far terrain for the clipmap preview…";yield return null;}
                    try
                    {
                        if(task.IsFaulted)throw task.Exception.GetBaseException();
                        StaticOverview=new BakedOverviewField(Source,LiveRefinement.WorldField,task.Result);
                        WorldField=StaticOverview;
                        LiveRefinement.RegionChanged+=bounds=>StaticOverview.RefreshBaseSnapshot();
                        TerrainRendererStatus="Static far source paired and checked · native comparison pending";
                    }
                    catch(Exception e){TerrainRendererStatus="Static far preview unavailable; Legacy source: "+e.Message;Debug.LogWarning(TerrainRendererStatus,this);}
                }
                else TerrainRendererStatus="Static far cache missing; Legacy source and renderer retained";
                if(StaticOverview==null)ActiveSettings.TerrainRenderer=TerrainRendererMode.Legacy;
            }
            if(StaticOverview==null)
            {
                OverviewRefinement=worldObject.AddComponent<ExplorationOverviewRefinement>();
                WorldField=OverviewRefinement.Configure(Source,LiveRefinement.WorldField,Vista,ActiveSettings.OverviewEndpoint);
                LiveRefinement.RegionChanged+=region=>OverviewRefinement.RefreshBaseSnapshot();
            }
        }
        void InitializeClipmap(double width,double depth)
        {
            if(ActiveSettings.TerrainRenderer!=TerrainRendererMode.Clipmap)return;
            var renderer=worldObject.AddComponent<TerrainClipmap>();
            try
            {
                renderer.Initialize(Vista,(int)ActiveSettings.ClipmapQuality,ActiveSettings.ClipmapFinestSpacing,width*.5,depth*.5,
                    Math.Max(ActiveSettings.ViewDistanceKm,ActiveSettings.FarHorizonKm)*1000);
                Clipmap=renderer;TerrainRendererStatus="Clipmap preview active · native visual acceptance pending";
            }
            catch(Exception e)
            {
                TerrainRendererStatus="Clipmap preview unavailable; Legacy renderer: "+e.Message;
                renderer.Shutdown(false);Destroy(renderer);Vista.DeferLegacyTerrain=false;Vista.ForceReplan();Debug.LogWarning(TerrainRendererStatus,this);
            }
        }
        void DrawClipmapSettings()
        {
            GUILayout.Label("Terrain renderer · next Play",title);
            GUILayout.Label(Clipmap!=null?Clipmap.Status:TerrainRendererStatus,text);
            if(GUILayout.Button("Selected: "+ActiveSettings.TerrainRenderer+" (change next Play)"))
            {ActiveSettings.TerrainRenderer=ActiveSettings.TerrainRenderer==TerrainRendererMode.Legacy?TerrainRendererMode.Clipmap:TerrainRendererMode.Legacy;clipmapChoiceSaved=true;}
            if(GUILayout.Button("Roll back to Legacy next Play and save"))
            {ActiveSettings.TerrainRenderer=TerrainRendererMode.Legacy;ActiveSettings.UseStaticFarTerrain=false;clipmapChoiceSaved=true;SaveOptions();optionsNotice="Legacy renderer/source saved. Restart Play to apply.";}
            if(ActiveSettings.TerrainRenderer==TerrainRendererMode.Clipmap)
            {
                if(GUILayout.Button("Clipmap quality: "+ActiveSettings.ClipmapQuality+" (Fast / High / Ultra)"))
                    ActiveSettings.ClipmapQuality=ActiveSettings.ClipmapQuality==TerrainClipmapQuality.Fast?TerrainClipmapQuality.High:ActiveSettings.ClipmapQuality==TerrainClipmapQuality.High?TerrainClipmapQuality.Ultra:TerrainClipmapQuality.Fast;
                GUILayout.Label("Clipmap is a preview. The source, 10cm matter and rendered triangle spacing remain separate. Unsupported configurations fall back automatically. Ultra uses more memory and triangles.",text);
            }
            if(Clipmap!=null)
                GUILayout.Label("Clipmap payload: CPU "+Clipmap.ResidentCpuBytes/1048576+" MiB · GPU "+Clipmap.GpuBytes/1048576+" MiB · "+Clipmap.DrawCalls+" draws · "+Clipmap.Triangles/1000+"k triangles/pass · "+Clipmap.InFlightSamples+" worker samples. Driver memory and frame time require native measurement.",text);
        }
    }
}
