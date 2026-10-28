using UnityEngine;
using AshenVoxel.WorldScale;

namespace AshenVoxel.Exploration
{
    public enum TerrainQualityPreset { Normal, Detailed, Maximum }

    /// <summary>Restart-applied mesh requests, independent of source resolution,
    /// vegetation and authoritative10cm matter. Payload quotas exclude total process/GPU memory.</summary>
    public readonly struct TerrainQualityConfiguration
    {
        public readonly TerrainQualityPreset Preset;
        public readonly int MaximumTiles,DetailMiB,ContactMiB;
        public readonly float TargetPixels,SplitFactor;
        public readonly bool GreedyVisible,ValidationOverrides;
        public string Label=>Preset+(ValidationOverrides?" (validation/custom overrides)":"");
        public TerrainQualityConfiguration(TerrainQualityPreset preset,int tiles,float pixels,int detail,int contact,float split,bool greedy,bool validation=false)
        {Preset=preset;MaximumTiles=tiles;TargetPixels=pixels;DetailMiB=detail;ContactMiB=contact;SplitFactor=split;GreedyVisible=greedy;ValidationOverrides=validation;}
        public static TerrainQualityConfiguration ForPreset(TerrainQualityPreset preset)
        {
            switch(preset)
            {
                case TerrainQualityPreset.Detailed:return new TerrainQualityConfiguration(preset,1536,.75f,2304,1024,4.4f,false);
                case TerrainQualityPreset.Maximum:return new TerrainQualityConfiguration(preset,4096,.5f,4096,1024,20,true);
                default:return new TerrainQualityConfiguration(TerrainQualityPreset.Normal,512,1.5f,1536,1024,2.2f,false);
            }
        }
        public bool SameRequest(TerrainQualityConfiguration other)=>Preset==other.Preset&&MaximumTiles==other.MaximumTiles&&
            TargetPixels==other.TargetPixels&&DetailMiB==other.DetailMiB&&ContactMiB==other.ContactMiB&&
            SplitFactor==other.SplitFactor&&GreedyVisible==other.GreedyVisible;
    }

    /// <summary>One editable asset for the first walk/fly world. Baked learned data
    /// makes startup independent of Python; the original provider remains optional.</summary>
    [CreateAssetMenu(menuName="Ashen Voxel/Exploration settings",fileName="ExplorationSettings")]
    public sealed class ExplorationSettings:ScriptableObject
    {
        [Header("Learned world data")]
        public TextAsset MacroWorld,MacroClimate,FineWorld,FineClimate,SourceBiomes,WorldMetadata;
        public TextAsset PrototypePack;
        [Tooltip("Surface/vegetation variation. The learned terrain seed belongs to the immutable world bake.")]
        public int Seed=714103;
        [Tooltip("Next Play: versioned small earth forms everywhere, shared by terrain, plants and collision. Does not replace the learned source.")]
        public bool EnableSurfaceRelief=true;
        public bool UseImportedDimensions=true;
        [Tooltip("Scale5 of the transferred map is approximately300 by150km. Horizontal resize does not change the10cm matter contract.")]
        public double WorldWidthMeters=299520,WorldDepthMeters=149760;
        [Range(.1f,3)] public float VerticalScale=1;
        [Range(100,2000)] public float FineBlendMeters=512;
        [Tooltip("Optional original-model service. Off keeps the bundled learned world completely offline; enable in F1 after starting the local provider.")]
        public bool EnableLiveRefinement;
        public string DiffusionEndpoint="http://127.0.0.1:8787";
        public bool UseRecommendedSpawn=true;
        public double SpawnX,SpawnY=10,SpawnZ;
        public float SpawnYaw=20,SpawnPitch=8;

        [Header("Basic player")]
        [Range(.5f,12)] public float WalkSpeed=4;
        [Range(1,20)] public float SprintSpeed=7;
        [Range(4,200)] public float FlightSpeed=40;
        [Range(1,8)] public float FlightBoost=4;
        [Range(45,110)] public float FieldOfView=75;
        [Range(.02f,.4f)] public float MouseSensitivity=.1f;
        [Range(4,40)] public float EyeSmoothing=20;
        public bool StartFlying;
        public bool AllowNoClipToggle=true;
        [Range(.1f,.45f)] public float BodyRadius=.28f;
        [Range(1.4f,2.2f)] public float BodyHeight=1.8f;
        [Range(1.2f,2)] public float EyeHeight=1.65f;
        [Range(.1f,.5f)] public float StepHeight=.32f;
        [Range(30,60)] public float MaximumWalkSlope=50;
        [Tooltip("Per overlapping 10cm leaf cell; sticks count four times. Appearance and material IDs are unchanged.")]
        [Range(0,.005f)] public float VegetationDragPerVoxel=.0008f;
        [Range(.4f,1)] public float MinimumVegetationSpeed=.65f;

