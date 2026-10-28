from pathlib import Path
import datetime,difflib,hashlib,json,shutil
D=Path(__file__).resolve().parent
assert not (D/'flight').exists(),'Preserve earlier candidate'
def change(text,old,new):
    assert text.count(old)==1,old[:100]
    return text.replace(old,new)
motion='''        WorldPoint voxelMotionPosition;
        double voxelMotionTime=-1,voxelMotionX,voxelMotionZ,voxelFastSince=-1,voxelSlowSince=-1,voxelRecoveryUntil;
        bool voxelTravelDeferring;
        public bool VoxelTravelDetailDeferred=>voxelTravelDeferring;
        void SetVoxelTravelDeferral(bool value)
        {
            if(voxelTravelDeferring==value)return;
            voxelTravelDeferring=value;voxelRefinementScanPending=true;
            if(!value){voxelFastSince=-1;ForceReplan();}
        }
        void ResetVoxelTravelMotion()
        {
            voxelMotionTime=voxelFastSince=voxelSlowSince=-1;voxelMotionX=voxelMotionZ=voxelRecoveryUntil=0;
            voxelTravelDeferring=false;
        }
        void UpdateVoxelTravelMotion()
        {
            if(!UseVoxelLodTerrain||!ExternalNavigation){ResetVoxelTravelMotion();return;}
            double now=Time.unscaledTime,dt=now-voxelMotionTime;WorldPoint eye=GlobalCamera;
            if(voxelMotionTime<0||dt>1)
            {SetVoxelTravelDeferral(false);voxelMotionTime=now;voxelMotionPosition=eye;voxelFastSince=-1;return;}
            if(dt<=0)return;
            double vx=(eye.X-voxelMotionPosition.X)/dt,vy=(eye.Y-voxelMotionPosition.Y)/dt,vz=(eye.Z-voxelMotionPosition.Z)/dt;
            double speed2=vx*vx+vz*vz,previous2=voxelMotionX*voxelMotionX+voxelMotionZ*voxelMotionZ;
            bool reverse=speed2>96*96&&previous2>96*96&&vx*voxelMotionX+vz*voxelMotionZ<0;
            voxelMotionTime=now;voxelMotionPosition=eye;voxelMotionX=vx;voxelMotionZ=vz;
            var key=new TileKey((long)Math.Floor(eye.X/25.6),(long)Math.Floor(eye.Z/25.6),0);
            // Unknown/stale bounds and descent preserve arrival work. Global
            // coordinates prevent floating-origin shifts from masquerading as speed.
            bool safe=!coarseStartup&&vy>=-12&&TryContactEnclosure(key,out LodTile bounds)&&eye.Y>bounds.MaximumHeight+32.1;
            if(!safe||reverse||double.IsNaN(speed2)||double.IsInfinity(speed2))
            {
                voxelFastSince=voxelSlowSince=-1;SetVoxelTravelDeferral(false);
                if(reverse)voxelRecoveryUntil=now+.5;
                return;
            }
            if(voxelTravelDeferring)
            {
                if(speed2<96*96)
                {if(voxelSlowSince<0)voxelSlowSince=now;if(now-voxelSlowSince>=.25)SetVoxelTravelDeferral(false);}
                else voxelSlowSince=-1;
            }
            else if(speed2>=160*160&&now>=voxelRecoveryUntil)
            {if(voxelFastSince<0)voxelFastSince=now;if(now-voxelFastSince>=.25)SetVoxelTravelDeferral(true);}
            else voxelFastSince=-1;
        }
        bool HoldVoxelDetailWork(Work work)=>voxelTravelDeferring&&work.Progressive&&work.Quads>32&&
            !externallyDirty.Contains(work.Tile.Key)&&surfaces.TryGetValue(work.Tile.Key,out Surface s)&&work.Quads>s.Quads;
'''
rows=[];diff=[]
for src in sorted((D/'altitude/Assets').rglob('*.cs')):
    rel=src.relative_to(D/'altitude');before=src.read_text();after=before
    if src.name=='WorldExplorer.cs':
        after=change(after,'            RefreshContactPlan();\n            QueueNextVoxelRefinement();','            UpdateVoxelTravelMotion();\n            RefreshContactPlan();\n            QueueNextVoxelRefinement();')
        after=change(after,'        void ClearTerrain()\n        {\n            CancelBuild();','        void ClearTerrain()\n        {\n            ResetVoxelTravelMotion();\n            CancelBuild();')
    if src.name=='WorldExplorerVoxels.cs':
        after=change(after,'        int voxelPlanBoundsRevision=-1;',motion+'        int voxelPlanBoundsRevision=-1;')
        a=after.index('            if(voxelPlanBoundsRevision!=revision||!(BareField(field) is IWorldFieldBounds))return true;')
        b=after.index('        void WakeAdmissibleVoxelRefinements',a)
        after=after[:a]+'''            if(!TryContactEnclosure(tile.Key,out LodTile enclosure))return true;
            double dy=Math.Max(enclosure.MinimumHeight-.1-cameraY,Math.Max(0,cameraY-enclosure.MaximumHeight-.1));
            return distance+dy*dy<=32*32;
        }
        bool TryContactEnclosure(TileKey contactKey,out LodTile enclosure)
        {
            enclosure=default;
            if(voxelPlanBoundsRevision!=revision||!(BareField(field) is IWorldFieldBounds))return false;
            for(int level=0;level<=14;level++)
            {
                long divisor=1L<<level;
                var key=new TileKey((long)Math.Floor((double)contactKey.X/divisor),(long)Math.Floor((double)contactKey.Z/divisor),level);
                if(desiredTiles.TryGetValue(key,out enclosure))return true;
            }
            return false;
        }
'''+after[b:]
        after=change(after,'            if(contactPending.Count>0&&(consecutiveContactBuilds<2||HasUrgentContactWork()))return;',
            '            if(contactPending.Count>0&&(voxelTravelDeferring?HasUrgentContactWork():consecutiveContactBuilds<2||HasUrgentContactWork()))return;')
        after=change(after,'                int target=VoxelQuadsFor(tile);if(s.Quads>=target||voxelDeferredQuads.ContainsKey(tile.Key))continue;',
            '                int target=VoxelQuadsFor(tile);if(voxelTravelDeferring)target=Math.Min(target,32);\n                if(s.Quads>=target||voxelDeferredQuads.ContainsKey(tile.Key))continue;')
        old='            for(int i=0;i<contactPending.Count;i++){double d=DistanceSquared(contactPending[i].Tile);if(d<distance){distance=d;best=i;}}'
        new='            for(int i=0;i<contactPending.Count;i++){if(voxelTravelDeferring&&!ContactWorkIsUrgent(contactPending[i].Tile))continue;double d=DistanceSquared(contactPending[i].Tile);if(d<distance){distance=d;best=i;}}'
        after=change(after,old,new)
    if src.name=='WorldExplorerIntegration.cs':
        a=after.index('        Work NextWork()');b=after.index('        void AdvanceBuilds()',a)
        after=after[:a]+'''        bool TryNextWork(out Work best)
        {
            best=default;double distance=double.MaxValue;bool found=false;
            int count=pending.Count;
            for(int i=0;i<count;i++)
            {
                Work next=pending.Dequeue();
                if(HoldVoxelDetailWork(next)){pending.Enqueue(next);continue;}
                double candidate=WorkPriority(next.Tile);
                if(!found||candidate<distance)
                {if(found)pending.Enqueue(best);best=next;distance=candidate;found=true;}
                else pending.Enqueue(next);
            }
            return found;
        }
'''+after[b:]
        old='''                    bool contactFirst=contactPending.Count>0&&(pending.Count==0||consecutiveContactBuilds<2||HasUrgentContactWork());
                    Work work=contactFirst?NextContactWork():NextWork();'''
        new='''                    bool contactAvailable=contactPending.Count>0&&(!voxelTravelDeferring||HasUrgentContactWork());
                    bool contactFirst=contactAvailable&&(pending.Count==0||consecutiveContactBuilds<2||HasUrgentContactWork());
                    Work work;
                    if(contactFirst)work=NextContactWork();
                    else if(!TryNextWork(out work)){if(contactAvailable)work=NextContactWork();else break;}'''
        after=change(after,old,new)
    dst=D/'flight'/rel;dst.parent.mkdir(parents=True,exist_ok=True)
    if after==before:shutil.copyfile(src,dst)
    else:dst.write_text(after,encoding='utf-8',newline='\n')
    rows.append({'path':rel.as_posix(),'before':hashlib.sha256((D/'baseline'/rel).read_bytes()).hexdigest(),'sha256':hashlib.sha256(dst.read_bytes()).hexdigest()})
    original=(D/'baseline'/rel).read_text()
    diff.extend(difflib.unified_diff(original.splitlines(True),after.splitlines(True),fromfile='a/'+rel.as_posix(),tofile='b/'+rel.as_posix()))
(D/'flight/manifest.json').write_text(json.dumps({'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'status':'CPU-only fast elevated travel experiment; NOT INSTALLED','files':rows},indent=2)+'\n')
(D/'flight/candidate.diff').write_text(''.join(diff))
print(D/'flight')
