#if UNITY_EDITOR
using System;
using System.IO;
using System.Security.Cryptography;
using AshenVoxel.WorldScale;
using UnityEditor;
using UnityEngine;

namespace AshenVoxel.Exploration.Editor
{
    /// <summary>Read-only Edit-mode validation of the installed source assets.</summary>
    public static class CoarseOverviewNativeCheck
    {
        const string Folder="Docs/Coarse320Evidence/2026-10-27/installation";
        [Serializable] sealed class Report
        {
            public string status,utc,unity,scope,world_id,detail_world_id,source_identity,error;
            public string macro_sha256,climate_sha256,fine_sha256,biome_sha256;
            public int checks,columns,rows;
            public double spacing_m,saved_spawn_ground_m,decoded_patch_ground_m;
            public bool playing,scene_dirty_before,scene_dirty_after;
        }
        static string Hash(byte[] data){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant();}
        static HeightAtlas Height(TextAsset asset){using(var stream=new MemoryStream(asset.bytes,false))return AtlasIO.Read(stream);}
        static ClimateAtlas Climate(TextAsset asset){using(var stream=new MemoryStream(asset.bytes,false))return ClimateArchiveIO.Read(stream);}
        public static void Run()
        {
            var report=new Report{status="FAILED",utc=DateTime.UtcNow.ToString("o"),unity=Application.unityVersion,
                scope="Actual imported Unity assets and source composition in Edit mode; no Play, visual scene or FPS acceptance",
                playing=EditorApplication.isPlaying,scene_dirty_before=UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty};
            void Check(bool value,string message){if(!value)throw new InvalidDataException(message);report.checks++;}
            try
            {
                Check(!EditorApplication.isPlayingOrWillChangePlaymode,"Source check requires stopped Editor");
                var settings=AssetDatabase.LoadAssetAtPath<ExplorationSettings>("Assets/AshenVoxel/Settings/ExplorationSettings.asset");
                Check(settings!=null&&settings.MacroWorld!=null&&settings.MacroClimate!=null&&settings.FineWorld!=null&&settings.FineClimate!=null,"Next-launch settings resolve imported source assets");
                var macro=Height(settings.MacroWorld);var fine=Height(settings.FineWorld);
                ExplorationBiomeAtlas biomes;
                using(var stream=new MemoryStream(settings.SourceBiomes.bytes,false))biomes=ExplorationBiomeAtlasIO.Read(stream);
                var source=new ExplorationAtlasField(macro,Climate(settings.MacroClimate),fine,Climate(settings.FineClimate),
                    settings.FineBlendMeters,macro.ExtentX,macro.ExtentZ,settings.VerticalScale,biomes,macro.ExtentX,macro.ExtentZ);
                report.world_id=source.WorldId;report.detail_world_id=source.DetailWorldId;report.source_identity=source.SourceIdentity;
                report.columns=macro.Columns;report.rows=macro.Rows;report.spacing_m=source.ModelResolutionMeters(-40000,-20000);
                report.macro_sha256=Hash(settings.MacroWorld.bytes);report.climate_sha256=Hash(settings.MacroClimate.bytes);
                report.fine_sha256=Hash(settings.FineWorld.bytes);report.biome_sha256=Hash(settings.SourceBiomes.bytes);
                Check(report.world_id=="653a72959be6e6ec9abb8e9643c685b2c690c183768a109f2110ede02b7dfb99","Installed overview identity");
                Check(report.detail_world_id=="14fb32f26ff725abaf9f1e04668a1f46c5b4783a66f91d23253676f595f613e4"&&biomes.WorldId==report.detail_world_id,"Original decoded/service and biome identity retained");
                Check(report.macro_sha256=="50b4265dc3e19d9b3e7dd179e9d7241ae90aa0e94ab47b1cf6cc04318408bc70"&&
                    report.climate_sha256=="a73e7b03be8dfdb22bacd4da711640a7f18a0d1a6a8a903389b02275b9bc1040","Imported overview payloads match the validated bake exactly");
                Check(macro.Columns==937&&macro.Rows==469&&report.spacing_m==320,"Whole-map320m source resolution");
                Check(report.fine_sha256=="0b9896f85e253b8bb1794473dbf01cc3205afe501fa5581d2ca098ee9a458ddb","Saved decoded patch is unchanged");
                report.saved_spawn_ground_m=source.Sample(settings.SpawnX,settings.SpawnZ).HeightMeters;
                report.decoded_patch_ground_m=source.Sample(-19065,-10200).HeightMeters;
                Check(Math.Abs(report.decoded_patch_ground_m-288.04400634765625)<.000001&&source.ModelResolutionMeters(-19065,-10200)==5,"Original decoded interior stays exact at5m");
                Check(source.TrySampleClimate(settings.SpawnX,settings.SpawnZ,out _)&&source.TrySampleClimate(-19065,-10200,out _),"Both source stages provide paired physical climate");
                var metadata=JsonUtility.FromJson<ExplorationBootstrap.WorldMetadata>(settings.WorldMetadata.text);
                Check(metadata.world_id==source.WorldId,"Spawn metadata matches the new overview identity");
                report.scene_dirty_after=UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty;
                Check(report.scene_dirty_after==report.scene_dirty_before,"Read-only verification leaves scene state unchanged");
                report.status="PASS";
                Debug.Log("COARSE320_NATIVE_SOURCE_PASS "+report.checks+" checks");
            }
            catch(Exception error){report.error=error.ToString();throw;}
            finally{Directory.CreateDirectory(Folder);File.WriteAllText(Folder+"/native-source.json",JsonUtility.ToJson(report,true));}
        }
    }
}
#endif
