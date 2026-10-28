using System;
using AshenVoxel.Core;
using RootContactPrototype;
using RootContactInteraction;
using RootBuilder=RootContactPrototype.Builder;
using InteractionBuilder=RootContactInteraction.Builder;

namespace RootContactCache
{
    // Reservation is a declared managed working-set allowance, not a CLR heap sandbox.
    // Underlying frozen builders independently cap geometry, chunks, core sets and traversal queues.
    public sealed class FrozenJob:IJob
    {
        public const long WorkingReservation=24L*1024*1024;
        RootBuilder rootBuilder;InteractionBuilder interactionBuilder;
        RootContactPrototype.Result root;RootContactInteraction.Result interactions;
        UpperContext context;ICurrentIdentities current;
        Int3[] positions;uint[] colors;byte[] roles;int copied,render,guards;
        public Identity Identity{get;}public long ReservedBytes=>WorkingReservation;
        public JobState State{get;private set;}=JobState.Running;
        public string Phase=>State!=JobState.Running?State.ToString():rootBuilder!=null?"Root:"+rootBuilder.State:interactionBuilder!=null?"Interaction:"+interactionBuilder.State:"PackingCellsAndRoles";
        public string Failure{get;private set;}="";public OwnedArtifact Output{get;private set;}
        public int CopiedCells=>copied;
        public FrozenJob(Template template,ITerrain terrain,UpperContext upper,ICurrentIdentities identities,Pose pose)
        {Identity=Identity.For(template,terrain,upper,pose);context=upper;current=identities;rootBuilder=new RootBuilder(template,terrain,pose);}
        static long Count(RootBuilder b)=>b.QueryCandidates+b.CellCandidates+b.ConnectivityCandidates+b.FaceCandidates;
        public int Step(int budget)
        {
            if(budget<0)throw new ArgumentOutOfRangeException(nameof(budget));if(State!=JobState.Running)return 0;int used=0;
            while(used<budget&&State==JobState.Running)
            {
                if(rootBuilder!=null)
                {
                    if(rootBuilder.Terminal)
                    {
                        used++;
                        if(rootBuilder.State!=RootContactPrototype.Phase.Complete){Fail("Root:"+rootBuilder.Failure);break;}
                        root=rootBuilder.Published;rootBuilder=null;interactionBuilder=new InteractionBuilder(root,context,current);continue;
                    }
                    int left=budget-used;long before=Count(rootBuilder);
                    switch(rootBuilder.State)
                    {
                        case RootContactPrototype.Phase.Queries:rootBuilder.Step(new Budget(left,0,0));break;
                        case RootContactPrototype.Phase.Cells:rootBuilder.Step(new Budget(0,left,0));break;
                        default:rootBuilder.Step(new Budget(0,0,left));break;
                    }
                    int consumed=checked((int)(Count(rootBuilder)-before));used+=consumed;
                    if(consumed==0&&!rootBuilder.Terminal)break;continue;
                }
                if(interactionBuilder!=null)
                {
                    if(interactionBuilder.Terminal)
                    {
                        used++;
                        if(interactionBuilder.State!=RootContactInteraction.Phase.Complete){Fail("Interaction:"+interactionBuilder.Failure);break;}
                        interactions=interactionBuilder.Published;interactionBuilder=null;context=null;current=null;
                        positions=new Int3[root.OccupiedCount];colors=new uint[positions.Length];roles=new byte[positions.Length];continue;
                    }
                    long before=interactionBuilder.Candidates;interactionBuilder.Step(budget-used);
                    int consumed=checked((int)(interactionBuilder.Candidates-before));used+=consumed;
                    if(consumed==0&&!interactionBuilder.Terminal)break;continue;
                }
                used++;Int3 p=root.Cell(copied);Channel role=interactions.At(copied);
                if(root.NeighborMaterial(p)!=MaterialId.Wood||p.y>=30&&role!=Channel.None||p.y<30&&role!=Channel.Hard&&role!=Channel.Stick){Fail("InvalidCellRolePartition");break;}
                positions[copied]=p;colors[copied]=root.Color(p);roles[copied]=(byte)role;
                if(p.y<30)render++;else guards++;
                if(++copied==positions.Length)
                {
                    Output=new OwnedArtifact(Identity,positions,colors,roles,render,guards);positions=null;colors=null;roles=null;root=null;interactions=null;State=JobState.Complete;
                }
            }
            return used;
        }
        void Clear()
        {rootBuilder?.Cancel();interactionBuilder?.Cancel();rootBuilder=null;interactionBuilder=null;root=null;interactions=null;context=null;current=null;positions=null;colors=null;roles=null;}
        void Fail(string reason){Failure=reason;State=JobState.Failed;Clear();Output=null;}
        public void Cancel(){if(State!=JobState.Cancelled){Clear();Output=null;State=JobState.Cancelled;Failure="Cancelled";}}
        public void Dispose(){Clear();Output=null;}
    }
}
