using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.GameData.Machines;
using StardewValley.Locations;
using StardewValley.TerrainFeatures;
using SObject = StardewValley.Object;

namespace HarvestLedger.Framework.Services;

public sealed class TaxService
{
    private const int CurrentAutomationTaxRuleVersion = 7;

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
        int automationTax = this.CalculateAutomationTax(automationMachineCount);

        if (UsesSeparateWallets())
        {
            this.AssessPlayerTaxes(usedTillableTiles, landUseTax, automationMachineCount, automationTax);
            return;
        }

        this.PrepareForSharedWallet();
        this.AssessFarmTaxes(usedTillableTiles, landUseTax, automationMachineCount, automationTax);
    }

    public void ReconcileAutomationTaxRules()
    {
        if (this.State.AutomationTaxRuleVersion >= CurrentAutomationTaxRuleVersion)
            return;

        int currentMachineCount = this.CountAutomationMachines();
        int currentAutomationTax = this.CalculateAutomationTax(currentMachineCount);
        if (this.State.UsesPlayerTaxLedgers)
            this.ReconcilePlayerAutomationTax(currentMachineCount, currentAutomationTax);
        else
            this.ReconcileFarmAutomationTax(currentMachineCount, currentAutomationTax);

        this.State.AutomationTaxRuleVersion = CurrentAutomationTaxRuleVersion;
    }

    private void CollectFarmDueTaxes()
    {
        TaxLedger ledger = this.State.TaxLedger;
        int due = ledger.PendingTaxes + ledger.UnpaidTaxes;
        if (due <= 0)
        {
            ledger.LastCollectedTaxes = 0;
            ledger.LastUnpaidTaxPenalty = 0;
            return;
        }

        int available = Math.Max(0, Game1.player.Money);
        int paid = Math.Min(available, due);
        if (paid > 0)
            Game1.player.Money -= paid;

        int remaining = due - paid;
        if (remaining > 0)
        {
            int penalty = this.CalculateUnpaidTaxPenalty(remaining);
            ledger.UnpaidTaxes = remaining + penalty;
            ledger.LastUnpaidTaxPenalty = penalty;
            string penaltySuffix = penalty > 0 ? $" after a {penalty}g late fee" : "";
            this.Monitor.Log($"Collected {paid}g in taxes; {ledger.UnpaidTaxes}g remains unpaid{penaltySuffix}.", LogLevel.Info);
        }
        else
        {
            ledger.UnpaidTaxes = 0;
            ledger.LastUnpaidTaxPenalty = 0;
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

    private void ReconcileFarmAutomationTax(int currentMachineCount, int currentAutomationTax)
    {
        TaxLedger ledger = this.State.TaxLedger;
        int previousAutomationTax = Math.Max(0, ledger.LastAutomationTax);
        int correctedAutomationTax = Math.Min(previousAutomationTax, currentAutomationTax);
        int removed = RemoveOutstandingTaxes(ledger, previousAutomationTax - correctedAutomationTax);
        ledger.LastAutomationMachineCount = currentMachineCount;
        if (removed <= 0)
            return;

        ledger.LastAutomationTax = correctedAutomationTax;
        ledger.LastAssessedTaxes = Math.Max(0, ledger.LastAssessedTaxes - removed);
        ledger.LifetimeAssessedTaxes = Math.Max(0, ledger.LifetimeAssessedTaxes - removed);
        this.Monitor.Log($"Removed {removed}g of invalid automation tax from the outstanding bill.", LogLevel.Info);
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
                ledger.LastUnpaidTaxPenalty = 0;
                continue;
            }

            int available = Math.Max(0, farmer.Money);
            int paid = Math.Min(available, due);
            if (paid > 0)
                farmer.Money -= paid;

            int remaining = due - paid;
            int penalty = remaining > 0 ? this.CalculateUnpaidTaxPenalty(remaining) : 0;
            ledger.UnpaidTaxes = remaining + penalty;
            ledger.LastUnpaidTaxPenalty = penalty;
            ledger.PendingTaxes = 0;
            ledger.LastCollectedTaxes = paid;
            ledger.LifetimeCollectedTaxes += paid;
            totalCollected += paid;

            if (remaining > 0)
            {
                string penaltySuffix = penalty > 0 ? $" after a {penalty}g late fee" : "";
                this.Monitor.Log($"Collected {paid}g from {farmer.Name}; {ledger.UnpaidTaxes}g remains unpaid{penaltySuffix}.", LogLevel.Info);
            }
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

    private void ReconcilePlayerAutomationTax(int currentMachineCount, int currentAutomationTax)
    {
        IReadOnlyList<Farmer> farmers = this.GetTaxableFarmers();
        Dictionary<long, int> correctedShares = this.AllocateSharedCost(currentAutomationTax, farmers);
        int totalRemoved = 0;

        foreach (Farmer farmer in farmers)
        {
            PlayerTaxLedger ledger = this.GetOrCreatePlayerLedger(farmer);
            int previousAutomationTax = Math.Max(0, ledger.LastAutomationTax);
            int correctedAutomationTax = Math.Min(previousAutomationTax, correctedShares.GetValueOrDefault(farmer.UniqueMultiplayerID));
            int removed = RemoveOutstandingTaxes(ledger, previousAutomationTax - correctedAutomationTax);
            if (removed <= 0)
                continue;

            ledger.LastAutomationTax = correctedAutomationTax;
            ledger.LastAssessedTaxes = Math.Max(0, ledger.LastAssessedTaxes - removed);
            ledger.LifetimeAssessedTaxes = Math.Max(0, ledger.LifetimeAssessedTaxes - removed);
            totalRemoved += removed;
        }

        this.State.TaxLedger.LifetimeAssessedTaxes = Math.Max(0, this.State.TaxLedger.LifetimeAssessedTaxes - totalRemoved);
        this.SyncFarmTaxOverview(automationMachineCount: currentMachineCount);
        if (totalRemoved > 0)
            this.Monitor.Log($"Removed {totalRemoved}g of invalid automation tax from outstanding player bills.", LogLevel.Info);
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
        farmLedger.LastUnpaidTaxPenalty = playerLedgers.Sum(ledger => ledger.LastUnpaidTaxPenalty);
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

    private int CalculateUnpaidTaxPenalty(int remaining)
    {
        if (!this.Config.Taxes.ApplyUnpaidTaxPenalty || remaining <= 0)
            return 0;

        return (int)Math.Ceiling(remaining * Math.Clamp(this.Config.Taxes.UnpaidTaxPenalty, 0, 1));
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

    private int CalculateAutomationTax(int machineCount)
    {
        return (int)Math.Round(Math.Sqrt(machineCount) * Math.Max(0, this.Config.Taxes.AutomationRate), MidpointRounding.AwayFromZero);
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
        HashSet<string> machineIds = this.GetProductionMachineIds();
        if (machineIds.Count == 0)
            return 0;

        int count = 0;
        foreach (GameLocation location in this.GetFarmOwnedLocations())
        {
            foreach (SObject obj in location.objects.Values)
            {
                if (!obj.bigCraftable.Value)
                    continue;

                if (!WasPlacedByPlayer(obj))
                    continue;

                if (!machineIds.Contains(NormalizeMachineId(obj.QualifiedItemId)))
                    continue;

                count++;
            }
        }

        return count;
    }

    private IEnumerable<GameLocation> GetFarmOwnedLocations()
    {
        Farm farm = Game1.getFarm();
        HashSet<GameLocation> locations = new() { farm };
        if (Game1.getLocationFromName("FarmHouse") is FarmHouse farmhouse)
            AddFarmHouseAndCellar(locations, farmhouse);

        GameLocation? farmCave = Game1.getLocationFromName("FarmCave");
        if (farmCave is not null)
            locations.Add(farmCave);

        foreach (Building building in farm.buildings)
        {
            GameLocation? indoors = building.GetIndoors();
            if (indoors is not null)
            {
                locations.Add(indoors);
                if (indoors is FarmHouse buildingFarmhouse)
                    AddFarmHouseAndCellar(locations, buildingFarmhouse);
            }
        }

        return locations;
    }

    private HashSet<string> GetProductionMachineIds()
    {
        try
        {
            return DataLoader.Machines(Game1.content)
                .Where(pair => pair.Value.OutputRules?.Count > 0)
                .Select(pair => NormalizeMachineId(pair.Key))
                .Where(itemId => !string.IsNullOrWhiteSpace(itemId))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Could not load machine data for automation tax: {ex.Message}", LogLevel.Trace);
            return [];
        }
    }

    private static string NormalizeMachineId(string? itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return "";

        string normalized = itemId.Trim();
        int prefixEnd = normalized.IndexOf(')');
        return normalized.StartsWith("(", StringComparison.Ordinal) && prefixEnd >= 0
            ? normalized[(prefixEnd + 1)..]
            : normalized;
    }

    private static void AddFarmHouseAndCellar(ISet<GameLocation> locations, FarmHouse farmhouse)
    {
        locations.Add(farmhouse);
        if (farmhouse.upgradeLevel >= 3 && farmhouse.GetCellar() is GameLocation cellar)
            locations.Add(cellar);
    }

    private static bool WasPlacedByPlayer(SObject obj)
    {
        return obj.owner.Value != 0;
    }

    private static int RemoveOutstandingTaxes(TaxLedger ledger, int amount)
    {
        int removedFromPending = Math.Min(Math.Max(0, amount), Math.Max(0, ledger.PendingTaxes));
        ledger.PendingTaxes -= removedFromPending;
        int removedFromUnpaid = Math.Min(Math.Max(0, amount - removedFromPending), Math.Max(0, ledger.UnpaidTaxes));
        ledger.UnpaidTaxes -= removedFromUnpaid;
        return removedFromPending + removedFromUnpaid;
    }

    private static int RemoveOutstandingTaxes(PlayerTaxLedger ledger, int amount)
    {
        int removedFromPending = Math.Min(Math.Max(0, amount), Math.Max(0, ledger.PendingTaxes));
        ledger.PendingTaxes -= removedFromPending;
        int removedFromUnpaid = Math.Min(Math.Max(0, amount - removedFromPending), Math.Max(0, ledger.UnpaidTaxes));
        ledger.UnpaidTaxes -= removedFromUnpaid;
        return removedFromPending + removedFromUnpaid;
    }
}
