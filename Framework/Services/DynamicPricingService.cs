using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Objects;
using SObject = StardewValley.Object;

namespace HarvestLedger.Framework.Services;

public sealed class DynamicPricingService
{
    private readonly IMonitor Monitor;
    private readonly DynamicPricingConfig Config;
    private readonly LedgerSaveData State;

    public DynamicPricingService(IMonitor monitor, DynamicPricingConfig config, LedgerSaveData state)
    {
        this.Monitor = monitor;
        this.Config = config;
        this.State = state;
    }

    public int ApplyToPlayerInventory()
    {
        if (!Context.IsWorldReady)
            return 0;

        return this.ApplyToItems(Game1.player.Items);
    }

    public int ApplyToItems(IEnumerable<Item?> items)
    {
        int updated = 0;

        foreach (Item? item in items)
        {
            if (this.TryApplyPrice(item))
                updated++;
        }

        return updated;
    }

    public void TrackSoldItem(Item? item)
    {
        if (!this.TryGetPriceableObject(item, out SObject? obj, out string itemId))
            return;

        int stack = Math.Max(1, obj.Stack);
        this.State.MarketPressureByItemId[itemId] = this.State.MarketPressureByItemId.GetValueOrDefault(itemId) + stack;
        this.State.LifetimeSoldByItemId[itemId] = this.State.LifetimeSoldByItemId.GetValueOrDefault(itemId) + stack;
        this.State.LastDay.SoldByItemId[itemId] = this.State.LastDay.SoldByItemId.GetValueOrDefault(itemId) + stack;
        this.State.LastDay.SoldItemCount += stack;
    }

    public int EstimateSaleValue(Item? item)
    {
        if (item is not SObject obj)
            return 0;

        int stack = Math.Max(1, obj.Stack);
        int unitPrice = Math.Max(0, obj.sellToStorePrice());
        return unitPrice * stack;
    }

    public void EditObjectData(IAssetData asset)
    {
        IDictionary<string, ObjectData> data = asset.AsDictionary<string, ObjectData>().Data;
        foreach ((string rawItemId, ObjectData itemData) in data)
        {
            string qualifiedItemId = $"(O){rawItemId}";
            if (this.Config.ExemptItemIds.Contains(rawItemId) || this.Config.ExemptItemIds.Contains(qualifiedItemId))
                continue;

            int basePrice = this.GetOrTrackBasePrice(qualifiedItemId, itemData.Price);
            int nextPrice = this.CalculatePrice(rawItemId, itemData, basePrice);
            if (nextPrice > 0)
                itemData.Price = nextPrice;
        }
    }

    public void RecoverMarketDemand()
    {
        double recovery = Math.Clamp(this.Config.DailyDemandRecovery, 0, 1);
        if (recovery <= 0 || this.State.MarketPressureByItemId.Count == 0)
            return;

        foreach (string itemId in this.State.MarketPressureByItemId.Keys.ToArray())
        {
            double recovered = this.State.MarketPressureByItemId[itemId] * (1 - recovery);
            if (recovered < 0.01)
                this.State.MarketPressureByItemId.Remove(itemId);
            else
                this.State.MarketPressureByItemId[itemId] = recovered;
        }
    }

    public bool TryApplyPrice(Item? item)
    {
        if (!this.TryGetPriceableObject(item, out SObject? obj, out string itemId))
            return false;

        int basePrice = this.GetOrTrackBasePrice(itemId, obj);
        int nextPrice = this.CalculatePrice(obj, itemId, basePrice);
        if (nextPrice <= 0 || nextPrice == obj.Price)
            return false;

        obj.Price = nextPrice;
        return true;
    }

    public int CalculatePrice(SObject item, string itemId, int basePrice)
    {
        ItemMarketCategory category = ItemClassifier.GetCategory(item);
        return this.CalculatePrice(itemId, category, item.Quality, basePrice);
    }

