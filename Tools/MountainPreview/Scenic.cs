using System;using System.IO;using System.Threading.Tasks;using System.Collections.Generic;using AshenVoxel.Core;using AshenVoxel.WorldScale;
/// <summary>Offline look-development renderer for distant mountain ranges (not Unity).
/// Same terrain heights and SurfaceMaterialSampler tints as the game, plus optional
/// candidate features: cast sun shadows + sky occlusion, forest canopy that climbs slopes
/// until too steep/cold, and a hydraulic-erosion pass with flow/deposit colouring.
/// Usage: root out cx cz size grid W H az el dist features(csv: shadow,trees,erosion,rocks)</summary>
static class Scenic
{
    static TerrainReshapeAtlas Reshape;
    static void Main(string[] a)
    {
        string root=a[0],output=a[1];double cx=double.Parse(a[2]),cz=double.Parse(a[3]),size=double.Parse(a[4]),grid=double.Parse(a[5]);
        int W=int.Parse(a[6]),H=int.Parse(a[7]);double az=double.Parse(a[8])*Math.PI/180,el=double.Parse(a[9])*Math.PI/180,distF=double.Parse(a[10]);
        var feat=new HashSet<string>((a.Length>11?a[11]:"").Split(',',StringSplitOptions.RemoveEmptyEntries));
        bool grade=feat.Contains("grade"),shadow=feat.Contains("shadow"),trees=feat.Contains("trees"),erosion=feat.Contains("erosion"),rocks=feat.Contains("rocks");
        double hscale=double.Parse(Environment.GetEnvironmentVariable("HSCALE")??"1");double treeline=double.Parse(Environment.GetEnvironmentVariable("TREELINE")??"2300");bool montane=Environment.GetEnvironmentVariable("MONTANE")!=null;
        var sampler=Load(Path.Combine(root,"Assets/AshenVoxel/Exploration/Samples"));double sea=sampler.Profile.SeaLevelMeters;
        int n=(int)(size/grid)+1;var hgt=new double[n*n];var cols=new SurfaceColumn[n*n];
        Parallel.For(0,n,z=>{for(int x=0;x<n;x++){var c=sampler.EvaluateColumn(cx-size/2+x*grid,cz-size/2+z*grid,grid);cols[x+z*n]=c;hgt[x+z*n]=Math.Max(sea+(c.SurfaceHeightMeters-sea)*hscale,sea);}});
        var flow=new double[n*n];var deposit=new double[n*n];var original=(double[])hgt.Clone();
        int thermal=int.Parse(Environment.GetEnvironmentVariable("THERMAL")??"0");
        if(thermal>0)Talus(hgt,deposit,n,grid,sea,thermal,double.Parse(Environment.GetEnvironmentVariable("REPOSE")??"36"));
        if(erosion&&Reshape==null)Erode(hgt,flow,deposit,n,grid,sea);
        if(Reshape!=null)for(int z=0;z<n;z++)for(int x=0;x<n;x++){double wx=cx-size/2+x*grid,wz=cz-size/2+z*grid;flow[x+z*n]=Math.Exp(Reshape.Flow(wx,wz)*9)-1;deposit[x+z*n]=Math.Pow(Reshape.Talus(wx,wz),2)*40;}
        bool shapedWorld=hscale<1||Reshape!=null;double treeT=double.Parse(Environment.GetEnvironmentVariable("TREET")??"-4");
        if(Environment.GetEnvironmentVariable("DUMP") is string dump){var bb=new byte[n*n*8];Buffer.BlockCopy(hgt,0,bb,0,bb.Length);File.WriteAllBytes(dump,bb);
            var cv=new double[n*n];for(int i=0;i<n*n;i++)cv[i]=cols[i].HasWater?-1:ForestBiomes.Sample(cols[i],cx-size/2+(i%n)*grid,cz-size/2+(i/n)*grid,714103).Density;var cb=new byte[n*n*8];Buffer.BlockCopy(cv,0,cb,0,cb.Length);File.WriteAllBytes(dump+".forest",cb);}
        double Hh(int x,int z)=>hgt[Math.Clamp(x,0,n-1)+Math.Clamp(z,0,n-1)*n];
        // Lighting basis
        double sunAz=double.Parse(Environment.GetEnvironmentVariable("SUNAZ")??"160")*Math.PI/180,sunEl=double.Parse(Environment.GetEnvironmentVariable("SUNEL")??"20")*Math.PI/180;
        double lx=Math.Cos(sunAz)*Math.Cos(sunEl),ly=Math.Sin(sunEl),lz=Math.Sin(sunAz)*Math.Cos(sunEl);
        var lit=new double[n*n];var sky=new double[n*n];
        Parallel.For(0,n,z=>{for(int x=0;x<n;x++){
            int i=x+z*n;double sh=1,sk=1;
            if(shadow)
            {
                // March toward the sun over the heightfield.
                double px=x,pz=z,py=hgt[i]+2;double step=1,dy=ly/Math.Sqrt(lx*lx+lz*lz)*grid;
                for(int k=0;k<600;k++){px+=lx/Math.Sqrt(lx*lx+lz*lz)*step;pz+=lz/Math.Sqrt(lx*lx+lz*lz)*step;py+=dy*step;if(px<0||pz<0||px>n-1||pz>n-1)break;
                    double th=Hh((int)px,(int)pz);if(th>py){sh=0;break;}step=Math.Min(4,step*1.04);}
                // Sky occlusion from 8 horizon directions.
                double sum=0;for(int d=0;d<8;d++){double ang=d*Math.PI/4,ddx=Math.Cos(ang),ddz=Math.Sin(ang),maxSlope=0;
                    for(int r=1;r<40;r+=1+r/6){double hh=Hh((int)(x+ddx*r),(int)(z+ddz*r))-hgt[i];maxSlope=Math.Max(maxSlope,hh/(r*grid));}
                    sum+=1-Math.Sin(Math.Atan(maxSlope));}
                sk=sum/8;
            }
            lit[i]=sh;sky[i]=sk;}});
        var canopy=new double[n*n];var standOf=new ForestStand[n*n];
        if(trees)Parallel.For(0,n,z=>{for(int x=0;x<n;x++){
            int i=x+z*n;var c=cols[i];if(c.HasWater||!c.IsLand)continue;
            double hx=(Hh(x+1,z)-Hh(x-1,z))/(2*grid),hz=(Hh(x,z+1)-Hh(x,z-1))/(2*grid);double s=Math.Atan(Math.Sqrt(hx*hx+hz*hz))*180/Math.PI;
            var stand=ForestBiomes.Sample(c,cx-size/2+x*grid,cz-size/2+z*grid,714103);if(montane&&c.PrecipitationMm>350&&(hscale<1||(c.TemperatureC>treeT-3&&c.TemperatureC<14))&&(!stand.HasTrees||stand.Density<.6))
                stand=new ForestStand(ForestHabitat.PineTaiga,ForestStage.MatureOpen,AshenVoxel.Core.Trees.TreeSpecies.Pine,.75,1);
            standOf[i]=stand;if(!stand.HasTrees)continue;
            double steep=Math.Clamp((46-s)/14,0,1),cold=hscale<1?Math.Clamp((treeline-hgt[i])/600,0,1):Math.Clamp((c.TemperatureC-treeT)/3,0,1);
            bool bare=c.SurfaceMaterial==MaterialId.Snow||c.SurfaceMaterial==MaterialId.Ice||false;if(c.SurfaceMaterial==MaterialId.Gravel)steep*=.45;
            // Ragged edges: noise lets fingers of forest run up gullies and leaves avalanche/rockfall gaps.
            double ragged=Math.Sin(x*.9+Math.Sin(z*.31)*2.5)*Math.Sin(z*.7+Math.Sin(x*.23)*2)*.5+.5;
            double gully=erosion?Math.Clamp(Math.Log(1+flow[i])/5,0,1):0;
            double v=montane?Math.Min(1,stand.Density*1.4)*Math.Clamp(steep+gully*.35-.25*ragged,0,1)*cold:Math.Min(1,stand.Density*1.3);if(bare)v*=.1;// game rule: ForestBiomes density only
            canopy[i]=Math.Clamp(v,0,1);}});
        if(trees&&montane){ // soften biome-tile edges into natural stand boundaries
            int R=(int)Math.Max(2,60/grid);var tmp=new double[n*n];
            Parallel.For(0,n,z=>{double acc=0;int c0=0;for(int x=-R;x<n;x++){if(x+R<n){acc+=canopy[x+R+z*n];c0++;}if(x-R-1>=0){acc-=canopy[x-R-1+z*n];c0--;}if(x>=0)tmp[x+z*n]=acc/c0;}});
            var blurred=new double[n*n];Parallel.For(0,n,x=>{double acc=0;int c0=0;for(int z=-R;z<n;z++){if(z+R<n){acc+=tmp[x+(z+R)*n];c0++;}if(z-R-1>=0){acc-=tmp[x+(z-R-1)*n];c0--;}if(z>=0)blurred[x+z*n]=acc/c0;}});
            for(int i=0;i<n*n;i++)if(!cols[i].HasWater)canopy[i]=Math.Min(canopy[i]>0?1:0.0,Math.Clamp((blurred[i]-.25)*1.6,0,1))*(canopy[i]>0?1:0);
        }
        if(Environment.GetEnvironmentVariable("DIAG2")!=null){var hist=new Dictionary<string,int>();for(int i=0;i<n*n;i+=5){if(hgt[i]<1000||hgt[i]>2600)continue;var c=cols[i];
            double m=ForestBiomes.Montane(c,0,0,714103);var st=ForestBiomes.Sample(c,cx-size/2+(i%n)*grid,cz-size/2+(i/n)*grid,714103);
            string k=$"b{c.BiomeUsed} T{Math.Round(c.TemperatureC/3)*3} S{Math.Round(c.SlopeDegrees/10)*10} P{Math.Round(c.PrecipitationMm/200)*200} snow{(c.SnowDepthMeters>2.2?1:0)} m{Math.Round(m*4)/4} d{Math.Round(st.Density*4)/4}";hist[k]=hist.TryGetValue(k,out var v)?v+1:1;}
            foreach(var kv in hist)if(kv.Value>300)Console.WriteLine(kv.Value+" "+kv.Key);}
        if(Environment.GetEnvironmentVariable("DIAG")!=null){var hist=new Dictionary<string,int>();for(int i=0;i<n*n;i+=7){if(hgt[i]<600||hgt[i]>1800)continue;var c=cols[i];
            string k=$"T{Math.Round(c.TemperatureC/4)*4} P{Math.Round(c.PrecipitationMm/200)*200} {c.SurfaceMaterial} b{c.BiomeUsed} can{Math.Round(canopy[i]*4)/4}";hist[k]=hist.TryGetValue(k,out var v)?v+1:1;}
            foreach(var kv in hist)if(kv.Value>150)Console.WriteLine(kv.Key+" "+kv.Value);}
        if(Environment.GetEnvironmentVariable("MAPS") is string mp){var bb=new byte[n*n*3];for(int i=0;i<n*n;i++){double hx=(Hh(i%n+1,i/n)-Hh(i%n-1,i/n))/(2*grid),hz=(Hh(i%n,i/n+1)-Hh(i%n,i/n-1))/(2*grid);double sl=Math.Atan(Math.Sqrt(hx*hx+hz*hz))*180/Math.PI;
            bb[i*3]=(byte)Math.Min(255,sl*3);bb[i*3+1]=(byte)(canopy[i]*255);bb[i*3+2]=(byte)Math.Min(255,hgt[i]/14);}File.WriteAllBytes(mp,bb);}
        // Colours
        var col=new double[n*n*3];var slopeDeg=new double[n*n];
        Parallel.For(0,n,z=>{for(int x=0;x<n;x++){
            int i=x+z*n;double hx=(Hh(x+1,z)-Hh(x-1,z))/(2*grid),hz=(Hh(x,z+1)-Hh(x,z-1))/(2*grid);
            double nx=-hx,ny=1,nz=-hz,nl=Math.Sqrt(nx*nx+1+nz*nz);nx/=nl;ny/=nl;nz/=nl;slopeDeg[i]=Math.Acos(ny)*180/Math.PI;
            var c=cols[i];uint t=c.HasWater?0xff2e5a78u:c.SurfaceTint;
            double r=((t>>16)&255)/255.0,g=((t>>8)&255)/255.0,b=(t&255)/255.0;
            if(erosion&&!c.HasWater)
            {
                // Eroded geometry: re-expose rock on newly steep faces, dark wet channels, pale fresh deposits.
                double rockMix=Math.Clamp((slopeDeg[i]-44)/14,0,1)*.6;
                if(rockMix>0&&c.SurfaceMaterial!=MaterialId.Snow){double rr=.47,gg=.45,bb=.43;r+= (rr-r)*rockMix*.7;g+=(gg-g)*rockMix*.7;b+=(bb-b)*rockMix*.7;}
                double ch=Math.Clamp(Math.Log(1+flow[i])/6,0,1);r*=1-.35*ch;g*=1-.30*ch;b*=1-.22*ch;
                double dep=Math.Clamp(deposit[i]/(grid*.08),0,1);r+=(.62-r)*dep*.45;g+=(.58-g)*dep*.45;b+=(.52-b)*dep*.45;
            }
            if(erosion&&grade&&!c.HasWater&&c.TemperatureC<-1)
            {
                double ch=Math.Clamp(Math.Log(1+flow[i])/3-1.3,0,1)*Math.Clamp((-1-c.TemperatureC)/4,0,1)*.85;r+=(.93-r)*ch;g+=(.94-g)*ch;b+=(.97-b)*ch;
            }
            if(rocks&&grade&&!c.HasWater&&shapedWorld)
            {
                double alp=(hscale<1?Math.Clamp((hgt[i]-(treeline-300))/500,0,1):Math.Clamp((treeT+1.5-c.TemperatureC)/3,0,1))*Math.Clamp((slopeDeg[i]-24)/10,0,1);double lum0=(r+g+b)/3;
                double gr=.34+.07*Math.Sin(x*.21+Math.Sin(z*.13)*2)+.05*Math.Sin(hgt[i]/37.0);r+=(gr*.98-r)*alp*.8;g+=(gr*.98-g)*alp*.8;b+=(gr*1.03-b)*alp*.8;
                double dep2=Math.Clamp(deposit[i]/(grid*.05),0,1)*Math.Clamp((slopeDeg[i]-15)/10,0,1)*(1-canopy[i]);r+=(.60-r)*dep2*.5;g+=(.59-g)*dep2*.5;b+=(.57-b)*dep2*.5;
            }
            if(rocks&&!c.HasWater&&c.SurfaceMaterial!=MaterialId.Snow)
            {
                // Sheer faces: cold grey granite with dark streaks; talus aprons below.
                double face=Math.Clamp((slopeDeg[i]-46)/12,0,1);double lum=(r+g+b)/3;r+=(lum*.72-r)*face*.65;g+=(lum*.72-g)*face*.65;b+=(lum*.76-b)*face*.65;
                // Streaky desert-varnish: darker vertical bands down sheer faces.
                double streak=Math.Sin((x*1.7+z*0.6)*1.3+Math.Sin(z*.37)*3)*.5+.5;double dk=1-face*.28*streak;r*=dk;g*=dk;b*=dk;
            }
            if(trees&&canopy[i]>0){double cv=canopy[i];r+=(.15-r)*cv*.8;g+=(.24-g)*cv*.8;b+=(.13-b)*cv*.8;}
            r=Math.Pow(r,2.2);g=Math.Pow(g,2.2);b=Math.Pow(b,2.2);
            double diffuse=Math.Max(0,nx*lx+ny*ly+nz*lz)*lit[i],ambient=(.20+.25*ny)*(shadow?sky[i]*1.25:1);
            double lr,lg,lb;
            if(grade){double amb=(.30+.30*ny)*(shadow?Math.Pow(sky[i],1.5):1);lr=r*(diffuse*2.1+amb*.55);lg=g*(diffuse*1.95+amb*.68);lb=b*(diffuse*1.70+amb*.95);}
            else{lr=r*(diffuse*1.25+ambient*.75);lg=g*(diffuse*1.18+ambient*.85);lb=b*(diffuse*1.05+ambient*1.05);}
            col[i*3]=lr;col[i*3+1]=lg;col[i*3+2]=lb;}});
        // Camera
        double hmin=double.MaxValue,hmax=double.MinValue;foreach(var v in hgt){hmin=Math.Min(hmin,v);hmax=Math.Max(hmax,v);}
        double dist=size*distF,target=hmin+(hmax-hmin)*.30;
        double ex=-Math.Sin(az)*Math.Cos(el)*dist,ey=target+Math.Sin(el)*dist,ez=-Math.Cos(az)*Math.Cos(el)*dist;
        ey=Math.Max(ey,Hh((int)((ex+size/2)/grid),(int)((ez+size/2)/grid))+60);
        double tx0=0,ty0=target,tz0=0;
        var cam=Environment.GetEnvironmentVariable("CAM");// "eyeX eyeZ eyeAboveGround targetX targetZ targetAboveGround" (world metres)
        if(cam!=null){var q=Array.ConvertAll(cam.Split(' ',StringSplitOptions.RemoveEmptyEntries),double.Parse);
            ex=q[0]-cx;ez=q[1]-cz;ey=Hh((int)((ex+size/2)/grid),(int)((ez+size/2)/grid))+q[2];tx0=q[3]-cx;tz0=q[4]-cz;ty0=Hh((int)((tx0+size/2)/grid),(int)((tz0+size/2)/grid))+q[5];}
        double fx=tx0-ex,fy=ty0-ey,fz=tz0-ez;double fl=Math.Sqrt(fx*fx+fy*fy+fz*fz);fx/=fl;fy/=fl;fz/=fl;
        double rx=fz,rz=-fx;double rl=Math.Sqrt(rx*rx+rz*rz);rx/=rl;rz/=rl;double ux=-rz*fy,uy=rz*fx-rx*fz,uz=rx*fy;
        double focal=W/(2*Math.Tan(32*Math.PI/180));
        var img=new double[W*H*3];var depth=new double[W*H];for(int i=0;i<depth.Length;i++)depth[i]=double.MaxValue;
        for(int i=0;i<W*H;i++){double s=(double)(i/W)/H;img[i*3]=Math.Pow(.50+.25*s,2.2);img[i*3+1]=Math.Pow(.62+.18*s,2.2);img[i*3+2]=Math.Pow(.80+.08*s,2.2);}
        (double X,double Y,double D) Project(double wx,double wy,double wz){double px=wx-ex,py=wy-ey,pz=wz-ez;double d=px*fx+py*fy+pz*fz;return (W/2+(px*rx+pz*rz)*focal/d,H/2+(px*ux+py*uy+pz*uz)*focal/d,d);}
        void Fog(ref double r,ref double g,ref double b,double d){double f=1-Math.Exp(-d/(grade?size*6:size*3.5));r=r*(1-f)+Math.Pow(.70,2.2)*f;g=g*(1-f)+Math.Pow(.77,2.2)*f;b=b*(1-f)+Math.Pow(.86,2.2)*f;}
        void Tri((double X,double Y,double D) p0,(double X,double Y,double D) p1,(double X,double Y,double D) p2,double[] c0,double[] c1,double[] c2)
        {
            if(p0.D<1||p1.D<1||p2.D<1)return;double area=(p1.X-p0.X)*(p2.Y-p0.Y)-(p2.X-p0.X)*(p1.Y-p0.Y);if(Math.Abs(area)<1e-9)return;
            int minx=Math.Max(0,(int)Math.Floor(Math.Min(p0.X,Math.Min(p1.X,p2.X)))),maxx=Math.Min(W-1,(int)Math.Ceiling(Math.Max(p0.X,Math.Max(p1.X,p2.X))));
            int miny=Math.Max(0,(int)Math.Floor(Math.Min(p0.Y,Math.Min(p1.Y,p2.Y)))),maxy=Math.Min(H-1,(int)Math.Ceiling(Math.Max(p0.Y,Math.Max(p1.Y,p2.Y))));
            if(maxx-minx>W||maxy-miny>H)return;
            for(int py=miny;py<=maxy;py++)for(int px=minx;px<=maxx;px++)
            {
                double qx=px+.5,qy=py+.5;double w0=((p1.X-qx)*(p2.Y-qy)-(p2.X-qx)*(p1.Y-qy))/area,w1=((p2.X-qx)*(p0.Y-qy)-(p0.X-qx)*(p2.Y-qy))/area,w2=1-w0-w1;
                if(w0<-1e-6||w1<-1e-6||w2<-1e-6)continue;double d=w0*p0.D+w1*p1.D+w2*p2.D;int p=px+py*W;if(d>=depth[p])continue;depth[p]=d;
                double r=w0*c0[0]+w1*c1[0]+w2*c2[0],g=w0*c0[1]+w1*c1[1]+w2*c2[1],b=w0*c0[2]+w1*c1[2]+w2*c2[2];Fog(ref r,ref g,ref b,d);
                img[p*3]=r;img[p*3+1]=g;img[p*3+2]=b;
            }
        }
        var proj=new (double X,double Y,double D)[n*n];
        for(int z=0;z<n;z++)for(int x=0;x<n;x++)proj[x+z*n]=Project(x*grid-size/2,hgt[x+z*n],z*grid-size/2);
        double[] C(int i)=>new[]{col[i*3],col[i*3+1],col[i*3+2]};
        for(int z=0;z<n-1;z++)for(int x=0;x<n-1;x++){int i=x+z*n;Tri(proj[i],proj[i+1],proj[i+n+1],C(i),C(i+1),C(i+n+1));Tri(proj[i],proj[i+n+1],proj[i+n],C(i),C(i+n+1),C(i+n));}
        if(trees)
        {
            // Forest canopy: stands from ForestBiomes; thin out from 28 to 40 degrees and with cold; none on rock/snow.
            var rng=new Random(714103);int planted=0;double treesPerCell=Math.Min(6,grid*grid/90.0);
            for(int z=0;z<n-1;z++)for(int x=0;x<n-1;x++)
            {
                int i=x+z*n;var c=cols[i];if(c.HasWater||!c.IsLand)continue;
                double wx=x*grid-size/2+cx,wz=z*grid-size/2+cz;var stand=standOf[i];
                double cover=canopy[i];var stand2=standOf[i];
                int count=(int)Math.Floor(cover*treesPerCell+rng.NextDouble());for(int k=0;k<count;k++)
                {
                    double tx=x*grid-size/2+(rng.NextDouble())*grid,tz=z*grid-size/2+rng.NextDouble()*grid;
                    double th=Hh(x,z)+(Hh(x+1,z)-Hh(x,z))*((tx-(x*grid-size/2))/grid);
                    double tall=18+rng.NextDouble()*14,wide=tall*(stand.Species==AshenVoxel.Core.Trees.TreeSpecies.Pine?.32:.5);
                    var b0=Project(tx,th-2,tz);var top=Project(tx,th+tall,tz);if(b0.D<1)continue;
                    double halfW=wide*focal/b0.D;var l=(b0.X-halfW,b0.Y,b0.D-1);var r=(b0.X+halfW,b0.Y,b0.D-1);
                    double shade=lit[i]*.7+.3,v=.85+.3*rng.NextDouble();
                    double cr=Math.Pow(.16*v,2.2)*(.4+.9*shade),cg=Math.Pow(.29*v,2.2)*(.4+.9*shade),cb=Math.Pow(.14*v,2.2)*(.4+.9*shade);
                    if(stand.Species==AshenVoxel.Core.Trees.TreeSpecies.Birch||stand.Species==AshenVoxel.Core.Trees.TreeSpecies.Oak){cr*=1.25;cg*=1.15;}
                    var tc=new[]{cr,cg,cb};var bc=new[]{cr*.55,cg*.55,cb*.55};Tri(l,r,(top.X,top.Y,top.D-1),bc,bc,tc);planted++;
                }
            }
            Console.WriteLine("trees drawn: "+planted);
        }
        var bytes=new byte[W*H*3];for(int i=0;i<bytes.Length;i++)bytes[i]=(byte)Math.Min(255,Math.Round(Math.Pow(Math.Max(0,img[i]),1/2.2)*255));
        using(var f=File.Create(output)){var head=System.Text.Encoding.ASCII.GetBytes("P6\n"+W+" "+H+"\n255\n");f.Write(head,0,head.Length);f.Write(bytes,0,bytes.Length);}
        Console.WriteLine("scenic "+n+"x"+n+" heights "+hmin.ToString("F0")+".."+hmax.ToString("F0"));
    }
    /// <summary>Thermal weathering on a 4x coarser grid: rock above the angle of repose collapses into
    /// talus aprons at the foot of cliffs. Only the coarse difference is added back, so fine detail stays.</summary>
    static void Talus(double[] h,double[] deposit,int n,double grid,double sea,int iterations,double reposeDeg)
    {
        double keepCliffs=double.Parse(Environment.GetEnvironmentVariable("CLIFFKEEP")??"1");
        const int k=4;int m=(n+k-1)/k;var c=new double[m*m];var cnt=new int[m*m];
        for(int z=0;z<n;z++)for(int x=0;x<n;x++){int j=x/k+z/k*m;c[j]+=h[x+z*n];cnt[j]++;}
        for(int j=0;j<m*m;j++)c[j]/=cnt[j];var c0=(double[])c.Clone();double cg=grid*k,limit=Math.Tan(reposeDeg*Math.PI/180)*cg;
        var delta=new double[m*m];int[] dx={1,-1,0,0,1,1,-1,-1},dz={0,0,1,-1,1,-1,1,-1};
        for(int it=0;it<iterations;it++)
        {
            Array.Clear(delta,0,delta.Length);
            Parallel.For(1,m-1,z=>{for(int x=1;x<m-1;x++){int j=x+z*m;double total=0,maxd=0;
                for(int d=0;d<8;d++){double dist=d<4?1:1.4142;double diff=c[j]-c[x+dx[d]+(z+dz[d])*m]-limit*dist;if(diff>0){total+=diff;maxd=Math.Max(maxd,diff);}}
                if(total<=0)continue;double move=maxd*.25;
                lock(delta){delta[j]-=move;for(int d=0;d<8;d++){double dist=d<4?1:1.4142;double diff=c[j]-c[x+dx[d]+(z+dz[d])*m]-limit*dist;if(diff>0)delta[x+dx[d]+(z+dz[d])*m]+=move*diff/total;}}}});
            for(int j=0;j<m*m;j++)c[j]+=delta[j];
        }
        for(int z=0;z<n;z++)for(int x=0;x<n;x++)
        {
            double fx=(x+.5)/k-.5,fz=(z+.5)/k-.5;int x0=Math.Clamp((int)Math.Floor(fx),0,m-1),z0=Math.Clamp((int)Math.Floor(fz),0,m-1),x1=Math.Min(m-1,x0+1),z1=Math.Min(m-1,z0+1);double tx=Math.Clamp(fx-x0,0,1),tz=Math.Clamp(fz-z0,0,1);
            double D(int xx,int zz)=>c[xx+zz*m]-c0[xx+zz*m];double v=(D(x0,z0)*(1-tx)+D(x1,z0)*tx)*(1-tz)+(D(x0,z1)*(1-tx)+D(x1,z1)*tx)*tz;
            int i=x+z*n;if(h[i]<=sea+.1&&v>0)continue;if(v<0)v*=keepCliffs;h[i]=Math.Max(sea,h[i]+v);if(v>0)deposit[i]+=v*.02;
        }
    }
    /// <summary>Particle hydraulic erosion (droplets carve, carry and deposit sediment) on the sampled grid.</summary>
    static void Erode(double[] h,double[] flow,double[] deposit,int n,double grid,double sea)
    {
        var rng=new Random(42);int drops=(int)(n*n*double.Parse(Environment.GetEnvironmentVariable("DROPS")??"0.8"));double inertia=.08,capacityK=4,erodeK=.35,depositK=.25,evap=.015,gravity=9.8,minSlope=.01;
        double G(int x,int z)=>h[Math.Clamp(x,0,n-1)+Math.Clamp(z,0,n-1)*n];
        for(int d=0;d<drops;d++)
        {
            double px=rng.NextDouble()*(n-2),pz=rng.NextDouble()*(n-2),dx=0,dz=0,speed=1,water=1,sed=0;
            for(int life=0;life<90;life++)
            {
                int ix=(int)px,iz=(int)pz;if(ix<1||iz<1||ix>=n-2||iz>=n-2)break;double fx=px-ix,fz=pz-iz;
                double h00=G(ix,iz),h10=G(ix+1,iz),h01=G(ix,iz+1),h11=G(ix+1,iz+1);
                double gx=(h10-h00)*(1-fz)+(h11-h01)*fz,gz=(h01-h00)*(1-fx)+(h11-h10)*fx;double hOld=h00*(1-fx)*(1-fz)+h10*fx*(1-fz)+h01*(1-fx)*fz+h11*fx*fz;
                if(hOld<=sea+.5)break;
                dx=dx*inertia-gx*(1-inertia);dz=dz*inertia-gz*(1-inertia);double len=Math.Sqrt(dx*dx+dz*dz);if(len<1e-9)break;dx/=len;dz/=len;
                double nxp=px+dx,nzp=pz+dz;int jx=(int)nxp,jz=(int)nzp;if(jx<1||jz<1||jx>=n-2||jz>=n-2)break;
                double hNew=G(jx,jz)*(1-(nxp-jx))*(1-(nzp-jz))+G(jx+1,jz)*(nxp-jx)*(1-(nzp-jz))+G(jx,jz+1)*(1-(nxp-jx))*(nzp-jz)+G(jx+1,jz+1)*(nxp-jx)*(nzp-jz);
                double dh=hNew-hOld;flow[ix+iz*n]+=water;
                double cap=Math.Max(-dh/grid,minSlope)*speed*water*capacityK*grid*.05;
                if(sed>cap||dh>0)
                {
                    double amt=dh>0?Math.Min(dh,sed):(sed-cap)*depositK;sed-=amt;
                    h[ix+iz*n]+=amt*(1-fx)*(1-fz);h[ix+1+iz*n]+=amt*fx*(1-fz);h[ix+(iz+1)*n]+=amt*(1-fx)*fz;h[ix+1+(iz+1)*n]+=amt*fx*fz;deposit[ix+iz*n]+=amt;
                }
                else
                {
                    double amt=Math.Min((cap-sed)*erodeK,-dh);sed+=amt;
                    h[ix+iz*n]-=amt*(1-fx)*(1-fz);h[ix+1+iz*n]-=amt*fx*(1-fz);h[ix+(iz+1)*n]-=amt*(1-fx)*fz;h[ix+1+(iz+1)*n]-=amt*fx*fz;
                }
                speed=Math.Sqrt(Math.Max(0,speed*speed-dh/grid*gravity*.1));water*=1-evap;px=nxp;pz=nzp;
            }
        }
    }
    static SurfaceMaterialSampler Load(string p)
    {
        HeightAtlas macro,fine;ClimateAtlas mc,fc;ExplorationBiomeAtlas biomes;
        using(var f=File.OpenRead(Path.Combine(p,"ExplorationMacro.bytes")))macro=AtlasIO.Read(f);using(var f=File.OpenRead(Path.Combine(p,"ExplorationFine.bytes")))fine=AtlasIO.Read(f);
        using(var f=File.OpenRead(Path.Combine(p,"ExplorationMacro.climate.bytes")))mc=ClimateArchiveIO.Read(f);using(var f=File.OpenRead(Path.Combine(p,"ExplorationFine.climate.bytes")))fc=ClimateArchiveIO.Read(f);
        using(var f=File.OpenRead(Path.Combine(p,"ExplorationBiomes.bytes")))biomes=ExplorationBiomeAtlasIO.Read(f);
        var field=new ExplorationAtlasField(macro,mc,fine,fc,512,macro.ExtentX,macro.ExtentZ,1,biomes,macro.ExtentX,macro.ExtentZ);
        IWorldField shaped=field;
        if(Environment.GetEnvironmentVariable("PATCHES") is string pp)shaped=new PackedOverview(field,pp);
        if(Environment.GetEnvironmentVariable("RESHAPE") is string rp){using var rf=File.OpenRead(rp);Reshape=TerrainReshapeIO.Read(rf);shaped=new TerrainReshapeField(field,Reshape);}
        return new SurfaceMaterialSampler(new TerrainDetailField(shaped,714103),new SurfaceProfile(seed:714103,maxSnowDepthMeters:2.4f,winterFraction:.2f));
    }
}
/// <summary>Renderer-only: the game's overview composition over the bundled atlas from a packed .avov set.</summary>
sealed class PackedOverview:IWorldFieldBounds,IClimateWorldField
{
    sealed class Patch{public double X0,Z0;public int Spacing,Columns;public float[] H;public long CX,CZ;}
    readonly ExplorationAtlasField atlas;readonly Dictionary<(long,long),Patch> patches=new Dictionary<(long,long),Patch>();
    public PackedOverview(ExplorationAtlasField a,string path)
    {
        atlas=a;using var r=new BinaryReader(File.OpenRead(path));int n=r.ReadInt32();
        for(int i=0;i<n;i++){var p=new Patch{CX=r.ReadInt64(),CZ=r.ReadInt64(),Spacing=r.ReadInt32(),Columns=r.ReadInt32()};p.H=new float[p.Columns*p.Columns];
            var b=r.ReadBytes(p.H.Length*4);Buffer.BlockCopy(b,0,p.H,0,b.Length);p.X0=p.CX*2560.0-320;p.Z0=p.CZ*2560.0-320;patches[(p.CX,p.CZ)]=p;}
    }
    public string SourceIdentity=>atlas.SourceIdentity+"/packed-overview";
    static double Weight(Patch p,double x,double z){double minX=p.CX*2560.0,minZ=p.CZ*2560.0,maxX=minX+2560,maxZ=minZ+2560;double dx=Math.Max(0,Math.Max(minX-x,x-maxX)),dz=Math.Max(0,Math.Max(minZ-z,z-maxZ));double t=Math.Max(0,Math.Min(1,1-Math.Max(dx,dz)/320));return t*t*(3-2*t);}
    static double Height(Patch p,double x,double z){int C=p.Columns;double px=Math.Clamp((x-p.X0)/p.Spacing,0,C-1),pz=Math.Clamp((z-p.Z0)/p.Spacing,0,C-1);int ix=Math.Min(C-2,(int)Math.Floor(px)),iz=Math.Min(C-2,(int)Math.Floor(pz));double fx=px-ix,fz=pz-iz;int a=ix+iz*C;return (p.H[a]*(1-fx)+p.H[a+1]*fx)*(1-fz)+(p.H[a+C]*(1-fx)+p.H[a+C+1]*fx)*fz;}
    double H(double x,double z,double b){long cx=(long)Math.Floor(x/2560),cz=(long)Math.Floor(z/2560);double sum=0,total=0;
        for(long j=cz-1;j<=cz+1;j++)for(long i=cx-1;i<=cx+1;i++){if(!patches.TryGetValue((i,j),out var p))continue;double w=Weight(p,x,z);if(w<=0)continue;sum+=Height(p,x,z)*w;total+=w;}
        if(total<=0)return b;double weight=Math.Min(1,total)*(1-atlas.FineWeightAt(x,z));return b+(sum/total-b)*weight;}
    public SurfaceSample Sample(double x,double z){var a=atlas.Sample(x,z);double h=H(x,z,a.HeightMeters);return new SurfaceSample(h,a.SourceBiome,a.EffectiveBiome,h>=0);}
    public HeightBounds QueryBounds(double x0,double z0,double x1,double z1){var b=atlas.QueryBounds(x0,z0,x1,z1);return new HeightBounds(b.Minimum-3000,b.Maximum+3000);}
    public bool TrySampleClimate(double x,double z,out SurfaceClimate c)
    {
        if(!atlas.TrySampleClimate(x,z,out var a)){c=default;return false;}double o=atlas.Sample(x,z).HeightMeters,n=H(x,z,o);
        c=new SurfaceClimate(Math.Clamp(a.TemperatureC+a.LapseRateCPerMeter*(Math.Max(n,0)-Math.Max(o,0)),-150,100),a.SeasonalityC,a.PrecipitationMm,a.PrecipitationCvPercent,a.LapseRateCPerMeter);return true;
    }
}
