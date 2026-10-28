using AshenVoxel.Core;
using AshenVoxel.Core.Trees;
using UnityEngine;

namespace AshenVoxel.Authoring
{
    [CreateAssetMenu(fileName = "Tree Profile", menuName = "Ashen Voxel/Tree Profile")]
    public sealed class TreeProfile : ScriptableObject
    {
        // Initial values come from the shared preset; Unity deserialization then restores saved edits.
        // Do not reset values in OnEnable or when Species changes.
        static TreeParameters Defaults => TreePresets.Get(TreeSpecies.Oak);
        [Header("Identity")]
        public TreeSpecies Species = Defaults.Species;
        public int Seed = 714103;
        [Tooltip("Mature height in meters. Age also changes the generated size.")]
        [Range(2, 120)] public float Height = Defaults.Height;
        [Range(.08f, 6)] public float TrunkRadius = Defaults.TrunkRadius;
        [Range(.6f, 35)] public float CrownRadius = Defaults.CrownRadius;
        [Tooltip("Young trees are smaller and carry fewer branches. 1 is mature.")]
        [Range(.15f, 1.25f)] public float Age = Defaults.Age;

        [Header("Shape")]
        [Range(5, 115)] public float BranchAngle = Defaults.BranchAngle;
        [Range(1, 5)] public int BranchLevels = Defaults.BranchLevels;
        [Range(1, 160)] public int BranchCount = Defaults.BranchCount;
        [Tooltip("How strongly the tree keeps a central upright leader.")]
        [Range(0, 1)] public float LeaderStrength = Defaults.ApicalDominance;
        [Tooltip("How strongly branches turn upward as they grow.")]
        [Range(-1, 1)] public float UpwardGrowth = Defaults.Tropism;
        [Range(0, 1)] public float ShapeVariation = Defaults.Variation;

        [Header("Habit")]
        [Tooltip("Branches emerging together at the same height. Useful for conifer tiers.")]
        [Range(1, 12)] public int WhorlSize = Defaults.WhorlSize;
        [Tooltip("How far the main leader reaches relative to mature height.")]
        [Range(.5f, 1)] public float LeaderHeightFraction = Defaults.LeaderHeightFraction;
        [Tooltip("Downward sweep in older branches and hanging twigs.")]
        [Range(0, 1)] public float BranchDroop = Defaults.BranchDroop;
        [Tooltip("Irregular bends in the woody structure.")]
        [Range(0, 1)] public float Gnarl = Defaults.Gnarl;
        [Tooltip("Spread and thickness around the roots and trunk foot.")]
        [Range(0, 2)] public float RootFlare = Defaults.RootFlare;
        [Tooltip("Spread between daughter branches at a fork, in degrees.")]
        [Range(5, 85)] public float ForkAngle = Defaults.ForkAngle;

        [Header("Crown")]
        [Tooltip("Fraction of tree height where the main crown begins.")]
        [Range(.05f, .85f)] public float CrownStart = Defaults.CrownStart;
        [Tooltip("How sharply the upper crown narrows. Zero keeps a broad crown.")]
        [Range(0, 3)] public float ConeTaper = Defaults.ConeTaper;

        [Header("Foliage and fruit")]
        public TreeFoliageStyle FoliageStyle = Defaults.FoliageStyle;
        [Range(0, 1)] public float FoliageDensity = Defaults.FoliageDensity;
        [Range(.06f, 3)] public float LeafClusterRadius = Defaults.LeafClusterRadius;
        [Range(0, 1)] public float FruitProbability = Defaults.FruitProbability;

        [Header("Bark and palette")]
        public TreeBarkStyle BarkStyle = Defaults.BarkStyle;
        [Tooltip("Depth of actual woody bark ridges in meters; preserved on the 10 cm material grid.")]
        [Range(0, 1.5f)] public float BarkRelief = Defaults.BarkRelief;
        [Tooltip("Number of broad longitudinal bark ridges around the trunk.")]
        [Range(3, 64)] public int BarkRidgeCount = Defaults.BarkRidgeCount;
        [ColorUsage(false)] public Color BarkColor = Unpack(Defaults.BarkColor);
        [ColorUsage(false)] public Color LeafColor = Unpack(Defaults.LeafColor);
        [ColorUsage(false)] public Color FruitColor = Unpack(Defaults.FruitColor);

        /// <summary>Species changes do not replace edits; applying a preset is always explicit.</summary>
        public TreeParameters GetParameters()
        {
            return TreePresets.Get(Species).With(height: Height, trunkRadius: TrunkRadius,
                crownRadius: CrownRadius, branchAngle: BranchAngle, branchLevels: BranchLevels,
                branchCount: BranchCount, foliageDensity: FoliageDensity,
                fruitProbability: FruitProbability, age: Age, apicalDominance: LeaderStrength,
                tropism: UpwardGrowth, leafClusterRadius: LeafClusterRadius,
                variation: ShapeVariation, barkColor: Pack(BarkColor), leafColor: Pack(LeafColor),
                fruitColor: Pack(FruitColor), whorlSize: WhorlSize, crownStart: CrownStart,
                coneTaper: ConeTaper, leaderHeightFraction: LeaderHeightFraction,
                branchDroop: BranchDroop, gnarl: Gnarl, rootFlare: RootFlare,
                forkAngle: ForkAngle, foliageStyle: FoliageStyle, barkStyle: BarkStyle,
                barkRelief: BarkRelief, barkRidgeCount: BarkRidgeCount);
        }

