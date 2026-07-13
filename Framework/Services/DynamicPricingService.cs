using System.Reflection;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.GameData.Objects;
using StardewValley.TerrainFeatures;
using SObject = StardewValley.Object;

namespace HarvestLedger.Framework.Services;

public sealed class DynamicPricingService
{
    private static readonly DemandEventTemplate[] DemandEventTemplates =
    [
        new("pelican-town-festival", "Pelican Town Festival Demand", new Dictionary<ItemMarketCategory, double>
        {
            [ItemMarketCategory.Vegetable] = 0.12,
            [ItemMarketCategory.Flower] = 0.08
        }),
        new("mines-supply-order", "Mines Supply Order", new Dictionary<ItemMarketCategory, double>
        {
            [ItemMarketCategory.Mining] = 0.15,
            [ItemMarketCategory.MonsterLoot] = 0.10
        }),
        new("fish-market-shortage", "Fish Market Shortage", new Dictionary<ItemMarketCategory, double>
        {
            [ItemMarketCategory.Fish] = 0.18,
            [ItemMarketCategory.Cooking] = 0.06
        }),
        new("pantry-restock", "Pantry Restock", new Dictionary<ItemMarketCategory, double>
        {
            [ItemMarketCategory.Fruit] = 0.10,
            [ItemMarketCategory.AnimalProduct] = 0.08
        }),
        new("artisan-fair", "Artisan Fair", new Dictionary<ItemMarketCategory, double>
        {
            [ItemMarketCategory.ArtisanGoods] = 0.14,
            [ItemMarketCategory.Flower] = 0.06
        })
    ];

