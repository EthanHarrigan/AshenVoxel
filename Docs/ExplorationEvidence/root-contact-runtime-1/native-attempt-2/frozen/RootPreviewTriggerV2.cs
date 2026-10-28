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
    [InitializeOnLoad] public static class RootPreviewTriggerV2
    {
        const string Request=".terrain-cache/root-contact-preview-v2.request";
        const string Dll="C:/Users/ethan/Downloads/AshenVoxel-WorldVoxels/.terrain-cache/root-contact-mesh-prototype/native-api/20261027T120855950302Z/RootContactNativeFixture.dll";
        const string Expected="c4640de4c18f2df2827676006bc645a63e2d9c147abb2658f64916baf3120103";
        static double next;static bool running;
        static RootPreviewTriggerV2(){EditorApplication.update+=Poll;}
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
                string method=request=="validate-root-preview-2"?"ValidateOnly":request=="reviewed-root-preview-2"?"Run":throw new InvalidDataException("Unexpected or expired root-preview request");
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