    public int CalculatePrice(string rawItemId, ObjectData itemData, int basePrice)
    {
        ItemMarketCategory category = ItemClassifier.GetCategory(itemData);
        return this.CalculatePrice($"(O){rawItemId}", category, 0, basePrice);
    }

    private int CalculatePrice(string itemId, ItemMarketCategory category, int quality, int basePrice)
    {
        double multiplier = this.GetBaseMultiplier(category);
        multiplier += this.GetSkillInfluence(category);
        multiplier += this.GetQualityInfluence(quality);
        multiplier += this.GetSeasonalInfluence(category);
        multiplier -= this.GetMarketPressure(itemId);

        multiplier = Math.Clamp(multiplier, this.Config.MinimumPriceMultiplier, this.Config.MaximumPriceMultiplier);
        return Math.Max(1, (int)Math.Round(basePrice * multiplier, MidpointRounding.AwayFromZero));
    }

    public double GetAverageMarketPressure()
    {
        if (this.State.MarketPressureByItemId.Count == 0)
            return 0;

        double total = 0;
        foreach (double pressure in this.State.MarketPressureByItemId.Values)
            total += pressure * this.Config.SalePressurePerItem;

        return total / this.State.MarketPressureByItemId.Count;
    }

    private int GetOrTrackBasePrice(string itemId, SObject item)
    {
        return this.GetOrTrackBasePrice(itemId, item.Price);
    }

    private int GetOrTrackBasePrice(string itemId, int price)
    {
        int current = Math.Max(1, price);

        if (!this.State.BasePricesByItemId.TryGetValue(itemId, out int basePrice) || basePrice <= 0)
        {
            this.State.BasePricesByItemId[itemId] = current;
            return current;
        }

        return basePrice;
    }

    private double GetBaseMultiplier(ItemMarketCategory category)
    {
        return this.Config.CategoryBaseMultipliers.TryGetValue(category, out double multiplier)
            ? multiplier
            : 1.0;
    }

    private double GetSkillInfluence(ItemMarketCategory category)
    {
        if (!Context.IsWorldReady)
            return 0;

        int level = category switch
        {
            ItemMarketCategory.Seed or ItemMarketCategory.Vegetable or ItemMarketCategory.Fruit or ItemMarketCategory.Flower or ItemMarketCategory.ArtisanGoods => Game1.player.FarmingLevel,
            ItemMarketCategory.Forage => Game1.player.ForagingLevel,
            ItemMarketCategory.Fish => Game1.player.FishingLevel,
            ItemMarketCategory.Mining => Game1.player.MiningLevel,
            ItemMarketCategory.MonsterLoot => Game1.player.CombatLevel,
            _ => 0
        };

        return level * this.Config.SkillBonus;
    }

    private double GetQualityInfluence(int quality)
    {
        return Math.Max(0, quality) * this.Config.QualityPriceWeight;
    }

    private double GetSeasonalInfluence(ItemMarketCategory category)
    {
        if (!Context.IsWorldReady)
            return 0;

        if (Game1.IsWinter && category is ItemMarketCategory.Forage or ItemMarketCategory.Fish or ItemMarketCategory.Mining)
            return this.Config.SeasonalBonus;

        return category is ItemMarketCategory.Vegetable or ItemMarketCategory.Fruit or ItemMarketCategory.Flower
            ? this.Config.SeasonalBonus / 2
            : 0;
    }

    private double GetMarketPressure(string itemId)
    {
        double pressure = this.State.MarketPressureByItemId.GetValueOrDefault(itemId);
        return Math.Min(0.85, pressure * this.Config.SalePressurePerItem);
    }

    private bool TryGetPriceableObject(Item? item, out SObject obj, out string itemId)
    {
        obj = null!;
        itemId = "";

        if (item is not SObject candidate)
            return false;

        if (candidate.bigCraftable.Value)
            return false;

        itemId = ItemClassifier.GetStableItemId(candidate);
        if (string.IsNullOrWhiteSpace(itemId) || this.Config.ExemptItemIds.Contains(itemId))
            return false;

        obj = candidate;
        return true;
    }
}
