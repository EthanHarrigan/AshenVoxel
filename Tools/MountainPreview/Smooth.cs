using System;using System.IO;using System.Threading.Tasks;using AshenVoxel.Core;using AshenVoxel.WorldScale;
/// <summary>Engine-free perspective preview of the smooth terrain surface: per-vertex tint from
/// SurfaceMaterialSampler (footprint-filtered like distant game tiles), sun + sky lighting, fog.</summary>
static class SmoothPreview
{
    static void Main(string[] a)
    {
        string root=a[0],output=a[1];double cx=double.Parse(a[2]),cz=double.Parse(a[3]),size=double.Parse(a[4]),grid=double.Parse(a[5]);
        int W=a.Length>6?int.Parse(a[6]):1200,H=a.Length>7?int.Parse(a[7]):750;double az=(a.Length>8?double.Parse(a[8]):45)*Math.PI/180,el=(a.Length>9?double.Parse(a[9]):28)*Math.PI/180;
        double sunAz=(a.Length>10?double.Parse(a[10]):287.35)*Math.PI/180,sunEl=40*Math.PI/180;
        var sampler=Load(Path.Combine(root,"Assets/AshenVoxel/Exploration/Samples"));
        int n=(int)(size/grid)+1;var hgt=new double[n*n];var tint=new uint[n*n];
        Parallel.For(0,n,z=>{for(int x=0;x<n;x++){
#if BASE
 var c=sampler.EvaluateColumn(cx-size/2+x*grid,cz-size/2+z*grid);
#else
 var c=sampler.EvaluateColumn(cx-size/2+x*grid,cz-size/2+z*grid,grid);
#endif
hgt[x+z*n]=Math.Max(c.SurfaceHeightMeters,sampler.Profile.SeaLevelMeters);tint[x+z*n]=c.HasWater?0xff2e5a78u:c.SurfaceTint;}});
        if(Environment.GetEnvironmentVariable("HIST")!=null)
        {
            var counts=new System.Collections.Generic.Dictionary<string,int>();
            for(int z=n/2-40;z<n/2+40;z++)for(int x=n/2-40;x<n/2+40;x++)
            {
#if BASE
                var c=sampler.EvaluateColumn(cx-size/2+x*grid,cz-size/2+z*grid);
#else
                var c=sampler.EvaluateColumn(cx-size/2+x*grid,cz-size/2+z*grid,grid);
#endif
                string k=c.SurfaceMaterial+"/"+c.BaseSurfaceMaterial+"/b"+c.BiomeUsed;counts[k]=counts.TryGetValue(k,out int v)?v+1:1;
            }
            foreach(var kv in counts)if(kv.Value>40)Console.WriteLine(kv.Key+" "+kv.Value);
        }
        double hmin=double.MaxValue,hmax=double.MinValue;foreach(var v in hgt){hmin=Math.Min(hmin,v);hmax=Math.Max(hmax,v);}
        // Camera: orbit the centre.
        double dist=size*1.05,target=(hmin+hmax)*.45;
        double ex=-Math.Sin(az)*Math.Cos(el)*dist,ey=target+Math.Sin(el)*dist,ez=-Math.Cos(az)*Math.Cos(el)*dist;
        double fx=-ex,fy=target-ey,fz=-ez;double fl=Math.Sqrt(fx*fx+fy*fy+fz*fz);fx/=fl;fy/=fl;fz/=fl;
        double rx=fz,rz=-fx;double rl=Math.Sqrt(rx*rx+rz*rz);rx/=rl;rz/=rl;double ux=-rz*fy,uy=rz*fx-rx*fz,uz=rx*fy;// up = r x f
        double focal=W/(2*Math.Tan(30*Math.PI/180));
        var sx=new double[n*n];var sy=new double[n*n];var sd=new double[n*n];var col=new double[n*n*3];
        double lx=Math.Cos(sunAz)*Math.Cos(sunEl),ly=Math.Sin(sunEl),lz=Math.Sin(sunAz)*Math.Cos(sunEl);
        for(int z=0;z<n;z++)for(int x=0;x<n;x++)
        {
            int i=x+z*n;double px=x*grid-size/2-ex,py=hgt[i]-ey,pz=z*grid-size/2-ez;
            double d=px*fx+py*fy+pz*fz;sd[i]=d;sx[i]=W/2+(px*rx+pz*rz)*focal/d;sy[i]=H/2+(px*ux+py*uy+pz*uz)*focal/d;
            double hx=(hgt[Math.Min(n-1,x+1)+z*n]-hgt[Math.Max(0,x-1)+z*n])/((Math.Min(n-1,x+1)-Math.Max(0,x-1))*grid);
            double hz=(hgt[x+Math.Min(n-1,z+1)*n]-hgt[x+Math.Max(0,z-1)*n])/((Math.Min(n-1,z+1)-Math.Max(0,z-1))*grid);
            double nx=-hx,ny=1,nz=-hz,nl=Math.Sqrt(nx*nx+ny*ny+nz*nz);nx/=nl;ny/=nl;nz/=nl;
            double diffuse=Math.Max(0,nx*lx+ny*ly+nz*lz),sky=.25+.20*ny;
            uint t=tint[i];double r=((t>>16)&255)/255.0,g=((t>>8)&255)/255.0,b=(t&255)/255.0;
            r=Math.Pow(r,2.2);g=Math.Pow(g,2.2);b=Math.Pow(b,2.2);
            double lr=r*(diffuse*1.15+sky*.9),lg=g*(diffuse*1.12+sky*.95),lb=b*(diffuse*1.05+sky*1.1);
            double fog=1-Math.Exp(-d/(size*9));lr=lr*(1-fog)+.55*fog;lg=lg*(1-fog)+.62*fog;lb=lb*(1-fog)+.72*fog;
            col[i*3]=lr;col[i*3+1]=lg;col[i*3+2]=lb;
        }
        var img=new double[W*H*3];var depth=new double[W*H];for(int i=0;i<depth.Length;i++)depth[i]=double.MaxValue;
        for(int i=0;i<W*H;i++){double s=(double)(i/W)/H;img[i*3]=.42+.2*s;img[i*3+1]=.52+.15*s;img[i*3+2]=.68+.08*s;}
        void Tri(int a0,int a1,int a2)
        {
            double x0=sx[a0],y0=sy[a0],x1=sx[a1],y1=sy[a1],x2=sx[a2],y2=sy[a2];
            double area=(x1-x0)*(y2-y0)-(x2-x0)*(y1-y0);if(Math.Abs(area)<1e-9||sd[a0]<1||sd[a1]<1||sd[a2]<1)return;
            int minx=Math.Max(0,(int)Math.Floor(Math.Min(x0,Math.Min(x1,x2)))),maxx=Math.Min(W-1,(int)Math.Ceiling(Math.Max(x0,Math.Max(x1,x2))));
            int miny=Math.Max(0,(int)Math.Floor(Math.Min(y0,Math.Min(y1,y2)))),maxy=Math.Min(H-1,(int)Math.Ceiling(Math.Max(y0,Math.Max(y1,y2))));
            for(int py=miny;py<=maxy;py++)for(int px=minx;px<=maxx;px++)
            {
                double qx=px+.5,qy=py+.5;
                double w0=((x1-qx)*(y2-qy)-(x2-qx)*(y1-qy))/area,w1=((x2-qx)*(y0-qy)-(x0-qx)*(y2-qy))/area,w2=1-w0-w1;
                if(w0<-1e-6||w1<-1e-6||w2<-1e-6)continue;
                double d=w0*sd[a0]+w1*sd[a1]+w2*sd[a2];int p=px+py*W;if(d>=depth[p])continue;depth[p]=d;
                for(int c=0;c<3;c++)img[p*3+c]=w0*col[a0*3+c]+w1*col[a1*3+c]+w2*col[a2*3+c];
            }
        }
        for(int z=0;z<n-1;z++)for(int x=0;x<n-1;x++){int i=x+z*n;Tri(i,i+1,i+n+1);Tri(i,i+n+1,i+n);}
        var bytes=new byte[W*H*3];for(int i=0;i<bytes.Length;i++)bytes[i]=(byte)Math.Min(255,Math.Round(Math.Pow(Math.Max(0,img[i]),1/2.2)*255));
        using(var f=File.Create(output)){var head=System.Text.Encoding.ASCII.GetBytes("P6\n"+W+" "+H+"\n255\n");f.Write(head,0,head.Length);f.Write(bytes,0,bytes.Length);}
        Console.WriteLine("smooth "+n+"x"+n+", heights "+hmin.ToString("F0")+".."+hmax.ToString("F0"));
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