        public void ApplySpeciesDefaults()
        {
            TreeParameters p = TreePresets.Get(Species);
            Height = p.Height; TrunkRadius = p.TrunkRadius; CrownRadius = p.CrownRadius;
            BranchAngle = p.BranchAngle; BranchLevels = p.BranchLevels; BranchCount = p.BranchCount;
            FoliageDensity = p.FoliageDensity; FruitProbability = p.FruitProbability; Age = p.Age;
            LeaderStrength = p.ApicalDominance; UpwardGrowth = p.Tropism;
            LeafClusterRadius = p.LeafClusterRadius; ShapeVariation = p.Variation;
            WhorlSize = p.WhorlSize; CrownStart = p.CrownStart; ConeTaper = p.ConeTaper;
            LeaderHeightFraction = p.LeaderHeightFraction; BranchDroop = p.BranchDroop;
            Gnarl = p.Gnarl; RootFlare = p.RootFlare; ForkAngle = p.ForkAngle;
            FoliageStyle = p.FoliageStyle; BarkStyle = p.BarkStyle;
            BarkRelief = p.BarkRelief; BarkRidgeCount = p.BarkRidgeCount;
            BarkColor = Unpack(p.BarkColor); LeafColor = Unpack(p.LeafColor); FruitColor = Unpack(p.FruitColor);
        }

        void OnValidate()
        {
            if (!System.Enum.IsDefined(typeof(TreeSpecies), Species)) Species = Defaults.Species;
            if (!System.Enum.IsDefined(typeof(TreeFoliageStyle), FoliageStyle)) FoliageStyle = Defaults.FoliageStyle;
            if (!System.Enum.IsDefined(typeof(TreeBarkStyle), BarkStyle)) BarkStyle = Defaults.BarkStyle;
            Height = FiniteClamp(Height, 2, 120, Defaults.Height);
            TrunkRadius = FiniteClamp(TrunkRadius, .08f, Mathf.Min(6, Height * .4f), Defaults.TrunkRadius);
            CrownRadius = FiniteClamp(CrownRadius, .6f, Mathf.Min(35, Height * 2.5f), Defaults.CrownRadius);
            Age = FiniteClamp(Age, .15f, 1.25f, Defaults.Age);
            BranchAngle = FiniteClamp(BranchAngle, 5, 115, Defaults.BranchAngle);
            BranchLevels = Mathf.Clamp(BranchLevels, 1, 5); BranchCount = Mathf.Clamp(BranchCount, 1, 160);
            FoliageDensity = FiniteClamp(FoliageDensity, 0, 1, Defaults.FoliageDensity);
            LeafClusterRadius = FiniteClamp(LeafClusterRadius, .06f, 3, Defaults.LeafClusterRadius);
            FruitProbability = FiniteClamp(FruitProbability, 0, 1, Defaults.FruitProbability);
            LeaderStrength = FiniteClamp(LeaderStrength, 0, 1, Defaults.ApicalDominance);
            UpwardGrowth = FiniteClamp(UpwardGrowth, -1, 1, Defaults.Tropism);
            ShapeVariation = FiniteClamp(ShapeVariation, 0, 1, Defaults.Variation);
            WhorlSize = Mathf.Clamp(WhorlSize, 1, 12);
            CrownStart = FiniteClamp(CrownStart, .05f, .85f, Defaults.CrownStart);
            ConeTaper = FiniteClamp(ConeTaper, 0, 3, Defaults.ConeTaper);
            LeaderHeightFraction = FiniteClamp(LeaderHeightFraction, .5f, 1, Defaults.LeaderHeightFraction);
            BranchDroop = FiniteClamp(BranchDroop, 0, 1, Defaults.BranchDroop);
            Gnarl = FiniteClamp(Gnarl, 0, 1, Defaults.Gnarl);
            RootFlare = FiniteClamp(RootFlare, 0, 2, Defaults.RootFlare);
            ForkAngle = FiniteClamp(ForkAngle, 5, 85, Defaults.ForkAngle);
            BarkRelief = FiniteClamp(BarkRelief, 0, 1.5f, Defaults.BarkRelief);
            BarkRidgeCount = Mathf.Clamp(BarkRidgeCount, 3, 64);
            BarkColor.a = LeafColor.a = FruitColor.a = 1;
        }
        static float FiniteClamp(float value, float min, float max, float fallback)
            => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);
        static uint Pack(Color color)
        {
            Color32 c = color; return VoxelColor.Pack(c.r, c.g, c.b);
        }
        static Color Unpack(uint color) => new Color32((byte)(color >> 16), (byte)(color >> 8), (byte)color, 255);
    }
}
