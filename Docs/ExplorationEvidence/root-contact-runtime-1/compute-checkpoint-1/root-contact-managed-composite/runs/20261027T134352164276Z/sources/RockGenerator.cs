using System;

namespace AshenVoxel.Core.Rocks
{
    /// <summary>Deterministic bounded rock/outcrop rasterization on the required
    /// 10 cm grid. Irregular polygonal pieces supply six silhouette families;
    /// columns share buried roots and restrained six-color material palettes.</summary>
    public static class RockGenerator
    {
        public const int Version = 2;
        // Pigment changes are independent of the unchanged polygonal shape contract.
        public const int AppearanceVersion = 2;
        const double Cell = .1;

        /// <param name="groundCell">First exposed cell at the rock's horizontal
        /// center. Its lower face is the nominal soil surface. BuriedDepthMeters
        /// extends the rock below that surface; existing terrain is retained by
        /// default. Bounds in the result include only written/unchanged own matter.</param>
        public static RockResult Generate(VoxelWorld world, Int3 groundCell, RockProfile profile)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (Math.Abs(world.VoxelSize - Cell) > 0.000001)
                throw new ArgumentException("Rock matter uses the required 0.1 meter voxel grid.", nameof(world));

            var result = new RockResult { ResolvedShape = profile.ResolvedShape };
            var geometry = new RockShapeGeometry(profile);
            uint[] palette = BuildPalette(profile.Material);
            int radiusX = (int)Math.Ceiling(profile.WidthMeters / (Cell * 2));
            int radiusZ = (int)Math.Ceiling(profile.DepthMeters / (Cell * 2));
            int bottom = -(int)Math.Ceiling(profile.BuriedDepthMeters / Cell);
            for (int iz = -radiusZ; iz <= radiusZ; iz++)
            for (int ix = -radiusX; ix <= radiusX; ix++)
            {
                result.ColumnEvaluations++;
                double dx = ix * Cell, dz = iz * Cell;
                double cap = geometry.ColumnTop(dx, dz);
                if (double.IsNaN(cap)) continue;
                int top = (int)Math.Ceiling(cap / Cell) - 1;
                for (int iy = bottom; iy <= top; iy++)
                {
                    if (result.CandidateCount >= profile.MaxCandidates)
                    { result.Truncated = true; return result; }
                    result.CandidateCount++;
                    double height = (iy + .5) * Cell;
                    if (height < -profile.BuriedDepthMeters || height > cap) continue;
                    result.ShapeCells++;
                    long wideX = (long)groundCell.x + ix, wideY = (long)groundCell.y + iy, wideZ = (long)groundCell.z + iz;
                    if (wideX < 0 || wideY < 0 || wideZ < 0 || wideX >= world.SizeX || wideY >= world.SizeY || wideZ >= world.SizeZ)
                    { result.ClippedCells++; continue; }
                    int x = (int)wideX, y = (int)wideY, z = (int)wideZ;
                    MaterialId old = world.Get(x, y, z);
                    if (!profile.OverwriteExisting && old != MaterialId.Air)
                    { result.BlockedCells++; continue; }
                    uint tint = ColorAt(dx, height, dz, profile, palette);
                    if (old == profile.Material && world.GetTint(x, y, z) == tint)
                    { result.UnchangedCells++; Include(result, x, y, z); continue; }
                    if (result.WrittenCells >= profile.MaxWrittenCells)
                    { result.Truncated = true; return result; }
                    if (world.Set(x, y, z, profile.Material, tint))
                    { result.WrittenCells++; Include(result, x, y, z); }
                }
            }
            return result;
        }

        static void Include(RockResult r, int x, int y, int z)
        {
            if (!r.HasMatter)
            { r.HasMatter = true; r.BoundsMinCellInclusive = r.BoundsMaxCellInclusive = new Int3(x, y, z); return; }
            Int3 a = r.BoundsMinCellInclusive, b = r.BoundsMaxCellInclusive;
            r.BoundsMinCellInclusive = new Int3(Math.Min(a.x, x), Math.Min(a.y, y), Math.Min(a.z, z));
            r.BoundsMaxCellInclusive = new Int3(Math.Max(b.x, x), Math.Max(b.y, y), Math.Max(b.z, z));
        }

        static uint[] BuildPalette(MaterialId material)
        {
            uint baseColor = VoxelColor.Base(material);
            int r = (int)((baseColor >> 16) & 255), g = (int)((baseColor >> 8) & 255), b = (int)(baseColor & 255);
            // Six restrained opaque colors per individual rock, all the same
            // material. No geometry/material confetti and no baked lighting.
            var shades = new uint[6];
            for (int i = 0; i < shades.Length; i++)
            {
                // A10cm cell represents many small mineral grains. Keep intrinsic
                // contrast modest and let the normal, sky, shadow and AO do the shading.
                double brightness = .94 + i * .024;
                double warm = (i - 2.5) * (material == MaterialId.Sandstone ? .8 : material == MaterialId.Granite ? .6 : .4);
                shades[i] = VoxelColor.Pack(Byte(r * brightness + warm), Byte(g * brightness + warm * .12), Byte(b * brightness - warm));
            }
            return shades;
        }

        static uint ColorAt(double x, double y, double z, RockProfile p, uint[] palette)
        {
            double broad = Noise(x * .55 + 8.7, y * .55, z * .55 - 2.4, p.Seed + 407);
            double detail = Noise(x * 1.5, y * 1.5, z * 1.5, p.Seed + 811);
            double value = broad * .94 + detail * .06;
            if (p.Material == MaterialId.Sandstone || p.Material == MaterialId.Shale)
            {
                double warp = (Noise(x * .7, 0, z * .7, p.Seed + 91) - .5) * .22;
                double spacing = p.Material == MaterialId.Shale ? .16 : .26;
                double band = .5 + Math.Sin((y + warp) / spacing * Math.PI * 2) * .5;
                value = broad * .70 + band * .27 + detail * .03;
            }
            if (p.Material == MaterialId.Limestone)
                value = broad * .82 + Noise(x * .4, y * 1.2, z * .4, p.Seed + 119) * .18;
            int index = Math.Max(0, Math.Min(5, (int)(value * 6)));
            return palette[index];
        }

        static byte Byte(double x) => (byte)Math.Max(0, Math.Min(255, (int)Math.Round(x)));
        static double Fade(double x) => x * x * (3 - 2 * x);
        static double Lerp(double a, double b, double t) => a + (b - a) * t;
        static double Noise(double x, double y, double z, int seed)
        {
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y), iz = (int)Math.Floor(z);
            double tx = Fade(x - ix), ty = Fade(y - iy), tz = Fade(z - iz);
            double a = Lerp(Unit(seed, ix, iy, iz), Unit(seed, ix + 1, iy, iz), tx);
            double b = Lerp(Unit(seed, ix, iy + 1, iz), Unit(seed, ix + 1, iy + 1, iz), tx);
            double c = Lerp(Unit(seed, ix, iy, iz + 1), Unit(seed, ix + 1, iy, iz + 1), tx);
            double d = Lerp(Unit(seed, ix, iy + 1, iz + 1), Unit(seed, ix + 1, iy + 1, iz + 1), tx);
            return Lerp(Lerp(a, b, ty), Lerp(c, d, ty), tz);
        }
        static double Unit(int seed, int x, int y, int z)
        {
            unchecked
            {
                uint h = (uint)seed ^ (uint)x * 0x9e3779b9u ^ (uint)y * 0x85ebca6bu ^ (uint)z * 0xc2b2ae35u;
                h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
                return (h & 0xffffffu) / 16777215.0;
            }
        }
    }
}
