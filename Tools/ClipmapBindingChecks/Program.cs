using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using AshenVoxel.WorldScaleRuntime;
using AshenVoxel.WorldScale;
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
        foreach(string name in new[]{"_CmHeights","_CmTint","_CmAppearance","_CmFarTint","_CmFarAppearance"})
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
        resources.CopySurfaceStyle(null);
        Check(resources.FarAppearanceGpuBytes==0&&!resources.FarAppearancePublished,"optional appearance allocates nothing before requested");
        int oldBuffers=ComputeBuffer.Created;
        var first=new FarAppearanceWindow(384,128.0/3,-192,-384,1);
        var valuesFar=new FarAppearanceSample[384*384];var full=new ClipmapRect{X=first.OriginX,Z=first.OriginZ,Width=384,Height=384};
        resources.BeginFarAppearance(first,false,valuesFar.Length);
        Check(resources.FarAppearanceGpuBytes==2359296,"Fast pair of banks and channels is exactly 2.25 MiB");
        Check(TerrainClipmapResources.EstimateFarAppearanceGpuBytes(768)==9437184,"High pair of banks and channels is exactly 9 MiB");
        Check(TerrainClipmapResources.EstimateGpuBytes(512,15)+TerrainClipmapResources.EstimateFarAppearanceGpuBytes(768)==61L*1048576&&TerrainClipmapResources.EstimateGpuBytes(512,16)+TerrainClipmapResources.EstimateFarAppearanceGpuBytes(768)==64L*1048576,"High total payload includes primary arrays and reused upload buffer");
        Check(ComputeBuffer.Created==oldBuffers,"appearance reuses the primary GPU upload buffer");
        var farTint=target.GetTexture("_CmFarTint");var farAppearance=target.GetTexture("_CmFarAppearance");
        Check(farTint!=null&&farAppearance!=null,"declared optional texture bindings are populated");
        resources.CopySurfaceStyle(legacy);
        Check(ReferenceEquals(target.GetTexture("_CmFarTint"),farTint)&&ReferenceEquals(target.GetTexture("_CmFarAppearance"),farAppearance),"style refresh retains exact optional texture bindings");
        Check(!resources.CanPublishFarAppearance,"partial optional upload cannot publish");
        Rejected(()=>resources.PublishFarAppearance(first),"early publication rejected");
        var stale=new FarAppearanceWindow(384,first.Spacing,first.OriginX,first.OriginZ,0);
        Rejected(()=>resources.UploadFarAppearance(stale,full,valuesFar),"stale upload generation rejected");
        resources.UploadFarAppearance(first,full,valuesFar);
        Check(resources.CanPublishFarAppearance,"first complete window can publish from zero blend");
        resources.PublishFarAppearance(first);
        Check(resources.FarAppearancePublished&&resources.FarAppearanceBlend==0,"first publication starts on existing coarse appearance");
        for(int i=0;i<5;i++)resources.AdvanceFarAppearance(.1);
        Check(resources.FarAppearanceBlend==1,"published appearance smoothly reaches full contribution");
        var next=new FarAppearanceWindow(384,first.Spacing,first.OriginX,first.OriginZ,2);
        resources.BeginFarAppearance(next,true,valuesFar.Length);resources.UploadFarAppearance(next,full,valuesFar);
        Check(!resources.CanPublishFarAppearance,"complete replacement cannot overwrite a visibly sampled bank");
        resources.AdvanceFarAppearance(.1);
        Check(resources.FarAppearanceBlend>0&&resources.FarAppearanceBlend<1,"replacement fades old appearance toward coarse before swap");
        resources.CancelFarAppearance();for(int i=0;i<5;i++)resources.AdvanceFarAppearance(.1);
        Check(resources.FarAppearanceBlend==1&&resources.FarAppearancePublished&&!resources.CanPublishFarAppearance,"cancelled generation restores existing bank without publishing stale work");
        resources.BeginFarAppearance(next,true,valuesFar.Length);resources.UploadFarAppearance(next,full,valuesFar);
        for(int i=0;i<4;i++)resources.AdvanceFarAppearance(.1);
        Check(resources.CanPublishFarAppearance&&resources.FarAppearanceBlend==0,"replacement swap waits until old bank is no longer sampled");
        Rejected(()=>resources.PublishFarAppearance(first),"stale publication generation rejected");
        resources.PublishFarAppearance(next);resources.AdvanceFarAppearance(.1);
        Check(resources.FarAppearanceBlend>0&&resources.FarAppearanceBlend<1,"new bank fades in after atomic publication");
        resources.DisableFarAppearance();
        Check(resources.FarAppearanceGpuBytes==0&&!resources.FarAppearancePublished,"optional failure/disable releases only appearance allocations");
        Check(ReferenceEquals(target.GetTexture("_CmHeights"),heights),"disabling optional detail preserves primary height resources");
        Console.WriteLine("PASS "+checks+" modeled attachment/material-binding checks; no native rendering performed.");
    }
    static void Rejected(Action action,string message)
    {bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}Check(rejected,message);}
}
