using System;
namespace AshenVoxel.Core.Trees
{
    /// <summary>Species are presets of one scaffold/voxelization algorithm, not separate implementations.
    /// Dimensions describe representative specimens rather than the maximum biological size.</summary>
    public static class TreePresets
    {
        static readonly TreeParameters Birch=new TreeParameters(TreeSpecies.Birch,10,.18f,2.5f,
            branchLevels:4,branchCount:20,branchAngle:51,apicalDominance:.7f,tropism:.12f,foliageDensity:.72f,leafClusterRadius:.3f,
            variation:.62f,barkColor:0xffc8c7b2,leafColor:0xff667d43,whorlSize:1,crownStart:.35f,leaderHeightFraction:1,
            branchDroop:.76f,gnarl:.2f,rootFlare:.12f,forkAngle:31,barkStyle:TreeBarkStyle.Birch);
        static readonly TreeParameters Oak=new TreeParameters(TreeSpecies.Oak,9,.52f,4,
            branchLevels:4,branchCount:10,branchAngle:71,apicalDominance:.24f,tropism:.22f,foliageDensity:.86f,leafClusterRadius:.43f,
            variation:.72f,barkColor:0xff594637,leafColor:0xff5c7338,fruitColor:0xff78552f,whorlSize:1,crownStart:.28f,leaderHeightFraction:.82f,
            branchDroop:.12f,gnarl:.73f,rootFlare:.52f,forkAngle:41,barkStyle:TreeBarkStyle.Oak);
        // Mature Sequoia sempervirens, not a maximum-height specimen or a giant sequoia.
        static readonly TreeParameters Redwood=new TreeParameters(TreeSpecies.Redwood,75,2.2f,8,
            branchLevels:3,branchCount:56,branchAngle:94,apicalDominance:.98f,tropism:.2f,foliageDensity:.87f,leafClusterRadius:.8f,
            variation:.52f,barkColor:0xff85513b,leafColor:0xff486648,foliageStyle:TreeFoliageStyle.ScaleSpray,whorlSize:1,
            crownStart:.43f,coneTaper:.64f,leaderHeightFraction:1,branchDroop:.24f,gnarl:.055f,rootFlare:.95f,forkAngle:31,barkStyle:TreeBarkStyle.Redwood,
            barkRelief:.3f,barkRidgeCount:13);
        static readonly TreeParameters Pine=new TreeParameters(TreeSpecies.Pine,14,.35f,3.2f,
            branchLevels:3,branchCount:50,branchAngle:88,apicalDominance:.94f,tropism:.18f,foliageDensity:.66f,leafClusterRadius:.48f,
            variation:.42f,barkColor:0xff6b4b34,leafColor:0xff36543b,foliageStyle:TreeFoliageStyle.NeedleFan,whorlSize:5,
            crownStart:.22f,coneTaper:1.15f,leaderHeightFraction:1,branchDroop:.18f,gnarl:.1f,rootFlare:.24f,forkAngle:40,barkStyle:TreeBarkStyle.Pine);
        public static TreeParameters Get(TreeSpecies species)
        {
            switch(species)
            {case TreeSpecies.Birch:return Birch;case TreeSpecies.Oak:return Oak;case TreeSpecies.Redwood:return Redwood;case TreeSpecies.Pine:return Pine;default:throw new ArgumentOutOfRangeException(nameof(species));}
        }
        public static TreeParameters FruitedOak=>Oak.With(fruitProbability:.4f);
    }
}
