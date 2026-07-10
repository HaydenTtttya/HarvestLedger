using StardewModdingAPI;
using StardewValley;
using StardewValley.TerrainFeatures;
using SObject = StardewValley.Object;

namespace HarvestLedger.Framework.Services;

public sealed class TaxService
{
    private static readonly HashSet<string> AutomationMachineNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Keg",
        "Preserves Jar",
        "Crystalarium",
        "Fish Smoker",
        "Dehydrator",
        "Cask",
        "Bee House"
    };

    private readonly IMonitor Monitor;
    private readonly ModConfig Config;
    private readonly LedgerSaveData State;

    public TaxService(IMonitor monitor, ModConfig config, LedgerSaveData state)
    {
        this.Monitor = monitor;
        this.Config = config;
        this.State = state;
    }

    public void CollectDueTaxes()
    {
        TaxLedger ledger = this.State.TaxLedger;
        int due = ledger.PendingTaxes + ledger.UnpaidTaxes;
        if (due <= 0)
        {
            ledger.LastCollectedTaxes = 0;
            return;
        }

        int available = Math.Max(0, Game1.player.Money);
        int paid = Math.Min(available, due);
        if (paid > 0)
            Game1.player.Money -= paid;

        int remaining = due - paid;
        if (remaining > 0)
        {
            int penalty = (int)Math.Ceiling(remaining * Math.Clamp(this.Config.Taxes.UnpaidTaxPenalty, 0, 1));
            ledger.UnpaidTaxes = remaining + penalty;
            this.Monitor.Log($"Collected {paid}g in taxes; {ledger.UnpaidTaxes}g remains unpaid after penalty.", LogLevel.Info);
        }
        else
        {
            ledger.UnpaidTaxes = 0;
            if (paid > 0)
                this.Monitor.Log($"Collected {paid}g in taxes.", LogLevel.Info);
        }

        ledger.PendingTaxes = 0;
        ledger.LastCollectedTaxes = paid;
        ledger.LifetimeCollectedTaxes += paid;
    }

    public void AssessDailyTaxes()
    {
        TaxLedger ledger = this.State.TaxLedger;
        int incomeTax = this.CalculateIncomeTax(this.State.LastDay.GrossShippingIncome);
        int usedTillableTiles = this.CountUsedTillableTiles();
        int landUseTax = this.CalculateLandUseTax(usedTillableTiles);
        int automationMachineCount = this.CountAutomationMachines();
        int automationTax = (int)Math.Round(Math.Sqrt(automationMachineCount) * Math.Max(0, this.Config.Taxes.AutomationRate), MidpointRounding.AwayFromZero);
        int totalBeforeSubsidy = Math.Max(0, incomeTax + landUseTax + automationTax);
        int subsidyReduction = (int)Math.Round(totalBeforeSubsidy * Math.Clamp(this.State.SeasonalSubsidyTaxReduction, 0, 0.95), MidpointRounding.AwayFromZero);
        int total = Math.Max(0, totalBeforeSubsidy - subsidyReduction);

        ledger.PendingTaxes += total;
        ledger.LastAssessedTaxes = total;
        ledger.LastIncomeTax = incomeTax;
        ledger.LastLandUseTax = landUseTax;
        ledger.LastAutomationTax = automationTax;
        ledger.LastSubsidyReduction = subsidyReduction;
        ledger.LastUsedTillableTiles = usedTillableTiles;
        ledger.LastAutomationMachineCount = automationMachineCount;
        ledger.LifetimeAssessedTaxes += total;

        if (total > 0)
            this.Monitor.Log($"Assessed {total}g in taxes for tomorrow.", LogLevel.Trace);
    }

    private int CalculateIncomeTax(int income)
    {
        if (income <= 0)
            return 0;

        double rate;
        if (income <= this.Config.Taxes.FirstIncomeBracket)
            rate = this.Config.Taxes.FirstIncomeTaxRate;
        else if (income <= this.Config.Taxes.SecondIncomeBracket)
            rate = this.Config.Taxes.SecondIncomeTaxRate;
        else if (income <= this.Config.Taxes.ThirdIncomeBracket)
            rate = this.Config.Taxes.ThirdIncomeTaxRate;
        else
            rate = this.Config.Taxes.TopIncomeTaxRate;

        return Math.Max(0, (int)Math.Round(income * Math.Clamp(rate, 0, 1), MidpointRounding.AwayFromZero));
    }

    private int CalculateLandUseTax(int usedTiles)
    {
        if (usedTiles <= this.Config.Taxes.FreeLandUseTiles)
            return 0;

        int rate;
        if (usedTiles <= this.Config.Taxes.LowLandUseTileLimit)
            rate = this.Config.Taxes.LowLandUseTaxPerTile;
        else if (usedTiles <= this.Config.Taxes.MediumLandUseTileLimit)
            rate = this.Config.Taxes.MediumLandUseTaxPerTile;
        else
            rate = this.Config.Taxes.HighLandUseTaxPerTile;

        return Math.Max(0, usedTiles * Math.Max(0, rate));
    }

    private int CountUsedTillableTiles()
    {
        try
        {
            int count = 0;
            foreach (var pair in Game1.getFarm().terrainFeatures.Pairs)
            {
                if (pair.Value is HoeDirt)
                    count++;
            }

            return count;
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Could not count farm tillable tiles for taxes: {ex.Message}", LogLevel.Trace);
            return 0;
        }
    }

    private int CountAutomationMachines()
    {
        int count = 0;
        foreach (GameLocation location in Game1.locations)
        {
            foreach (SObject obj in location.objects.Values)
            {
                if (IsAutomationMachine(obj))
                    count++;
            }
        }

        foreach (Item? item in Game1.player.Items)
        {
            if (item is SObject obj && IsAutomationMachine(obj))
                count += Math.Max(1, obj.Stack);
        }

        return count;
    }

    private static bool IsAutomationMachine(SObject obj)
    {
        return obj.bigCraftable.Value && AutomationMachineNames.Contains(obj.Name);
    }
}
