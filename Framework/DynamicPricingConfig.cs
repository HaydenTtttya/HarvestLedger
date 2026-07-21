namespace HarvestLedger.Framework;

public sealed class DynamicPricingConfig
{
    public double MinimumPriceMultiplier { get; set; } = 0.35;
    public double MaximumPriceMultiplier { get; set; } = 2.5;
    public double SaturationPoint { get; set; } = 220;
    public double MaxPenalty { get; set; } = 0.45;
    public double BaseRecovery { get; set; } = 0.06;
    public double MaxDiversityRecovery { get; set; } = 0.05;
    public double SubsidyRecovery { get; set; } = 0.02;
    public double SubsidyCropCurveScale { get; set; } = 2;
    public int SubsidyMaximumCropCount { get; set; } = 64;
    public double ExposurePenaltyCap { get; set; } = 0.10;
    public double MaxProcessingTraceBonus { get; set; } = 0.05;
    public double ApiaryRadius { get; set; } = 5;
    public double ApiaryBaseWildHoneyPrice { get; set; } = 100;
    public double ApiaryFlowerCountTarget { get; set; } = 6;
    public double ApiaryFlowerDiversityTarget { get; set; } = 3;
    public double ApiaryMaxFlowerMultiplier { get; set; } = 2.8;
    public double SeasonalBonus { get; set; } = 0.12;
    public double FriendshipBonus { get; set; } = 0.002;
    public double SkillBonus { get; set; } = 0.01;
    public Dictionary<ItemMarketCategory, double> CategoryBaseMultipliers { get; set; } = new()
    {
        [ItemMarketCategory.Seed] = 1.12,
        [ItemMarketCategory.Vegetable] = 0.95,
        [ItemMarketCategory.Fruit] = 0.90,
        [ItemMarketCategory.Flower] = 1.08,
        [ItemMarketCategory.Forage] = 1.02,
        [ItemMarketCategory.Fish] = 0.92,
        [ItemMarketCategory.AnimalProduct] = 1.00,
        [ItemMarketCategory.ArtisanGoods] = 1.00,
        [ItemMarketCategory.Cooking] = 1.05,
        [ItemMarketCategory.Mining] = 1.03,
        [ItemMarketCategory.MonsterLoot] = 1.10,
        [ItemMarketCategory.Other] = 1.00
    };
    public Dictionary<ItemMarketCategory, double> CategoryPressureWeights { get; set; } = new()
    {
        [ItemMarketCategory.Seed] = 0.7,
        [ItemMarketCategory.Vegetable] = 1.0,
        [ItemMarketCategory.Fruit] = 1.1,
        [ItemMarketCategory.Flower] = 0.8,
        [ItemMarketCategory.Fish] = 1.2,
        [ItemMarketCategory.Forage] = 0.9,
        [ItemMarketCategory.AnimalProduct] = 0.9,
        [ItemMarketCategory.ArtisanGoods] = 1.4,
        [ItemMarketCategory.Cooking] = 1.0,
        [ItemMarketCategory.Mining] = 1.0,
        [ItemMarketCategory.MonsterLoot] = 1.1,
        [ItemMarketCategory.Other] = 1.0
    };

    public HashSet<string> ExemptItemIds { get; set; } = [];

    public int GetSubsidyCropRequirement(int totalCropCount)
    {
        int maximum = Math.Clamp(this.SubsidyMaximumCropCount, 1, 500);
        if (totalCropCount <= 0)
            return 1;

        double curveScale = Math.Clamp(this.SubsidyCropCurveScale, 0.1, 10);
        int curvedRequirement = (int)Math.Ceiling(curveScale * Math.Sqrt(totalCropCount));
        return Math.Min(totalCropCount, Math.Min(maximum, curvedRequirement));
    }
}
