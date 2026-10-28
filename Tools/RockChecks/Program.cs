using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AshenVoxel.Core;
using AshenVoxel.Core.Rocks;

static class Program
{
    static int checks;
    static void Check(bool condition, string name)
    { if (!condition) throw new Exception("FAIL " + name); checks++; Console.WriteLine("PASS " + name); }
    static void Reject(Action action, string name)
    { bool rejected = false; try { action(); } catch (ArgumentException) { rejected = true; } Check(rejected, name); }
    static VoxelWorld World() => new VoxelWorld(72, 48, 72, .1f, 123);
    static readonly MaterialId[] Rocks = { MaterialId.Stone, MaterialId.Granite, MaterialId.Limestone, MaterialId.Sandstone, MaterialId.Shale };
    static ulong Hash(VoxelWorld world)
    {
        ulong h = 14695981039346656037UL;
        unchecked
        {
            for (int z = 0; z < world.SizeZ; z++) for (int y = 0; y < world.SizeY; y++) for (int x = 0; x < world.SizeX; x++)
            { h = (h ^ (byte)world.Get(x, y, z)) * 1099511628211UL; h = (h ^ world.GetTint(x, y, z)) * 1099511628211UL; }
        }
        return h;
    }
    static HashSet<Int3> Matter(VoxelWorld world)
    {
        var cells = new HashSet<Int3>();
        foreach (var pair in world.Chunks)
        {
            ReadOnlySpan<MaterialId> values = pair.Value.Cells;
            for (int i = 0; i < values.Length; i++) if (values[i] != MaterialId.Air)
            { Int3 local = Chunk.Decode(i); cells.Add(new Int3(pair.Key.x * 16 + local.x, pair.Key.y * 16 + local.y, pair.Key.z * 16 + local.z)); }
        }
        return cells;
    }
    static int Components(HashSet<Int3> cells)
    {
        var left = new HashSet<Int3>(cells); int components = 0;
        var queue = new Queue<Int3>();
        var dirs = new[] { new Int3(1, 0, 0), new Int3(-1, 0, 0), new Int3(0, 1, 0), new Int3(0, -1, 0), new Int3(0, 0, 1), new Int3(0, 0, -1) };
        while (left.Count > 0)
        {
            Int3 first = default; foreach (Int3 p in left) { first = p; break; }
            left.Remove(first); queue.Enqueue(first); components++;
            while (queue.Count > 0) { Int3 p = queue.Dequeue(); foreach (Int3 d in dirs) if (left.Remove(p + d)) queue.Enqueue(p + d); }
        }
        return components;
    }
    static HashSet<Int3> Footprint(VoxelWorld world, int fromY)
    {
        var result = new HashSet<Int3>();
        foreach (Int3 p in Matter(world)) if (p.y >= fromY) result.Add(new Int3(p.x, 0, p.z));
        return result;
    }
    static double ConvexFill(HashSet<Int3> cells)
    {
        var points = new List<(int x, int z)>();
        foreach (Int3 p in cells) { points.Add((p.x, p.z)); points.Add((p.x + 1, p.z)); points.Add((p.x, p.z + 1)); points.Add((p.x + 1, p.z + 1)); }
        points.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.z.CompareTo(b.z));
        var hull = new List<(int x, int z)>();
        long Turn((int x, int z) a, (int x, int z) b, (int x, int z) c) => (long)(b.x - a.x) * (c.z - a.z) - (long)(b.z - a.z) * (c.x - a.x);
        foreach (var p in points) { while (hull.Count >= 2 && Turn(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0) hull.RemoveAt(hull.Count - 1); hull.Add(p); }
        int k = hull.Count;
        for (int i = points.Count - 2; i >= 0; i--) { var p = points[i]; while (hull.Count > k && Turn(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0) hull.RemoveAt(hull.Count - 1); hull.Add(p); }
        if (hull.Count < 4) return 1;
        double twiceArea = 0;
        for (int i = 1; i < hull.Count; i++) twiceArea += (double)hull[i - 1].x * hull[i].z - (double)hull[i].x * hull[i - 1].z;
        return cells.Count / (Math.Abs(twiceArea) * .5);
    }
    static double CentralSymmetry(HashSet<Int3> cells)
    {
        int minX = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxZ = int.MinValue;
        foreach (Int3 p in cells) { minX = Math.Min(minX, p.x); minZ = Math.Min(minZ, p.z); maxX = Math.Max(maxX, p.x); maxZ = Math.Max(maxZ, p.z); }
        int matches = 0;
        foreach (Int3 p in cells) if (cells.Contains(new Int3(minX + maxX - p.x, 0, minZ + maxZ - p.z))) matches++;
        return (double)matches / cells.Count;
    }
    static double MaximumSpan(HashSet<Int3> cells)
    {
        int minX = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxZ = int.MinValue;
        foreach (Int3 p in cells) { minX = Math.Min(minX, p.x); minZ = Math.Min(minZ, p.z); maxX = Math.Max(maxX, p.x); maxZ = Math.Max(maxZ, p.z); }
        return Math.Max(maxX - minX + 1, maxZ - minZ + 1) * .1;
    }
    static double CapPlanarity(VoxelWorld world, int ground)
    {
        var footprint = Footprint(world, ground); var values = new List<(double x, double z, double y)>();
        foreach (Int3 p in footprint)
        {
            if (!footprint.Contains(p + new Int3(2, 0, 0)) || !footprint.Contains(p + new Int3(-2, 0, 0))
                || !footprint.Contains(p + new Int3(0, 0, 2)) || !footprint.Contains(p + new Int3(0, 0, -2))) continue;
            int top = ground; for (int y = ground; y < world.SizeY; y++) if (world.Get(p.x, y, p.z) != MaterialId.Air) top = y;
            values.Add((p.x, p.z, top));
        }
        double mx = 0, mz = 0, my = 0;
        foreach (var v in values) { mx += v.x; mz += v.z; my += v.y; }
        mx /= values.Count; mz /= values.Count; my /= values.Count;
        double xx = 0, zz = 0, xz = 0, xy = 0, zy = 0, yy = 0;
        foreach (var v in values) { double x = v.x - mx, z = v.z - mz, y = v.y - my; xx += x*x; zz += z*z; xz += x*z; xy += x*y; zy += z*y; yy += y*y; }
        double determinant = xx * zz - xz * xz;
        if (values.Count < 8 || determinant < .001 || yy < .001) return 0;
        double sx = (xy * zz - zy * xz) / determinant, sz = (zy * xx - xy * xz) / determinant, residual = 0;
        foreach (var v in values) { double difference = v.y - my - sx * (v.x - mx) - sz * (v.z - mz); residual += difference * difference; }
        return 1 - residual / yy;
    }
    static void Main(string[] args)
    {
        var specimens = new List<object>();
        var morphology = new List<object>();
        foreach (MaterialId material in Rocks)
        {
            var p = RockPresets.For(material, 714103); var a = World(); var b = World();
            RockResult ra = RockGenerator.Generate(a, new Int3(36, 5, 36), p);
            RockResult rb = RockGenerator.Generate(b, new Int3(36, 5, 36), p);
            var cells = Matter(a); var colors = new HashSet<uint>();
            bool materialCorrect = true, boundsCorrect = true, hasAbove = false, hasBelow = false;
            foreach (Int3 c in cells)
            {
                colors.Add(a.GetTint(c)); materialCorrect &= a.Get(c) == material;
                boundsCorrect &= Math.Abs(c.x - 36) * .1 <= p.WidthMeters * .5 + 1e-9
                    && Math.Abs(c.z - 36) * .1 <= p.DepthMeters * .5 + 1e-9
                    && (c.y - 5 + .5) * .1 <= p.HeightMeters + 1e-9
                    && (c.y - 5 + .5) * .1 >= -p.BuriedDepthMeters - 1e-9;
                hasAbove |= c.y >= 5; hasBelow |= c.y < 5;
            }
            Check(cells.Count > 100 && !ra.Truncated && ra.WrittenCells == cells.Count, material + " specimen emits hundreds of real cells with exact accounting");
            Check(Hash(a) == Hash(b) && ra.CandidateCount == rb.CandidateCount, material + " seed repeats exact matter and intrinsic pigment");
            Check(materialCorrect && colors.Count > 1 && colors.Count <= 6, material + " uses one shared rock material and at most six opaque colors");
            Check(boundsCorrect && hasAbove && hasBelow && ra.HasMatter, material + " obeys meter dimensions and has a buried attachment root");
            Check(Components(cells) == 1, material + " forms one six-connected mass without detached weathering confetti");
            bool varied = false, connected = true;
            for (int seed = 1; seed <= 5; seed++)
            {
                var c = World(); RockGenerator.Generate(c, new Int3(36, 5, 36), p.With(seed: seed));
                varied |= Hash(c) != Hash(a); connected &= Components(Matter(c)) == 1;
            }
            Check(varied && connected, material + " five alternative seeds vary while retaining connected matter");
            specimens.Add(new { material = material.ToString(), cells = cells.Count, palette_colors = colors.Count,
                candidate_count = ra.CandidateCount, column_evaluations = ra.ColumnEvaluations, identity = p.Identity });
        }
        var normal = RockPresets.For(MaterialId.Granite);
        var limitedCandidates = World(); RockResult rc = RockGenerator.Generate(limitedCandidates, new Int3(36, 5, 36), normal.With(maxCandidates: 17));
        Check(rc.Truncated && rc.CandidateCount == 17 && rc.WrittenCells <= 17, "candidate budget is strict and reports truncated output");
        var limitedWrites = World(); RockResult rw = RockGenerator.Generate(limitedWrites, new Int3(36, 5, 36), normal.With(maxWrittenCells: 13));
        Check(rw.Truncated && rw.WrittenCells == 13 && Matter(limitedWrites).Count == 13, "write budget is strict and never silently exceeds material output cap");
        var prefix = World(); RockResult rp = RockGenerator.Generate(prefix, new Int3(36, 5, 36), normal.With(maxWrittenCells: 13));
        Check(Hash(prefix) == Hash(limitedWrites) && rp.CandidateCount == rw.CandidateCount, "same bounded generation repeats the identical deterministic prefix");
        var occupied = World();
        for (int z = 20; z < 53; z++) for (int x = 20; x < 53; x++) for (int y = 0; y < 8; y++)
            occupied.Set(x, y, z, MaterialId.Wood, VoxelColor.Pack(11, 22, 33));
        RockResult ro = RockGenerator.Generate(occupied, new Int3(36, 5, 36), normal);
        bool preserved = true;
        for (int z = 20; z < 53; z++) for (int x = 20; x < 53; x++) for (int y = 0; y < 8; y++)
            preserved &= occupied.Get(x, y, z) == MaterialId.Wood && occupied.GetTint(x, y, z) == VoxelColor.Pack(11, 22, 33);
        Check(preserved && ro.BlockedCells > 0 && ro.WrittenCells > 0, "default placement preserves existing material and tint while placing exposed rock");
        RockResult overwrite = RockGenerator.Generate(occupied, new Int3(36, 5, 36), normal.With(overwriteExisting: true));
        Check(overwrite.WrittenCells > 0 && occupied.Get(36, 4, 36) == MaterialId.Granite, "replacement requires an explicit overwrite profile");
        RockResult unchanged = RockGenerator.Generate(occupied, new Int3(36, 5, 36), normal.With(overwriteExisting: true));
        Check(unchanged.WrittenCells == 0 && unchanged.UnchangedCells > 0 && unchanged.HasMatter, "identical explicit regeneration preserves revisions for unchanged cells");
        var edge = World(); RockResult re = RockGenerator.Generate(edge, new Int3(-2, 0, 36), normal);
        Check(re.ClippedCells > 0 && re.WrittenCells > 0 && !re.Truncated && Matter(edge).Count == re.WrittenCells, "negative center and bottom clipping are explicit and bounded");
        var far = World(); RockResult rf = RockGenerator.Generate(far, new Int3(int.MaxValue, int.MinValue, 0), normal);
        Check(rf.WrittenCells == 0 && rf.ClippedCells == rf.ShapeCells && !rf.Truncated, "extreme signed origins clip safely without integer overflow");
        Check(normal.With(seed: 7).Identity != normal.Identity && normal.With(weathering: .1).Identity != normal.Identity
            && normal.With(jointStrength: .9).Identity != normal.Identity, "identity includes version, seed and all shape controls");
        var smooth = World(); var jointed = World();
        RockGenerator.Generate(smooth, new Int3(36, 5, 36), normal.With(jointStrength: 0, weathering: 0));
        RockGenerator.Generate(jointed, new Int3(36, 5, 36), normal.With(jointStrength: 1, weathering: 1));
        Check(Hash(smooth) != Hash(jointed), "weathering and joint controls change actual cell occupancy");
        var noJoint = World();
        RockGenerator.Generate(noJoint, new Int3(36, 5, 36), normal.With(jointStrength: 0, weathering: 1));
        int maximumCutCells = 0;
        for (int z = 0; z < noJoint.SizeZ; z++) for (int x = 0; x < noJoint.SizeX; x++)
        {
            int plainTop = -1, cutTop = -1;
            for (int y = 0; y < noJoint.SizeY; y++)
            {
                if (noJoint.Get(x, y, z) != MaterialId.Air) plainTop = y;
                if (jointed.Get(x, y, z) != MaterialId.Air) cutTop = y;
            }
            maximumCutCells = Math.Max(maximumCutCells, plainTop - cutTop);
        }
        Check(maximumCutCells >= 2, "normal-size weathered joints alter at least two occupied height cells rather than subvoxel-only detail");
        var habits = new List<HashSet<Int3>>(); bool distinctHabits = true;
        foreach (MaterialId material in Rocks)
        {
            var habit = World();
            RockGenerator.Generate(habit, new Int3(36, 5, 36), new RockProfile(material, 714103, 2, 1.2, 1.8, .2, .7, .65));
            var set = Matter(habit); foreach (var prior in habits) distinctHabits &= !set.SetEquals(prior); habits.Add(set);
        }
        Check(distinctHabits, "five lithologies have distinct geometry even with identical dimensions and seed");
        bool tinySurvives = true;
        foreach (MaterialId material in Rocks)
        {
            var tiny = World();
            RockResult t = RockGenerator.Generate(tiny, new Int3(36, 5, 36), new RockProfile(material, 1, .1, .1, .1, 0));
            tinySurvives &= t.WrittenCells == 1 && tiny.Get(36, 5, 36) == material;
        }
        Check(tinySurvives, "one-cell mockups survive quantization for all five rock habits");
        var plate = World();
        RockResult plateResult = RockGenerator.Generate(plate, new Int3(36, 5, 36), new RockProfile(MaterialId.Shale, 1, 1, .1, 1, 0));
        Check(plateResult.WrittenCells > 1 && Components(Matter(plate)) == 1, "minimum-height unburied shale remains a connected plate instead of silently vanishing");
        var forms = new Dictionary<RockShape, HashSet<Int3>>();
        var shapeWorlds = new Dictionary<RockShape, VoxelWorld>();
        foreach (RockShape shape in Enum.GetValues(typeof(RockShape)))
        {
            if (shape == RockShape.Auto) continue;
            var form = World();
            var profile = new RockProfile(MaterialId.Granite, 714103, 2.2, 1.5, 1.85, .2, .7, 0, shape: shape);
            RockResult generated = RockGenerator.Generate(form, new Int3(36, 5, 36), profile);
            var footprint = Footprint(form, 5); forms.Add(shape, footprint); shapeWorlds.Add(shape, form);
            Check(!generated.Truncated && Components(Matter(form)) == 1 && generated.ResolvedShape == shape,
                shape + " explicit shape is deterministic connected matter with observable resolved family");
            morphology.Add(new { shape = shape.ToString(), above_ground_columns = footprint.Count, convex_fill = ConvexFill(footprint),
                central_symmetry_overlap = CentralSymmetry(footprint), maximum_horizontal_span_m = MaximumSpan(footprint),
                above_ground_components = Components(footprint) });
        }
        bool allDifferent = true;
        foreach (var a in forms) foreach (var b in forms) if (a.Key < b.Key) allDifferent &= !a.Value.SetEquals(b.Value);
        Check(allDifferent, "six primary silhouettes differ with the exact same material dimensions seed and color-independent comparison");
        Check(ConvexFill(forms[RockShape.Cluster]) < .86 && ConvexFill(forms[RockShape.SplitOutcrop]) < .82,
            "cluster and split footprints have substantial measured concavity rather than elliptical outlines");
        Check(CentralSymmetry(forms[RockShape.AngularBoulder]) < .9 && CentralSymmetry(forms[RockShape.Wedge]) < .9,
            "angular and wedge footprints break central symmetry instead of retaining a circular outline");
        Check(Components(forms[RockShape.SplitOutcrop]) >= 2 && Components(Matter(shapeWorlds[RockShape.SplitOutcrop])) == 1,
            "split outcrop has an open above-ground cleft and a connected buried root");
        Check(MaximumSpan(forms[RockShape.Spire]) < MaximumSpan(forms[RockShape.AngularBoulder]) * .8,
            "spire uses intrinsically narrower bounds instead of filling a boulder-width circle");
        double wedgePlanarity = CapPlanarity(shapeWorlds[RockShape.Wedge], 5);
        Check(wedgePlanarity > .85, "wedge interior is a measurable large inclined plane rather than a rounded dome");
        bool familyVaries = true, stableFamily = true;
        foreach (MaterialId material in Rocks)
        {
            var families = new HashSet<RockShape>();
            for (int seed = 0; seed < 32; seed++)
            {
                RockProfile p = RockPresets.For(material, seed); families.Add(p.ResolvedShape);
                stableFamily &= p.With(widthMeters: .42, heightMeters: .7).ResolvedShape == p.ResolvedShape;
            }
            familyVaries &= families.Count >= 4;
        }
        Check(familyVaries && stableFamily, "Auto varies material-biased shape families by seed while resizing preserves the resolved family");
        Check(normal.With(shape: RockShape.Wedge).Identity != normal.Identity
            && normal.With(shape: RockShape.Wedge).With(seed: 3).ResolvedShape == RockShape.Wedge,
            "explicit shape override is validated persistent authoring input and part of identity");
        bool skinnySpireConnected = true;
        foreach (MaterialId material in Rocks)
        {
            var tinySpire = World();
            RockGenerator.Generate(tinySpire, new Int3(36, 5, 36), new RockProfile(material, 9, .42, .2352, .3276, .2, shape: RockShape.Spire));
            skinnySpireConnected &= Components(Matter(tinySpire)) == 1;
        }
        Check(skinnySpireConnected, "reviewer seed9 skinny spire retains cardinal root connections for every lithology");
        bool allConnected = true, allBounded = true, allTinyVisible = true; int shapeCases = 0;
        foreach (RockShape shape in Enum.GetValues(typeof(RockShape)))
        {
            if (shape == RockShape.Auto) continue;
            for (int seed = 0; seed < 8; seed++)
            foreach (var dims in new[] { (.42, .2352, .3276, .2), (.9, .5, .7, 0.0), (1.97, 1.45, 1.6, .04), (3.4, 2.1, 1.3, .2), (.1, .1, .1, .2) })
            {
                var test = World(); var p = new RockProfile(MaterialId.Granite, seed, dims.Item1, dims.Item2, dims.Item3, dims.Item4, shape: shape);
                RockResult r = RockGenerator.Generate(test, new Int3(36, 5, 36), p); var cells = Matter(test); shapeCases++;
                int components = Components(cells), above = Footprint(test, 5).Count;
                if (cells.Count == 0 || components != 1 || above == 0)
                    Console.WriteLine("SHAPE_CASE_FAIL " + shape + " seed=" + seed + " dims=" + dims + " count=" + cells.Count + " components=" + components + " visible=" + above);
                allConnected &= cells.Count > 0 && components == 1;
                allTinyVisible &= above > 0;
                allBounded &= !r.Truncated && r.CandidateCount <= p.MaxCandidates && r.WrittenCells <= p.MaxWrittenCells;
                foreach (Int3 c in cells) allBounded &= Math.Abs(c.x - 36) * .1 <= p.WidthMeters * .5 + 1e-9
                    && Math.Abs(c.z - 36) * .1 <= p.DepthMeters * .5 + 1e-9;
            }
        }
        Check(allConnected && allBounded && allTinyVisible, "240 shape/seed/aspect/burial combinations stay connected visible and within physical bounds");
        bool extremeShapesValid = true; int extremeShapeCases = 0;
        foreach (RockShape shape in Enum.GetValues(typeof(RockShape)))
        {
            if (shape == RockShape.Auto) continue;
            for (int seed = 0; seed < 4; seed++)
            foreach (var dims in new[] { (12.0, .1, .1), (.1, .1, 12.0), (.1, .5, .42), (.42, .5, .1), (12.0, .1, 12.0) })
            foreach (double burial in new[] { .04, .2 })
            {
                var test = new VoxelWorld(144, 32, 144, .1f, 123);
                var p = new RockProfile(MaterialId.Granite, seed, dims.Item1, dims.Item2, dims.Item3, burial, shape: shape);
                RockResult r = RockGenerator.Generate(test, new Int3(72, 5, 72), p);
                var cells = Matter(test); extremeShapeCases++;
                extremeShapesValid &= cells.Count > 0 && Components(cells) == 1 && Footprint(test, 5).Count > 0
                    && !r.Truncated && r.CandidateCount <= p.MaxCandidates && r.WrittenCells <= p.MaxWrittenCells;
                foreach (Int3 c in cells) extremeShapesValid &= Math.Abs(c.x - 72) * .1 <= p.WidthMeters * .5 + 1e-9
                    && Math.Abs(c.z - 72) * .1 <= p.DepthMeters * .5 + 1e-9;
            }
        }
        Check(extremeShapesValid, "240 additional extreme thin-axis and12m plate cases preserve visible connected matter at burial thresholds");
        Reject(() => new RockProfile(MaterialId.Sand), "non-rock materials reject the rock pipeline");
        Reject(() => new RockProfile(widthMeters: double.NaN), "nonfinite physical dimensions reject");
        Reject(() => new RockProfile(heightMeters: 11), "unbounded physical dimensions reject");
        Reject(() => new RockProfile(maxCandidates: 0), "zero candidate budget rejects");
        Reject(() => new RockProfile(maxWrittenCells: 1000001), "excessive write budget rejects");
        Reject(() => new RockProfile(weathering: -1), "invalid weathering range rejects");
        Reject(() => new RockProfile(shape: (RockShape)99), "unknown shape family rejects");
        Reject(() => RockGenerator.Generate(new VoxelWorld(40, 40, 40, .05f, 1), new Int3(20, 1, 20), normal), "generator never silently changes required ten-centimeter scale");
        string result = JsonSerializer.Serialize(new { status = "PASS", checks, scope = "Actual engine-free rock generation, not physical weathering, Unity import or rendering", rock_version = RockGenerator.Version, specimens, morphology, shape_cases = shapeCases, extreme_shape_cases = extremeShapeCases, wedge_plane_fit_r_squared = wedgePlanarity }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(args[0], result + "\n");
        Console.WriteLine("All " + checks + " rock checks passed.");
    }
}