    private static readonly Dictionary<string, string[]> ProcessingRawItemCandidates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["(O)303"] = ["(O)304"],
        ["(O)346"] = ["(O)262"],
        ["(O)424"] = ["(O)184", "(O)186"],
        ["(O)426"] = ["(O)436", "(O)438"],
        ["(O)306"] = ["(O)176", "(O)174", "(O)180", "(O)182"],
        ["(O)307"] = ["(O)442"],
        ["(O)308"] = ["(O)305"],
        ["(O)432"] = ["(O)430"],
        ["(O)428"] = ["(O)440"],
        ["(O)459"] = ["(O)340"]
    };

    private readonly IMonitor Monitor;
    private readonly DynamicPricingConfig Config;
    private readonly LedgerSaveData State;
    private readonly Dictionary<string, int> PriceCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> ApiaryQualityByFlowerId = new(StringComparer.OrdinalIgnoreCase);
    private string ApiaryQualityCacheDate = "";
    private bool ApiaryQualityCacheDirty = true;
    private IDictionary<string, ObjectData>? CurrentObjectData;

    public DynamicPricingService(IMonitor monitor, DynamicPricingConfig config, LedgerSaveData state)
    {
        this.Monitor = monitor;
        this.Config = config;
        this.State = state;
    }

    public bool EnsureSeasonState()
    {
        if (!Context.IsWorldReady)
            return false;

        string seasonKey = GetSeasonKey();
        bool changed = !string.Equals(this.State.CurrentSeasonKey, seasonKey, StringComparison.Ordinal);
        if (!changed && this.State.DemandEvents.Count > 0 && !string.IsNullOrWhiteSpace(this.State.SubsidizedCropItemId))
            return false;

        this.State.CurrentSeasonKey = seasonKey;
        this.State.MarketPressureByItemId.Clear();
        this.State.SeasonSoldCountByItemId.Clear();
        this.State.SeasonalProducedRawByItemId.Clear();
        this.State.ActiveRotationBonusByItemId.Clear();
        this.State.SeasonalSubsidyTaxReduction = 0;
        this.State.LastRecoveryRate = 0;
        this.ChooseSubsidizedCrop(seasonKey);
        this.GenerateDemandEvents(seasonKey);
        this.CaptureCropRotationSnapshot();
        this.PriceCache.Clear();
        this.InvalidateApiaryQualityCache();
        return true;
    }

    public string GetSeasonStartMessage()
    {
        return string.IsNullOrWhiteSpace(this.State.SubsidizedCropName)
            ? "Harvest Ledger: no subsidized crop selected."
            : $"Harvest Ledger subsidy crop: {this.State.SubsidizedCropName}";
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

    public int ApplyToLocations(IEnumerable<GameLocation> locations)
    {
        if (!Context.IsWorldReady)
            return 0;

        int updated = 0;
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        foreach (GameLocation location in locations)
        {
            if (location is null || !visited.Add(location.NameOrUniqueName))
                continue;

            updated += this.ApplyToLocationItems(location);
        }

        return updated;
    }

    public void InvalidateApiaryQualityCache()
    {
        this.ApiaryQualityCacheDirty = true;
    }

    public void TrackProducedItem(Item? item)
    {
        if (!this.TryGetPriceableObject(item, out SObject? obj, out string itemId))
            return;

        ItemMarketCategory category = ItemClassifier.GetCategory(obj);
        if (!IsTraceableRawCategory(category))
            return;

        int stack = Math.Max(1, obj.Stack);
        this.State.SeasonalProducedRawByItemId[itemId] = this.State.SeasonalProducedRawByItemId.GetValueOrDefault(itemId) + stack;
    }

    public void TrackSoldItem(Item? item, int estimatedValue)
    {
        if (!this.TryGetPriceableObject(item, out SObject? obj, out string itemId))
            return;

        ItemMarketCategory category = ItemClassifier.GetCategory(obj);
        int stack = Math.Max(1, obj.Stack);
        this.GetOrTrackBasePrice(itemId, obj);
        this.TryApplyPrice(obj);
        int unitSalePrice = Math.Max(0, obj.sellToStorePrice());
        double adjustedSold = stack * this.GetCategoryPressureWeight(category) * this.GetExposurePressureGrowth(category);

        this.State.MarketPressureByItemId[itemId] = this.State.MarketPressureByItemId.GetValueOrDefault(itemId) + adjustedSold;
        this.State.SeasonSoldCountByItemId[itemId] = this.State.SeasonSoldCountByItemId.GetValueOrDefault(itemId) + stack;
        this.State.LifetimeSoldByItemId[itemId] = this.State.LifetimeSoldByItemId.GetValueOrDefault(itemId) + stack;
        this.State.LastSoldQualityByItemId[itemId] = Math.Max(0, obj.Quality);
        this.State.LastSoldUnitPriceByItemId[itemId] = unitSalePrice;
        this.State.LastDay.SoldByItemId[itemId] = this.State.LastDay.SoldByItemId.GetValueOrDefault(itemId) + stack;
        this.State.LastDay.SoldByCategory[category] = this.State.LastDay.SoldByCategory.GetValueOrDefault(category) + stack;
        this.State.LastDay.IncomeByCategory[category] = this.State.LastDay.IncomeByCategory.GetValueOrDefault(category) + Math.Max(0, estimatedValue);
        this.State.LastDay.SoldItemCount += stack;

        if (IsTraceableRawCategory(category))
            this.State.SeasonalProducedRawByItemId[itemId] = this.State.SeasonalProducedRawByItemId.GetValueOrDefault(itemId) + stack;

        this.PriceCache.Clear();
    }

    public int EstimateSaleValue(Item? item, bool applyDynamicPricing)
    {
        if (item is not SObject obj)
            return 0;

        int stack = Math.Max(1, obj.Stack);
        if (applyDynamicPricing)
            this.TryApplyPrice(obj);
        int unitPrice = Math.Max(0, obj.sellToStorePrice());
        return unitPrice * stack;
    }

    public void EditObjectData(IAssetData asset)
    {
        IDictionary<string, ObjectData> data = asset.AsDictionary<string, ObjectData>().Data;
        IDictionary<string, ObjectData>? previousObjectData = this.CurrentObjectData;
        this.CurrentObjectData = data;

        try
        {
            IReadOnlyDictionary<string, double> apiaryQualityByFlower = this.GetApiaryQualityByFlowerId();

            foreach ((string rawItemId, ObjectData itemData) in data)
            {
                string qualifiedItemId = $"(O){rawItemId}";
                if (this.Config.ExemptItemIds.Contains(rawItemId) || this.Config.ExemptItemIds.Contains(qualifiedItemId))
                    continue;

                int basePrice = this.TryGetApiaryHoneyBasePrice(qualifiedItemId, itemData, apiaryQualityByFlower, out int honeyBasePrice)
                    ? honeyBasePrice
                    : this.GetOrTrackBasePrice(qualifiedItemId, itemData.Price);
                int nextPrice = this.CalculatePrice(rawItemId, itemData, basePrice);
                if (nextPrice > 0)
                    itemData.Price = nextPrice;
            }
        }
        finally
        {
            this.CurrentObjectData = previousObjectData;
        }
    }

    public void RecoverMarketDemand()
    {
        double baseRecovery = Math.Clamp(this.Config.BaseRecovery, 0, 1);
        double diversityRecovery = Math.Min(this.State.LastDay.DistinctSoldCategoryCount / 8.0, 1.0) * Math.Clamp(this.Config.MaxDiversityRecovery, 0, 1);
        double subsidyRecovery = this.State.LastDay.SubsidyConditionMet ? Math.Clamp(this.Config.SubsidyRecovery, 0, 1) : 0;
        double recovery = Math.Clamp(baseRecovery + diversityRecovery + subsidyRecovery, 0, 0.95);
        this.State.LastRecoveryRate = recovery;
        this.State.LastDay.DailyRecoveryRate = recovery;

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

        this.PriceCache.Clear();
    }

    public void EvaluateSeasonalSubsidy()
    {
        if (!Context.IsWorldReady || string.IsNullOrWhiteSpace(this.State.SubsidizedCropItemId))
            return;

        (int subsidizedCount, int totalCount) = this.CountFarmCrops(this.State.SubsidizedCropItemId);
        bool conditionMet = totalCount > 0 && subsidizedCount >= 25 && subsidizedCount / (double)totalCount >= 0.25;

        this.State.LastDay.SubsidizedCropCount = subsidizedCount;
        this.State.LastDay.TotalCropCount = totalCount;
        this.State.LastDay.SubsidyConditionMet = conditionMet;

        if (conditionMet)
            this.State.SeasonalSubsidyTaxReduction = Math.Clamp(this.State.SeasonalSubsidyTaxReduction + 0.01, 0, 0.28);
    }

    public void CaptureCropRotationSnapshot()
    {
        if (!Context.IsWorldReady)
            return;

        string seasonKey = GetSeasonKey();
        this.State.ActiveRotationBonusByItemId.Clear();

        foreach (CropTile cropTile in this.GetFarmCropTiles())
        {
            if (string.IsNullOrWhiteSpace(cropTile.HarvestItemId))
                continue;

            if (!this.State.CropRotationByTile.TryGetValue(cropTile.TileKey, out CropRotationTileState? tileState))
            {
                tileState = new CropRotationTileState();
                this.State.CropRotationByTile[cropTile.TileKey] = tileState;
            }

            int rotationCount = tileState.RotationCount;
            if (!string.Equals(tileState.SeasonKey, seasonKey, StringComparison.Ordinal))
            {
                rotationCount = !string.IsNullOrWhiteSpace(tileState.HarvestItemId) && tileState.CropCategory != cropTile.CropCategory
                    ? Math.Min(tileState.RotationCount + 1, 99)
                    : 0;
            }

            tileState.SeasonKey = seasonKey;
            tileState.HarvestItemId = cropTile.HarvestItemId;
            tileState.CropCategory = cropTile.CropCategory;
            tileState.RotationCount = rotationCount;

            double bonus = GetRotationBonus(rotationCount);
            if (bonus > this.State.ActiveRotationBonusByItemId.GetValueOrDefault(cropTile.HarvestItemId))
                this.State.ActiveRotationBonusByItemId[cropTile.HarvestItemId] = bonus;
        }
    }

    public void UpdateMarketExposure()
    {
        int totalIncome = this.State.LastDay.GrossShippingIncome;
        if (totalIncome <= 0 || this.State.LastDay.IncomeByCategory.Count == 0)
        {
            this.ApplyExposureDecay(0, "");
            this.PriceCache.Clear();
            return;
        }

        KeyValuePair<ItemMarketCategory, int> top = this.State.LastDay.IncomeByCategory
            .OrderByDescending(pair => pair.Value)
            .First();
        double topShare = top.Value / (double)Math.Max(1, totalIncome);
        string topCategory = top.Key.ToString();

        this.State.LastDay.MainIncomeCategory = topCategory;
        this.State.LastDay.MainIncomeCategoryShare = topShare;
        this.State.ExposureCategory = topCategory;

        double nonMainShare = Math.Clamp(1 - topShare, 0, 1);
        if (this.State.RecentNonMainIncomeShares.Count >= 3 && nonMainShare - this.State.RecentNonMainIncomeShares[0] >= 0.15)
            this.State.ExposureProtectedDays = Math.Max(this.State.ExposureProtectedDays, 3);

        this.State.RecentNonMainIncomeShares.Add(nonMainShare);
        while (this.State.RecentNonMainIncomeShares.Count > 3)
            this.State.RecentNonMainIncomeShares.RemoveAt(0);

        if (this.State.ExposureProtectedDays > 0)
        {
            this.State.ExposureProtectedDays--;
            this.PriceCache.Clear();
            return;
        }

        if (topShare >= 0.70)
        {
            this.State.ExposureScore = Math.Clamp(this.State.ExposureScore + 1, 0, 14);
            this.State.ExposureConsecutiveBelow60Days = 0;
            this.State.ExposureConsecutiveBelow50Days = 0;
            this.PriceCache.Clear();
            return;
        }

        this.ApplyExposureDecay(topShare, topCategory);
        this.PriceCache.Clear();
    }

    public void UpdateTopPressureSnapshot()
    {
        string topItemId = "";
        double topPressure = 0;

        foreach ((string itemId, double _) in this.State.MarketPressureByItemId)
        {
            double pressure = this.GetItemPressure(itemId);
            if (pressure <= topPressure)
                continue;

            topItemId = itemId;
            topPressure = pressure;
        }

        this.State.LastDay.TopPressuredItemId = topItemId;
        this.State.LastDay.TopPressuredItemPressure = topPressure;
        this.State.LastDay.MarketPressure = this.GetAverageMarketPressure();
    }

    public bool TryApplyPrice(Item? item)
    {
        if (!this.TryGetPriceableObject(item, out SObject? obj, out string itemId))
            return false;

        int basePrice = this.GetOrTrackBasePrice(itemId, obj);
        if (this.TryGetObjectData(itemId, out ObjectData? objectData)
            && IsHoney(objectData)
            && this.TryGetApiaryHoneyBasePrice(itemId, objectData, this.GetApiaryQualityByFlowerId(), out int honeyBasePrice))
            basePrice = honeyBasePrice;

        int nextPrice = this.CalculatePrice(obj, itemId, basePrice);
        if (nextPrice <= 0 || nextPrice == obj.Price)
            return false;

        obj.Price = nextPrice;
        return true;
    }

    public int CalculatePrice(SObject item, string itemId, int basePrice)
    {
        ItemMarketCategory category = ItemClassifier.GetCategory(item);
        return this.CalculatePrice(itemId, category, basePrice);
    }

    public int CalculatePrice(string rawItemId, ObjectData itemData, int basePrice)
    {
        ItemMarketCategory category = ItemClassifier.GetCategory(itemData);
        return this.CalculatePrice($"(O){rawItemId}", category, basePrice);
    }

    public double GetAverageMarketPressure()
    {
        if (this.State.MarketPressureByItemId.Count == 0)
            return 0;

        double total = 0;
        foreach (string itemId in this.State.MarketPressureByItemId.Keys)
            total += this.GetItemPressure(itemId);

        return total / this.State.MarketPressureByItemId.Count;
    }

    public double GetItemPressure(string itemId)
    {
        double adjustedSold = this.State.MarketPressureByItemId.GetValueOrDefault(itemId);
        double saturationPoint = Math.Max(1, this.Config.SaturationPoint);
        return Math.Clamp(1 - Math.Exp(-adjustedSold / saturationPoint), 0, 1);
    }

    public string GetExposureStateText()
    {
        return this.State.ExposureScore switch
        {
            <= 2 => "Stable",
            <= 6 => "Watch",
            <= 13 => "Exposed",
            _ => "Concentrated"
        };
    }

    public IReadOnlyList<DemandEventState> GetActiveDemandEvents()
    {
        if (!Context.IsWorldReady)
            return [];

        int day = SDate.Now().Day;
        return this.State.DemandEvents
            .Where(demandEvent => demandEvent.IsActive(day))
            .OrderBy(demandEvent => demandEvent.StartDay)
            .ToList();
    }

    private int CalculatePrice(string itemId, ItemMarketCategory category, int basePrice)
    {
        string cacheKey = $"{itemId}|{category}|{basePrice}";
        if (this.PriceCache.TryGetValue(cacheKey, out int cachedPrice))
        {
            this.State.LastCalculatedPricesByItemId[itemId] = cachedPrice;
            return cachedPrice;
        }

        double multiplier = this.GetBaseMultiplier(category);
        multiplier += this.GetSkillInfluence(category);
        multiplier += this.GetSeasonalInfluence(category);
        multiplier += this.GetDemandEventBonus(category);
        multiplier += this.GetRotationBonus(itemId);
        multiplier += this.GetProcessingTraceBonus(itemId, category);
        multiplier -= this.GetMarketPressurePenalty(itemId);
        multiplier -= this.GetExposurePenalty(category);

        multiplier = Math.Clamp(multiplier, this.Config.MinimumPriceMultiplier, this.Config.MaximumPriceMultiplier);
        int price = Math.Max(1, (int)Math.Round(basePrice * multiplier, MidpointRounding.AwayFromZero));
        this.PriceCache[cacheKey] = price;
        this.State.LastCalculatedPricesByItemId[itemId] = price;
        return price;
    }

    public int ApplyToLocationItems(GameLocation location)
    {
        int updated = 0;

        foreach (SObject obj in location.objects.Values)
            updated += this.ApplyToWorldObject(obj);

        return updated;
    }

    private int ApplyToWorldObject(SObject obj)
    {
        int updated = 0;

        if (obj is StardewValley.Objects.Chest chest)
            updated += this.ApplyToItems(chest.Items);

        if (obj.heldObject.Value is not null && this.TryApplyPrice(obj.heldObject.Value))
            updated++;

        if (!obj.bigCraftable.Value && this.TryApplyPrice(obj))
            updated++;

        return updated;
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

    private double GetCategoryPressureWeight(ItemMarketCategory category)
    {
        return this.Config.CategoryPressureWeights.TryGetValue(category, out double weight)
            ? Math.Max(0, weight)
            : 1.0;
    }

    private double GetSkillInfluence(ItemMarketCategory category)
    {
        if (!Context.IsWorldReady)
            return 0;

        Farmer priceAuthority = Game1.MasterPlayer;
        int level = category switch
        {
            ItemMarketCategory.Seed or ItemMarketCategory.Vegetable or ItemMarketCategory.Fruit or ItemMarketCategory.Flower or ItemMarketCategory.AnimalProduct or ItemMarketCategory.ArtisanGoods => priceAuthority.FarmingLevel,
            ItemMarketCategory.Forage => priceAuthority.ForagingLevel,
            ItemMarketCategory.Fish => priceAuthority.FishingLevel,
            ItemMarketCategory.Mining => priceAuthority.MiningLevel,
            ItemMarketCategory.MonsterLoot => priceAuthority.CombatLevel,
            _ => 0
        };

        return level * this.Config.SkillBonus;
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

    private double GetDemandEventBonus(ItemMarketCategory category)
    {
        if (!Context.IsWorldReady)
            return 0;

        int day = SDate.Now().Day;
        double bonus = 0;
        foreach (DemandEventState demandEvent in this.State.DemandEvents)
        {
            if (demandEvent.IsActive(day) && demandEvent.CategoryBonuses.TryGetValue(category, out double categoryBonus))
                bonus += categoryBonus;
        }

        return bonus;
    }

    private double GetRotationBonus(string itemId)
    {
        return this.State.ActiveRotationBonusByItemId.GetValueOrDefault(itemId);
    }

    private static double GetRotationBonus(int rotationCount)
    {
        return rotationCount switch
        {
            <= 0 => 0,
            1 => 0.03,
            2 => 0.05,
            _ => 0.07
        };
    }

    private double GetProcessingTraceBonus(string itemId, ItemMarketCategory category)
    {
        if (category is not ItemMarketCategory.ArtisanGoods and not ItemMarketCategory.Cooking)
            return 0;

        double bestBonus = 0;
        foreach (string rawItemId in this.GetProcessingRawCandidates(itemId))
        {
            if (this.State.SeasonalProducedRawByItemId.GetValueOrDefault(rawItemId) <= 0)
                continue;

            double rawPressure = this.GetItemPressure(rawItemId);
            bestBonus = Math.Max(bestBonus, Math.Clamp(this.Config.MaxProcessingTraceBonus, 0, 1) * (1 - rawPressure));
        }

        return bestBonus;
    }

    private IEnumerable<string> GetProcessingRawCandidates(string itemId)
    {
        string tracedIngredientId = MarketItemIdentity.GetIngredientItemId(itemId);
        if (!string.IsNullOrWhiteSpace(tracedIngredientId))
            yield return tracedIngredientId;

        string baseItemId = MarketItemIdentity.GetBaseItemId(itemId);
        if (ProcessingRawItemCandidates.TryGetValue(itemId, out string[]? mappedCandidates) || ProcessingRawItemCandidates.TryGetValue(baseItemId, out mappedCandidates))
        {
            foreach (string candidate in mappedCandidates)
                yield return candidate;
        }

        if (this.TryGetObjectData(itemId, out ObjectData? objectData))
        {
            string preserveId = NormalizeObjectId(GetObjectDataString(objectData, "PreserveId"));
            if (!string.IsNullOrWhiteSpace(preserveId))
                yield return preserveId;

            ItemMarketCategory? rawCategory = GetRawCategoryFromProcessedName(objectData.Name ?? "");
            if (rawCategory is not null)
            {
                foreach ((string rawItemId, int count) in this.State.SeasonalProducedRawByItemId)
                {
                    if (count <= 0 || !this.TryGetObjectData(rawItemId, out ObjectData? rawData))
                        continue;

                    if (ItemClassifier.GetCategory(rawData) == rawCategory.Value)
                        yield return rawItemId;
                }
            }
        }
    }

    private static ItemMarketCategory? GetRawCategoryFromProcessedName(string name)
    {
        if (name.Contains("Wine", StringComparison.OrdinalIgnoreCase) || name.Contains("Jelly", StringComparison.OrdinalIgnoreCase))
            return ItemMarketCategory.Fruit;

        if (name.Contains("Juice", StringComparison.OrdinalIgnoreCase) || name.Contains("Pickles", StringComparison.OrdinalIgnoreCase))
            return ItemMarketCategory.Vegetable;

        return null;
    }

    private double GetMarketPressurePenalty(string itemId)
    {
        return Math.Clamp(this.Config.MaxPenalty, 0, 1) * this.GetItemPressure(itemId);
    }

    private double GetExposurePenalty(ItemMarketCategory category)
    {
        if (!string.Equals(this.State.ExposureCategory, category.ToString(), StringComparison.Ordinal) || this.State.ExposureProtectedDays > 0)
            return 0;

        double cap = Math.Clamp(this.Config.ExposurePenaltyCap, 0, 1);
        return this.State.ExposureScore switch
        {
            >= 14 => cap,
            >= 7 => cap * 0.5,
            _ => 0
        };
    }

    private double GetExposurePressureGrowth(ItemMarketCategory category)
    {
        if (!string.Equals(this.State.ExposureCategory, category.ToString(), StringComparison.Ordinal) || this.State.ExposureProtectedDays > 0)
            return 1;

        return this.State.ExposureScore switch
        {
            >= 14 => 1.20,
            >= 7 => 1.10,
            _ => 1
        };
    }

    private void ApplyExposureDecay(double topShare, string topCategory)
    {
        if (topShare < 0.50)
            this.State.ExposureConsecutiveBelow50Days++;
        else
            this.State.ExposureConsecutiveBelow50Days = 0;

        if (this.State.ExposureConsecutiveBelow50Days >= 3)
        {
            this.State.ExposureScore = 0;
            this.State.ExposureCategory = topCategory;
            this.State.ExposureConsecutiveBelow50Days = 0;
            this.State.ExposureConsecutiveBelow60Days = 0;
            return;
        }

        if (topShare < 0.60)
        {
            this.State.ExposureScore = Math.Clamp(this.State.ExposureScore - 1, 0, 14);
            this.State.ExposureConsecutiveBelow60Days++;
        }
        else
        {
            this.State.ExposureConsecutiveBelow60Days = 0;
        }

        if (this.State.ExposureConsecutiveBelow60Days >= 2)
        {
            this.StepExposureStateDown();
            this.State.ExposureConsecutiveBelow60Days = 0;
        }
    }

    private void StepExposureStateDown()
    {
        this.State.ExposureScore = this.State.ExposureScore switch
        {
            >= 14 => 13,
            >= 7 => 6,
            >= 3 => 2,
            _ => this.State.ExposureScore
        };
    }

    private void ChooseSubsidizedCrop(string seasonKey)
    {
        IEnumerable<KeyValuePair<string, ObjectData>> objectData = Game1.objectData is null
            ? Enumerable.Empty<KeyValuePair<string, ObjectData>>()
            : Game1.objectData;
        List<(string ItemId, string Name)> crops = objectData
            .Where(pair => IsSubsidyCropCandidate(pair.Value))
            .Select(pair => (ItemId: $"(O){pair.Key}", Name: pair.Value.Name ?? pair.Key))
            .OrderBy(pair => pair.ItemId, StringComparer.Ordinal)
            .ToList();

        if (crops.Count == 0)
        {
            this.State.SubsidizedCropItemId = "";
            this.State.SubsidizedCropName = "";
            return;
        }

        Random random = new(GetStableSeed(seasonKey));
        (string itemId, string name) = crops[random.Next(crops.Count)];
        this.State.SubsidizedCropItemId = itemId;
        this.State.SubsidizedCropName = name;
    }

    private void GenerateDemandEvents(string seasonKey)
    {
        Random random = new(GetStableSeed(seasonKey + ":demand"));
        List<DemandEventTemplate> candidates = DemandEventTemplates
            .OrderBy(_ => random.Next())
            .ToList();
        List<DemandEventState> selected = new();
        HashSet<ItemMarketCategory> reservedCategories = new();

        foreach (DemandEventTemplate template in candidates)
        {
            if (template.CategoryBonuses.Keys.Any(reservedCategories.Contains))
                continue;

            foreach (ItemMarketCategory category in template.CategoryBonuses.Keys)
                reservedCategories.Add(category);

            selected.Add(new DemandEventState
            {
                Id = template.Id,
                Name = template.Name,
                StartDay = random.Next(1, 23),
                Duration = 7,
                CategoryBonuses = new Dictionary<ItemMarketCategory, double>(template.CategoryBonuses)
            });

            if (selected.Count >= 2)
                break;
        }

        this.State.DemandEvents = selected
            .OrderBy(demandEvent => demandEvent.StartDay)
            .ToList();
    }

    private (int SubsidizedCount, int TotalCount) CountFarmCrops(string subsidizedItemId)
    {
        int subsidizedCount = 0;
        int totalCount = 0;

        foreach (CropTile cropTile in this.GetFarmCropTiles())
        {
            totalCount++;
            if (string.Equals(cropTile.HarvestItemId, subsidizedItemId, StringComparison.Ordinal))
                subsidizedCount++;
        }

        return (subsidizedCount, totalCount);
    }

    private IEnumerable<CropTile> GetFarmCropTiles()
    {
        Farm farm = Game1.getFarm();
        foreach (var pair in farm.terrainFeatures.Pairs)
        {
            if (pair.Value is not HoeDirt dirt || dirt.crop is null)
                continue;

            string harvestItemId = ProductResolver.GetCropHarvestItemId(dirt.crop);
            if (string.IsNullOrWhiteSpace(harvestItemId))
                continue;

            CropRotationCategory rotationCategory = this.GetCropRotationCategory(harvestItemId);
            string tileKey = $"{farm.NameOrUniqueName}:{(int)pair.Key.X},{(int)pair.Key.Y}";
            yield return new CropTile(tileKey, harvestItemId, rotationCategory);
        }
    }

    private CropRotationCategory GetCropRotationCategory(string harvestItemId)
    {
        if (!this.TryGetObjectData(harvestItemId, out ObjectData? data))
            return CropRotationCategory.OtherCrop;

        ItemMarketCategory marketCategory = ItemClassifier.GetCategory(data);
        string name = data.Name ?? "";
        if (marketCategory == ItemMarketCategory.Flower)
            return CropRotationCategory.Flower;

        if (ContainsAny(name, "Wheat", "Rice", "Corn"))
            return CropRotationCategory.Grain;

        if (ContainsAny(name, "Potato", "Carrot", "Radish", "Beet", "Yam", "Taro", "Ginger"))
            return CropRotationCategory.RootCrop;

        if (ContainsAny(name, "Grape", "Hops", "Bean", "Ancient Fruit"))
            return CropRotationCategory.VineCrop;

        if (ContainsAny(name, "Tomato", "Pepper", "Eggplant", "Pumpkin"))
            return CropRotationCategory.FruitingVegetable;

        if (ContainsAny(name, "Kale", "Tea", "Bok Choy", "Amaranth"))
            return CropRotationCategory.LeafyVegetable;

        return CropRotationCategory.OtherCrop;
    }

    private IReadOnlyDictionary<string, double> GetApiaryQualityByFlowerId()
    {
        if (!Context.IsWorldReady)
            return this.ApiaryQualityByFlowerId;

        string dateKey = SDate.Now().ToString();
        if (!this.ApiaryQualityCacheDirty && string.Equals(this.ApiaryQualityCacheDate, dateKey, StringComparison.Ordinal))
            return this.ApiaryQualityByFlowerId;

        this.ApiaryQualityByFlowerId.Clear();
        this.ApiaryQualityCacheDate = dateKey;
        this.ApiaryQualityCacheDirty = false;

        int radius = Math.Max(0, (int)Math.Round(this.Config.ApiaryRadius));
        if (radius <= 0)
            return this.ApiaryQualityByFlowerId;

        foreach (GameLocation location in Game1.locations)
        {
            foreach (var objectPair in location.objects.Pairs)
            {
                if (objectPair.Value is not SObject obj || !IsBeeHouse(obj))
                    continue;

                List<string> nearbyFlowerIds = new();
                int minX = (int)objectPair.Key.X - radius;
                int maxX = (int)objectPair.Key.X + radius;
                int minY = (int)objectPair.Key.Y - radius;
                int maxY = (int)objectPair.Key.Y + radius;
                for (int x = minX; x <= maxX; x++)
                {
                    for (int y = minY; y <= maxY; y++)
                    {
                        if (!location.terrainFeatures.TryGetValue(new Vector2(x, y), out TerrainFeature? terrainFeature)
                            || terrainFeature is not HoeDirt dirt
                            || dirt.crop is null)
                        {
                            continue;
                        }

                        string harvestItemId = ProductResolver.GetCropHarvestItemId(dirt.crop);
                        if (string.IsNullOrWhiteSpace(harvestItemId) || !this.TryGetObjectData(harvestItemId, out ObjectData? flowerData))
                            continue;

                        if (ItemClassifier.GetCategory(flowerData) == ItemMarketCategory.Flower)
                            nearbyFlowerIds.Add(harvestItemId);
                    }
                }

                if (nearbyFlowerIds.Count == 0)
                    continue;

                double flowerCountQuality = Math.Min(nearbyFlowerIds.Count / Math.Max(1, this.Config.ApiaryFlowerCountTarget), 1) * 0.7;
                double flowerDiversityQuality = Math.Min(nearbyFlowerIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() / Math.Max(1, this.Config.ApiaryFlowerDiversityTarget), 1) * 0.3;
                double quality = Math.Clamp(flowerCountQuality + flowerDiversityQuality, 0, 1);

                foreach (string flowerId in nearbyFlowerIds.Distinct(StringComparer.OrdinalIgnoreCase))
                    this.ApiaryQualityByFlowerId[flowerId] = Math.Max(this.ApiaryQualityByFlowerId.GetValueOrDefault(flowerId), quality);

                this.ApiaryQualityByFlowerId["*"] = Math.Max(this.ApiaryQualityByFlowerId.GetValueOrDefault("*"), quality);
            }
        }

        return this.ApiaryQualityByFlowerId;
    }

    private bool TryGetApiaryHoneyBasePrice(string itemId, ObjectData itemData, IReadOnlyDictionary<string, double> apiaryQualityByFlower, out int price)
    {
        price = 0;
        if (!IsHoney(itemData))
            return false;

        string flowerId = NormalizeObjectId(GetObjectDataString(itemData, "PreserveId"));
        double quality = !string.IsNullOrWhiteSpace(flowerId)
            ? apiaryQualityByFlower.GetValueOrDefault(flowerId)
            : apiaryQualityByFlower.GetValueOrDefault("*");
        int flowerBasePrice = 0;

        if (!string.IsNullOrWhiteSpace(flowerId) && this.TryGetObjectData(flowerId, out ObjectData? flowerData))
            flowerBasePrice = Math.Max(0, flowerData.Price);

        double multiplier = 1 + (quality * Math.Max(0, this.Config.ApiaryMaxFlowerMultiplier - 1));
        price = Math.Max(1, (int)Math.Round(this.Config.ApiaryBaseWildHoneyPrice + (flowerBasePrice * multiplier), MidpointRounding.AwayFromZero));
        return true;
    }

    private bool TryGetPriceableObject(Item? item, out SObject obj, out string itemId)
    {
        obj = null!;
        itemId = "";

        if (item is not SObject candidate)
            return false;

        if (candidate.bigCraftable.Value)
            return false;

        itemId = MarketItemIdentity.GetMarketItemId(candidate);
        if (string.IsNullOrWhiteSpace(itemId) || this.Config.ExemptItemIds.Contains(itemId) || this.Config.ExemptItemIds.Contains(GetRawObjectId(itemId)))
            return false;

        obj = candidate;
        return true;
    }

    private bool TryGetObjectData(string itemId, out ObjectData objectData)
    {
        objectData = null!;
        string rawItemId = GetRawObjectId(itemId);

        if (this.CurrentObjectData is not null && this.CurrentObjectData.TryGetValue(rawItemId, out ObjectData? currentData))
        {
            objectData = currentData;
            return true;
        }

        if (Game1.objectData is not null && Game1.objectData.TryGetValue(rawItemId, out ObjectData? gameData))
        {
            objectData = gameData;
            return true;
        }

        return false;
    }

    private static bool IsTraceableRawCategory(ItemMarketCategory category)
    {
        return category is ItemMarketCategory.Seed
            or ItemMarketCategory.Vegetable
            or ItemMarketCategory.Fruit
            or ItemMarketCategory.Flower
            or ItemMarketCategory.Forage
            or ItemMarketCategory.Fish
            or ItemMarketCategory.AnimalProduct
            or ItemMarketCategory.Mining
            or ItemMarketCategory.MonsterLoot;
    }

    private static bool IsSubsidyCropCandidate(ObjectData itemData)
    {
        if (itemData.Price <= 0)
            return false;

        ItemMarketCategory category = ItemClassifier.GetCategory(itemData);
        return category is ItemMarketCategory.Vegetable or ItemMarketCategory.Fruit or ItemMarketCategory.Flower;
    }

    private static bool IsBeeHouse(SObject obj)
    {
        string itemId = ItemClassifier.GetStableItemId(obj);
        return itemId is "(BC)10" or "10" || obj.Name.Equals("Bee House", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHoney(ObjectData itemData)
    {
        string name = itemData.Name ?? "";
        string preserveType = GetObjectDataString(itemData, "PreserveType");
        return name.Contains("Honey", StringComparison.OrdinalIgnoreCase) || preserveType.Contains("Honey", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetObjectDataString(ObjectData itemData, string propertyName)
    {
        object? raw = itemData.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(itemData);
        return raw?.ToString() ?? "";
    }

    private static string NormalizeObjectId(string itemId)
    {
        return MarketItemIdentity.NormalizeObjectId(itemId);
    }

    private static string GetRawObjectId(string itemId)
    {
        return MarketItemIdentity.GetRawObjectId(itemId);
    }

    private static string GetSeasonKey()
    {
        SDate date = SDate.Now();
        return $"{date.Year}:{date.Season}";
    }

    private static int GetStableSeed(string value)
    {
        unchecked
        {
            int hash = 17;
            foreach (char ch in value)
                hash = (hash * 31) + ch;

            hash = (hash * 31) + Game1.uniqueIDForThisGame.GetHashCode();
            return hash;
        }
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        return needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record DemandEventTemplate(string Id, string Name, Dictionary<ItemMarketCategory, double> CategoryBonuses);

    private sealed record CropTile(string TileKey, string HarvestItemId, CropRotationCategory CropCategory);
}
