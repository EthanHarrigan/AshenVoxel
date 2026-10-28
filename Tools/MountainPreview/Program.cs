using System;using System.IO;using System.Threading.Tasks;using AshenVoxel.Core;using AshenVoxel.WorldScale;
/// <summary>Engine-free oblique preview of real terrain materials: top surfaces and cliff walls
/// are coloured from the same SurfaceMaterialSampler matter used by the game (offline, not Unity).</summary>
static class MountainPreview
{
    static void Main(string[] a)
    {
        string root=a[0],output=a[1];double cx=double.Parse(a[2]),cz=double.Parse(a[3]),size=double.Parse(a[4]),grid=double.Parse(a[5]);
        int width=a.Length>6?int.Parse(a[6]):1600,height=a.Length>7?int.Parse(a[7]):1000;double az=(a.Length>8?double.Parse(a[8]):45)*Math.PI/180,el=(a.Length>9?double.Parse(a[9]):32)*Math.PI/180;
        var sampler=Load(Path.Combine(root,"Assets/AshenVoxel/Exploration/Samples"));
        int n=(int)(size/grid);var col=new SurfaceColumn[n*n];
        Parallel.For(0,n,z=>{for(int x=0;x<n;x++)col[x+z*n]=sampler.EvaluateColumn(cx-size/2+(x+.5)*grid,cz-size/2+(z+.5)*grid);});
        double hmin=double.MaxValue,hmax=double.MinValue;foreach(var c in col){hmin=Math.Min(hmin,c.SurfaceHeightMeters);hmax=Math.Max(hmax,c.SurfaceHeightMeters);}
        double ca=Math.Cos(az),sa=Math.Sin(az),ce=Math.Cos(el),se=Math.Sin(el);
        double span=size*1.42;double k=Math.Min(width/span,height/(span*se+(hmax-hmin)*ce))*.97;
        double mid=(hmax+hmin)*.5;
        var img=new byte[width*height*3];var depth=new double[width*height];for(int i=0;i<depth.Length;i++)depth[i]=double.MinValue;
        for(int i=0;i<img.Length;i+=3){img[i]=148;img[i+1]=166;img[i+2]=184;}
        // Light from the sun azimuth used by the snow model, 40 degrees high.
        double lx=Math.Cos(287.35*Math.PI/180)*.77,lz=Math.Sin(287.35*Math.PI/180)*.77,ly=.64;
        var order=new int[n*n];var keys=new double[n*n];
        for(int z=0;z<n;z++)for(int x=0;x<n;x++){double px=(x+.5)*grid-size/2,pz=(z+.5)*grid-size/2;keys[x+z*n]=px*sa+pz*ca;order[x+z*n]=x+z*n;}
        Array.Sort(keys,order);
        double half=grid*k*.8+.6;
        foreach(int idx in order)
        {
            int x=idx%n,z=idx/n;var c=col[idx];double px=(x+.5)*grid-size/2,pz=(z+.5)*grid-size/2;
            double rx=px*ca-pz*sa,rz=px*sa+pz*ca;double h=c.SurfaceHeightMeters;
            double u=width/2+rx*k,vt=height/2-(h-mid)*k*ce+rz*k*se;
            double topThick=Math.Max(1,grid*k*se);
            // Slope normal from neighbours.
            double hx=col[Math.Min(n-1,x+1)+z*n].SurfaceHeightMeters-col[Math.Max(0,x-1)+z*n].SurfaceHeightMeters,hz=col[x+Math.Min(n-1,z+1)*n].SurfaceHeightMeters-col[x+Math.Max(0,z-1)*n].SurfaceHeightMeters;
            double nx=-hx/(2*grid),nz=-hz/(2*grid),inv=1/Math.Sqrt(nx*nx+nz*nz+1);
            double lambert=Math.Max(0,(nx*lx+ly+nz*lz)*inv);double topShade=.38+.75*lambert;
            // Find how far down this column is exposed: lowest front neighbour.
            double front=Math.Min(col[x+Math.Min(n-1,z+1)*n].SurfaceHeightMeters,Math.Min(col[Math.Min(n-1,x+1)+z*n].SurfaceHeightMeters,col[Math.Max(0,x-1)+z*n].SurfaceHeightMeters));
            double bottom=Math.Min(h-grid,front)-grid*2;
            int u0=(int)Math.Floor(u-half),u1=(int)Math.Ceiling(u+half);
            int v0=(int)Math.Floor(vt-topThick*.5),vTopEnd=(int)Math.Ceiling(vt+topThick*.5),v1=(int)Math.Ceiling(vt+(h-bottom)*k*ce);
            uint top=c.SurfaceTint;
            for(int v=Math.Max(0,v0);v<Math.Min(height,v1);v++)
            {
                uint tint;double shade;
                if(v<vTopEnd){tint=top;shade=topShade;}
                else
                {
                    double y=h-(v-vt)/(k*ce);if(y<bottom)break;
                    var m=sampler.SampleMatter(new WorldPoint(cx+px,y,cz+pz),c);if(m.Material==MaterialId.Air)continue;tint=m.Tint;
                    // Side faces: lit by horizontal sun component of the viewer-facing direction.
                    shade=.52+.25*Math.Max(0,sa*lx+ca*lz);
                }
                for(int uu=Math.Max(0,u0);uu<Math.Min(width,u1);uu++)
                {
                    int p=uu+v*width;if(depth[p]>rz+1e-9&&v>=vTopEnd)continue;depth[p]=rz;
                    img[p*3]=(byte)Math.Min(255,((tint>>16)&255)*shade);img[p*3+1]=(byte)Math.Min(255,((tint>>8)&255)*shade);img[p*3+2]=(byte)Math.Min(255,(tint&255)*shade);
                }
            }
        }
        using(var f=File.Create(output)){var head=System.Text.Encoding.ASCII.GetBytes("P6\n"+width+" "+height+"\n255\n");f.Write(head,0,head.Length);f.Write(img,0,img.Length);}
        Console.WriteLine("rendered "+n+"x"+n+" columns, heights "+hmin.ToString("F0")+".."+hmax.ToString("F0"));
    }
    static SurfaceMaterialSampler Load(string p)
    {
        HeightAtlas macro,fine;ClimateAtlas mc,fc;ExplorationBiomeAtlas biomes;
        using(var f=File.OpenRead(Path.Combine(p,"ExplorationMacro.bytes")))macro=AtlasIO.Read(f);using(var f=File.OpenRead(Path.Combine(p,"ExplorationFine.bytes")))fine=AtlasIO.Read(f);
        using(var f=File.OpenRead(Path.Combine(p,"ExplorationMacro.climate.bytes")))mc=ClimateArchiveIO.Read(f);using(var f=File.OpenRead(Path.Combine(p,"ExplorationFine.climate.bytes")))fc=ClimateArchiveIO.Read(f);
        using(var f=File.OpenRead(Path.Combine(p,"ExplorationBiomes.bytes")))biomes=ExplorationBiomeAtlasIO.Read(f);
        var field=new ExplorationAtlasField(macro,mc,fine,fc,512,macro.ExtentX,macro.ExtentZ,1,biomes,macro.ExtentX,macro.ExtentZ);
        return new SurfaceMaterialSampler(new TerrainDetailField(field,714103),new SurfaceProfile(seed:714103,maxSnowDepthMeters:2.4f,winterFraction:.2f));
    }
}
