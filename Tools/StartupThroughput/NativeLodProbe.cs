using System;using System.Collections.Generic;using System.Diagnostics;using System.IO;
using UnityEditor;using UnityEngine;using AshenVoxel.WorldScale;
public static class NativeLodProbe
{
    [Serializable] sealed class Report{public string status="RUNNING",scope="Unity Mono CPU-only actual-world LOD generation. One tile at a time; no GPU uploads, native frame rate or whole-startup claim.",unity,source,error;public double plan_ms;public int pending_splits;public List<LodMeasurements.PlanRow> plan;public List<LodMeasurements.Row> rows=new List<LodMeasurements.Row>();public List<string> capacity_failures=new List<string>();}
    static Report report;static SurfaceMaterialSampler sampler;static WorldPoint eye;static List<Tuple<LodTile,int,string>> cases;static int index;static string output;
    public static void Run()
    {
        try
        {
            output=Path.GetFullPath("Logs/lod-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".json");Directory.CreateDirectory(Path.GetDirectoryName(output));
            AssetDatabase.DesiredWorkerCount=0;AssetDatabase.ForceToDesiredWorkerCount();
            sampler=LodMeasurements.Load(Path.GetFullPath("../frozen"));eye=LodMeasurements.Eye(sampler);var sw=Stopwatch.StartNew();var plan=LodMeasurements.Plan(sampler,eye);sw.Stop();
            report=new Report{unity=Application.unityVersion,source=sampler.SourceIdentity,plan_ms=sw.Elapsed.TotalMilliseconds,pending_splits=plan.PendingSplits,plan=LodMeasurements.Describe(plan,eye)};cases=LodMeasurements.Cases(plan,eye);
            EditorApplication.update+=Tick;
        }
        catch(Exception e){Fail(e);}
    }
    static void Tick()
    {
        try
        {
            if(index>=cases.Count*4){report.status="PASS";Save();EditorApplication.update-=Tick;UnityEngine.Debug.Log("LOD_PROBE_PASS "+output);EditorApplication.Exit(0);return;}
            int repeat=index/cases.Count-1,which=index%cases.Count;var item=cases[which];
            try{var row=LodMeasurements.Measure(sampler,eye,item,repeat);if(repeat>=0)report.rows.Add(row);}
            catch(VoxelLodCapacityException e){if(repeat==0)report.capacity_failures.Add(item.Item1.Key+"/"+item.Item2+":"+e.Message);}
            index++;Save();if(index%8==0)GC.Collect();
        }
        catch(Exception e){Fail(e);}
    }
    static void Save()=>File.WriteAllText(output,JsonUtility.ToJson(report,true));
    static void Fail(Exception e){if(report!=null){report.status="FAIL";report.error=e.ToString();Save();}UnityEngine.Debug.LogException(e);EditorApplication.Exit(1);}
}
