using StardewModdingAPI;
using StardewValley;

namespace HarvestLedger.Framework.Services;

public sealed class DailyLedgerService
{
    private readonly IMonitor Monitor;
    private readonly ModConfig Config;
    private readonly LedgerSaveData State;
    private readonly DynamicPricingService DynamicPricing;

    public DailyLedgerService(IMonitor monitor, ModConfig config, LedgerSaveData state, DynamicPricingService dynamicPricing)
    {
        this.Monitor = monitor;
        this.Config = config;
        this.State = state;
        this.DynamicPricing = dynamicPricing;
    }

    public void CloseCurrentDay()
    {
        this.State.LastDay.Reset();
        this.DynamicPricing.EvaluateSeasonalSubsidy();

        foreach ((Farmer farmer, Item item) in this.GetShippingBinItems())
        {
            this.State.LastDay.TrackedStacks++;
            int estimatedValue = this.DynamicPricing.EstimateSaleValue(item, this.Config.EnableDynamicPricing);
            this.State.LastDay.GrossShippingIncome += estimatedValue;
            this.State.LastDay.ShippingIncomeByPlayerId[farmer.UniqueMultiplayerID] =
                this.State.LastDay.ShippingIncomeByPlayerId.GetValueOrDefault(farmer.UniqueMultiplayerID) + estimatedValue;

            if (this.Config.EnableDynamicPricing)
                this.DynamicPricing.TrackSoldItem(item, estimatedValue);
        }

        this.RecordRecentShippingIncome();

        this.State.LastDay.DistinctSoldCategoryCount = this.State.LastDay.SoldByCategory.Count(pair => pair.Value > 0);
        this.State.LastDay.HadSales = this.State.LastDay.SoldItemCount > 0;
        this.DynamicPricing.UpdateMarketExposure();
        this.DynamicPricing.CaptureCropRotationSnapshot();
        this.DynamicPricing.UpdateTopPressureSnapshot();

        if (this.Config.EnableDailyLedger && this.State.LastDay.HadSales)
            this.Monitor.Log($"Tracked {this.State.LastDay.SoldItemCount} shipped items across {this.State.LastDay.TrackedStacks} stacks.", LogLevel.Trace);
    }

    public string GetConsoleSummary()
    {
        return
            "Harvest Ledger status: " +
            $"dynamic pricing={(this.Config.EnableDynamicPricing ? "on" : "off")}, " +
            $"tracked base prices={this.State.BasePricesByItemId.Count}, " +
            $"market pressure entries={this.State.MarketPressureByItemId.Count}, " +
            $"last sold items={this.State.LastDay.SoldItemCount}, " +
            $"subsidy={this.State.SeasonalSubsidyTaxReduction:P0}, " +
            $"pending taxes={this.State.TaxLedger.PendingTaxes}g.";
    }

    private IEnumerable<(Farmer Farmer, Item Item)> GetShippingBinItems()
    {
        if (!Context.IsWorldReady)
            yield break;

        HashSet<Item> seenItems = new(ReferenceEqualityComparer.Instance);
        Farm farm = Game1.getFarm();

        foreach (Farmer farmer in Game1.getAllFarmers())
        {
            IList<Item?>? bin = null;
            try
            {
                bin = farm.getShippingBin(farmer);
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Could not read the shipping bin for {farmer.Name}: {ex.Message}", LogLevel.Warn);
            }

            if (bin is null)
                continue;

            foreach (Item? item in bin)
            {
                if (item is not null && seenItems.Add(item))
                    yield return (farmer, item);
            }
        }
    }

    private void RecordRecentShippingIncome()
    {
        foreach (Farmer farmer in Game1.getAllFarmers().GroupBy(player => player.UniqueMultiplayerID).Select(group => group.First()))
        {
            long playerId = farmer.UniqueMultiplayerID;
            if (!this.State.RecentShippingIncomeByPlayerId.TryGetValue(playerId, out List<int>? incomeHistory))
            {
                incomeHistory = new List<int>();
                this.State.RecentShippingIncomeByPlayerId[playerId] = incomeHistory;
            }

            incomeHistory.Add(Math.Max(0, this.State.LastDay.ShippingIncomeByPlayerId.GetValueOrDefault(playerId)));
            while (incomeHistory.Count > 7)
                incomeHistory.RemoveAt(0);
        }
    }
}
