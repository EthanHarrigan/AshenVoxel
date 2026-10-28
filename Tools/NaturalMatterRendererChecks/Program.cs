using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Collections.Generic;
using System.Security.Cryptography;
using AshenVoxel.Core;
using AshenVoxel.Exploration.Experimental;
using UnityEngine;
static class Program
{
    static int checks;
    static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
    static T Get<T>(object value,string name)=>(T)value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value);
    static void Complete(NaturalMatterRenderer renderer)
    {
        for(int i=0;i<10000;i++)
        {
            int uploads=Mesh.Uploads;renderer.Tick(Vector3.zero);
            if(Mesh.Uploads-uploads>1)throw new Exception("More than one actual group uploaded in a tick");
            if(renderer.Error!=null)throw new Exception(renderer.Error);if(renderer.Current)return;Thread.Sleep(1);
        }
        throw new Exception("Async cache failed to finish");
    }
    static void Worker(NaturalMatterRenderer renderer)
    {
        renderer.Tick(Vector3.zero);var task=Get<Task>(renderer,"worker");if(task==null||!task.Wait(10000))throw new Exception("Worker did not complete");
    }
    static NaturalMatterRenderer Renderer(NaturalTreePhysics physics)
    {var result=new NaturalMatterRenderer(new GameObject("test root").transform,new Material((Material)null));result.SetPhysics(physics);return result;}
    static string GeometryHash(object result)
    {
        object Field(object value,string name)=>value.GetType().GetField(name,BindingFlags.Public|BindingFlags.Instance).GetValue(value);
        using var stream=new MemoryStream();using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))
        {
            foreach(object geometry in (Array)Field(result,"Geometry"))
            {
                foreach(string field in new[]{"Vertices","Normals"})
                {var values=(List<Vector3>)Field(geometry,field);writer.Write(values.Count);foreach(var v in values){writer.Write(v.x);writer.Write(v.y);writer.Write(v.z);}}
                var colors=(List<Color32>)Field(geometry,"Colors");writer.Write(colors.Count);foreach(var c in colors){writer.Write(c.r);writer.Write(c.g);writer.Write(c.b);writer.Write(c.a);}
                var indices=(List<int>)Field(geometry,"Indices");writer.Write(indices.Count);foreach(int index in indices)writer.Write(index);
            }
            var mask=(byte[])Field(result,"Mask");writer.Write(mask.Length);writer.Write(mask);
        }
        using var sha=SHA256.Create();return Convert.ToHexString(sha.ComputeHash(stream.ToArray())).ToLowerInvariant();
    }
    static void Main(string[] args)
    {
        object realRedwood=null;
        var world=new VoxelWorld(8,8,8,.1f,1);world.Set(0,0,0,MaterialId.Wood);world.Set(1,0,0,MaterialId.Wood);
        var physics=new NaturalTreePhysics(world,new Int3(0,0,0),1.2f);var renderer=Renderer(physics);Complete(renderer);
        var source=Get<Mesh[]>(renderer,"published")[0];var objects=Get<GameObject[]>(renderer,"objects");
        Check(source.vertexCount==40&&source.Indices.Length==60,"two coherent cubes emit ten exposed faces");
        Check(objects.Length==9&&renderer.RenderingCellSize==.1f,"small matter retains10cm appearance in nine bounded objects");
        Check(Math.Abs(source.Vertices.Min(v=>v.x))<1e-6&&Math.Abs(source.Vertices.Max(v=>v.x)-.2f)<1e-6,"source geometry stays canonical despite initial yaw");
        int uploads=Mesh.Uploads;renderer.SetVisible(true);for(int i=0;i<30;i++)renderer.Tick(new Vector3(10,20,30));
        Check(Mesh.Uploads==uploads&&objects[0].transform.position.x==10&&Math.Abs(objects[0].transform.rotation.y-physics.SourceRotation.Y)<1e-6,"idle origin/rotation updates rewrite no mesh vertices");
        renderer.SetVisible(false);Check(Get<MeshRenderer[]>(renderer,"renderers").All(r=>!r.enabled),"visibility disables all ordinary rigid draws");renderer.Dispose();
        Check(Get<Mesh[]>(renderer,"published").All(m=>m==null)&&objects.All(o=>o.WasDestroyed),"dispose releases bounded rigid objects and meshes");

        world=new VoxelWorld(1,1,1,.1f,1);world.Set(0,0,0,MaterialId.Wood);physics=new NaturalTreePhysics(world,new Int3(0,0,0),0){SuctionEnabled=false,TerrainHeight=(x,z)=>0};
        physics.Cut(new Int3(0,0,0));physics.Step(.02f);renderer=Renderer(physics);Complete(renderer);renderer.SetVisible(true);
        int instancesBefore=Graphics.Instances;renderer.Tick(Vector3.zero);
        Check(physics.AwakeDebris==0&&physics.DormantDebris==1&&Graphics.Instances-instancesBefore==1,"actual renderer submits dormant parcel despite empty motion list");
        physics.Hand=physics.Read(physics.LooseIndex(0)).Position;physics.SuctionEnabled=true;physics.Step(.02f);physics.Step(.02f);instancesBefore=Graphics.Instances;renderer.Tick(Vector3.zero);
        Check(physics.CargoCells==1&&physics.PendingDebris==0&&Graphics.Instances==instancesBefore,"actual renderer retires collected dormant parcel exactly once");renderer.Dispose();

        world=new VoxelWorld(2000,2,400,.1f,1);for(int z=0;z<100;z++)for(int x=0;x<500;x++)world.Set(x*4,0,z*4,MaterialId.Wood);
        physics=new NaturalTreePhysics(world,new Int3(0,0,0),0);renderer=Renderer(physics);Complete(renderer);
        Check(renderer.GeometryBins<=32768&&renderer.RenderingCellSize>=.8f&&physics.Count==50000&&physics.SourceCells==50000,"sparse large source retries coarser render bins without dropping authoritative matter");
        physics.TerrainHeight=(x,z)=>0;physics.SuctionEnabled=false;Check(physics.Cut(new Int3(0,0,0)),"large source aimed edit accepted");
        for(int i=0;i<100&&physics.StructureBusy;i++)physics.Step(.02f,32768);
        Complete(renderer);
        source=Get<Mesh[]>(renderer,"published")[0];
        Check(renderer.Current&&renderer.RenderingCellSize>=.8f&&source.Vertices.Any(v=>Math.Abs(v.x-.1f)<1e-5&&v.z>=.3999f&&v.z<=.5001f),"large source exposes exact10cm cells beside latest cut while distant geometry stays coarse");renderer.Dispose();

        world=new VoxelWorld(8,16,8,.1f,1);for(int y=0;y<12;y++)world.Set(0,y,0,MaterialId.Wood);
        physics=new NaturalTreePhysics(world,new Int3(0,0,0),.4f){SuctionEnabled=false};renderer=Renderer(physics);Complete(renderer);renderer.SetVisible(true);
        Check(physics.Cut(new Int3(0,1,0)),"fixture cut accepted");renderer.Tick(Vector3.zero);
        Check(physics.StructureBusy&&renderer.LastBuildCandidates==0&&!renderer.Current,"partial support membership is never captured as a complete render revision");
        physics.Step(.02f,8192,1);Check(!physics.StructureBusy&&physics.IslandCells==10,"fixture publishes one rigid section");
        Complete(renderer);
        source=Get<Mesh[]>(renderer,"published")[1];Check(source.vertexCount>0&&renderer.Current,"detached section receives local rigid geometry");uploads=Mesh.Uploads;
        physics.Step(.02f,8192,1);renderer.Tick(new Vector3(1,2,3));physics.BodyPose(0,out var pose,out _);
        Check(Mesh.Uploads==uploads&&Math.Abs(Get<GameObject[]>(renderer,"objects")[1].transform.position.y-(2+pose.Y))<1e-6,"falling rigid section moves only its transform");renderer.Dispose();

        world=new VoxelWorld(80,3,80,.1f,1);for(int z=0;z<70;z++)for(int x=0;x<70;x++)world.Set(x,0,z,MaterialId.Wood);
        physics=new NaturalTreePhysics(world,new Int3(0,0,0),0){SuctionEnabled=false};
        // Controlled ownership fixture makes4900 cut leaders without queueing
        // thousands of unrelated support searches in a renderer-only check.
        var remove=typeof(NaturalTreePhysics).GetMethod("RemoveCell",BindingFlags.Instance|BindingFlags.NonPublic);
        for(int i=0;i<physics.Count;i++)remove.Invoke(physics,new object[]{i});renderer=Renderer(physics);Complete(renderer);renderer.SetVisible(true);
        int before=Graphics.Instances;renderer.Tick(Vector3.zero);
        Check(renderer.DebrisSampled&&renderer.DrawnDebris==4096&&Graphics.Instances-before==4096&&Graphics.MaximumBatch<=1023,"loose visual samples cap4096 in valid1023-instance batches");
        Check(physics.Conserved&&physics.IndividualCells==4900,"visual sampling never deletes or collects matter");renderer.Dispose();
        world=new VoxelWorld(100,100,100,.1f,1);for(int z=0;z<100;z++)for(int y=0;y<100;y++)for(int x=0;x<100;x++)world.Set(x,y,z,MaterialId.Wood);
        physics=new NaturalTreePhysics(world,new Int3(0,0,0),0);renderer=Renderer(physics);uploads=Mesh.Uploads;
        Worker(renderer);renderer.Tick(Vector3.zero);
        Check(renderer.Current&&Mesh.Uploads-uploads==1,"million-cell initial cache scans only on worker and publishes one nonempty source upload");
        Check(renderer.LastWorkerCandidates>=1000000&&renderer.LastBuildCandidates==0,"main Tick performs no whole-tree cell traversal");
        double capture=renderer.LastSnapshotMilliseconds,workerTime=renderer.LastWorkerMilliseconds;
        Console.WriteLine("METRIC million-cell capture_ms="+capture+" worker_ms="+workerTime);
        var snapshot=physics.CaptureRenderSnapshot();var cell=snapshot.CellAt(0).Cell;Check(physics.Cut(cell),"snapshot edit fixture");
        Check(snapshot.GroupAt(0)==0,"captured membership remains immutable after source mutation");renderer.Dispose();

        world=new VoxelWorld(8,8,8,.1f,1);world.Set(0,0,0,MaterialId.Wood);world.Set(1,0,0,MaterialId.Wood);
        physics=new NaturalTreePhysics(world,new Int3(0,0,0),0);renderer=Renderer(physics);Worker(renderer);
        Check(physics.Cut(new Int3(1,0,0)),"revision invalidation fixture");while(physics.PendingSupport>0)physics.StepSupport(100);
        renderer.Tick(Vector3.zero);Check(!renderer.Ready,"completed stale worker is not published after a newer edit");Complete(renderer);
#if !BASELINE
        Check(renderer.CancelledBuilds==1&&renderer.CompletedPublications==1&&renderer.PublishedRevision==physics.StructureRevision,"revision diagnostics count cancelled stale work and one current publication");
#endif
        Check(Get<Mesh[]>(renderer,"published")[0].vertexCount==24,"replacement revision publishes only remaining source cell");renderer.Dispose();
        physics=new NaturalTreePhysics(world,new Int3(0,0,0),0);renderer=Renderer(physics);renderer.Tick(Vector3.zero);
        var nextWorld=new VoxelWorld(8,8,8,.1f,1);nextWorld.Set(2,0,0,MaterialId.Stone);var replacement=new NaturalTreePhysics(nextWorld,new Int3(0,0,0),0);
        renderer.SetPhysics(replacement);Complete(renderer);source=Get<Mesh[]>(renderer,"published")[0];
        Check(source.Vertices.Min(v=>v.x)>.19999f&&source.Vertices.Max(v=>v.x)<.30001f,"reassignment never publishes cancelled prior-owner geometry");renderer.Dispose();
        renderer=Renderer(replacement);renderer.Tick(Vector3.zero);var cancelled=Get<Task>(renderer,"worker");renderer.Dispose();
        try{cancelled?.Wait(10000);}catch(AggregateException e){if(!e.InnerExceptions.All(x=>x is OperationCanceledException))throw;}
        renderer.Tick(Vector3.zero);Check(Get<Mesh[]>(renderer,"published").All(m=>m==null),"disposed pending worker never uploads or publishes");
        physics=new NaturalTreePhysics(nextWorld,new Int3(0,0,0),0);
        Get<int[]>(physics,"renderGroups")[0]=99;renderer=Renderer(physics);renderer.Tick(Vector3.zero);
        var failed=Get<Task>(renderer,"worker");try{failed.Wait(10000);}catch(AggregateException){}
        renderer.Tick(Vector3.zero);Check(renderer.Error!=null&&!renderer.Ready&&Get<Mesh[]>(renderer,"published").All(m=>m==null),"worker failure is observed and never publishes invalid geometry");
        renderer.SetPhysics(replacement);Complete(renderer);Check(renderer.Error==null&&renderer.Current,"reassignment clears prior worker failure safely");renderer.Dispose();
        var sharedMesh=new Mesh{name="borrowed original selected submesh"};var sharedMaterial=new Material((Material)null);sharedMaterial.SetFloat("_MatterRigid",0);sharedMaterial.SetFloat("_VoxelWindStaticInstances",1);
        var shared=new NaturalMatterRenderSource(new[]{new NaturalMatterRenderSource.Part(sharedMesh,1,true)},sharedMaterial);
        world=new VoxelWorld(32,1,1,.1f,1);for(int x=0;x<32;x++)world.Set(x,0,0,MaterialId.Wood);
        physics=new NaturalTreePhysics(world,new Int3(0,0,0),0);renderer=new NaturalMatterRenderer(new GameObject("borrowed root").transform,sharedMaterial);uploads=Mesh.Uploads;
        renderer.SetPhysics(physics,shared);renderer.SetVisible(true);renderer.Tick(Vector3.zero);
        Check(renderer.Current&&renderer.Ready&&Get<Task>(renderer,"worker")==null&&Mesh.Uploads==uploads,"borrowed intact source is immediately ready with no scan, mesh upload or worker");
        Check(Graphics.LastShared==sharedMesh&&Graphics.LastSubmesh==1&&renderer.RenderingCellSize==.1f,"borrowed drawing keeps actual original mesh and selected submesh without coarse replacement");
        Check(Get<Material[]>(renderer,"borrowedMaterials")[0].Floats["_MatterRigid"]==0,"borrowed intact material retains source wind policy");
        for(int i=0;i<16;i++)remove.Invoke(physics,new object[]{i});Worker(renderer);
        Check(!Get<bool>(renderer,"maskEnabled"),"borrowed original mask remains disabled until matching patches are ready");renderer.Tick(Vector3.zero);
        var mask=Get<Texture3D>(renderer,"ownerMask");source=Get<Mesh[]>(renderer,"published")[0];
        Check(renderer.Current&&renderer.RenderingCellSize==.1f&&mask.Data.Length==2&&mask.Data.All(x=>x==0),"removed chunk and unchanged owner boundary publish exact patches with matching mask atomically");
#if !BASELINE
        Check(renderer.LastPatchGroupPasses==1&&renderer.LastPatchCellLookups==18L*18*18,"fully removed chunks skip empty mesher passes while remaining owner keeps exact halo");
#endif
        Check(source.Vertices.Zip(source.Normals,(v,n)=>Math.Abs(v.x-1.6f)<1e-5&&n.x<-.9f).Any(x=>x),"uniform neighbor receives newly exposed cut cap across sixteen-cell boundary");
        renderer.Dispose();Check(!sharedMesh.WasDestroyed&&!sharedMaterial.WasDestroyed&&mask.WasDestroyed,"dispose releases owned mask while preserving borrowed mesh and material");

        world=new VoxelWorld(2,48,2,.1f,1);for(int y=0;y<48;y++)world.Set(0,y,0,MaterialId.Wood);physics=new NaturalTreePhysics(world,new Int3(0,0,0),.7f){SuctionEnabled=false};
        renderer=new NaturalMatterRenderer(new GameObject("borrowed island root").transform,sharedMaterial);renderer.SetPhysics(physics,shared);renderer.SetVisible(true);physics.Cut(new Int3(0,1,0));
        while(physics.PendingSupport>0)physics.StepSupport(1000);Complete(renderer);mask=Get<Texture3D>(renderer,"ownerMask");
        Check(mask.Data[2]==2&&physics.IslandCells==46,"uniform detached upper chunk reuses original mesh through island owner mask");
        Check(Get<Material[]>(renderer,"borrowedMaterials")[1].Floats["_MatterRigid"]==1,"falling borrowed geometry disables foliage wind without modifying source material");renderer.Dispose();
        world=new VoxelWorld(8,8,8,.1f,1);world.Set(1,0,1,MaterialId.Wood,0xffaa6611);world.Set(2,0,1,MaterialId.Stone);world.Set(1,1,2,MaterialId.Wood,0xff335577);world.Set(1,1,1,MaterialId.Wood,0xffaa6611);
        physics=new NaturalTreePhysics(world,new Int3(0,0,0),0);renderer=new NaturalMatterRenderer(new GameObject("AO patch root").transform,sharedMaterial);renderer.SetPhysics(physics,shared);
        int removedIndex=Enumerable.Range(0,physics.Count).First(i=>physics.ReadLocal(i).Cell.Equals(new Int3(1,1,2)));remove.Invoke(physics,new object[]{removedIndex});Complete(renderer);
        var expectedMesh=new Mesh();new AshenVoxel.Rendering.GreedyChunkMesher().Build(world,new Int3(0,0,0),expectedMesh);source=Get<Mesh[]>(renderer,"published")[0];
        Check(source.Vertices.SequenceEqual(expectedMesh.Vertices)&&source.Normals.SequenceEqual(expectedMesh.Normals)&&source.Colors.SequenceEqual(expectedMesh.Colors)&&source.Indices.SequenceEqual(expectedMesh.Indices),"exact mixed patch equals original greedy vertices normals intrinsic colors corner AO and material interface indices");renderer.Dispose();
#if !BASELINE
        world=new VoxelWorld(2,48,2,.1f,1);for(int y=0;y<48;y++)world.Set(0,y,0,MaterialId.Wood);
        physics=new NaturalTreePhysics(world,new Int3(0,0,0),0){SuctionEnabled=false};renderer=new NaturalMatterRenderer(new GameObject("owner mask group pruning").transform,sharedMaterial);renderer.SetPhysics(physics,shared);
        physics.Cut(new Int3(0,1,0));while(physics.PendingSupport>0)physics.StepSupport(1000);Complete(renderer);
        Check(renderer.LastPatchGroupPasses==3&&renderer.LastPatchCellLookups==3L*18*18*18,"mixed source/island chunk keeps both owner halos and skips groups with no interior cells");renderer.Dispose();

        world=new VoxelWorld(8,8,8,.1f,1);world.Set(1,0,1,MaterialId.Wood,0xffaa6611);world.Set(2,0,1,MaterialId.Wood,0xff335577);
        physics=new NaturalTreePhysics(world,new Int3(0,0,0),.6f){SuctionEnabled=false};renderer=new NaturalMatterRenderer(new GameObject("break acknowledgement").transform,sharedMaterial);renderer.SetPhysics(physics,shared);renderer.SetVisible(true);
        physics.Cut(new Int3(1,0,1));int feedbackRevision=physics.StructureRevision;long feedbackTotal=physics.SourceCells+physics.IslandCells+physics.IndividualCells+physics.CargoCells;
        renderer.BeginBreakFeedback(new Vector3(.15f,.05f,.15f),10);var localMaterials=Get<Material[]>(renderer,"borrowedMaterials");var sphere=localMaterials[0].Vectors["_MatterBreakSphere"];
        Check(sphere.x==.15f&&sphere.y==.05f&&sphere.z==.15f&&Math.Abs(sphere.w-.32f)<1e-6&&renderer.BreakFeedbackAmount>0,"accepted cut immediately identifies its local meter sphere for cosmetic feedback");
        renderer.UpdateBreakFeedback(11.5f);
        Check(renderer.BreakFeedbackAmount>=.449f&&renderer.BreakFeedbackAmount<=.751f&&!renderer.Current,"pending cut acknowledgement remains visible beyond one second without publication");
        Check(!sharedMaterial.Floats.ContainsKey("_MatterBreakAmount")&&localMaterials.Skip(1).All(m=>m.Floats["_MatterBreakAmount"]==0),"feedback never modifies shared source material or falling section materials");
        Check(physics.StructureRevision==feedbackRevision&&physics.SourceCells+physics.IslandCells+physics.IndividualCells+physics.CargoCells==feedbackTotal&&world.Get(2,0,1)==MaterialId.Wood&&world.GetTint(2,0,1)==0xff335577,"feedback preserves authoritative matter counts revision and intrinsic pigments");
        while(physics.PendingSupport>0)physics.StepSupport(1000);Complete(renderer);renderer.UpdateBreakFeedback(12);
        Check(renderer.BreakFeedbackAmount>0&&renderer.CompletedPublications==1&&renderer.PublishedRevision==feedbackRevision,"publication starts a short fade rather than dropping acknowledgement immediately");
        renderer.UpdateBreakFeedback(12.06f);Check(renderer.BreakFeedbackAmount>.25f&&renderer.BreakFeedbackAmount<.35f,"published cut feedback fades over120ms");
        renderer.UpdateBreakFeedback(12.13f);Check(renderer.BreakFeedbackAmount==0&&localMaterials[0].Floats["_MatterBreakAmount"]==0,"completed feedback clears after fade");
        renderer.BeginBreakFeedback(new Vector3(.25f,.05f,.15f),20);renderer.UpdateBreakFeedback(20.001f);renderer.UpdateBreakFeedback(20.1f);
        Check(renderer.BreakFeedbackAmount>0,"fast publication retains a minimum visible acknowledgement");
        renderer.SetVisible(false);Check(renderer.BreakFeedbackAmount==0,"hiding session clears break feedback");renderer.SetVisible(true);renderer.BeginBreakFeedback(new Vector3(),21);renderer.SetPhysics(physics,shared);
        Check(renderer.BreakFeedbackAmount==0&&Get<Material[]>(renderer,"borrowedMaterials")[0].Floats["_MatterBreakAmount"]==0,"reassignment cannot carry feedback into another session");renderer.SetVisible(true);renderer.BeginBreakFeedback(new Vector3(),22);renderer.Dispose();Check(renderer.BreakFeedbackAmount==0,"disposal clears pending feedback");
#endif
        world=new VoxelWorld(48,48,48,.1f,1);for(int z=0;z<48;z++)for(int y=0;y<48;y++)for(int x=0;x<48;x++)if((x+y+z)%2==0)world.Set(x,y,z,MaterialId.Wood);
        physics=new NaturalTreePhysics(world,new Int3(0,0,0),0);renderer=new NaturalMatterRenderer(new GameObject("capacity source").transform,sharedMaterial);renderer.SetPhysics(physics,shared);renderer.SetVisible(true);
        removedIndex=Enumerable.Range(0,physics.Count).First(i=>physics.ReadLocal(i).Cell.Equals(new Int3(24,24,24)));remove.Invoke(physics,new object[]{removedIndex});renderer.Tick(Vector3.zero);
        failed=Get<Task>(renderer,"worker");try{failed.Wait(10000);}catch(AggregateException){}renderer.Tick(Vector3.zero);
        Check(renderer.Error!=null&&renderer.Ready&&!Get<bool>(renderer,"maskEnabled")&&renderer.RenderingCellSize==.1f,"patch face cap retains original visible appearance and reports failure instead of coarsening");renderer.Dispose();
        world=NaturalTreeTemplate.Replay(11,out var redwoodAnchor,out _);physics=new NaturalTreePhysics(world,redwoodAnchor,0){SuctionEnabled=false};
        renderer=new NaturalMatterRenderer(new GameObject("actual mature redwood borrowed source").transform,sharedMaterial);renderer.SetPhysics(physics,shared);
        Check(renderer.Current&&renderer.RenderingCellSize==.1f,"actual million-cell mature redwood is immediately ready on borrowed path without geometry replacement");
        Int3 bark=new Int3(-1,-1,-1);for(int i=0;i<physics.Count;i++){var c=physics.ReadLocal(i);if(c.Matter.Material==MaterialId.Wood&&c.Cell.y==redwoodAnchor.y+10&&c.Cell.x>bark.x)bark=c.Cell;}
        int removedRedwood=physics.CutSphere(bark);while(physics.PendingSupport>0)physics.StepSupport(32768,1.25);Complete(renderer);
        mask=Get<Texture3D>(renderer,"ownerMask");int patchVertices=Get<Mesh[]>(renderer,"published").Where(m=>m!=null).Sum(m=>m.vertexCount);
        Check(renderer.Error==null&&renderer.Current&&mask.Data.Length<=262144&&patchVertices<=262144&&renderer.RenderingCellSize==.1f,"actual mature redwood first sphere fits exact chunk-mask and patch caps without coarsening");
        Check(removedRedwood>0&&physics.IslandCells==0&&physics.Conserved,"actual first bark sphere preserves attached-tree ownership and exact matter counts");
        realRedwood=new{cells=physics.Count,size_x=world.SizeX,size_y=world.SizeY,size_z=world.SizeZ,removed=removedRedwood,mask_bytes=mask.Data.Length,patch_vertices=patchVertices,patch_chunks=renderer.GeometryBins,capture_ms=renderer.LastSnapshotMilliseconds,worker_ms=renderer.LastWorkerMilliseconds,worker_candidates=renderer.LastWorkerCandidates};
        Console.WriteLine("METRIC actual_redwood="+JsonSerializer.Serialize(realRedwood));
#if !BASELINE
        Check(renderer.LastPatchGroupPasses==renderer.GeometryBins&&renderer.LastPatchCellLookups==(long)renderer.GeometryBins*18*18*18,"attached redwood rebuild samples one owner group per patch instead of all nine");
#endif
        var build=typeof(NaturalMatterRenderer).GetMethod("BuildPrototype",BindingFlags.Static|BindingFlags.NonPublic);var measuredSnapshot=physics.CaptureRenderSnapshot();var times=new double[9];string geometryHash=null;
        for(int sample=0;sample<times.Length;sample++)
        {
            object result=build.Invoke(null,new object[]{measuredSnapshot,CancellationToken.None});times[sample]=(double)result.GetType().GetField("Milliseconds").GetValue(result);string hash=GeometryHash(result);
            if(geometryHash==null)geometryHash=hash;Check(geometryHash==hash,"repeated redwood patch geometry and ownership digest is stable "+sample);
        }
        var sorted=(double[])times.Clone();Array.Sort(sorted);var repeatedRedwood=new{samples_ms=times,p50_ms=sorted[sorted.Length/2],minimum_ms=sorted[0],maximum_ms=sorted[sorted.Length-1],geometry_sha256=geometryHash};
        Console.WriteLine("METRIC repeated_redwood="+JsonSerializer.Serialize(repeatedRedwood));renderer.Dispose();
        object pairedRedwood=null;
#if PAIRED
        var baselineBuild=typeof(NaturalMatterRendererPreimage).GetMethod("BuildPrototype",BindingFlags.Static|BindingFlags.NonPublic);
        var beforeTimes=new double[11];var afterTimes=new double[11];
        baselineBuild.Invoke(null,new object[]{measuredSnapshot,CancellationToken.None});build.Invoke(null,new object[]{measuredSnapshot,CancellationToken.None});
        for(int pair=0;pair<beforeTimes.Length;pair++)for(int turn=0;turn<2;turn++)
        {
            bool baselineTurn=(turn+pair)%2==0;var method=baselineTurn?baselineBuild:build;
            object result=method.Invoke(null,new object[]{measuredSnapshot,CancellationToken.None});double ms=(double)result.GetType().GetField("Milliseconds").GetValue(result);
            (baselineTurn?beforeTimes:afterTimes)[pair]=ms;Check(GeometryHash(result)==geometryHash,"paired original/current redwood exact geometry mask digest "+pair+" "+baselineTurn);
        }
        double Median(double[] values){var copy=(double[])values.Clone();Array.Sort(copy);return copy[copy.Length/2];}
        pairedRedwood=new{order="alternating baseline/current per pair; two warmups; same immutable captured snapshot",baseline_samples_ms=beforeTimes,current_samples_ms=afterTimes,baseline_p50_ms=Median(beforeTimes),current_p50_ms=Median(afterTimes),geometry_sha256=geometryHash};
        Console.WriteLine("METRIC paired_redwood="+JsonSerializer.Serialize(pairedRedwood));
#endif
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{status="PASS",checks,real_redwood=realRedwood,repeated_redwood=repeatedRedwood,paired_redwood=pairedRedwood,capture_ms=capture,worker_ms=workerTime,scope=".NET8 numerical Unity substitutes; exposed faces, explicit renderLOD, bounded staging, transforms, instance caps and lifecycle; no native rendering/FPS claim"},new JsonSerializerOptions{WriteIndented=true}));
    }
}
