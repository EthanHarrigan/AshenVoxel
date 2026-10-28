using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace AshenVoxel.Exploration.Editor
{
    // Passive adapter only: one exact marker, consumed before any probe invocation.
    [InitializeOnLoad] public static class RootSceneProbeTrigger
    {
        const string Request = "C:/Users/ethan/Downloads/AshenVoxel/.terrain-cache/root-contact-scene-probe.request";
        const string Output = "C:/Users/ethan/Downloads/AshenVoxel-WorldVoxels/.terrain-cache/root-contact-scene-probe/native";
        const string Dll = "C:/Users/ethan/Downloads/AshenVoxel-WorldVoxels/.terrain-cache/root-contact-scene-probe/api/20261027T122546296461Z/RootSceneProbeDynamic.dll";
        const string Expected = "c526f5348e2e3e81ab38cc8c3e8e07eb1027a13e60989f5e9dfee16599bb4c8b";
        static bool attempted;
        static double next;
        [Serializable] sealed class AdapterReport
        {
            public string status = "RUNNING", error = "", dll_sha256, imported_report, dynamic_report;
            public bool marker_consumed, imported_called, dynamic_called;
        }
        static RootSceneProbeTrigger() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (attempted || EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 1;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Request)) return;
            attempted = true;
            var report = new AdapterReport(); string folder = null;
            try
            {
                if (!String.Equals(Path.GetFullPath(Application.dataPath).TrimEnd('/','\\'), "C:\\Users\\ethan\\Downloads\\AshenVoxel\\Assets", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Probe is scoped to the existing main Editor.");
                // Move before parsing/loading/invoking; a reload cannot replay this request.
                string consumed = Request + ".consumed-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ");
                File.Move(Request, consumed); report.marker_consumed = true;
                if (new FileInfo(consumed).Length > 128) throw new InvalidDataException("Oversized scene-probe marker.");
                string[] parts = File.ReadAllText(consumed).TrimEnd('\r','\n').Split('\t');
                if (parts.Length != 2 || parts[0] != "root-scene-probe-v1" || !DateTime.TryParseExact(parts[1], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime issued) || issued.Kind != DateTimeKind.Utc)
                    throw new InvalidDataException("Unexpected scene-probe marker.");
                double age = (DateTime.UtcNow - issued).TotalSeconds;
                if (age < 0 || age > 30) throw new InvalidDataException("Scene-probe marker expired or future dated.");
                if (new FileInfo(Dll).Length > 256 * 1024) throw new InvalidDataException("Probe DLL exceeds fixed size cap.");
                byte[] bytes = File.ReadAllBytes(Dll);
                using (var sha = SHA256.Create()) report.dll_sha256 = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                if (report.dll_sha256 != Expected) throw new InvalidDataException("Probe DLL differs from reviewed identity.");
                folder = Path.Combine(Output, DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ")); Directory.CreateDirectory(folder);
                report.imported_called = true;
                report.imported_report = RootSceneProbe.RunOnce("source-imported");
                File.WriteAllText(Path.Combine(folder, "imported.json"), report.imported_report);
                if (!RootSceneProbe.LastCleanupVerified) throw new InvalidOperationException("Imported probe did not restore originals; dynamic probe skipped.");
                if ((DateTime.UtcNow - issued).TotalSeconds > 30) throw new InvalidOperationException("Marker expired before dynamic probe; skipped.");
                var assembly = Assembly.Load(bytes);
                var type = assembly.GetType("AshenVoxel.Exploration.Editor.RootSceneProbe", true);
                report.dynamic_called = true;
                report.dynamic_report = (string)type.GetMethod("RunOnce", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[]{"dynamic-dll"});
                File.WriteAllText(Path.Combine(folder, "dynamic.json"), report.dynamic_report);
                bool restored = (bool)type.GetProperty("LastCleanupVerified", BindingFlags.Public | BindingFlags.Static).GetValue(null);
                report.status = restored ? "BOTH_PROBES_RECORDED" : "FAIL";
                if (!restored) report.error = "Dynamic probe did not verify restoration.";
            }
            catch (Exception e) { report.status = "FAIL"; report.error = e.ToString(); Debug.LogException(e); }
            finally
            {
                if (folder == null) { folder = Path.Combine(Output, "rejected-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ")); Directory.CreateDirectory(folder); }
                File.WriteAllText(Path.Combine(folder, "adapter.json"), JsonUtility.ToJson(report, true));
            }
        }
    }
}
