using System;
using System.Threading;
using AshenVoxel.Core;
using RootContactManagedComposite;
using ManagedLod=RootContactManagedComposite.LodTemplate;

namespace RootContactTemplateCache
{
    // Once-per-shared-template conversion. It does not create a worker or publish
    // runtime residency. The caller owns both bundle and assembled-template budgets.
    public sealed class ManagedTemplateAssembly
    {
        readonly ManagedLod[] lods;
        public readonly CanonicalSource Canonical;
        public readonly string Identity;
        public readonly long DeclaredRetainedElementBytes,MaximumConversionElementBytes;
        public int Count=>lods.Length;
        public ManagedLod At(int index)=>lods[index];
        ManagedTemplateAssembly(CanonicalSource canonical,ManagedLod[] values,string identity,long temporary)
        {
            Canonical=canonical;lods=values;Identity=identity;MaximumConversionElementBytes=temporary;
            DeclaredRetainedElementBytes=canonical.DeclaredElementBytes+40;
            foreach(var value in values)DeclaredRetainedElementBytes+=value.DeclaredElementBytes;
        }
        static void Current(Func<bool> current,CancellationToken token)
        {token.ThrowIfCancellationRequested();if(!current())throw new InvalidOperationException("StaleSource");token.ThrowIfCancellationRequested();}
        public static ManagedTemplateAssembly Create(TemplateBundle bundle,Limits limits,
            long maximumConversionBytes,Func<bool> isCurrent,CancellationToken token=default)
        {
            if(bundle==null||limits==null||isCurrent==null||maximumConversionBytes<0)throw new ArgumentException("Template conversion input");
            Current(isCurrent,token);int pins=0,maxSamples=0;long samples=0;
            for(int i=0;i<bundle.Context.Count;i++)if(bundle.Context.Cell(i).Position.y==29)pins++;
            for(int k=0;k<bundle.LodCount;k++){samples+=bundle.Lod(k).Count;maxSamples=Math.Max(maxSamples,bundle.Lod(k).Count);}
            // Temporary input arrays coexist with private constructor copies. The original
            // decoded bundle and Core/CLR object overhead are additional caller residency.
            long temporary=12L*bundle.OrderCount+16L*pins+32L*maxSamples+40;
            string identity=TemplateBundle.PolicyIdentity+"/"+bundle.FileIdentity;
            long retained=16L*(bundle.OrderCount+pins)+32L*samples+40+2L*(6*identity.Length+17);
            if(temporary>maximumConversionBytes||retained>limits.MaxTemplateBytes)throw new ArgumentException("Template conversion reservation");
            var order=new Int3[bundle.OrderCount];var pinned=new RootCell[pins];
            for(int i=0;i<order.Length;i++)order[i]=bundle.OrderedChunk(i);
            int next=0;for(int i=0;i<bundle.Context.Count;i++){var c=bundle.Context.Cell(i);if(c.Position.y==29)pinned[next++]=new RootCell(c.Position,c.Color);}
            var transform=new GridTransform(new Int3(235,4,235),1024,470,470,bundle.Seed);
            var canonical=new CanonicalSource(order,pinned,transform,identity,limits,isCurrent,token);
            var lods=new ManagedLod[5];
            for(int k=0;k<lods.Length;k++)
            {
                Current(isCurrent,token);var lod=bundle.Lod(k);var values=new FixedSample[lod.Count];
                for(int i=0;i<values.Length;i++){var s=lod.At(i);values[i]=new FixedSample(s.Cell,s.Material,s.Color,s.Rank,s.Index);}
                lods[k]=new ManagedLod(canonical,lod.Factor,lod.Cut,values,identity+"/F"+lod.Factor,limits,isCurrent,token);
            }
            Current(isCurrent,token);var result=new ManagedTemplateAssembly(canonical,lods,identity,temporary);
            if(result.DeclaredRetainedElementBytes>limits.MaxTemplateBytes)throw new ArgumentException("Final template reservation");
            return result;
        }
        // The array itself is a fresh caller-owned view of immutable template references.
        public ManagedLod[] CopyTemplateReferences()=>(ManagedLod[])lods.Clone();
    }
}
