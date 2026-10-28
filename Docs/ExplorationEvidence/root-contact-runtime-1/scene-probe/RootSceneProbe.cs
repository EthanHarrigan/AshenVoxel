using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AshenVoxel.Exploration.Editor
{
    // Deliberately has no InitializeOnLoad, menu item, persistence, or rendering path.
    public static class RootSceneProbe
    {
        [Serializable] sealed class SceneRecord
        {
            public int handle, roots;
            public string path, name;
            public bool valid, loaded, dirty;
            public static SceneRecord Read(Scene scene)
            {
                bool valid = scene.IsValid();
                return new SceneRecord { handle = scene.handle, valid = valid,
                    loaded = valid && scene.isLoaded, path = valid ? scene.path : "",
                    name = valid ? scene.name : "", dirty = valid && scene.isDirty,
                    roots = valid && scene.isLoaded ? scene.rootCount : -1 };
            }
            public bool Same(SceneRecord other) => handle == other.handle && roots == other.roots &&
                path == other.path && name == other.name && valid == other.valid && loaded == other.loaded && dirty == other.dirty;
        }
        [Serializable] sealed class Report
        {
            public string label, status = "RUNNING", error = "";
            public string scope = "One synchronous empty additive Editor scene; no GameObjects, materials, cameras, rendering, shader setters, Play, or scene save. Scene snapshots are JSON text in primitive string fields.";
            public string scene_creation_api = "EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive)";
            public string editor_scene_manager_assembly_full_name, editor_scene_manager_assembly_location;
            public string scene_manager_assembly_full_name, scene_manager_assembly_location;
            public string json_utility_assembly_full_name, json_utility_assembly_location;
            public string scene_assembly_full_name, scene_assembly_location;
            public string previous_scene, new_scene, new_scene_after_set, new_scene_after_cleanup;
            public string active_before, active_after_new, active_after_set, active_after_cleanup;
            public string scenes_before, scenes_after_new, scenes_after_set, scenes_after_cleanup;
            public int scene_count_before, scene_count_after_new, scene_count_after_set, scene_count_after_cleanup;
            public bool new_scene_returned, set_active_attempted, set_active_result, set_active_postcondition;
            public bool owned_close_attempted, owned_close_result, active_restore_attempted, active_restore_result;
            public bool active_restored, originals_unchanged, owned_removed, cleanup_verified;
        }
        public static bool LastCleanupVerified { get; private set; }
        static SceneRecord[] Snapshot()
        {
            var result = new SceneRecord[SceneManager.sceneCount];
            for (int i = 0; i < result.Length; i++) result[i] = SceneRecord.Read(SceneManager.GetSceneAt(i));
            return result;
        }
        static string Json(Scene scene) => JsonUtility.ToJson(SceneRecord.Read(scene));
        static string Json(SceneRecord[] scenes) => "[" + String.Join(",", scenes.Select(s => JsonUtility.ToJson(s))) + "]";
        static bool Same(SceneRecord[] before, SceneRecord[] after)
        {
            if (before.Length != after.Length) return false;
            for (int i = 0; i < before.Length; i++) if (!before[i].Same(after[i])) return false;
            return true;
        }
        public static string RunOnce(string label)
        {
            LastCleanupVerified = false;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Scene probe requires idle Editor.");
            // Exact compile-time engine types: no loaded-assembly search or substitute types.
            var report = new Report { label = label,
                editor_scene_manager_assembly_full_name = typeof(EditorSceneManager).Assembly.FullName,
                editor_scene_manager_assembly_location = typeof(EditorSceneManager).Assembly.Location,
                scene_manager_assembly_full_name = typeof(SceneManager).Assembly.FullName,
                scene_manager_assembly_location = typeof(SceneManager).Assembly.Location,
                json_utility_assembly_full_name = typeof(JsonUtility).Assembly.FullName,
                json_utility_assembly_location = typeof(JsonUtility).Assembly.Location,
                scene_assembly_full_name = typeof(Scene).Assembly.FullName,
                scene_assembly_location = typeof(Scene).Assembly.Location };
            Scene previous = SceneManager.GetActiveScene(), owned = default;
            SceneRecord[] originals = Snapshot();
            var originalHandles = new HashSet<int>(originals.Select(s => s.handle));
            report.previous_scene = report.active_before = Json(previous);
            report.scenes_before = Json(originals); report.scene_count_before = originals.Length;
            try
            {
                owned = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                report.new_scene_returned = true; report.new_scene = Json(owned);
                report.active_after_new = Json(SceneManager.GetActiveScene());
                var afterNew = Snapshot(); report.scenes_after_new = Json(afterNew); report.scene_count_after_new = afterNew.Length;
                if (originalHandles.Contains(owned.handle)) throw new InvalidOperationException("New scene returned an original handle; refusing to change it.");
                report.set_active_attempted = true;
                report.set_active_result = SceneManager.SetActiveScene(owned);
                report.set_active_postcondition = SceneManager.GetActiveScene() == owned;
                report.active_after_set = Json(SceneManager.GetActiveScene()); report.new_scene_after_set = Json(owned);
                var afterSet = Snapshot(); report.scenes_after_set = Json(afterSet); report.scene_count_after_set = afterSet.Length;
                report.status = report.set_active_result && report.set_active_postcondition ? "SCENE_ACTIVE" : "SCENE_NOT_ACTIVE";
            }
            catch (Exception e) { report.status = "FAIL"; report.error = e.ToString(); }
            finally
            {
                void Cleanup(Action action)
                { try { action(); } catch (Exception e) { report.status = "FAIL"; report.error += " Cleanup: " + e; } }
                Cleanup(() =>
                {
                    if (SceneManager.GetActiveScene() != previous)
                    { report.active_restore_attempted = true; report.active_restore_result = SceneManager.SetActiveScene(previous); }
                    else report.active_restore_result = true;
                    report.active_restored = SceneManager.GetActiveScene() == previous;
                });
                Cleanup(() =>
                {
                    if (report.new_scene_returned && owned.IsValid() && !originalHandles.Contains(owned.handle))
                    { report.owned_close_attempted = true; report.owned_close_result = EditorSceneManager.CloseScene(owned, true); }
                });
                Cleanup(() =>
                {
                    var final = Snapshot(); report.scene_count_after_cleanup = final.Length; report.scenes_after_cleanup = Json(final);
                    report.active_after_cleanup = Json(SceneManager.GetActiveScene()); report.new_scene_after_cleanup = Json(owned);
                    report.active_restored = SceneManager.GetActiveScene() == previous;
                    report.originals_unchanged = Same(originals, final);
                    report.owned_removed = !report.new_scene_returned || !owned.IsValid() || (!originalHandles.Contains(owned.handle) && !final.Any(s => s.handle == owned.handle));
                    report.cleanup_verified = report.active_restored && report.originals_unchanged && report.owned_removed;
                    LastCleanupVerified = report.cleanup_verified;
                    if (!report.cleanup_verified) { report.status = "FAIL"; report.error += " Original scene restoration not verified."; }
                });
            }
            return JsonUtility.ToJson(report, true);
        }
    }
}