        [Header("Terrain and distant mountains")]
        [Tooltip("Requires restarting Play. Maximum greedily requests256 columns for visible tiles and is expensive; mesh/plan caps can leave demand unmet.")]
        public TerrainQualityPreset TerrainQuality=TerrainQualityPreset.Normal;
        [Tooltip("Next Play: voxel terrain across the whole view.10cm implicit matter, exposed-face LODs and existing material layers. No terrain editing or simulation is enabled.")]
        public bool EnableVoxelTerrain=true;
        [Tooltip("Smaller values favor detail. Projected horizontal face-size target; tile/sample caps can limit it. Applies on next Play.")]
        [Range(.5f,8)] public float VoxelTargetPixels=1.5f;
        [Tooltip("Independent10cm ground coverage follows you everywhere and publishes each completed tile immediately. Distant terrain keeps voxel LODs. Applies on next Play.")]
        [Range(16,160)] public float VoxelFullDetailRadius=100;
        [Tooltip("Applies next Play. Far mesh detail MiB, additional to reserved coarse coverage. This is mesh payload, not total RAM/VRAM.")]
        [Range(64,4096)] public int VoxelDetailBudgetMB=1536;
        [Tooltip("Applies next Play. Contact mesh payload MiB; admitted tiles retain10cm detail.")]
        [Range(128,2048)] public int VoxelContactBudgetMB=1024;
        [Range(20,40)] public float ViewDistanceKm=30;
        [Tooltip("Zero disables the extra coarse horizon. This is bounded low-detail coverage, not full-resolution terrain.")]
        [Range(0,100)] public float FarHorizonKm;
        [Range(192,4096)] public int MaximumTerrainTiles=512;
        [Range(16,64)] public float MinimumTerrainTileMeters=32;
        [Range(32,256)] public float NearTerrainRadius=96;
        [Range(32,64)] public int NearTerrainQuads=64;
        [Range(.25f,8)] public float TerrainBuildMilliseconds=2;
        [Range(32,2048)] public int TerrainSampleWork=256;

        [Header("Forest and ground cover")]
        [Range(0,2),Tooltip("Visible branch sway and restrained leaf flutter. Zero disables wind; collision stays fixed.")] public float WindStrength=1;
        [Range(.1f,2)] public float TreeDensity=1;
        [Range(.1f,2)] public float GroundCoverDensity=1;
        [Range(20,100)] public float FullTreeDetailMeters=100;
        [Tooltip("A full10cm75m redwood is expensive; use its coarser LODs beyond the trunk vicinity.")]
        [Range(6,100)] public float RedwoodFullDetailMeters=40;
        [Range(100,500)] public float MediumTreeDetailMeters=300;
        [Range(500,2500)] public float TreeDistanceMeters=1400;
        [Tooltip("Fit mature redwood bases to terrain. Requires restarting Play.")]
        public bool EnableGroundedRoots=false;
        [Tooltip("Experimental broad rock formations; reduces repeating bands but can look uniform. Restart Play.")]
        public bool ExperimentalBroadLithology=false;
        [Tooltip("Experimental next-Play far render grid, reducing mesh payload and cubic steps. Near/contact remains cubic. Native acceptance pending.")]
        public bool ExperimentalDistantHeightProxies=false;
        public bool EnableForestHorizon=true;
        [Tooltip("Forests follow the complete terrain view, including the extra coarse horizon, instead of stopping at a separate forest distance.")]
        public bool ForestFollowsTerrainView=true;
        [Tooltip("Next Play: use compute visibility filtering and indirect drawing for distant stands when supported. Habitat planning remains asynchronous CPU work.")]
        public bool UseGpuForestHorizon=true;
        [Tooltip("Next Play: use actual-prototype silhouette images for far trees. Disable to compare the original voxel LODs.")]
        public bool UseTreeImpostors=true;
        [Tooltip("Manual forest range when Forest Follows Terrain View is disabled.")]
        [Range(2,100)] public float ForestHorizonKm=30;
        public float EffectiveForestDistanceKm=>Mathf.Clamp(ForestFollowsTerrainView?Mathf.Max(ViewDistanceKm,FarHorizonKm):Mathf.Min(ForestHorizonKm,Mathf.Max(ViewDistanceKm,FarHorizonKm)),2,100);
        [Range(12,64)] public float GroundCoverDistanceMeters=64;
        [Range(60,600)] public float RockDistanceMeters=220;
        [Range(2000,60000)] public int MaximumResidentInstances=40000;
        [Range(.25f,6)] public float PopulationBuildMilliseconds=2;
        [Range(1000000,64000000)] public int VegetationTriangleBudget=32000000;

        [Header("Image quality")]
        public ExplorationUpscaleMode Upscaling=ExplorationUpscaleMode.NvidiaDlaa;

