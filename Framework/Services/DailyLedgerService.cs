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

        foreach (Item? item in this.GetShippingBinItems())
        {
            if (item is null)
                continue;

            this.State.LastDay.TrackedStacks++;
            int estimatedValue = this.DynamicPricing.EstimateSaleValue(item, this.Config.EnableDynamicPricing);
            this.State.LastDay.GrossShippingIncome += estimatedValue;

            if (this.Config.EnableDynamicPricing)
                this.DynamicPricing.TrackSoldItem(item, estimatedValue);
        }

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

    private IEnumerable<Item?> GetShippingBinItems()
    {
        if (!Context.IsWorldReady)
            yield break;

        IList<Item?>? bin = null;

        try
        {
            bin = Game1.getFarm().getShippingBin(Game1.player);
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Could not read the shipping bin: {ex.Message}", LogLevel.Warn);
        }

        if (bin is null)
            yield break;

        foreach (Item? item in bin)
            yield return item;
    }
}
