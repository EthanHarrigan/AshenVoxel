using System;
using System.Collections.Generic;

namespace AshenVoxel.Core.Rocks
{
    /// <summary>Small unions of clipped polygonal pieces. The footprint is never
    /// an ellipse: silhouette, major planes and arrangement are primary shape
    /// controls; weathering only bevels those existing forms.</summary>
    internal sealed class RockShapeGeometry
    {
        readonly RockProfile profile;
        readonly List<Piece> pieces = new List<Piece>(4);
        readonly double bevel;

        readonly struct Point
        {
            public readonly double X, Z;
            public Point(double x, double z) { X = x; Z = z; }
        }
        sealed class Piece
        {
            public Point[] Polygon;
            public double Cap, TiltX, TiltZ, PeakX, PeakZ, PeakSlope;
            public Point Center;
        }

        public RockShapeGeometry(RockProfile p)
        {
            profile = p;
            bevel = Math.Min(Math.Min(p.WidthMeters, p.DepthMeters) * .13, .035 + p.Weathering * .18);
            switch (p.ResolvedShape)
            {
                case RockShape.AngularBoulder:
                    Add(.85, -.13, .11, 0, -.06, .02,
                        -.94, -.68, .28, -.91, .87, -.36, .63, .79, -.69, .43);
                    // A lower broken shoulder extends one side, making a
                    // deliberate reentrant corner instead of a circular dome.
                    Add(.43, .14, -.12, 0, 0, 0,
                        -.89, .10, -.37, -.04, -.23, .67, -.73, .93);
                    break;
                case RockShape.Slab:
                    Add(.48, .13, -.045, 0, 0, 0,
                        -.98, -.41, -.48, -.60, .72, -.39, .96, .14, .57, .44, -.85, .36);
                    break;
                case RockShape.Wedge:
                    Add(.57, .51, -.09, 0, 0, 0,
                        -.94, -.72, .73, -.52, .89, .27, .26, .61, -.78, .80);
                    break;
                case RockShape.Spire:
                    Add(.99, -.07, .05, 1.12, -.03, -.15,
                        -.28, -.91, .29, -.70, .37, .43, -.15, .83, -.40, .20);
                    Add(.27, .08, -.08, 0, 0, 0,
                        -.77, -.37, -.03, -.63, .65, -.04, .22, .58, -.64, .65);
                    break;
                case RockShape.SplitOutcrop:
                    Add(.91, -.14, .10, 0, 0, 0,
                        -.94, -.72, -.22, -.63, -.16, .62, -.60, .91, -.91, .26);
                    Add(.69, .19, -.16, 0, 0, 0,
                        .15, -.88, .81, -.61, .94, .41, .39, .77, .12, .35);
                    break;
                case RockShape.Cluster:
                    Add(.66, -.15, -.08, 0, 0, 0,
                        -.93, -.48, -.35, -.76, .08, -.36, -.10, .39, -.77, .50);
                    Add(.91, .12, -.13, 0, 0, 0,
                        .17, -.27, .74, -.34, .94, .25, .56, .87, .07, .49);
                    Add(.43, -.06, .15, 0, 0, 0,
                        -.16, -.94, .43, -.82, .59, -.34, .03, -.07, -.39, -.46);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(p.ResolvedShape));
            }
            TransformPieces();
        }

        void Add(double cap, double tiltX, double tiltZ, double peakSlope, double peakX, double peakZ, params double[] coords)
        {
            var polygon = new Point[coords.Length / 2];
            int pieceIndex = pieces.Count;
            double size = .91 + Random(profile.Seed, pieceIndex, 113) * .13;
            for (int i = 0; i < polygon.Length; i++)
            {
                // Large coherent corner changes, not per-cell surface noise.
                double jitterX = (Random(profile.Seed, i + pieceIndex * 31, 71) - .5) * .14;
                double jitterZ = (Random(profile.Seed, i + pieceIndex * 31, 89) - .5) * .14;
                polygon[i] = new Point(coords[i * 2] * size + jitterX, coords[i * 2 + 1] * size + jitterZ);
            }
            pieces.Add(new Piece
            {
                Polygon = ConvexHull(polygon),
                Cap = cap * (.91 + Random(profile.Seed, pieceIndex, 47) * .12),
                TiltX = tiltX * (.8 + Random(profile.Seed, pieceIndex, 53) * .4),
                TiltZ = tiltZ * (.8 + Random(profile.Seed, pieceIndex, 59) * .4),
                PeakSlope = peakSlope,
                PeakX = peakX, PeakZ = peakZ
            });
        }

