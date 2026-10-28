using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace AshenVoxel.Exploration.Editor
{
    // Passive two-phase reviewed diagnostic. Each invocation needs its own explicit
    // one-shot marker. Data validation never arms or invokes rendering.
    [InitializeOnLoad] public static class RootPreviewTriggerV3
    {
        const string Request=".terrain-cache/root-contact-preview-v3.request";
        const string Dll="C:/Users/ethan/Downloads/AshenVoxel-WorldVoxels/.terrain-cache/root-contact-mesh-prototype/native-api/20261027T123046059947Z/RootContactNativeFixture.dll";
        const string Expected="a2ebac329e6097a79955cb9758086ce4727d53a8e2ce75708cb2ade75e2edfe1";
        static double next;static bool running;
        static RootPreviewTriggerV3(){EditorApplication.update+=Poll;}
        static void Poll()
        {
            if(running||EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+1;
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists(Request))return;
            running=true;
            try
            {
                var info=new FileInfo(Request);bool valid=info.Length<=128&&(DateTime.UtcNow-info.LastWriteTimeUtc).TotalSeconds<=30;
                string request=valid?File.ReadAllText(Request).Trim():"expired-or-oversized";
                File.Move(Request,Request+".consumed-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ"));
                string method=request=="validate-root-preview-3"?"ValidateOnly":request=="reviewed-root-preview-3"?"Run":throw new InvalidDataException("Unexpected or expired root-preview request");
                byte[] bytes=File.ReadAllBytes(Dll);string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
                if(hash!=Expected)throw new InvalidDataException("Root-preview DLL differs from reviewed fixture");
                var assembly=Assembly.Load(bytes);var type=assembly.GetType("AshenVoxel.Exploration.Editor.RootContactNativePreview",true);
                type.GetMethod(method,BindingFlags.Public|BindingFlags.Static).Invoke(null,null);
            }
            catch(Exception e){Debug.LogException(e);}
            finally{running=false;}
        }
    }
}
