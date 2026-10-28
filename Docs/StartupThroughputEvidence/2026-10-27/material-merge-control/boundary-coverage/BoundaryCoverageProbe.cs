using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using AshenVoxel.WorldScale;

static class BoundaryCoverageProbe
{
    static void Main(string[] args)
    {
        var sampler=LodMeasurements.Load(args[0]);const int level=9,n=8;double size=25.6*(1L<<level);
        long tx=(long)Math.Floor(-24000/size),tz=(long)Math.Floor(-17000/size);double x=tx*size,z=tz*size;
        var tile=new LodTile(new TileKey(tx,tz,level),x,z,size,-12000,16000);
        double floor=((IWorldFieldBounds)sampler.BaseField).QueryBounds(x-size*2,z-size*2,x+size*3,z+size*3).Minimum-1;
        var mesh=VoxelLodSurface.Build(sampler,tile,n,floor,parallelSampling:true);
        // Independent interval oracle reads column heights, including the halo,
        // without consulting mesh faces or merger output.
        long cells=256L<<level,step=cells/n;var top=new long[(n+2)*(n+2)];
        for(int iz=-1;iz<=n;iz++)for(int ix=-1;ix<=n;ix++)
        {
            var p=new WorldCell(tx*cells+ix*step+step/2,0,tz*cells+iz*step+step/2).Center;
            top[ix+1+(iz+1)*(n+2)]=VoxelTerrainPage.TopCell(sampler.EvaluateColumn(p.X,p.Z).SurfaceHeightMeters)+1;
        }
        var faces=mesh.Faces.Select(f=>new{axis=f.Axis,positive=f.Positive,x=(double)f.X,y=(double)f.Y,z=(double)f.Z,u=(double)f.U,v=(double)f.V}).ToArray();
        File.WriteAllText(args[1],JsonSerializer.Serialize(new{label="rear-lod-coverage",key=tile.Key.ToString(),level,n,size,step,mesh.AnchorY,
            floor, floorCell=VoxelTerrainPage.TopCell(floor)-1,haloTopCells=top,faces,source=sampler.SourceIdentity},new JsonSerializerOptions{WriteIndented=true}));
    }
}
