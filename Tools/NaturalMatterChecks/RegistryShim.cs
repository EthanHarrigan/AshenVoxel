using System;
using System.Collections.Generic;
using AshenVoxel.WorldScale;
namespace AshenVoxel.WorldScale
{
 public readonly struct WorldPoint{public readonly double X,Y,Z;public WorldPoint(double x,double y,double z){X=x;Y=y;Z=z;}}
 public readonly struct PopulationKey{public readonly long X,Z;public double SizeMeters=>64;public PopulationKey(long x,long z){X=x;Z=z;}}
 public struct PopulationInstance{public int PrototypeId;public float Scale,YawDegrees;public ulong StableKey;public WorldPoint Position;}
 public sealed class PopulationCell{public PopulationKey Key;public PopulationInstance[] Instances=Array.Empty<PopulationInstance>();}
}
namespace AshenVoxel.Exploration
{
 public interface IShrubMatterOwner{}
 public sealed partial class VegetationStreaming
 {
  sealed class CachedCell{public PopulationCell Data=new PopulationCell();public bool Published=true;public long Epoch=1;}
  sealed class Model{public ExplorationPrototype Data=null;}
  sealed class SourceField{public string SourceIdentity="fixture";}
  public sealed class ShrubLease{public PopulationKey Cell;public PopulationInstance Instance;public long Epoch,Token;public object OwnerCell;internal object NaturalRoot;public IShrubMatterOwner Owner;public string Source;}
  bool Ready=true;bool HasReservedShrubMatter=>shrubLease!=null;SourceField rootField=new SourceField();long shrubToken;ShrubLease shrubLease;float maximumRadius=24;
  readonly Dictionary<int,CachedCell> cells=new Dictionary<int,CachedCell>();readonly Dictionary<int,Model> models=new Dictionary<int,Model>();
  sealed class RootRequest{}
  sealed class RootCpu{public RootContactBuildPipeline.CompleteArtifact Artifact=null;public FakeRootCollision Collision=null;public RootRequest Request=new RootRequest();}
  sealed class RootBinding{public RootCpu Cpu=new RootCpu();public bool Retiring=false;}
  sealed class FakeRootCollision
  {
   public ExplorationPrototype Prototype;public double MinY=>-1;
   public bool BlocksLocal(double a,double b,double c,double d,double e,double f)=>Prototype.BlocksLocal(a,b,c,d,e,f);
   public double ResistanceLocal(double a,double b,double c,double d,double e,double f)=>Prototype.ResistanceLocal(a,b,c,d,e,f);
  }
  RootBinding fixtureRoot=null;public bool FixtureRootCurrent=true;
  RootBinding SelectedRoot(PopulationCell c,PopulationInstance i,bool next)=>fixtureRoot;
  bool RootSourceCurrent(RootRequest request)=>FixtureRootCurrent;
  bool ShrubCurrent(ShrubLease lease)=>lease!=null&&object.ReferenceEquals(lease,shrubLease)&&rootField.SourceIdentity==lease.Source&&NaturalLeaseRootCurrent(lease);
  public void FixtureAdaptedRoot(RootContactBuildPipeline.CompleteArtifact artifact,ExplorationPrototype collision){fixtureRoot=new RootBinding{Cpu=new RootCpu{Artifact=artifact,Collision=new FakeRootCollision{Prototype=collision}}};}
  public static VegetationStreaming Fixture(ExplorationPrototype prototype,params PopulationInstance[] instances)
  {
   var registry=new VegetationStreaming();registry.models.Add(prototype.Id,new Model{Data=prototype});
   registry.cells.Add(0,new CachedCell{Data=new PopulationCell{Instances=instances}});return registry;
  }
 }
}


namespace RootContactBuildPipeline
{
 public readonly struct CellData{public readonly AshenVoxel.Core.Int3 Position;public readonly uint Color;public AshenVoxel.Core.MaterialId Material=>AshenVoxel.Core.MaterialId.Wood;public CellData(AshenVoxel.Core.Int3 p,uint c){Position=p;Color=c;}}
 public sealed class FakeIdentity{public string Hash="root-fixture";}
 public sealed class CompleteArtifact
 {
  readonly CellData[] cells;public readonly int RenderCellCount;public int CellCount=>cells.Length;public FakeIdentity Identity=new FakeIdentity();
  public CompleteArtifact(CellData[] source,int owned){cells=source;RenderCellCount=owned;}
  public CellData CellAt(int i)=>cells[i];
 }
}
