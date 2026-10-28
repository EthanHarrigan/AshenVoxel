using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace AshenVoxel.Exploration.Editor
{
    // Passive owned diagnostic adapter. No request => no allocation or invocation.
    // Only the one frozen fixture is permitted; request contents cannot supply code.
    [InitializeOnLoad] public static class RootPreviewTrigger
    {
        const string Request=".terrain-cache/root-contact-preview.request";
        const string Dll="C:/Users/ethan/Downloads/AshenVoxel-WorldVoxels/.terrain-cache/root-contact-mesh-prototype/native-api/20261027T115309745673Z/RootContactNativeFixture.dll";
        const string Expected="490b33713722f84da1db528b720621fe5072681959255da80677ccea03a517d3";
        static double next;static bool running;
        static RootPreviewTrigger(){EditorApplication.update+=Poll;}
        static void Poll()
        {
            if(running||EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+1;
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists(Request))return;
            running=true;
            try
            {
                // Consume before invoking, so a domain reload cannot repeat a failed run.
                string request=File.ReadAllText(Request).Trim();
                File.Move(Request,Request+".consumed-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ"));
                if(request!="reviewed-root-preview-1")throw new InvalidDataException("Unexpected root-preview request identity");
                byte[] bytes=File.ReadAllBytes(Dll);string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
                if(hash!=Expected)throw new InvalidDataException("Root-preview DLL hash differs from reviewed fixture");
                var assembly=Assembly.Load(bytes);var type=assembly.GetType("AshenVoxel.Exploration.Editor.RootContactNativePreview",true);
                type.GetMethod("Run",BindingFlags.Public|BindingFlags.Static).Invoke(null,null);
            }
            catch(Exception e){Debug.LogException(e);}
            finally{running=false;}
        }
    }
}
