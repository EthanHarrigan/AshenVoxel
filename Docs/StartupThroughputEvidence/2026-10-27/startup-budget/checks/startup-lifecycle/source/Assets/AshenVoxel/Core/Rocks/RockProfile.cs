using System;
using System.Globalization;

namespace AshenVoxel.Core.Rocks
{
    public enum RockShape { Auto, AngularBoulder, Slab, Wedge, Spire, SplitOutcrop, Cluster }

    /// <summary>Physical dimensions and bounded static weathering controls. This is
    /// an art-directed shape generator, not an erosion or fracture simulation.</summary>
    public sealed class RockProfile
    {
        public readonly MaterialId Material;
        public readonly int Seed, MaxCandidates, MaxWrittenCells;
        public readonly double WidthMeters, HeightMeters, DepthMeters, BuriedDepthMeters, Weathering, JointStrength;
        public readonly bool OverwriteExisting;
        public RockShape Shape { get; }
        public RockShape ResolvedShape { get; }
        public string Identity { get; }

        public RockProfile(MaterialId material = MaterialId.Granite, int seed = 714103,
            double widthMeters = 2.2, double heightMeters = 1.2, double depthMeters = 1.8,
            double buriedDepthMeters = .2, double weathering = .65, double jointStrength = .45,
            int maxCandidates = 500000, int maxWrittenCells = 100000, bool overwriteExisting = false,
            RockShape shape = RockShape.Auto)
        {
            if (!IsRock(material)) throw new ArgumentException("Only supported bedrock materials can generate a rock.", nameof(material));
            if ((int)shape < 0 || (int)shape > (int)RockShape.Cluster) throw new ArgumentOutOfRangeException(nameof(shape));
            Validate(widthMeters, .1, 12, nameof(widthMeters)); Validate(heightMeters, .1, 10, nameof(heightMeters));
            Validate(depthMeters, .1, 12, nameof(depthMeters)); Validate(buriedDepthMeters, 0, 2, nameof(buriedDepthMeters));
            Validate(weathering, 0, 1, nameof(weathering)); Validate(jointStrength, 0, 1, nameof(jointStrength));
            if (maxCandidates < 1 || maxCandidates > 4000000) throw new ArgumentOutOfRangeException(nameof(maxCandidates));
            if (maxWrittenCells < 1 || maxWrittenCells > 1000000) throw new ArgumentOutOfRangeException(nameof(maxWrittenCells));
            Material = material; Seed = seed; WidthMeters = widthMeters; HeightMeters = heightMeters;
            DepthMeters = depthMeters; BuriedDepthMeters = buriedDepthMeters; Weathering = weathering;
            JointStrength = jointStrength; MaxCandidates = maxCandidates; MaxWrittenCells = maxWrittenCells;
            OverwriteExisting = overwriteExisting;
            Shape = shape; ResolvedShape = ResolveShape(material, seed, shape);
            var inv = CultureInfo.InvariantCulture;
            Identity = "rock-v" + RockGenerator.Version + "/catalog-v" + MaterialCatalog.Version + "/" + ((byte)material).ToString(inv)
                + "/" + seed.ToString(inv) + "/" + widthMeters.ToString("R", inv) + "/" + heightMeters.ToString("R", inv)
                + "/" + depthMeters.ToString("R", inv) + "/" + buriedDepthMeters.ToString("R", inv)
                + "/" + weathering.ToString("R", inv) + "/" + jointStrength.ToString("R", inv)
                + "/" + maxCandidates.ToString(inv) + "/" + maxWrittenCells.ToString(inv) + "/" + overwriteExisting
                + "/shape=" + Shape + "/resolved=" + ResolvedShape + "/appearance-v" + RockGenerator.AppearanceVersion;
        }

        public RockProfile With(MaterialId? material = null, int? seed = null, double? widthMeters = null,
            double? heightMeters = null, double? depthMeters = null, double? buriedDepthMeters = null,
            double? weathering = null, double? jointStrength = null, int? maxCandidates = null,
            int? maxWrittenCells = null, bool? overwriteExisting = null, RockShape? shape = null)
            => new RockProfile(material ?? Material, seed ?? Seed, widthMeters ?? WidthMeters,
                heightMeters ?? HeightMeters, depthMeters ?? DepthMeters, buriedDepthMeters ?? BuriedDepthMeters,
                weathering ?? Weathering, jointStrength ?? JointStrength, maxCandidates ?? MaxCandidates,
                maxWrittenCells ?? MaxWrittenCells, overwriteExisting ?? OverwriteExisting, shape ?? Shape);

