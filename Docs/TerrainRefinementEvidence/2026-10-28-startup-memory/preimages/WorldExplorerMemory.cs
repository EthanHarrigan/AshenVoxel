using System;
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
