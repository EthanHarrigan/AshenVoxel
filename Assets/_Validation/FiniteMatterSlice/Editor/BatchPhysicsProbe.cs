using System;
using System.IO;
using AshenVoxel.Core.Experimental;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace AshenVoxel.FiniteMatter.Editor
{
    [InitializeOnLoad] public static class BatchPhysicsProbe
    {
        static string Root=>Path.GetDirectoryName(Application.dataPath);
        static string Request=>Path.Combine(Root,".terrain-cache/content-batch-physics.request");
        static bool running;static double began;static int samples;
        [Serializable] sealed class Report{public string status;public int samples;public float maximumRotation;public bool impact,conserved;public long owned,credited;public int debris,remaining;}
        static BatchPhysicsProbe(){EditorApplication.update+=Tick;}
        static void Tick()
        {
            if(!EditorApplication.isPlaying)
            {
                if(running){running=false;return;}
                string prepare=Path.Combine(Root,".terrain-cache/content-batch-prepare-physics.request");
                if(File.Exists(prepare)&&!EditorApplication.isCompiling&&!EditorApplication.isUpdating)
                {string token=File.ReadAllText(prepare);File.Delete(prepare);if(token.Trim()=="prepare-finite-slice-1")FiniteMatterSliceMenu.Open();}
                return;
            }
            if(SceneManager.GetActiveScene().path!="Assets/Scenes/FiniteMatterSlice.unity")return;
            var owner=UnityEngine.Object.FindFirstObjectByType<FiniteMatterSliceOwner>();if(owner==null)return;
            if(!running)
            {
                if(!File.Exists(Request))return;string token=File.ReadAllText(Request);File.Delete(Request);
                if(token.Trim()!="finite-slice-runtime-1")return;
                began=EditorApplication.timeSinceStartup;samples=0;running=true;owner.AutoCut=true;owner.AutoSuctionAfterImpact=true;
            }
            samples++;
            if(EditorApplication.timeSinceStartup-began<12)return;
            var actor=owner.Actor;
            var r=new Report{samples=samples,maximumRotation=owner.MaximumRotationDegrees,impact=actor!=null&&actor.Aggregate.ImpactReported,
                conserved=owner.ConservationIntact,owned=owner.OwnedCells,credited=owner.Matter.CreditedCells,debris=owner.Matter.DebrisCount,remaining=actor==null?-1:actor.Aggregate.Remaining};
            r.status=r.impact&&r.maximumRotation>1&&r.conserved&&r.credited==72?"FINITE_BRANCH_FALL_IMPACT_COLLECTION_PASS":"FINITE_BRANCH_ACCEPTANCE_FAILED";
            var dir=Path.Combine(Root,".terrain-cache/content-batch-physics");Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"report.json"),JsonUtility.ToJson(r,true));
            running=false;EditorApplication.isPlaying=false;
        }
    }
}
