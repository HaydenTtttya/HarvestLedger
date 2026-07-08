using StardewModdingAPI;
using StardewValley;
using StardewValley.Tools;
using SObject = StardewValley.Object;

namespace HarvestLedger.Framework.Services;

public sealed class TaxService
{
    private static readonly HashSet<string> SprinklerIds = ["(BC)599", "(BC)621", "(BC)645", "599", "621", "645"];

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
        int propertyTax = this.Config.Taxes.DailyPropertyTax + this.CountFarmBuildings() * this.Config.Taxes.BuildingTax;
        int capitalTax = this.CountCapitalItems() * this.Config.Taxes.CapitalItemTax;
        int sprinklerTax = this.CountSprinklers() * this.Config.Taxes.SprinklerTax;

        int total = Math.Max(0, incomeTax + propertyTax + capitalTax + sprinklerTax);
        ledger.PendingTaxes += total;
        ledger.LastAssessedTaxes = total;
        ledger.LastIncomeTax = incomeTax;
        ledger.LastPropertyTax = propertyTax;
        ledger.LastCapitalTax = capitalTax;
        ledger.LastSprinklerTax = sprinklerTax;
        ledger.LifetimeAssessedTaxes += total;

        if (total > 0)
            this.Monitor.Log($"Assessed {total}g in taxes for tomorrow.", LogLevel.Trace);
    }

    private int CalculateIncomeTax(int income)
    {
        if (income <= 0)
            return 0;

        double rate = this.Config.Taxes.IncomeTaxRate;
        if (this.Config.Taxes.ProgressiveIncomeTax && income > this.Config.Taxes.HighIncomeThreshold)
        {
            int baseIncome = this.Config.Taxes.HighIncomeThreshold;
            int highIncome = income - baseIncome;
            double total = baseIncome * rate + highIncome * this.Config.Taxes.HighIncomeTaxRate;
            return this.ApplyHouseholdReduction(total);
        }

        return this.ApplyHouseholdReduction(income * rate);
    }

    private int ApplyHouseholdReduction(double amount)
    {
        double reduction = 0;
        if (!string.IsNullOrWhiteSpace(Game1.player.spouse))
            reduction += this.Config.Taxes.MarriedReduction;

        reduction += Math.Max(0, Game1.player.getChildrenCount()) * this.Config.Taxes.ChildReduction;
        reduction = Math.Clamp(reduction, 0, 0.75);
        return Math.Max(0, (int)Math.Round(amount * (1 - reduction), MidpointRounding.AwayFromZero));
    }

    private int CountFarmBuildings()
    {
        try
        {
            return Game1.getFarm().buildings.Count;
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Could not count farm buildings for taxes: {ex.Message}", LogLevel.Trace);
            return 0;
        }
    }

    private int CountCapitalItems()
    {
        int count = 0;
        foreach (GameLocation location in Game1.locations)
        {
            foreach (SObject obj in location.objects.Values)
            {
                if (obj.bigCraftable.Value && !IsSprinkler(obj))
                    count++;
            }
        }

        foreach (Item? item in Game1.player.Items)
        {
            if (item is SObject obj && obj.bigCraftable.Value && !IsSprinkler(obj))
                count += Math.Max(1, obj.Stack);
        }

        return count;
    }

    private int CountSprinklers()
    {
        int count = 0;
        foreach (GameLocation location in Game1.locations)
        {
            foreach (SObject obj in location.objects.Values)
            {
                if (IsSprinkler(obj))
                    count++;
            }
        }

        foreach (Item? item in Game1.player.Items)
        {
            if (item is SObject obj && IsSprinkler(obj))
                count += Math.Max(1, obj.Stack);
        }

        return count;
    }

    private static bool IsSprinkler(SObject obj)
    {
        string id = ItemClassifier.GetStableItemId(obj);
        return SprinklerIds.Contains(id) || obj.Name.Contains("Sprinkler", StringComparison.OrdinalIgnoreCase);
    }
}
