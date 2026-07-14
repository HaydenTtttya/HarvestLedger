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
        if (UsesSeparateWallets())
        {
            this.CollectPlayerDueTaxes();
            return;
        }

        this.PrepareForSharedWallet();
        this.CollectFarmDueTaxes();
    }

    public void AssessDailyTaxes()
    {
        int usedTillableTiles = this.CountUsedTillableTiles();
        int landUseTax = this.CalculateLandUseTax(usedTillableTiles);
        int automationMachineCount = this.CountAutomationMachines();
        int automationTax = (int)Math.Round(Math.Sqrt(automationMachineCount) * Math.Max(0, this.Config.Taxes.AutomationRate), MidpointRounding.AwayFromZero);

        if (UsesSeparateWallets())
        {
            this.AssessPlayerTaxes(usedTillableTiles, landUseTax, automationMachineCount, automationTax);
            return;
        }

        this.PrepareForSharedWallet();
        this.AssessFarmTaxes(usedTillableTiles, landUseTax, automationMachineCount, automationTax);
    }

    private void CollectFarmDueTaxes()
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

    private void AssessFarmTaxes(int usedTillableTiles, int landUseTax, int automationMachineCount, int automationTax)
    {
        TaxLedger ledger = this.State.TaxLedger;
        int incomeTax = this.CalculateIncomeTax(this.State.LastDay.GrossShippingIncome);
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

    private void CollectPlayerDueTaxes()
    {
        this.PrepareForSeparateWallets();
        int totalCollected = 0;

        foreach (Farmer farmer in this.GetTaxableFarmers())
        {
            PlayerTaxLedger ledger = this.GetOrCreatePlayerLedger(farmer);
            int due = ledger.PendingTaxes + ledger.UnpaidTaxes;
            if (due <= 0)
            {
                ledger.LastCollectedTaxes = 0;
                continue;
            }

            int available = Math.Max(0, farmer.Money);
            int paid = Math.Min(available, due);
            if (paid > 0)
                farmer.Money -= paid;

            int remaining = due - paid;
            ledger.UnpaidTaxes = remaining > 0
                ? remaining + (int)Math.Ceiling(remaining * Math.Clamp(this.Config.Taxes.UnpaidTaxPenalty, 0, 1))
                : 0;
            ledger.PendingTaxes = 0;
            ledger.LastCollectedTaxes = paid;
            ledger.LifetimeCollectedTaxes += paid;
            totalCollected += paid;

            if (remaining > 0)
                this.Monitor.Log($"Collected {paid}g from {farmer.Name}; {ledger.UnpaidTaxes}g remains unpaid after penalty.", LogLevel.Info);
        }

        this.State.TaxLedger.LifetimeCollectedTaxes += totalCollected;
        this.SyncFarmTaxOverview();
    }

    private void AssessPlayerTaxes(int usedTillableTiles, int landUseTax, int automationMachineCount, int automationTax)
    {
        this.PrepareForSeparateWallets();
        IReadOnlyList<Farmer> farmers = this.GetTaxableFarmers();
        Dictionary<long, int> landUseShares = this.AllocateSharedCost(landUseTax, farmers);
        Dictionary<long, int> automationShares = this.AllocateSharedCost(automationTax, farmers);
        double subsidyRate = Math.Clamp(this.State.SeasonalSubsidyTaxReduction, 0, 0.95);
        int totalAssessed = 0;

        foreach (Farmer farmer in farmers)
        {
            PlayerTaxLedger ledger = this.GetOrCreatePlayerLedger(farmer);
            int shippingIncome = Math.Max(0, this.State.LastDay.ShippingIncomeByPlayerId.GetValueOrDefault(farmer.UniqueMultiplayerID));
            int incomeTax = this.CalculateIncomeTax(shippingIncome);
            int landUseShare = landUseShares.GetValueOrDefault(farmer.UniqueMultiplayerID);
            int automationShare = automationShares.GetValueOrDefault(farmer.UniqueMultiplayerID);
            int totalBeforeSubsidy = Math.Max(0, incomeTax + landUseShare + automationShare);
            int subsidyReduction = (int)Math.Round(totalBeforeSubsidy * subsidyRate, MidpointRounding.AwayFromZero);
            int total = Math.Max(0, totalBeforeSubsidy - subsidyReduction);

            ledger.LastShippingIncome = shippingIncome;
            ledger.LastIncomeTax = incomeTax;
            ledger.LastLandUseTax = landUseShare;
            ledger.LastAutomationTax = automationShare;
            ledger.LastSubsidyReduction = subsidyReduction;
            ledger.LastAssessedTaxes = total;
            ledger.PendingTaxes += total;
            ledger.LifetimeAssessedTaxes += total;
            totalAssessed += total;
        }

        this.State.TaxLedger.LifetimeAssessedTaxes += totalAssessed;
        this.SyncFarmTaxOverview(usedTillableTiles, automationMachineCount);

        if (totalAssessed > 0)
            this.Monitor.Log($"Assessed {totalAssessed}g in player taxes for tomorrow.", LogLevel.Trace);
    }

    private IReadOnlyList<Farmer> GetTaxableFarmers()
    {
        return Game1.getAllFarmers()
            .GroupBy(farmer => farmer.UniqueMultiplayerID)
            .Select(group => group.First())
            .ToList();
    }

    private PlayerTaxLedger GetOrCreatePlayerLedger(Farmer farmer)
    {
        long playerId = farmer.UniqueMultiplayerID;
        if (!this.State.PlayerTaxLedgers.TryGetValue(playerId, out PlayerTaxLedger? ledger))
        {
            ledger = new PlayerTaxLedger();
            this.State.PlayerTaxLedgers[playerId] = ledger;
        }

        ledger.LastKnownPlayerName = farmer.Name;
        return ledger;
    }

    private Dictionary<long, int> AllocateSharedCost(int totalCost, IReadOnlyList<Farmer> farmers)
    {
        Dictionary<long, int> shares = farmers.ToDictionary(farmer => farmer.UniqueMultiplayerID, _ => 0);
        if (totalCost <= 0 || farmers.Count == 0)
            return shares;

        string allocation = this.Config.Taxes.SharedCostAllocation?.Trim() ?? "";
        if (string.Equals(allocation, "HostPays", StringComparison.OrdinalIgnoreCase))
        {
            long hostId = Game1.MasterPlayer.UniqueMultiplayerID;
            if (!shares.ContainsKey(hostId))
                hostId = farmers[0].UniqueMultiplayerID;

            shares[hostId] = totalCost;
            return shares;
        }

        Dictionary<long, long> weights = new();
        foreach (Farmer farmer in farmers)
        {
            long playerId = farmer.UniqueMultiplayerID;
            weights[playerId] = string.Equals(allocation, "Equal", StringComparison.OrdinalIgnoreCase)
                ? 1
                : this.State.RecentShippingIncomeByPlayerId.GetValueOrDefault(playerId)?.Sum(income => Math.Max(0, income)) ?? 0;
        }

        if (weights.Values.All(weight => weight <= 0))
        {
            foreach (long playerId in weights.Keys.ToList())
                weights[playerId] = 1;
        }

        long totalWeight = weights.Values.Sum();
        int allocated = 0;
        List<(long PlayerId, long Remainder)> remainders = new();
        foreach ((long playerId, long weight) in weights)
        {
            long scaled = totalCost * weight;
            int share = (int)(scaled / totalWeight);
            shares[playerId] = share;
            allocated += share;
            remainders.Add((playerId, scaled % totalWeight));
        }

        foreach ((long playerId, _) in remainders.OrderByDescending(entry => entry.Remainder).ThenBy(entry => entry.PlayerId).Take(totalCost - allocated))
            shares[playerId]++;

        return shares;
    }

    private void PrepareForSeparateWallets()
    {
        if (this.State.UsesPlayerTaxLedgers)
            return;

        PlayerTaxLedger hostLedger = this.GetOrCreatePlayerLedger(Game1.MasterPlayer);
        hostLedger.PendingTaxes += this.State.TaxLedger.PendingTaxes;
        hostLedger.UnpaidTaxes += this.State.TaxLedger.UnpaidTaxes;
        this.State.TaxLedger.PendingTaxes = 0;
        this.State.TaxLedger.UnpaidTaxes = 0;
        this.State.UsesPlayerTaxLedgers = true;
    }

    private void PrepareForSharedWallet()
    {
        if (!this.State.UsesPlayerTaxLedgers)
            return;

        this.SyncFarmTaxOverview();
        foreach (PlayerTaxLedger ledger in this.State.PlayerTaxLedgers.Values)
        {
            ledger.PendingTaxes = 0;
            ledger.UnpaidTaxes = 0;
        }

        this.State.UsesPlayerTaxLedgers = false;
    }

    private void SyncFarmTaxOverview(int? usedTillableTiles = null, int? automationMachineCount = null)
    {
        TaxLedger farmLedger = this.State.TaxLedger;
        IEnumerable<PlayerTaxLedger> playerLedgers = this.State.PlayerTaxLedgers.Values;
        farmLedger.PendingTaxes = playerLedgers.Sum(ledger => ledger.PendingTaxes);
        farmLedger.UnpaidTaxes = playerLedgers.Sum(ledger => ledger.UnpaidTaxes);
        farmLedger.LastAssessedTaxes = playerLedgers.Sum(ledger => ledger.LastAssessedTaxes);
        farmLedger.LastCollectedTaxes = playerLedgers.Sum(ledger => ledger.LastCollectedTaxes);
        farmLedger.LastIncomeTax = playerLedgers.Sum(ledger => ledger.LastIncomeTax);
        farmLedger.LastLandUseTax = playerLedgers.Sum(ledger => ledger.LastLandUseTax);
        farmLedger.LastAutomationTax = playerLedgers.Sum(ledger => ledger.LastAutomationTax);
        farmLedger.LastSubsidyReduction = playerLedgers.Sum(ledger => ledger.LastSubsidyReduction);
        if (usedTillableTiles.HasValue)
            farmLedger.LastUsedTillableTiles = usedTillableTiles.Value;
        if (automationMachineCount.HasValue)
            farmLedger.LastAutomationMachineCount = automationMachineCount.Value;
    }

    private static bool UsesSeparateWallets()
    {
        try
        {
            return Game1.player.team.useSeparateWallets.Value;
        }
        catch
        {
            return false;
        }
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
