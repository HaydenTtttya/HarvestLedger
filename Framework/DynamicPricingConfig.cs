namespace HarvestLedger.Framework;

public sealed class DynamicPricingConfig
{
    public double MinimumPriceMultiplier { get; set; } = 0.35;
    public double MaximumPriceMultiplier { get; set; } = 2.5;
    public double DailyDemandRecovery { get; set; } = 0.08;
    public double SalePressurePerItem { get; set; } = 0.015;
    public double QualityPriceWeight { get; set; } = 0.08;
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
        [ItemMarketCategory.ArtisanGoods] = 1.00,
        [ItemMarketCategory.Cooking] = 1.05,
        [ItemMarketCategory.Mining] = 1.03,
        [ItemMarketCategory.MonsterLoot] = 1.10,
        [ItemMarketCategory.Other] = 1.00
    };

    public HashSet<string> ExemptItemIds { get; set; } = [];
}