        [Header("Atmosphere and rock lighting")]
        public bool FogEnabled=true;
        public FogMode FogMode=UnityEngine.FogMode.ExponentialSquared;
        [Range(.000005f,.0002f)] public float FogDensity=.00004f;
        public Color FogColor=new Color(.49f,.59f,.68f,1);
        public Color SkyColor=new Color(.42f,.53f,.65f,1);
        [Range(0,2),Tooltip("Sky and ground fill in shadows. Independent of sun direction.")] public float AmbientFillStrength=1.5f;
        public Color AmbientSky=new Color(.72f,.78f,.85f,1);
        public Color AmbientEquator=new Color(.58f,.66f,.73f,1);
        public Color AmbientGround=new Color(.36f,.39f,.32f,1);
        public Color SunColor=new Color(1,.92f,.81f,1);
        [Range(.5f,4)] public float SunIntensity=2.1f;
        [Range(5,80)] public float SunElevation=36;
        [Range(0,360)] public float SunAzimuth=325;
        [Range(50,300)] public float ShadowDistanceMeters=260;
        [Tooltip("Applied when the Editor creates or repairs the exploration renderer.")]
        public bool EnableAmbientOcclusion=true;
        public bool ShowVisualWater=true;

        [Header("Static surface generation")]
        public bool EnableSnow=true;
        [Range(0,8)] public float MaximumSnowDepth=2.4f;
        [Range(-30,30)] public float TemperatureBias;
        [Range(-1,1)] public float WinterFraction=.2f;
        [Range(-100,100)] public float SeaLevel;

        public ExplorationMovementSettings Movement()=>new ExplorationMovementSettings(
            walkSpeed:Mathf.Clamp(WalkSpeed,.5f,12),sprintSpeed:Mathf.Clamp(SprintSpeed,1,20),
            flightSpeed:Mathf.Clamp(FlightSpeed,4,200),flightBoost:Mathf.Clamp(FlightBoost,1,8),
            radius:Mathf.Clamp(BodyRadius,.1f,.45f),height:Mathf.Clamp(BodyHeight,1.4f,2.2f),
            eyeHeight:Mathf.Min(Mathf.Clamp(EyeHeight,1.2f,2),Mathf.Clamp(BodyHeight,1.4f,2.2f)-.05f),
            stepHeight:Mathf.Clamp(StepHeight,.1f,.5f),maxSlopeDegrees:Mathf.Clamp(MaximumWalkSlope,30,60),
            dragPerSoftVoxel:Mathf.Clamp(VegetationDragPerVoxel,0,.005f),minimumVegetationSpeed:Mathf.Clamp(MinimumVegetationSpeed,.4f,1));
        public SurfaceProfile Surface()=>new SurfaceProfile(seed:Seed,seaLevelMeters:SeaLevel,
            maxSnowDepthMeters:MaximumSnowDepth,enableSnow:EnableSnow,winterFraction:WinterFraction,temperatureBiasC:TemperatureBias,experimentalBroadLithology:ExperimentalBroadLithology);
        public TerrainQualityConfiguration ResolveTerrainQuality()
        {
            var preset=(TerrainQualityPreset)Mathf.Clamp((int)TerrainQuality,0,2);
            if(preset!=TerrainQualityPreset.Normal)return TerrainQualityConfiguration.ForPreset(preset);
            // Existing serialized/copied test profiles keep their explicit values.
            // Selecting Normal explicitly restores the canonical shipping request.
            var normal=TerrainQualityConfiguration.ForPreset(TerrainQualityPreset.Normal);
            int tiles=Mathf.Clamp(MaximumTerrainTiles,64,4096),detail=Mathf.Clamp(VoxelDetailBudgetMB,64,4096),contact=Mathf.Clamp(VoxelContactBudgetMB,128,2048);
            float pixels=Mathf.Clamp(VoxelTargetPixels,.5f,8);
            return new TerrainQualityConfiguration(preset,tiles,pixels,detail,contact,normal.SplitFactor,false,
                tiles!=normal.MaximumTiles||pixels!=normal.TargetPixels||detail!=normal.DetailMiB||contact!=normal.ContactMiB);
        }
        public void SelectTerrainQuality(TerrainQualityPreset preset)
        {
            var selected=TerrainQualityConfiguration.ForPreset(preset);TerrainQuality=selected.Preset;
            MaximumTerrainTiles=selected.MaximumTiles;VoxelTargetPixels=selected.TargetPixels;
            VoxelDetailBudgetMB=selected.DetailMiB;VoxelContactBudgetMB=selected.ContactMiB;
        }
        void OnValidate()
        {
            MinimumTerrainTileMeters=Mathf.Clamp(MinimumTerrainTileMeters,16,64);
            NearTerrainQuads=Mathf.Clamp(Mathf.Max(NearTerrainQuads,Mathf.CeilToInt(MinimumTerrainTileMeters)),32,64);
            MaximumTerrainTiles=Mathf.Clamp(MaximumTerrainTiles,64,4096);
            TerrainQuality=(TerrainQualityPreset)Mathf.Clamp((int)TerrainQuality,0,2);
        }
    }
}