        static RockShape ResolveShape(MaterialId material, int seed, RockShape requested)
        {
            if (requested != RockShape.Auto) return requested;
            uint h;
            unchecked { h = (uint)seed; h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16; }
            int choice = (int)(h % 6);
            switch (material)
            {
                case MaterialId.Stone:
                    switch (choice) { case 0: return RockShape.Spire; case 1: return RockShape.AngularBoulder; case 2: return RockShape.SplitOutcrop; case 3: return RockShape.Cluster; case 4: return RockShape.Wedge; default: return RockShape.Spire; }
                case MaterialId.Limestone:
                    switch (choice) { case 0: return RockShape.SplitOutcrop; case 1: return RockShape.Spire; case 2: return RockShape.AngularBoulder; case 3: return RockShape.Cluster; case 4: return RockShape.Wedge; default: return RockShape.Slab; }
                case MaterialId.Sandstone:
                    switch (choice) { case 0: return RockShape.Slab; case 1: return RockShape.Wedge; case 2: return RockShape.SplitOutcrop; case 3: return RockShape.Spire; case 4: return RockShape.AngularBoulder; default: return RockShape.Slab; }
                case MaterialId.Shale:
                    switch (choice) { case 0: return RockShape.Wedge; case 1: return RockShape.Slab; case 2: return RockShape.Cluster; case 3: return RockShape.SplitOutcrop; case 4: return RockShape.Slab; default: return RockShape.AngularBoulder; }
                default:
                    switch (choice) { case 0: return RockShape.AngularBoulder; case 1: return RockShape.Cluster; case 2: return RockShape.SplitOutcrop; case 3: return RockShape.Wedge; case 4: return RockShape.Slab; default: return RockShape.AngularBoulder; }
            }
        }

        public static bool IsRock(MaterialId material) => material == MaterialId.Stone || material == MaterialId.Granite
            || material == MaterialId.Limestone || material == MaterialId.Sandstone || material == MaterialId.Shale;
        static void Validate(double v, double min, double max, string name)
        { if (double.IsNaN(v) || double.IsInfinity(v) || v < min || v > max) throw new ArgumentOutOfRangeException(name); }
    }

    public static class RockPresets
    {
        public static RockProfile For(MaterialId material, int seed = 714103)
        {
            switch (material)
            {
                case MaterialId.Stone: return new RockProfile(material, seed, 1.8, 1.25, 1.65, .2, .4, .7);
                case MaterialId.Granite: return new RockProfile(material, seed, 2.2, 1.3, 1.85, .2, .82, .3);
                case MaterialId.Limestone: return new RockProfile(material, seed, 2.0, 1.15, 1.75, .2, .72, .65);
                case MaterialId.Sandstone: return new RockProfile(material, seed, 2.2, 1.0, 1.8, .2, .7, .55);
                case MaterialId.Shale: return new RockProfile(material, seed, 2.15, .72, 1.8, .2, .5, .72);
                default: throw new ArgumentException("Unsupported rock preset.", nameof(material));
            }
        }
    }

    public sealed class RockResult
    {
        public RockShape ResolvedShape { get; internal set; }
        public int CandidateCount { get; internal set; }
        public int ColumnEvaluations { get; internal set; }
        public int ShapeCells { get; internal set; }
        public int WrittenCells { get; internal set; }
        public int ClippedCells { get; internal set; }
        public int BlockedCells { get; internal set; }
        public int UnchangedCells { get; internal set; }
        public bool Truncated { get; internal set; }
        public bool HasMatter { get; internal set; }
        /// <summary>Actual cells written/retained by this generation, inclusive. Undefined when HasMatter is false.</summary>
        public Int3 BoundsMinCellInclusive { get; internal set; }
        public Int3 BoundsMaxCellInclusive { get; internal set; }
    }
}
