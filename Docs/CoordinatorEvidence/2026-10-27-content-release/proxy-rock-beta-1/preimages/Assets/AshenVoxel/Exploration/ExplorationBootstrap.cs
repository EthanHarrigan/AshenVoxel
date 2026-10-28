using System;
using System.Collections;
using System.Globalization;
using System.IO;
using AshenVoxel.WorldScale;
using AshenVoxel.WorldScaleRuntime;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace AshenVoxel.Exploration
{
    /// <summary>First playable walk/fly scene. All terrain is checked learned data;
    /// missing assets stop explicitly instead of quietly switching to analytic hills.</summary>
    [DefaultExecutionOrder(200)]
    public sealed class ExplorationBootstrap:MonoBehaviour
    {
        public ExplorationSettings Settings;
        public bool LoadSavedOptions=true;
        public string Status {get;private set;}="Preparing the world…";
        public bool Ready {get;private set;}
        public bool Failed {get;private set;}
        public ExplorationAtlasField Source {get;private set;}
        public IWorldField WorldField {get;private set;}
        public WorldExplorer Vista {get;private set;}
        public ExplorationLiveRefinement LiveRefinement {get;private set;}
        public ExplorationController Player {get;private set;}
        public VegetationStreaming Population {get;private set;}
        public ExplorationSettings ActiveSettings {get;private set;}
        [Serializable] public sealed class SpawnMetadata {public double x_m,y_m,z_m;public float yaw_degrees;}
        [Serializable] public sealed class WorldMetadata {public SpawnMetadata recommended_spawn;public string world_id;}
        GameObject worldObject;
        public ExplorationVoxelTerrain VoxelTerrain {get;private set;}
#if UNITY_5_3_OR_NEWER
        public ExplorationUpscaling Upscaler {get;private set;}
        public ExplorationWind Wind {get;private set;}
#endif
        Light sun;
        Material sky,instanceMaterial;
        RenderPipelineAsset ownedPipeline,previousPipeline,previousQualityPipeline;
        Material previousSky;
        float previousShadowDistance;
        WorldPoint spawn;
        ExplorationDistantRockPatch distantRocks;
        public ExplorationDistantRockPatch DistantRockPatch=>distantRocks;
        AshenVoxel.WildlifeDraft.ExplorationBeePatch bees;
        public AshenVoxel.WildlifeDraft.ExplorationBeePatch BeePatch=>bees;
        Vector2 settingsScroll;
        bool settingsOpen,showHud=true;
        float nextApply;
        string optionsNotice="";
        TerrainQualityConfiguration appliedTerrainQuality;
        bool terrainQualityCaptured,appliedGroundedRoots,appliedBroadLithology;
        public TerrainQualityConfiguration AppliedTerrainQuality=>appliedTerrainQuality;
        public bool AppliedGroundedRoots=>appliedGroundedRoots;
        public bool AppliedBroadLithology=>appliedBroadLithology;
        public bool GeologyRestartPending=>terrainQualityCaptured&&ActiveSettings!=null&&appliedBroadLithology!=ActiveSettings.ExperimentalBroadLithology;
        public bool GroundedRootsRestartPending=>terrainQualityCaptured&&ActiveSettings!=null&&appliedGroundedRoots!=ActiveSettings.EnableGroundedRoots;
        public bool TerrainQualityRestartPending=>terrainQualityCaptured&&ActiveSettings!=null&&!appliedTerrainQuality.SameRequest(ActiveSettings.ResolveTerrainQuality());
        public bool TerrainQualityValidationProfile=>terrainQualityCaptured&&(appliedTerrainQuality.ValidationOverrides||
            (Vista!=null&&(Vista.MaximumTiles!=appliedTerrainQuality.MaximumTiles||Vista.VoxelTargetPixels!=appliedTerrainQuality.TargetPixels||
            Vista.TerrainDetailLimitBytes!=(long)appliedTerrainQuality.DetailMiB*1048576||Vista.ContactMeshLimitBytes!=(long)appliedTerrainQuality.ContactMiB*1048576)));
        GUIStyle text,title;

        IEnumerator Start()
        {
            IEnumerator work=Boot();
            while(true)
            {
                bool more;object current;
                try{more=work.MoveNext();current=more?work.Current:null;}
                catch(Exception exception){Fail(exception);yield break;}
                if(!more)break;
                yield return current;
            }
        }
        IEnumerator Boot()
        {
            if(Settings==null)throw new InvalidDataException("Exploration settings are missing. Open Assets/Scenes/Exploration.unity.");
            ActiveSettings=Instantiate(Settings);ActiveSettings.name=Settings.name+" (current session)";
            if(LoadSavedOptions)LoadOptions();
            appliedTerrainQuality=ActiveSettings.ResolveTerrainQuality();terrainQualityCaptured=true;
            appliedGroundedRoots=ActiveSettings.EnableGroundedRoots;appliedBroadLithology=ActiveSettings.ExperimentalBroadLithology;
            Status="Reading the learned world…";yield return null;
            Require(ActiveSettings.MacroWorld,"whole-world height");Require(ActiveSettings.MacroClimate,"whole-world climate");
            Require(ActiveSettings.FineWorld,"starting-region height");Require(ActiveSettings.FineClimate,"starting-region climate");
            Require(ActiveSettings.PrototypePack,"vegetation and rock prototypes");
            HeightAtlas macro=ReadHeight(ActiveSettings.MacroWorld),fine=ReadHeight(ActiveSettings.FineWorld);
            yield return null;
            ClimateAtlas macroClimate=ReadClimate(ActiveSettings.MacroClimate),fineClimate=ReadClimate(ActiveSettings.FineClimate);
            ExplorationBiomeAtlas biomes=null;
            if(ActiveSettings.SourceBiomes!=null)using(var stream=new MemoryStream(ActiveSettings.SourceBiomes.bytes,false))biomes=ExplorationBiomeAtlasIO.Read(stream);
            double width=ActiveSettings.UseImportedDimensions?macro.ExtentX:ActiveSettings.WorldWidthMeters;
            double depth=ActiveSettings.UseImportedDimensions?macro.ExtentZ:ActiveSettings.WorldDepthMeters;
            Source=new ExplorationAtlasField(macro,macroClimate,fine,fineClimate,ActiveSettings.FineBlendMeters,width,depth,ActiveSettings.VerticalScale,biomes,
                originalWidthMeters:macro.ExtentX,originalDepthMeters:macro.ExtentZ);
            spawn=RecommendedSpawn();
            worldObject=new GameObject("InfiniteDiffusion exploration");worldObject.transform.SetParent(transform,false);
            SetupLighting();
            Vista=worldObject.AddComponent<WorldExplorer>();
            Vista.ConfigureInitialVoxelMeshBudgets(appliedTerrainQuality.DetailMiB,appliedTerrainQuality.ContactMiB);
            ApplyVistaSettings(false);
            Vista.ShowResearchHud=false;
            LiveRefinement=worldObject.AddComponent<ExplorationLiveRefinement>();
            WorldField=LiveRefinement.Configure(Source,Vista,ActiveSettings.DiffusionEndpoint,enabled:ActiveSettings.EnableLiveRefinement);
            if(ActiveSettings.EnableSurfaceRelief)WorldField=new TerrainDetailField(WorldField,ActiveSettings.Seed);
            Vista.ConfigureExternal(WorldField,ActiveSettings.Surface(),new WorldPoint(spawn.X,spawn.Y+ActiveSettings.EyeHeight,spawn.Z),Quaternion.Euler(ActiveSettings.SpawnPitch,ActiveSettings.SpawnYaw,0));
            ApplyCamera();
#if UNITY_5_3_OR_NEWER
            worldObject.AddComponent<ExplorationVisualQuality>().Initialize(Vista.MainCamera,ownedPipeline);
            Upscaler=worldObject.AddComponent<ExplorationUpscaling>();Upscaler.Initialize(Vista.MainCamera,ownedPipeline,ActiveSettings,Vista);
            Wind=worldObject.AddComponent<ExplorationWind>();Wind.Initialize(ActiveSettings.WindStrength,Vista.RenderOrigin);
#endif
            if(Vista.MainCamera.GetComponent<AudioListener>()==null)Vista.MainCamera.gameObject.AddComponent<AudioListener>();
            Population=worldObject.AddComponent<VegetationStreaming>();ApplyPopulationSettings();
            Material template=Resources.Load<Material>("VoxelSurface");
            if(template==null)throw new InvalidDataException("The shared voxel surface material is missing.");
            instanceMaterial=new Material(template){name="Exploration instanced matter",enableInstancing=true};
#if UNITY_5_3_OR_NEWER
            instanceMaterial.SetFloat("_VoxelWindStaticInstances",1);
#endif
            if(ActiveSettings.EnableVoxelTerrain&&!Vista.UseVoxelLodTerrain)
            {
                VoxelTerrain=worldObject.AddComponent<ExplorationVoxelTerrain>();
                VoxelTerrain.Initialize(Vista,WorldField,ActiveSettings.Surface(),instanceMaterial);
                LiveRefinement.RegionChanged+=bounds=>VoxelTerrain.InvalidateRegion(bounds.MinimumX,bounds.MinimumZ,bounds.MaximumX,bounds.MaximumZ);
            }
            Status="Loading trees, plants and rocks…";yield return null;
            Population.Initialize(WorldField,ActiveSettings.Surface(),ActiveSettings.PrototypePack,instanceMaterial,ActiveSettings.Seed);
            distantRocks=worldObject.AddComponent<ExplorationDistantRockPatch>();distantRocks.Initialize(Population,WorldField,ActiveSettings.Seed);
            LiveRefinement.RegionChanged+=bounds=>Population.InvalidateRegion(bounds.MinimumX,bounds.MinimumZ,bounds.MaximumX,bounds.MaximumZ);
            Player=worldObject.AddComponent<ExplorationController>();Player.SetInputEnabled(false);
            IExplorationCollision collision=new FieldExplorationCollision(Vista.TerrainField,Population);
            if(ActiveSettings.EnableVoxelTerrain)collision=new VoxelTerrainCollision(Vista.TerrainField,collision);
            Player.Configure(Vista,collision,spawn,ActiveSettings.Movement(),ActiveSettings.SpawnYaw,ActiveSettings.SpawnPitch,false);
            Player.MouseSensitivity=ActiveSettings.MouseSensitivity;Player.GroundedEyeSmoothing=ActiveSettings.EyeSmoothing;Player.AllowNoClipToggle=ActiveSettings.AllowNoClipToggle;
            while(!Population.Ready||!Vista.IsTerrainReadyArea(spawn.X,spawn.Z,2)||!Population.IsReady(spawn.X,spawn.Z,16))
            {
                Status="Preparing the starting clearing… "+Population.Status;
                yield return null;
            }
            if(!Player.TrySpawn(spawn,16))
                throw new InvalidDataException("A clear starting position was not found in the prepared area. Choose another spawn in the settings asset.");
            spawn=Player.Feet;
            if(ActiveSettings.StartFlying)Player.ResetAt(new WorldPoint(spawn.X,spawn.Y+2,spawn.Z),true);
            Player.CapturePointerOnEnable=true;Ready=true;Player.SetInputEnabled(!settingsOpen);
            bees=worldObject.AddComponent<AshenVoxel.WildlifeDraft.ExplorationBeePatch>();bees.Initialize(this);
            AshenVoxel.Exploration.ExplorationBetaMatter.Attach(this);
            Status="Explore";
        }
        static void Require(TextAsset asset,string label){if(asset==null)throw new InvalidDataException("Missing bundled "+label+" data. Use the complete Play project archive.");}
        static HeightAtlas ReadHeight(TextAsset asset){using(var s=new MemoryStream(asset.bytes,false))return AtlasIO.Read(s);}
        static ClimateAtlas ReadClimate(TextAsset asset){using(var s=new MemoryStream(asset.bytes,false))return ClimateArchiveIO.Read(s);}
        WorldPoint RecommendedSpawn()
        {
            double x=ActiveSettings.SpawnX,y=ActiveSettings.SpawnY,z=ActiveSettings.SpawnZ;
            if(ActiveSettings.UseRecommendedSpawn&&ActiveSettings.WorldMetadata!=null)
            {
                WorldMetadata metadata=JsonUtility.FromJson<WorldMetadata>(ActiveSettings.WorldMetadata.text);
                if(metadata!=null&&metadata.recommended_spawn!=null)
                {
                    if(!string.IsNullOrEmpty(metadata.world_id)&&metadata.world_id!=Source.WorldId)throw new InvalidDataException("Spawn metadata belongs to another learned world.");
                    x=metadata.recommended_spawn.x_m*Source.WorldWidthMeters/Source.OriginalWidthMeters;
                    z=metadata.recommended_spawn.z_m*Source.WorldDepthMeters/Source.OriginalDepthMeters;
                    y=Source.Sample(x,z).HeightMeters+2;ActiveSettings.SpawnYaw=metadata.recommended_spawn.yaw_degrees;
                }
            }
            if(Math.Abs(x)>Source.WorldWidthMeters*.5-32||Math.Abs(z)>Source.WorldDepthMeters*.5-32)throw new InvalidDataException("The starting position is outside this map.");
            return new WorldPoint(x,y,z);
        }
        void Fail(Exception exception)
        {
            Failed=true;Ready=false;Status=exception.Message;
            if(Player!=null)Player.SetInputEnabled(false);
            Debug.LogException(exception,this);
        }
        void Update()
        {
            Keyboard keys=Keyboard.current;
            if(keys!=null&&keys.f1Key.wasPressedThisFrame)
            {settingsOpen=!settingsOpen;if(Player!=null)Player.SetInputEnabled(Ready&&!settingsOpen);}
            if(keys!=null&&keys.escapeKey.wasPressedThisFrame&&settingsOpen){settingsOpen=false;if(Player!=null)Player.SetInputEnabled(Ready);}
            if(keys!=null&&keys.hKey.wasPressedThisFrame)showHud=!showHud;
            if(Ready&&Time.unscaledTime>=nextApply){ApplyRuntimeSettings(false);nextApply=Time.unscaledTime+.5f;}
        }
        void LateUpdate()
        {
            if(Failed||Vista==null||Population==null||Vista.MainCamera==null)return;
            try
            {
                WorldPoint observer=Player!=null&&Player.IsConfigured?Player.Eye:new WorldPoint(spawn.X,spawn.Y+1.65,spawn.Z);
                if(LiveRefinement!=null)LiveRefinement.Tick(observer,Vista.MainCamera.transform.forward);
                #if UNITY_5_3_OR_NEWER
                if(Wind!=null)Wind.SetOrigin(Vista.RenderOrigin);
#endif
                Population.SetObserver(observer,Vista.RenderOrigin);
                Population.Tick(Vista.MainCamera);
                if(distantRocks!=null)distantRocks.TickAfterCamera(Vista.MainCamera,observer,Vista.RenderOrigin);
                if(VoxelTerrain!=null)VoxelTerrain.Tick(observer,Vista.RenderOrigin);
                if(bees!=null)bees.TickAfterCamera();
            }
            catch(Exception exception){Fail(exception);}
        }
        public void ApplyRuntimeSettings(bool replan=true)
        {
            if(ActiveSettings==null)return;
            if(Player!=null)
            {
                var movement=ActiveSettings.Movement();
                if(Player.EnableTraversalExperiment)movement=movement.WithTraversal(Player.Motor?.Settings.Traversal??new ExplorationTraversalSettings());
                Player.ApplyMovementSettings(movement);Player.MouseSensitivity=ActiveSettings.MouseSensitivity;Player.GroundedEyeSmoothing=ActiveSettings.EyeSmoothing;
            }
            ApplyVistaSettings(replan);ApplyPopulationSettings();ApplyCamera();ApplyLighting();
#if UNITY_5_3_OR_NEWER
            if(Wind!=null)Wind.Apply(ActiveSettings.WindStrength);
#endif
        }
        void ApplyVistaSettings(bool replan)
        {
            if(Vista==null)return;
            Vista.UseVoxelLodTerrain=ActiveSettings.EnableVoxelTerrain;
            Vista.VoxelTargetPixels=appliedTerrainQuality.TargetPixels;Vista.VoxelFullDetailRadius=ActiveSettings.VoxelFullDetailRadius;
            Vista.VoxelPlanSplitFactor=appliedTerrainQuality.SplitFactor;Vista.VoxelGreedyVisibleDetail=appliedTerrainQuality.GreedyVisible;
            Vista.ViewDistanceKm=Mathf.Clamp(ActiveSettings.ViewDistanceKm,20,40);Vista.FarHorizonDistanceKm=Mathf.Clamp(ActiveSettings.FarHorizonKm,0,100);
            Vista.MaximumTiles=appliedTerrainQuality.MaximumTiles;Vista.MinimumTileSizeMeters=Mathf.Clamp(ActiveSettings.MinimumTerrainTileMeters,16,64);
            Vista.NearTileQuads=Mathf.Clamp(Mathf.Max(ActiveSettings.NearTerrainQuads,Mathf.CeilToInt(Vista.MinimumTileSizeMeters)),32,64);Vista.NearDetailRadiusMeters=Mathf.Clamp(ActiveSettings.NearTerrainRadius,32,256);
            Vista.MeshBuildMilliseconds=Mathf.Clamp(ActiveSettings.TerrainBuildMilliseconds,.25f,8);Vista.MeshWorkUnitsPerFrame=Mathf.Clamp(ActiveSettings.TerrainSampleWork,32,2048);
            Vista.MeshBuildsPerFrame=1;Vista.ShowVisualWater=ActiveSettings.ShowVisualWater;
            if(replan)Vista.ForceReplan();
        }
        void ApplyPopulationSettings()
        {
            if(Population==null)return;
            if(!Population.GroundedRootsInitialized)Population.EnableGroundedRoots=appliedGroundedRoots;
            Population.TreeDistance=ActiveSettings.TreeDistanceMeters;Population.TreeDetailDistance=ActiveSettings.FullTreeDetailMeters;
            Population.EnableForestHorizon=ActiveSettings.EnableForestHorizon;Population.HorizonDistance=ActiveSettings.EffectiveForestDistanceKm*1000;
            Population.UseGpuForestHorizon=ActiveSettings.UseGpuForestHorizon;
            Population.UseTreeImpostors=ActiveSettings.UseTreeImpostors;
            Population.TreeMidDistance=Mathf.Min(ActiveSettings.MediumTreeDetailMeters,500);Population.TreeCoarseDistance=500;Population.TreeFarDistance=1000;
            Population.RedwoodDetailDistance=ActiveSettings.RedwoodFullDetailMeters;
            Population.GroundCoverDistance=ActiveSettings.GroundCoverDistanceMeters;Population.RockDistance=ActiveSettings.RockDistanceMeters;
            Population.TreeDensity=ActiveSettings.TreeDensity;Population.PlantDensity=ActiveSettings.GroundCoverDensity;Population.RockDensity=1;
            Population.TriangleBudget=ActiveSettings.VegetationTriangleBudget;Population.MaxInstances=Mathf.Clamp(ActiveSettings.MaximumResidentInstances,2000,60000);
            Population.WorkMilliseconds=ActiveSettings.PopulationBuildMilliseconds;
        }
        void ApplyCamera()
        {if(Vista!=null&&Vista.MainCamera!=null){Vista.MainCamera.fieldOfView=Mathf.Clamp(ActiveSettings.FieldOfView,45,110);Vista.MainCamera.nearClipPlane=.04f;Vista.MainCamera.allowHDR=true;Vista.MainCamera.clearFlags=CameraClearFlags.Skybox;}}
        void SetupLighting()
        {
            previousSky=RenderSettings.skybox;previousPipeline=GraphicsSettings.defaultRenderPipeline;previousQualityPipeline=QualitySettings.renderPipeline;previousShadowDistance=QualitySettings.shadowDistance;
            RenderPipelineAsset original=GraphicsSettings.currentRenderPipeline;
            if(original==null)throw new InvalidDataException("The exploration render pipeline is missing. Leave Play mode and choose Ashen Voxel → Exploration → Repair project setup, then Play again.");
            ownedPipeline=Instantiate(original);ownedPipeline.name="Exploration runtime pipeline";GraphicsSettings.defaultRenderPipeline=ownedPipeline;QualitySettings.renderPipeline=ownedPipeline;
            sun=new GameObject("Exploration daylight").AddComponent<Light>();sun.transform.SetParent(worldObject.transform,false);sun.type=LightType.Directional;sun.shadows=LightShadows.Soft;
            sun.shadowBias=.02f;sun.shadowNormalBias=.1f;sun.shadowNearPlane=.1f;
            Shader skyShader=Resources.Load<Shader>("ExplorationSky");if(skyShader==null)skyShader=Shader.Find("Skybox/Procedural");
            if(skyShader!=null){sky=new Material(skyShader);sky.name="Exploration sky";sky.SetFloat("_SunSize",.035f);sky.SetFloat("_AtmosphereThickness",1);sky.SetFloat("_Exposure",1);RenderSettings.skybox=sky;}
            RenderSettings.sun=sun;ApplyLighting();
        }
        void ApplyLighting()
        {
            if(ActiveSettings==null)return;
            QualitySettings.shadowDistance=ActiveSettings.ShadowDistanceMeters;
            RenderSettings.fog=ActiveSettings.FogEnabled;RenderSettings.fogMode=ActiveSettings.FogMode;RenderSettings.fogDensity=ActiveSettings.FogDensity;RenderSettings.fogColor=ActiveSettings.FogColor;
            RenderSettings.fogStartDistance=ActiveSettings.ViewDistanceKm*500;RenderSettings.fogEndDistance=ActiveSettings.ViewDistanceKm*1000;
#if UNITY_5_3_OR_NEWER
            AshenVoxel.Rendering.MaterialOpticsProfile.SetAmbient(ActiveSettings.AmbientSky,ActiveSettings.AmbientEquator,ActiveSettings.AmbientGround,ActiveSettings.AmbientFillStrength);
#endif
            RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=ActiveSettings.AmbientSky;RenderSettings.ambientEquatorColor=ActiveSettings.AmbientEquator;RenderSettings.ambientGroundColor=ActiveSettings.AmbientGround;
            if(sun!=null){sun.color=ActiveSettings.SunColor;sun.intensity=ActiveSettings.SunIntensity;sun.transform.rotation=Quaternion.Euler(ActiveSettings.SunElevation,ActiveSettings.SunAzimuth,0);}
            if(sky!=null){sky.SetColor("_SkyTint",ActiveSettings.SkyColor);sky.SetColor("_GroundColor",ActiveSettings.AmbientGround);
                if(sun!=null)sky.SetVector("_SunDirection",new Vector4(-sun.transform.forward.x,-sun.transform.forward.y,-sun.transform.forward.z,0));}
            if(ownedPipeline!=null)
            {
                var property=ownedPipeline.GetType().GetProperty("shadowDistance");
                if(property!=null&&property.CanWrite)property.SetValue(ownedPipeline,ActiveSettings.ShadowDistanceMeters,null);
            }
        }
        void OnGUI()
        {
            if(text==null){text=new GUIStyle(GUI.skin.label){fontSize=14,wordWrap=true};title=new GUIStyle(text){fontSize=18,fontStyle=FontStyle.Bold};}
            if(!Ready)
            {
                GUI.Box(new Rect(20,20,650,175),GUIContent.none);GUI.Label(new Rect(36,30,620,32),Failed?"World could not start":"Preparing InfiniteDiffusion world",title);
                GUI.Label(new Rect(36,68,620,70),Status,text);
                if(terrainQualityCaptured)GUI.Label(new Rect(36,140,620,40),"Terrain applied: "+appliedTerrainQuality.Label+(TerrainQualityValidationProfile?" · validation/custom profile":""),text);return;
            }
            if(showHud&&!settingsOpen)
            {
                GUI.Box(new Rect(14,14,720,68),GUIContent.none);GUI.Label(new Rect(28,21,690,25),Player.Status+" · WASD move · Space jump · F fly · Shift faster · F1 settings",text);
                GUI.Label(new Rect(28,48,690,25),"Explore · "+Source.WorldWidthMeters/1000+" × "+Source.WorldDepthMeters/1000+" km · H hides this display",text);
            }
            if(!settingsOpen)return;
            GUILayout.BeginArea(new Rect(20,20,580,Mathf.Min(Screen.height-40,780)),GUI.skin.box);
            GUILayout.Label("Exploration beta settings",title);GUILayout.Label("WASD · F walk/fly · Q/E or Ctrl/Space in flight · N noclip · Escape pointer",text);
            settingsScroll=GUILayout.BeginScrollView(settingsScroll);
            GUILayout.Label("Beta: terrain can pop while refining and higher quality can remain budget limited. Grounded roots and responsive traversal are experimental.",text);
            DrawTerrainQualitySettings();
            bool traversal=GUILayout.Toggle(Player.EnableTraversalExperiment,"Responsive traversal (experimental)");
            if(traversal!=Player.EnableTraversalExperiment)Player.SetTraversalExperiment(traversal);
            GUILayout.Label("Local bee patch: "+(bees==null?"preparing":bees.Status+" · drawn "+bees.DrawnActors),text);
            bool geology=GUILayout.Toggle(ActiveSettings.ExperimentalBroadLithology,"Broad rock formations (experimental, next Play)");
            if(geology!=ActiveSettings.ExperimentalBroadLithology){ActiveSettings.ExperimentalBroadLithology=geology;optionsNotice="Rock style selected. Save options, then restart Play.";}
            GUILayout.Label("Rock style applied: "+(appliedBroadLithology?"broad":"original")+(GeologyRestartPending?" - restart pending":"")+". Reduces repeated bands; may look too uniform. Source height unchanged.",text);
            if(distantRocks!=null){distantRocks.ShowDistantRocks=GUILayout.Toggle(distantRocks.ShowDistantRocks,"Distant rock cues (render-only, experimental)");GUILayout.Label(distantRocks.Status+". Exaggerated outcrops; no gameplay collision.",text);}
            bool roots=GUILayout.Toggle(ActiveSettings.EnableGroundedRoots,"Grounded bases (experimental, next Play)");
            if(roots!=ActiveSettings.EnableGroundedRoots)
            {ActiveSettings.EnableGroundedRoots=roots;optionsNotice="Grounded bases selected. Save these options, then restart Play to apply.";}
            GUILayout.Label("Grounded bases applied: "+(appliedGroundedRoots?"on":"off")+" · selected: "+(ActiveSettings.EnableGroundedRoots?"on":"off")+
                (GroundedRootsRestartPending?" · restart pending":"")+". Extra memory is required; native acceptance remains open.",text);
            if(Population.EnableGroundedRoots)GUILayout.Label(Population.GroundedRootStatus,text);
            ActiveSettings.WalkSpeed=Slider("Walk speed",ActiveSettings.WalkSpeed,1,10," m/s");
            ActiveSettings.FlightSpeed=Slider("Flight speed",ActiveSettings.FlightSpeed,4,200," m/s");
            ActiveSettings.FlightBoost=Slider("Flight boost",ActiveSettings.FlightBoost,1,8,"×");
            ActiveSettings.FieldOfView=Slider("Field of view",ActiveSettings.FieldOfView,45,110,"°");
            ActiveSettings.ViewDistanceKm=Slider("Terrain view",ActiveSettings.ViewDistanceKm,20,40," km");
            ActiveSettings.FarHorizonKm=Slider("Coarse horizon (0 off)",ActiveSettings.FarHorizonKm,0,100," km");
            ActiveSettings.TreeDistanceMeters=Slider("Tree distance",ActiveSettings.TreeDistanceMeters,500,2500," m");
            ActiveSettings.GroundCoverDistanceMeters=Slider("Small plants",ActiveSettings.GroundCoverDistanceMeters,12,64," m");
            ActiveSettings.FogEnabled=GUILayout.Toggle(ActiveSettings.FogEnabled,"Atmospheric fog");
            ActiveSettings.FogDensity=Slider("Fog density",ActiveSettings.FogDensity,.000005f,.0002f,"");
            ActiveSettings.AmbientFillStrength=Slider("Shadow fill",ActiveSettings.AmbientFillStrength,0,2,"×");
            ActiveSettings.WindStrength=Slider("Wind strength",ActiveSettings.WindStrength,0,2,"×");
            ActiveSettings.SunElevation=Slider("Sun elevation",ActiveSettings.SunElevation,5,80,"°");
            if(GUILayout.Button("Apply view and movement"))ApplyRuntimeSettings(true);
#if UNITY_5_3_OR_NEWER
            GUILayout.Label(Upscaler!=null?Upscaler.Status:"Native resolution",text);
            if(GUILayout.Button("Image quality: "+ActiveSettings.Upscaling+" (click to change)"))
            {ActiveSettings.Upscaling=(ExplorationUpscaleMode)(((int)ActiveSettings.Upscaling+1)%3);Upscaler?.ApplyMode();}
#endif
            if(GUILayout.Button("Save these options"))SaveOptions();
            if(GUILayout.Button("Return to starting clearing")){Player.ResetAt(spawn);Vista.ForceReplan();}
            GUILayout.Label(optionsNotice,text);
            bool live=GUILayout.Toggle(ActiveSettings.EnableLiveRefinement,"Use local InfiniteDiffusion refinement service");
            if(live!=ActiveSettings.EnableLiveRefinement){ActiveSettings.EnableLiveRefinement=live;LiveRefinement.SetEnabled(live);}
            if(LiveRefinement!=null)GUILayout.Label(LiveRefinement.Status,text);
            double weight=Source.FineWeightAt(Player.Feet.X,Player.Feet.Z);
            bool liveDetail=LiveRefinement!=null&&LiveRefinement.HasActiveDetail(Player.Feet.X,Player.Feet.Z);
            double resolution=LiveRefinement!=null?LiveRefinement.ResolutionMetersAt(Player.Feet.X,Player.Feet.Z):Source.ModelResolutionMeters(Player.Feet.X,Player.Feet.Z);
            GUILayout.Label(liveDetail?"Terrain here: live decoded InfiniteDiffusion · "+resolution.ToString("F0",CultureInfo.InvariantCulture)+"m source":weight>.999?"Terrain here: bundled decoded InfiniteDiffusion5m data":weight>0?"Terrain here: learned coarse/decoded transition":"Terrain here: learned coarse world; decoded detail is bundled around the starting area",text);
            GUILayout.Label("Trees "+Population.VisibleInstances+" · triangles "+Population.VisibleTriangles+" · terrain tiles "+Vista.ResidentTiles+" · "+Population.Status,text);
            if(VoxelTerrain!=null)GUILayout.Label(VoxelTerrain.Status+" · "+VoxelTerrain.Triangles+" triangles",text);
            if(Vista.UseVoxelLodTerrain)GUILayout.Label("World voxel LODs: "+Vista.VoxelLodTiles+" · "+Vista.TerrainTriangles+" triangles · "+Vista.PixelTargetLimitedTiles+" tiles at detail cap · worker "+Vista.LastVoxelWorkerMilliseconds.ToString("F1")+"ms",text);
            if(Vista.UseVoxelLodTerrain)GUILayout.Label("10cm ground: "+Vista.ContactTiles+" tiles ready · "+Vista.ContactPendingTiles+" pending · radius "+Vista.VoxelFullDetailRadius.ToString("F0")+"m · "+(Vista.ContactMeshBytes/1048576)+" MiB. Distant queue: "+Vista.PendingTiles,text);
            if(Population.Horizon!=null)GUILayout.Label("Forest horizon "+(Population.HorizonDistance/1000).ToString("F0")+" km · "+Population.Horizon.ResidentStands+" stands · "+(Population.Horizon.UsingGpu?"GPU indirect":"CPU instanced")+" · queue "+Population.Horizon.PendingSamples+" · "+Population.Horizon.Error,text);
            GUILayout.EndScrollView();
            if(GUILayout.Button("Continue exploring (F1)")){settingsOpen=false;Player.SetInputEnabled(true);ApplyRuntimeSettings(true);}
            GUILayout.EndArea();
        }
        float Slider(string label,float value,float min,float max,string unit)
        {GUILayout.Label(label+": "+value.ToString(max<.01f?"F6":"F1",CultureInfo.InvariantCulture)+unit,text);return GUILayout.HorizontalSlider(value,min,max);}
        /// <summary>Open controls or release pointer without changing saved options.</summary>
        public void SetSettingsPanel(bool open,bool gameplayInput=true)
        {settingsOpen=open;Player?.SetInputEnabled(Ready&&!open&&gameplayInput);}
        public void SelectTerrainQuality(TerrainQualityPreset preset)
        {
            if(ActiveSettings==null)return;
            ActiveSettings.SelectTerrainQuality(preset);
            optionsNotice="Terrain "+ActiveSettings.TerrainQuality+" selected. Save these options, then restart Play to apply.";
        }
        void DrawTerrainQualitySettings()
        {
            GUILayout.Label("Terrain quality · restart Play to apply",title);
            GUILayout.Label("Applied: "+appliedTerrainQuality.Label+" · Selected: "+ActiveSettings.TerrainQuality+
                (TerrainQualityRestartPending?" · restart pending":""),text);
            if(GUILayout.Button("Select terrain quality: "+ActiveSettings.TerrainQuality+" (click to change)"))
                SelectTerrainQuality((TerrainQualityPreset)(((int)ActiveSettings.TerrainQuality+1)%3));
            if(GUILayout.Button("Select Normal (rollback)"))SelectTerrainQuality(TerrainQualityPreset.Normal);
            GUILayout.Label("Detailed refines farther terrain. Maximum greedily requests the finest supported visible tiles and can take much longer and use several GiB. Save options, then restart Play.",text);
            var selected=ActiveSettings.ResolveTerrainQuality();long selectedReserve=(long)selected.MaximumTiles*WorldExplorer.CoverageTileMeshReserveBytes;
            GUILayout.Label("Selected payload ceilings: far detail "+selected.DetailMiB+" MiB + coarse "+selectedReserve/1048576+" MiB per plan; current + next plans "+
                ((long)selected.DetailMiB*1048576+2*selectedReserve)/1048576+" MiB, plus contact "+selected.ContactMiB+" MiB. These are limits, not eager allocations or a safe total-memory guarantee.",text);
            if(Vista==null)return;
            GUILayout.Label((TerrainQualityValidationProfile?"VALIDATION/CUSTOM PROFILE · ":"")+"Applied "+Vista.MaximumTiles+" tiles · "+Vista.VoxelTargetPixels.ToString("F2")+" px · "+
                (Vista.VoxelGreedyVisibleDetail?"greedy visible detail":"pixel target")+" · split "+Vista.VoxelPlanSplitFactor.ToString("F1"),text);
            GUILayout.Label("Mesh payload limits: far detail "+Vista.TerrainDetailLimitBytes/1048576+" MiB + coarse reserve "+Vista.TerrainCoveragePlanReserveBytes/1048576+
                " MiB per plan; current + next plan ceiling "+Vista.TerrainTransitionMeshLimitBytes/1048576+" MiB, plus contact "+Vista.ContactMeshLimitBytes/1048576+" MiB.",text);
            GUILayout.Label("These ceilings exclude worker data, upload scratch, native/driver copies, vegetation and the rest of the process. Actual far "+Vista.TerrainResidentMeshBytes/1048576+
                " MiB · contact "+Vista.ContactMeshBytes/1048576+" MiB · upload scratch "+Vista.VoxelScratchBytes/1048576+" MiB.",text);
            GUILayout.Label("Terrain quality: "+Vista.TerrainQualityLimitReason+" · planned "+Vista.TerrainPlannedTiles+" · refinement pending "+Vista.VoxelRefinementPendingTiles+
                " · budget deferred "+Vista.VoxelRefinementDeferredTiles+" · face limited "+Vista.VoxelFaceCapacityLimitedTiles+" · plan splits unmet "+Vista.TerrainPlanPendingSplits,text);
        }
        [Serializable] sealed class UserOptions
        {
            public float walk=4,sprint=7,flight=40,boost=4,fov=75,view=30,horizon,trees=1400,plants=32,fog=.00004f,sun=36;
            public bool fogOn=true;public int imageOptionsVersion,upscaling;public float ambientFill,windStrength;
            public int terrainOptionsVersion,terrainQuality;
            public int rootOptionsVersion;public bool groundedRoots;
            public int geologyOptionsVersion;public bool broadLithology;
        }
        string OptionsPath=>Path.Combine(Application.persistentDataPath,"exploration-options.json");
        void SaveOptions()
        {
            try
            {
                var s=ActiveSettings;var o=new UserOptions{walk=s.WalkSpeed,sprint=s.SprintSpeed,flight=s.FlightSpeed,boost=s.FlightBoost,fov=s.FieldOfView,view=s.ViewDistanceKm,horizon=s.FarHorizonKm,trees=s.TreeDistanceMeters,plants=s.GroundCoverDistanceMeters,fog=s.FogDensity,sun=s.SunElevation,fogOn=s.FogEnabled,imageOptionsVersion=3,upscaling=(int)s.Upscaling,ambientFill=s.AmbientFillStrength,windStrength=s.WindStrength};
                o.terrainOptionsVersion=1;o.terrainQuality=(int)ActiveSettings.TerrainQuality;
                o.rootOptionsVersion=1;o.groundedRoots=ActiveSettings.EnableGroundedRoots;
                o.geologyOptionsVersion=1;o.broadLithology=ActiveSettings.ExperimentalBroadLithology;
                Directory.CreateDirectory(Application.persistentDataPath);File.WriteAllText(OptionsPath,JsonUtility.ToJson(o,true));optionsNotice="Options saved."+(TerrainQualityRestartPending||GroundedRootsRestartPending||GeologyRestartPending?" Terrain quality/grounded bases/rock style apply after restarting Play.":"");
            }
            catch(Exception exception){optionsNotice="Could not save options: "+exception.Message;}
        }
        void LoadOptions()
        {
            try
            {
                if(!File.Exists(OptionsPath))return;UserOptions o=JsonUtility.FromJson<UserOptions>(File.ReadAllText(OptionsPath));if(o==null)return;
                var s=ActiveSettings;s.WalkSpeed=Mathf.Clamp(o.walk,.5f,12);s.SprintSpeed=Mathf.Clamp(o.sprint,1,20);s.FlightSpeed=Mathf.Clamp(o.flight,4,200);s.FlightBoost=Mathf.Clamp(o.boost,1,8);s.FieldOfView=Mathf.Clamp(o.fov,45,110);
                s.ViewDistanceKm=Mathf.Clamp(o.view,20,40);s.FarHorizonKm=Mathf.Clamp(o.horizon,0,100);s.TreeDistanceMeters=Mathf.Clamp(o.trees,500,2500);s.GroundCoverDistanceMeters=Mathf.Clamp(o.plants,12,64);s.FogDensity=Mathf.Clamp(o.fog,.000005f,.0002f);s.SunElevation=Mathf.Clamp(o.sun,5,80);s.FogEnabled=o.fogOn;
                if(o.imageOptionsVersion>=3)s.WindStrength=Mathf.Clamp(o.windStrength,0,2);
                if(o.imageOptionsVersion>=2)s.AmbientFillStrength=Mathf.Clamp(o.ambientFill,0,2);
                if(o.imageOptionsVersion>=1)s.Upscaling=(ExplorationUpscaleMode)Mathf.Clamp(o.upscaling,0,2);
                if(o.terrainOptionsVersion==1&&o.terrainQuality>=0&&o.terrainQuality<=2)s.SelectTerrainQuality((TerrainQualityPreset)o.terrainQuality);
                if(o.rootOptionsVersion==1)s.EnableGroundedRoots=o.groundedRoots;
                if(o.geologyOptionsVersion==1)s.ExperimentalBroadLithology=o.broadLithology;
            }
            catch(Exception exception){optionsNotice="Saved options ignored: "+exception.Message;}
        }
        void OnDestroy()
        {
#if UNITY_5_3_OR_NEWER
            AshenVoxel.Rendering.MaterialOpticsProfile.ClearAmbient();
#endif
            if(Player!=null)Player.SetInputEnabled(false);
            if(worldObject!=null)QualitySettings.shadowDistance=previousShadowDistance;
            if(ownedPipeline!=null){if(GraphicsSettings.defaultRenderPipeline==ownedPipeline)GraphicsSettings.defaultRenderPipeline=previousPipeline;if(QualitySettings.renderPipeline==ownedPipeline)QualitySettings.renderPipeline=previousQualityPipeline;Destroy(ownedPipeline);}
            if(sky!=null){if(RenderSettings.skybox==sky)RenderSettings.skybox=previousSky;Destroy(sky);}
            if(instanceMaterial!=null)Destroy(instanceMaterial);if(ActiveSettings!=null)Destroy(ActiveSettings);
        }
    }
}
