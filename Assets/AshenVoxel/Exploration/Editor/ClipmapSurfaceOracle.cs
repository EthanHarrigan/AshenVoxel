using System;

namespace AshenVoxel.Exploration.Editor
{
    // Independent numerical oracle: solve barycentric coordinates of emitted
    // triangles, rather than repeating the shader's coarse-height formula.
    internal static class ClipmapSurfaceOracle
    {
        internal readonly struct Vertex
        {
            public readonly double X,Y,Z;
            public Vertex(double x,double y,double z){X=x;Y=y;Z=z;}
        }
        internal static bool Triangle(Vertex a,Vertex b,Vertex c,double x,double z,out double height)
        {
            double det=(b.Z-c.Z)*(a.X-c.X)+(c.X-b.X)*(a.Z-c.Z);
            if(Math.Abs(det)<1e-20){height=0;return false;}
            double u=((b.Z-c.Z)*(x-c.X)+(c.X-b.X)*(z-c.Z))/det;
            double v=((c.Z-a.Z)*(x-c.X)+(a.X-c.X)*(z-c.Z))/det;
            double w=1-u-v;
            height=u*a.Y+v*b.Y+w*c.Y;
            return u>=-1e-9&&v>=-1e-9&&w>=-1e-9;
        }
        // Mesh winding/diagonal follows submitted index triples, independently
        // of the production height interpolation implementation.
        internal static double Quad(double x0,double z0,double spacing,double x,double z,
            double h00,double h10,double h01,double h11)
        {
            var a=new Vertex(x0,h00,z0);var b=new Vertex(x0+spacing,h10,z0);
            var c=new Vertex(x0,h01,z0+spacing);var d=new Vertex(x0+spacing,h11,z0+spacing);
            if(Triangle(a,c,d,x,z,out double height)||Triangle(a,d,b,x,z,out height))return height;
            throw new InvalidOperationException("Oracle query outside cell");
        }
        internal static double Terrain(double x,double z)
            =>300*Math.Sin(x/700)*Math.Cos(z/900)+40*Math.Sin(x/37+z/53);
        internal static double Percentile(double[] values,double fraction)
        {
            if(values.Length==0)throw new ArgumentException("Empty percentile sample");
            var sorted=(double[])values.Clone();Array.Sort(sorted);
            return sorted[Math.Max(0,Math.Min(sorted.Length-1,(int)Math.Ceiling(fraction*sorted.Length)-1))];
        }
    }
}
