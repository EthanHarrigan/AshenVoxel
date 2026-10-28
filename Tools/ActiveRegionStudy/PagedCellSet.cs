// Research candidate only. Not referenced by an Assets assembly.
using System;
using System.Collections.Generic;
namespace AshenVoxel.Core
{
    // FIFO scheduling remains in Simulation. Only membership storage changes.
    // Pages are deliberately retained until Release; this is NOT a streaming policy.
    public sealed class PagedCellSet
    {
        sealed class Page { public readonly ulong[] Bits=new ulong[64]; public long Epoch; }
        readonly Dictionary<Int3,Page> pages=new Dictionary<Int3,Page>();
        Int3 cachedKey; Page cached; long epoch=1;
        public int PageCount=>pages.Count;
        public int Count {get;private set;}
        Page Find(Int3 p,bool create)
        {
            var key=new Int3(p.x>>4,p.y>>4,p.z>>4);
            if(cached!=null&&cachedKey==key)return cached;
            if(!pages.TryGetValue(key,out var page))
            { if(!create)return null; page=new Page();pages.Add(key,page); }
            cachedKey=key;cached=page;return page;
        }
        static int Index(Int3 p)=>(p.x&15)+16*((p.y&15)+16*(p.z&15));
        public bool Add(Int3 p)
        {
            var page=Find(p,true);
            if(page.Epoch!=epoch){Array.Clear(page.Bits,0,64);page.Epoch=epoch;}
            int i=Index(p);ulong bit=1UL<<(i&63);
            if((page.Bits[i>>6]&bit)!=0)return false;
            page.Bits[i>>6]|=bit;Count++;return true;
        }
        public bool Contains(Int3 p)
        {
            var page=Find(p,false);if(page==null||page.Epoch!=epoch)return false;
            int i=Index(p);return (page.Bits[i>>6]&(1UL<<(i&63)))!=0;
        }
        public bool Remove(Int3 p)
        {
            var page=Find(p,false);if(page==null||page.Epoch!=epoch)return false;
            int i=Index(p);ulong bit=1UL<<(i&63);
            if((page.Bits[i>>6]&bit)==0)return false;
            page.Bits[i>>6]&=~bit;Count--;return true;
        }
        public void Clear(){if(epoch==long.MaxValue){Release();return;}epoch++;Count=0;}
        public void Release(){pages.Clear();cached=null;epoch=1;Count=0;}
    }
}
