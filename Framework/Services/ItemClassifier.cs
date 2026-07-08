using StardewValley.GameData.Objects;
using SObject = StardewValley.Object;

namespace HarvestLedger.Framework.Services;

public static class ItemClassifier
{
    public static string GetStableItemId(SObject item)
    {
        if (!string.IsNullOrWhiteSpace(item.QualifiedItemId))
            return item.QualifiedItemId;

        if (!string.IsNullOrWhiteSpace(item.ItemId))
            return item.ItemId;

        return item.Name;
    }

    public static ItemMarketCategory GetCategory(SObject item)
    {
        string type = item.Type ?? "";
        int category = item.Category;
        return GetCategory(type, category);
    }

    public static ItemMarketCategory GetCategory(ObjectData item)
    {
        string type = item.Type ?? "";
        int category = item.Category;
        return GetCategory(type, category);
    }

    private static ItemMarketCategory GetCategory(string type, int category)
    {
        if (category == -74 || type.Contains("seed", StringComparison.OrdinalIgnoreCase))
            return ItemMarketCategory.Seed;

        if (category == -75 || type.Contains("vegetable", StringComparison.OrdinalIgnoreCase))
            return ItemMarketCategory.Vegetable;

        if (category == -79 || type.Contains("fruit", StringComparison.OrdinalIgnoreCase))
            return ItemMarketCategory.Fruit;

        if (category == -80 || type.Contains("flower", StringComparison.OrdinalIgnoreCase))
            return ItemMarketCategory.Flower;

        if (category == -81 || type.Contains("forage", StringComparison.OrdinalIgnoreCase))
            return ItemMarketCategory.Forage;

        if (category == -4 || type.Contains("fish", StringComparison.OrdinalIgnoreCase))
            return ItemMarketCategory.Fish;

        if (category == -26 || type.Contains("artisan", StringComparison.OrdinalIgnoreCase))
            return ItemMarketCategory.ArtisanGoods;

        if (category == -7 || type.Contains("cooking", StringComparison.OrdinalIgnoreCase))
            return ItemMarketCategory.Cooking;

        if (category is -2 or -12 or -15 || type.Contains("mineral", StringComparison.OrdinalIgnoreCase))
            return ItemMarketCategory.Mining;

        if (category == -28 || type.Contains("monster", StringComparison.OrdinalIgnoreCase))
            return ItemMarketCategory.MonsterLoot;

        return ItemMarketCategory.Other;
    }
}
