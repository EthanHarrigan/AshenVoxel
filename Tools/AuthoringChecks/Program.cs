using System;
using System.Reflection;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;
using AshenVoxel.Authoring;
using AshenVoxel.Core.Trees;
static class Program
{
    static int checks;
    static void Check(bool value,string title){if(!value)throw new Exception(title);checks++;Console.WriteLine("PASS "+title);}
    static void ExportProfiles()
    {
        var result=new Dictionary<string,Dictionary<string,object>>();
        foreach(TreeSpecies species in Enum.GetValues(typeof(TreeSpecies)))
        {
            var profile=new TreeProfile{Species=species,Seed=714103+(int)species*977};profile.ApplySpeciesDefaults();
            var fields=new Dictionary<string,object>();
            foreach(var field in typeof(TreeProfile).GetFields(BindingFlags.Instance|BindingFlags.Public))
            {
                object value=field.GetValue(profile);
                if(value is Color color) value=new Dictionary<string,float>{{"r",color.r},{"g",color.g},{"b",color.b},{"a",1}};
                else if(value is Enum) value=Convert.ToInt32(value);
                fields.Add(field.Name,value);
            }
            result.Add(species.ToString(),fields);
        }
        Console.WriteLine(JsonSerializer.Serialize(result));
    }
    static void Main(string[] args)
    {
        if(args.Length>0&&args[0]=="--export-profiles"){ExportProfiles();return;}
        var fresh=new TreeProfile().GetParameters();var oak=TreePresets.Get(TreeSpecies.Oak);
        bool freshMatches=true;
        foreach(var field in typeof(TreeParameters).GetFields(BindingFlags.Instance|BindingFlags.Public))
            if(!Equals(field.GetValue(fresh),field.GetValue(oak)))freshMatches=false;
        Check(freshMatches,"fresh profile matches the entire current Oak genome without applying or resetting defaults");
        foreach(TreeSpecies species in Enum.GetValues(typeof(TreeSpecies)))
        {
            var profile=new TreeProfile{Species=species,Seed=91};profile.ApplySpeciesDefaults();
            var p=profile.GetParameters();var source=TreePresets.Get(species);
            Check(p.Height==source.Height&&p.TrunkRadius==source.TrunkRadius&&p.CrownRadius==source.CrownRadius&&p.BranchCount==source.BranchCount&&p.BranchAngle==source.BranchAngle,"shape controls preserve "+species+" preset, including large branch counts");
            Check(p.BarkColor==source.BarkColor&&p.LeafColor==source.LeafColor&&p.FruitColor==source.FruitColor,"opaque color round trip for "+species);
            Check(p.WhorlSize==source.WhorlSize&&p.CrownStart==source.CrownStart&&p.ConeTaper==source.ConeTaper&&p.LeaderHeightFraction==source.LeaderHeightFraction&&p.BranchDroop==source.BranchDroop&&p.Gnarl==source.Gnarl&&p.RootFlare==source.RootFlare&&p.ForkAngle==source.ForkAngle,"advanced habit controls preserve "+species+" defaults");
            Check(p.FoliageStyle==source.FoliageStyle&&p.BarkStyle==source.BarkStyle,"foliage and bark styles preserve "+species+" defaults");
            Check(p.BarkRelief==source.BarkRelief&&p.BarkRidgeCount==source.BarkRidgeCount,"geometric bark relief controls preserve "+species+" defaults");
            Check(profile.Seed==91,"applying "+species+" shape defaults keeps seed");
        }
        var edited=new TreeProfile();edited.Height=7;edited.Species=TreeSpecies.Pine;
        Check(edited.GetParameters().Height==7,"changing species preserves explicit artist height");
        edited.FruitProbability=.4f;edited.FoliageDensity=0;edited.Age=.5f;
        var values=edited.GetParameters();
        Check(values.FruitProbability==.4f&&values.FoliageDensity==0&&values.Age==.5f,"fruit, bare branches and age map into shared growth parameters");
        edited.WhorlSize=7;edited.CrownStart=.6f;edited.ConeTaper=1.8f;edited.LeaderHeightFraction=.95f;
        edited.BranchDroop=.8f;edited.Gnarl=.9f;edited.RootFlare=1.7f;edited.ForkAngle=61;
        edited.FoliageStyle=TreeFoliageStyle.ScaleSpray;edited.BarkStyle=TreeBarkStyle.Birch;
        values=edited.GetParameters();
        Check(values.WhorlSize==7&&values.CrownStart==.6f&&values.ConeTaper==1.8f&&values.LeaderHeightFraction==.95f&&values.BranchDroop==.8f&&values.Gnarl==.9f&&values.RootFlare==1.7f&&values.ForkAngle==61,"artist habit overrides map independently of species presets");
        Check(values.FoliageStyle==TreeFoliageStyle.ScaleSpray&&values.BarkStyle==TreeBarkStyle.Birch,"artist foliage and bark style overrides reach the shared generator");
        edited.BarkColor=new Color(.2f,.3f,.4f,0);edited.LeafColor=new Color(.3f,.4f,.5f,.2f);edited.FruitColor=new Color(.8f,.1f,.2f,.4f);
        values=edited.GetParameters();
        Check((values.BarkColor>>24)==255&&(values.LeafColor>>24)==255&&(values.FruitColor>>24)==255,"author palettes always map to opaque metadata");
        edited.Height=120;edited.TrunkRadius=6;edited.CrownRadius=35;edited.Age=1.25f;
        edited.BarkRelief=1.2f;edited.BarkRidgeCount=31;
        typeof(TreeProfile).GetMethod("OnValidate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(edited,null);
        values=edited.GetParameters();
        Check(values.Height==120&&values.TrunkRadius==6&&values.CrownRadius==35&&values.Age==1.25f,"workshop accepts full supported mature dimensions without truncating artist values");
        Check(values.BarkRelief==1.2f&&values.BarkRidgeCount==31,"artist geometric bark controls map into the shared genome");
        var tallBounds=TreeShowcase.CalculateBounds(new[]{values},true);
        Check(tallBounds.Height>values.Height*values.Age*1.08f&&tallBounds.Height<200,"preview vertical bounds accommodate the largest supported profile, age and seed variation below the domain cap");
        var redwood=new TreeProfile{Species=TreeSpecies.Redwood};redwood.ApplySpeciesDefaults();
        typeof(TreeProfile).GetMethod("OnValidate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(redwood,null);
        Check(redwood.GetParameters().TrunkRadius==TreePresets.Get(TreeSpecies.Redwood).TrunkRadius,"inspector keeps mature redwood trunk radius intact");
        var redwoodBounds=TreeShowcase.CalculateBounds(new[]{redwood.GetParameters()},true);
        Check(redwoodBounds.Height>redwood.Height*redwood.Age*1.08f&&redwoodBounds.Side>redwood.CrownRadius*2,"preview frames full mature redwood with vertical and canopy margin");
        var camera=new WorkshopOrbitCamera();camera.Frame(new Vector3(0,80,0),300);
        float framed=(float)typeof(WorkshopOrbitCamera).GetField("framedDistance",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(camera);
        Check(framed==300,"inspection camera can frame a large mature redwood beyond the former 180 meter clamp");
        var cameraType=typeof(WorkshopOrbitCamera);
        FieldInfo targetField=cameraType.GetField("target",BindingFlags.Instance|BindingFlags.NonPublic);
        FieldInfo distanceField=cameraType.GetField("distance",BindingFlags.Instance|BindingFlags.NonPublic);
        FieldInfo yawField=cameraType.GetField("yaw",BindingFlags.Instance|BindingFlags.NonPublic);
        FieldInfo pitchField=cameraType.GetField("pitch",BindingFlags.Instance|BindingFlags.NonPublic);
        MethodInfo updateCamera=cameraType.GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic);
        UnityEngine.InputSystem.Mouse.current=new UnityEngine.InputSystem.Mouse();
        var mouse=UnityEngine.InputSystem.Mouse.current;mouse.middleButton.isPressed=true;
        mouse.delta.value=new Vector2{x=10,y=10};
        yawField.SetValue(camera,90f);pitchField.SetValue(camera,0f);distanceField.SetValue(camera,100f);
        updateCamera.Invoke(camera,null);
        var pan100=(Vector3)targetField.GetValue(camera);
        Check(Math.Abs(pan100.x)<.0001&&pan100.y<80&&pan100.z>.9f,"middle mouse pans along rotated screen right/up toward details at the tree base");
        targetField.SetValue(camera,new Vector3(0,80,0));distanceField.SetValue(camera,200f);
        updateCamera.Invoke(camera,null);var pan200=(Vector3)targetField.GetValue(camera);
        Check(Math.Abs(pan200.z-pan100.z*2)<.0001&&Math.Abs((80-pan200.y)-(80-pan100.y)*2)<.0001,"pan speed scales with inspection distance in meters per screen pixel");
        mouse.middleButton.isPressed=false;mouse.scroll.value=new Vector2{y=120};
        UnityEngine.InputSystem.Keyboard.current=new UnityEngine.InputSystem.Keyboard();
        UnityEngine.InputSystem.Keyboard.current.homeKey.wasPressedThisFrame=true;
        updateCamera.Invoke(camera,null);var homeTarget=(Vector3)targetField.GetValue(camera);
        Check(homeTarget.x==0&&homeTarget.y==80&&homeTarget.z==0&&(float)distanceField.GetValue(camera)==300&&(float)yawField.GetValue(camera)==-35&&(float)pitchField.GetValue(camera)==16,"Home restores the original framing target, distance and angles after panning and zooming");
        UnityEngine.InputSystem.Mouse.current=null;UnityEngine.InputSystem.Keyboard.current=null;
        edited.Height=2;edited.TrunkRadius=2;edited.CrownRadius=8;
        typeof(TreeProfile).GetMethod("OnValidate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(edited,null);
        values=edited.GetParameters();
        Check(values.TrunkRadius<=values.Height*.4f&&values.CrownRadius<=values.Height*2.5f,"inspector validation preserves supported proportions");
        Console.WriteLine($"All {checks} authoring checks passed under test color, input and geometry substitutes. Unity editor serialization and inspectors remain unverified.");
    }
}