        void TransformPieces()
        {
            double angle = Random(profile.Seed, 17, 101) * Math.PI * 2;
            double co = Math.Cos(angle), si = Math.Sin(angle);
            double maxExtent = 0;
            foreach (Piece piece in pieces)
            for (int i = 0; i < piece.Polygon.Length; i++)
            {
                Point a = piece.Polygon[i];
                Point b = new Point(a.X * co - a.Z * si, a.X * si + a.Z * co);
                piece.Polygon[i] = b;
                maxExtent = Math.Max(maxExtent, Math.Max(Math.Abs(b.X), Math.Abs(b.Z)));
            }
            double scale = .94 / Math.Max(.1, maxExtent);
            // Profile dimensions are maximum bounds, not a requirement to fill
            // every axis. A spire keeps a genuinely narrow pillar/blade even in
            // a comparison that uses the same bounds for every shape family.
            if (profile.ResolvedShape == RockShape.Spire) scale *= .62;
            foreach (Piece piece in pieces)
            {
                double tx = piece.TiltX, tz = piece.TiltZ;
                piece.TiltX = tx * co - tz * si; piece.TiltZ = tx * si + tz * co;
                Point peak = new Point(piece.PeakX * co - piece.PeakZ * si, piece.PeakX * si + piece.PeakZ * co);
                piece.PeakX = peak.X * scale; piece.PeakZ = peak.Z * scale;
                double centerX = 0, centerZ = 0;
                for (int i = 0; i < piece.Polygon.Length; i++)
                {
                    Point a = piece.Polygon[i];
                    Point b = new Point(a.X * scale * profile.WidthMeters * .5, a.Z * scale * profile.DepthMeters * .5);
                    piece.Polygon[i] = b; centerX += b.X; centerZ += b.Z;
                }
                piece.Center = new Point(centerX / piece.Polygon.Length, centerZ / piece.Polygon.Length);
            }
        }

        /// <summary>Return the top height, with NaN for an absent column. A zero
        /// height is a buried-only connector, not an exposed low plateau.</summary>
        public double ColumnTop(double x, double z)
        {
            double best = double.NaN;
            double nx = x / (profile.WidthMeters * .5), nz = z / (profile.DepthMeters * .5);
            foreach (Piece piece in pieces)
            {
                double edge = InsideDistance(piece.Polygon, x, z, bevel * .38);
                if (edge < -1e-9) continue;
                double fraction = piece.Cap + nx * piece.TiltX + nz * piece.TiltZ;
                if (piece.PeakSlope > 0)
                {
                    // Oblique knife-like tip; four large planes meet off-center.
                    double px = nx - piece.PeakX, pz = nz - piece.PeakZ;
                    fraction = Math.Min(fraction, piece.Cap - piece.PeakSlope * Math.Abs(px * .72 + pz * .22));
                    fraction = Math.Min(fraction, piece.Cap - piece.PeakSlope * .61 * Math.Abs(pz - px * .18));
                }
                double h = fraction * profile.HeightMeters;
                if (bevel > 0)
                {
                    double t = Math.Max(0, Math.Min(1, edge / bevel));
                    double rounded = Math.Sqrt(Math.Max(0, 1 - (1 - t) * (1 - t)));
                    h -= Math.Min(bevel, profile.HeightMeters * .24) * (1 - rounded);
                }
                h -= Groove(x, z, piece);
                if (profile.Material == MaterialId.Sandstone || profile.Material == MaterialId.Shale)
                {
                    double spacing = profile.Material == MaterialId.Shale ? .15 : .24;
                    double amount = profile.Material == MaterialId.Shale ? .35 : .18;
                    h = h * (1 - amount) + Math.Floor(h / spacing) * spacing * amount;
                }
                h = Math.Min(profile.HeightMeters, Math.Max(.05, h));
                best = double.IsNaN(best) ? h : Math.Max(best, h);
            }
            if (RootContains(x, z))
            {
                // A subcell or absent burial receives a one-cell surface root
                // instead of detached pieces; normal burial hides the bridge.
                bool thinFootprint = Math.Min(profile.WidthMeters, profile.DepthMeters) <= .3;
                double root = profile.BuriedDepthMeters >= .05 && !thinFootprint ? 0 : .05;
                best = double.IsNaN(best) ? root : Math.Max(best, root);
            }
            return best;
        }

