using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using AshenVoxel.WorldScaleRuntime;
using UnityEngine;

static class Program
{
    static int checks;
    static void Check(bool value,string message)
    {if(!value)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
    static void Main(string[] args)
    {
        string shaderText=File.ReadAllText(args[0]);string properties=shaderText.Substring(0,shaderText.IndexOf("SubShader",StringComparison.Ordinal));
        Shader.ClipmapTextureNames=new HashSet<string>();
        foreach(string name in new[]{"_CmHeights","_CmTint","_CmAppearance"})
        {
            Check(Regex.IsMatch(properties,@"\[HideInInspector\]\s*\[NoScaleOffset\]\s*"+name+@"\s*\(""[^""]*"",\s*2DArray\)\s*=\s*""""\s*\{\s*\}"),"shader declares "+name+" as hidden NoScaleOffset 2DArray");
            Shader.ClipmapTextureNames.Add(name);
        }
        using var resources=new TerrainClipmapResources(16,1);
        Material target=resources.Material;
        var heights=target.GetTexture("_CmHeights");var tint=target.GetTexture("_CmTint");var appearance=target.GetTexture("_CmAppearance");
        target.shaderKeywords=new[]{"CLIPMAP_ONLY"};target.renderQueue=2000;
        var legacy=new Material(new Shader{TextureNames=new HashSet<string>{"_LegacyTexture"}});
        string[] names={"_GrainStrength","_FineGrainStrength","_VoxelSize","_Roughness"};
        float[] values={.07f,.02f,.1f,.88f};for(int i=0;i<names.Length;i++)legacy.SetFloat(names[i],values[i]);
        legacy.SetTexture("_LegacyTexture",new Texture());legacy.shaderKeywords=new[]{"LEGACY_ONLY"};legacy.renderQueue=2450;
        var world=new WorldExplorer(legacy);world.AttachClipmap(new TerrainClipmap(resources));
        Check(ReferenceEquals(target.GetTexture("_CmHeights"),heights),"attach retains exact owned height texture");
        Check(ReferenceEquals(target.GetTexture("_CmTint"),tint),"attach retains exact owned tint texture");
        Check(ReferenceEquals(target.GetTexture("_CmAppearance"),appearance),"attach retains exact owned appearance texture");
        for(int i=0;i<names.Length;i++)Check(target.GetFloat(names[i])==values[i],"shared style copied: "+names[i]);
        Check(target.shaderKeywords.Length==1&&target.shaderKeywords[0]=="CLIPMAP_ONLY","legacy keywords do not replace clipmap keywords");
        Check(target.renderQueue==2000,"legacy render queue is not inherited");
        Check(!target.HasProperty("_LegacyTexture"),"unrelated legacy texture is not imported");
        target.SetTexture("_CmHeights",null);target.SetTexture("_CmTint",new Texture());target.SetTexture("_CmAppearance",null);
        resources.CopySurfaceStyle(null);
        Check(ReferenceEquals(target.GetTexture("_CmHeights"),heights)&&ReferenceEquals(target.GetTexture("_CmTint"),tint)&&ReferenceEquals(target.GetTexture("_CmAppearance"),appearance),"style application restores all owned bindings after a prior reset");
        target.SetTexture("_CmHeights",null);Material.RejectTextureWrites=true;
        var fallbackWorld=new WorldExplorer(legacy);
        bool rejected=false;try{fallbackWorld.AttachClipmap(new TerrainClipmap(resources));}catch(InvalidOperationException){rejected=true;}
        finally{Material.RejectTextureWrites=false;}
        Check(rejected,"binding failure is observable instead of submitting a flat surface");
        Check(fallbackWorld.ClipmapRenderer==null&&!fallbackWorld.UsingClipmap,"failed validation leaves the replacement unattached for Legacy fallback");
        Console.WriteLine("PASS "+checks+" modeled attachment/material-binding checks; no native rendering performed.");
    }
}
