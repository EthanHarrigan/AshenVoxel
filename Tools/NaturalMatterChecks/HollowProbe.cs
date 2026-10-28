using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AshenVoxel.Core;
using AshenVoxel.Core.Experimental;
using AshenVoxel.Exploration.Experimental;
static class HollowProbe
{
 static readonly Int3[] Neighbours={new Int3(1,0,0),new Int3(-1,0,0),new Int3(0,1,0),new Int3(0,-1,0),new Int3(0,0,1),new Int3(0,0,-1)};
 sealed class Counts
 {
  public long total,exposed_air,exposed_air_or_material_interface,interface_nonair,buried_no_air,buried_same_material,material_y_runs,material_tint_y_runs,optimal_quarter_representatives;
 }
 static void Main(string[] args)
 {
  var records=new List<object>();int cubeBytes=Marshal.SizeOf(typeof(NaturalTreePhysics.Cube));int directArrayBytesPerCell=cubeBytes+26;
  foreach(int id in new[]{0,10,11,209})
  {
   var prepared=NaturalTreeTemplate.Prepare(id);var world=prepared.CreateWorld(out var anchor);var total=new Counts();var byMaterial=new Dictionary<MaterialId,Counts>();var keys=new Dictionary<CargoKey,long>();
   foreach(var pair in world.Chunks)for(int i=0;i<Chunk.Volume;i++)
   {
    var m=pair.Value.Cells[i];if(m==MaterialId.Air)continue;var local=Chunk.Decode(i);var p=new Int3(pair.Key.x*16+local.x,pair.Key.y*16+local.y,pair.Key.z*16+local.z);uint tint=world.GetTint(p);var key=new CargoKey(m,tint);keys.TryGetValue(key,out long count);keys[key]=count+1;
    if(!byMaterial.TryGetValue(m,out var sub)){sub=new Counts();byMaterial.Add(m,sub);}bool air=false,other=false,nonAirOther=false;
    foreach(var d in Neighbours){var n=world.Get(p+d);air|=n==MaterialId.Air;other|=n!=m;nonAirOther|=n!=m&&n!=MaterialId.Air;}
    var below=new Int3(p.x,p.y-1,p.z);bool materialRun=world.Get(below)!=m,tintRun=materialRun||world.GetTint(below)!=tint;
    foreach(var c in new[]{total,sub}){c.total++;if(air)c.exposed_air++;else c.buried_no_air++;if(other)c.exposed_air_or_material_interface++;else c.buried_same_material++;if(nonAirOther)c.interface_nonair++;if(materialRun)c.material_y_runs++;if(tintRun)c.material_tint_y_runs++;}
   }
   foreach(var key in keys){long reps=(key.Value+3)/4;total.optimal_quarter_representatives+=reps;byMaterial[key.Key.Material].optimal_quarter_representatives+=reps;}
   records.Add(new{id,total,by_material=byMaterial.ToDictionary(p=>p.Key.ToString(),p=>p.Value),distinct_material_tint_keys=keys.Count,cube_bytes=cubeBytes,direct_arrays_bytes_per_cell=directArrayBytesPerCell,air_buried_direct_array_savings_bytes=total.buried_no_air*directArrayBytesPerCell,same_material_buried_direct_array_savings_bytes=total.buried_same_material*directArrayBytesPerCell,material_tint_y_run_payload_bytes=total.material_tint_y_runs*13,material_only_y_run_payload_bytes=total.material_y_runs*9,uncompressed_cell_coordinate_material_tint_payload_bytes=total.total*17,matter_sha256=prepared.MatterIdentity});
   Console.WriteLine(id+" total="+total.total+" airSurface="+total.exposed_air+" buried="+total.buried_no_air+" runs="+total.material_tint_y_runs);
  }
  File.WriteAllText(args[0],JsonSerializer.Serialize(new{status="PASS",scope="Read-only actual complete10cm generator replay; not native Unity or performance measurements",definitions=new{exposed_air="at least one six-neighbor is Air",exposed_air_or_material_interface="at least one six-neighbor has different material, including Air",interface_nonair="at least one six-neighbor has a different non-Air material",buried_no_air="all six-neighbors occupied, irrespective of material",buried_same_material="all six-neighbors have this material",y_runs="run starts where previous Y cell differs in material or material+tint; raw packed run13B=Int16 X/Z/start/end(8)+material(1)+raw tint(4); material-only9B; no actualcompressedformatimplemented",quarter="global optimalceil(count per material+raw-tint key/4), exact finalweights; actualrelease/frame/spatialgrouping may need more",direct_arrays="Marshal.SizeOf(Cube)+bool seen+bool sleeping+double updated+four int arrays(queue/group/parcelNext/parcelWeight), excludes all CLR/dictionary/list/island/mesh overhead; prospective savings only"},records},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
 }
}