        double Groove(double x, double z, Piece piece)
        {
            if (profile.JointStrength == 0) return 0;
            double angle = Random(profile.Seed, 7, 157) * Math.PI * 2;
            double offset = (Random(profile.Seed, 11, 163) - .5) * Math.Min(profile.WidthMeters, profile.DepthMeters) * .35;
            double distance = Math.Abs(x * Math.Cos(angle) + z * Math.Sin(angle) - offset);
            double width = Math.Min(Math.Min(profile.WidthMeters, profile.DepthMeters) * .075, .12);
            width = Math.Max(.035, width);
            double falloff = Math.Exp(-distance * distance / (width * width));
            double habit = profile.Material == MaterialId.Limestone ? 1.2 : profile.Material == MaterialId.Stone ? 1.1 : 1;
            double depth = Math.Min(profile.HeightMeters * .32, .30 * Math.Sqrt(profile.JointStrength) * habit);
            return depth * falloff;
        }

        bool RootContains(double x, double z)
        {
            // Join all convex piece centers to the origin with physical-width
            // strips. The same strips also guarantee the one-cell mockup anchor.
            double width = Math.Max(.075, Math.Min(profile.WidthMeters, profile.DepthMeters) * .055);
            if (Math.Abs(x) > profile.WidthMeters * .5 || Math.Abs(z) > profile.DepthMeters * .5) return false;
            if (Math.Abs(x) <= .000001 && Math.Abs(z) <= .000001) return true;
            foreach (Piece piece in pieces)
            {
                // A convex polygon can rasterize a skinny tip with only a
                // diagonal neighbor. A subcell-expanded basal footprint supplies
                // cardinal root connections without fattening the exposed tip.
                if (InsideDistance(piece.Polygon, x, z, 0) >= 0) return true;
                // Use an actual Euclidean dilation. Offsetting only the polygon
                // half-planes would preserve acute miter tips, which can still
                // rasterize as isolated diagonal cells at extreme aspects.
                for (int i = 0; i < piece.Polygon.Length; i++)
                    if (SegmentDistanceSquared(x, z, piece.Polygon[i], piece.Polygon[(i + 1) % piece.Polygon.Length]) <= .075 * .075)
                        return true;
                double dx = piece.Center.X, dz = piece.Center.Z;
                double lengthSquared = dx * dx + dz * dz;
                double t = lengthSquared < 1e-10 ? 0 : Math.Max(0, Math.Min(1, (x * dx + z * dz) / lengthSquared));
                double qx = x - dx * t, qz = z - dz * t;
                if (qx * qx + qz * qz <= width * width) return true;
            }
            return false;
        }

        static double SegmentDistanceSquared(double x, double z, Point a, Point b)
        {
            double dx = b.X - a.X, dz = b.Z - a.Z;
            double length = dx * dx + dz * dz;
            double t = length < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((x - a.X) * dx + (z - a.Z) * dz) / length));
            double qx = x - a.X - t * dx, qz = z - a.Z - t * dz;
            return qx * qx + qz * qz;
        }

        static double InsideDistance(Point[] polygon, double x, double z, double smoothing)
        {
            double distance = double.PositiveInfinity;
            for (int i = 0; i < polygon.Length; i++)
            {
                Point a = polygon[i], b = polygon[(i + 1) % polygon.Length];
                double dx = b.X - a.X, dz = b.Z - a.Z;
                double side = (dx * (z - a.Z) - dz * (x - a.X)) / Math.Sqrt(dx * dx + dz * dz);
                if (double.IsPositiveInfinity(distance)) distance = side;
                else if (smoothing <= 0) distance = Math.Min(distance, side);
                else
                {
                    double h = Math.Max(smoothing - Math.Abs(distance - side), 0) / smoothing;
                    distance = Math.Min(distance, side) - h * h * smoothing * .25;
                }
            }
            return distance;
        }

        static Point[] ConvexHull(Point[] input)
        {
            Array.Sort(input, (a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Z.CompareTo(b.Z));
            var hull = new List<Point>(input.Length * 2);
            foreach (Point p in input)
            {
                while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            int lower = hull.Count;
            for (int i = input.Length - 2; i >= 0; i--)
            {
                Point p = input[i];
                while (hull.Count > lower && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            hull.RemoveAt(hull.Count - 1);
            return hull.ToArray();
        }
        static double Cross(Point a, Point b, Point c) => (b.X - a.X) * (c.Z - a.Z) - (b.Z - a.Z) * (c.X - a.X);
        static double Random(int seed, int a, int b)
        {
            unchecked
            {
                uint h = (uint)seed ^ (uint)a * 0x9e3779b9u ^ (uint)b * 0x85ebca6bu;
                h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
                return (h & 0xffffffu) / 16777215.0;
            }
        }
    }
}
