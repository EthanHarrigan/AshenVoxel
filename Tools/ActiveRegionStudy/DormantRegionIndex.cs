// Research spatial query only: no physics, scheduling or terrain ownership changes.
using System;
using System.Collections.Generic;
using System.Numerics;
using AshenVoxel.Core;
sealed class DormantRegionIndex
{
    readonly struct Entry { public readonly int Id;public readonly Vector3 Position;public Entry(int id,Vector3 p){Id=id;Position=p;} }
    readonly struct Location { public readonly Int3 Key;public readonly int Index;public Location(Int3 key,int index){Key=key;Index=index;} }
    readonly Dictionary<Int3,List<Entry>> buckets=new Dictionary<Int3,List<Entry>>();
    readonly Dictionary<int,Location> locations=new Dictionary<int,Location>();
    readonly float side;
    public int LastBuckets {get;private set;}
    public int LastCandidates {get;private set;}
    public int BucketCount=>buckets.Count;
    public DormantRegionIndex(float side){if(side<=0)throw new ArgumentOutOfRangeException(nameof(side));this.side=side;}
    Int3 Key(Vector3 p)=>new Int3((int)Math.Floor(p.X/side),(int)Math.Floor(p.Y/side),(int)Math.Floor(p.Z/side));
    public void Set(int id,Vector3 p)
    {
        var key=Key(p);
        if(locations.TryGetValue(id,out var old))
        {
            if(old.Key==key){buckets[key][old.Index]=new Entry(id,p);return;}
            Remove(id);
        }
        if(!buckets.TryGetValue(key,out var bucket)){bucket=new List<Entry>();buckets.Add(key,bucket);}
        locations[id]=new Location(key,bucket.Count);bucket.Add(new Entry(id,p));
    }
    public bool Remove(int id)
    {
        if(!locations.TryGetValue(id,out var location))return false;
        var bucket=buckets[location.Key];int last=bucket.Count-1;
        if(location.Index!=last){var moved=bucket[last];bucket[location.Index]=moved;locations[moved.Id]=new Location(location.Key,location.Index);}
        bucket.RemoveAt(last);if(bucket.Count==0)buckets.Remove(location.Key);return locations.Remove(id);
    }
    public void Query(Vector3 center,float radius,List<int> result)
    {
        result.Clear();LastBuckets=LastCandidates=0;
        var minimum=Key(center-new Vector3(radius));var maximum=Key(center+new Vector3(radius));
        for(int z=minimum.z;z<=maximum.z;z++)for(int y=minimum.y;y<=maximum.y;y++)for(int x=minimum.x;x<=maximum.x;x++)
        {
            LastBuckets++;if(!buckets.TryGetValue(new Int3(x,y,z),out var bucket))continue;
            foreach(var entry in bucket){LastCandidates++;if(Vector3.DistanceSquared(center,entry.Position)<radius*radius)result.Add(entry.Id);}
        }
    }
}
