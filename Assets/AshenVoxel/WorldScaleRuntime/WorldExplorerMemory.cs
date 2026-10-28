using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System.Runtime.InteropServices;
#endif

namespace AshenVoxel.WorldScaleRuntime
{
    public sealed partial class WorldExplorer
    {
        // Mesh payload quotas do not include Unity/driver copies or other apps.
        // Pause this existing worker/upload lane rather than overcommitting them.
        public const long VoxelCommitReserveBytes=2L*1024*1024*1024;
        const long CommitResumeMarginBytes=256L*1024*1024;
        const long UploadFixedMarginBytes=64L*1024*1024;
        long availableCommitBytes=-1;
        bool voxelMemoryPressureBlocked;
        double nextCommitProbe;
        public long VoxelAvailableCommitBytes=>availableCommitBytes;
        public bool VoxelMemoryPressureBlocked=>voxelMemoryPressureBlocked;
        public long VoxelRequiredCommitBytes=>VoxelCommitReserveBytes+UploadFixedMarginBytes+(voxelMemoryPressureBlocked?CommitResumeMarginBytes:0);
        public long VoxelPressureScratchReleasedBytes {get;private set;}
        public int VoxelCommitDeferredTiles=>voxelCommitDeferred.Count;
        readonly struct CommitDeferred
        {
            public readonly int Quads,Revision;public readonly long Bytes;
            public CommitDeferred(int quads,long bytes,int revision){Quads=quads;Bytes=bytes;Revision=revision;}
        }
        readonly Dictionary<(WorldScale.TileKey key,bool contact),CommitDeferred> voxelCommitDeferred=new Dictionary<(WorldScale.TileKey,bool),CommitDeferred>();

        bool VoxelCommitBlocked(WorldScale.TileKey key,bool contact,int quads)
            =>voxelCommitDeferred.TryGetValue((key,contact),out CommitDeferred request)&&quads>=request.Quads;
        void DeferVoxelCommit(WorldScale.LodTile tile,int quads,bool contact,long bytes)
        {
            voxelCommitDeferred[(tile.Key,contact)]=new CommitDeferred(quads,bytes,revision);
            RequeuePressureWork(new Work(tile,revision,true,contact,quads:quads,progressive:!contact));
        }
        void RequeuePressureWork(Work work)
        {
            if(work.Revision!=revision)return;
            if(work.Contact)
            {if(contactDesired.Contains(work.Tile.Key)&&!contactPending.Exists(w=>w.Tile.Key.Equals(work.Tile.Key)))contactPending.Add(work);}
            else if(TerrainWorkWanted(work)&&!System.Linq.Enumerable.Any(pending,w=>w.Tile.Key.Equals(work.Tile.Key)&&w.Quads==work.Quads))pending.Enqueue(work);
        }
        void PollCommitDeferred()
        {
            if(voxelCommitDeferred.Count==0)return;
            // Requests themselves stay queued, bounded by the two mesh plans and
            // fixed contact mask. Retired demand never retains pressure metadata.
            var done=new List<(WorldScale.TileKey key,bool contact)>();
            foreach(var entry in voxelCommitDeferred)
            {
                bool wanted=entry.Key.contact?contactDesired.Contains(entry.Key.key):desired.Contains(entry.Key.key)||active.Contains(entry.Key.key);
                if(!wanted||entry.Value.Revision!=revision||availableCommitBytes<0||availableCommitBytes>=VoxelCommitReserveBytes+UploadFixedMarginBytes+CommitResumeMarginBytes+entry.Value.Bytes*5)done.Add(entry.Key);
            }
            foreach(var key in done)voxelCommitDeferred.Remove(key);
            if(done.Count>0)voxelRefinementScanPending=true;
        }
        void ReclaimPausedVoxelWork()
        {
            // Cancel/requeue exact demand; never orphan the single worker. Drain
            // its completed result even though no new allocation/upload is safe.
            if(currentBuild!=null)
            {
                Work work=currentBuild.Work;
                if(voxelBuildJob!=null&&voxelBuildJob.IsCompleted&&!voxelBuildJob.IsFaulted&&!voxelBuildJob.IsCanceled)
                {
                    TerrainBuildResult data=voxelBuildJob.Result;
                    long bytes=data.Proxy!=null?data.Proxy.MeshPayloadBytes:(long)data.Voxels.Faces.Count*120;
                    int quads=work.Contact?256:work.Quads>0?work.Quads:VoxelQuadsFor(work.Tile);
                    DeferVoxelCommit(work.Tile,quads,work.Contact,bytes);
                }
                voxelBuildCancellation?.Cancel();CancelBuild();RequeuePressureWork(work);
            }
            if(voxelBuildJob!=null&&voxelBuildJob.IsCompleted)
            {if(voxelBuildJob.IsFaulted){var observed=voxelBuildJob.Exception;}voxelBuildJob=null;}
            long scratch=VoxelScratchBytes;voxelVertices=null;voxelIndices=null;VoxelPressureScratchReleasedBytes+=scratch;
            // Outer contact caches may retire only with complete current fallback;
            // retain the player's urgent contact and every unfinished request.
            remove.Clear();foreach(var entry in contactSurfaces)
            {
                if(ContactWorkIsUrgent(entry.Value.Tile)||!HasContactFallback(entry.Key))continue;
                remove.Add(entry.Key);if(remove.Count==4)break;
            }
            foreach(var key in remove)
            {var tile=contactSurfaces[key].Tile;ReleaseContact(key);RequeuePressureWork(new Work(tile,revision,true,true));}
            if(remove.Count>0)UpdateContactMask();
        }

        bool HasVoxelUploadHeadroom(long payloadBytes)
        {
            if(Time.unscaledTimeAsDouble>=nextCommitProbe)
            {
                nextCommitProbe=Time.unscaledTimeAsDouble+.5;
                availableCommitBytes=ReadAvailableCommitBytes();
            }
            // Unsupported/unavailable platforms retain the existing mesh quotas.
            if(availableCommitBytes<0){voxelMemoryPressureBlocked=false;return true;}
            // Expanded scratch can approach twice the exact payload; allow for
            // replacement overlap and native/driver upload copies as well.
            long required=VoxelCommitReserveBytes+UploadFixedMarginBytes+Math.Max(0,payloadBytes)*5;
            if(voxelMemoryPressureBlocked)required+=CommitResumeMarginBytes;
            voxelMemoryPressureBlocked=availableCommitBytes<required;
            return !voxelMemoryPressureBlocked;
        }

        static long ReadAvailableCommitBytes()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            var status=new MemoryStatus{Length=64};
            try
            {
                if(GlobalMemoryStatusEx(ref status))return status.AvailablePageFile>(ulong)long.MaxValue?long.MaxValue:(long)status.AvailablePageFile;
            }
            catch(DllNotFoundException){}
            catch(EntryPointNotFoundException){}
#endif
            return -1;
        }
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        // ullAvailPageFile is current-process commit headroom (not free RAM/VRAM).
        [StructLayout(LayoutKind.Sequential)]
        struct MemoryStatus
        {
            public uint Length,Load;
            public ulong TotalPhysical,AvailablePhysical,TotalPageFile,AvailablePageFile,TotalVirtual,AvailableVirtual,AvailableExtendedVirtual;
        }
        [DllImport("kernel32.dll")]
        [return:MarshalAs(UnmanagedType.Bool)]
        static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
#endif
    }
}
