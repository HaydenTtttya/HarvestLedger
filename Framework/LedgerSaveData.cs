namespace HarvestLedger.Framework;

public sealed class LedgerSaveData
{
    public int SchemaVersion { get; set; } = 3;
    public string LastUpdatedOn { get; set; } = "";
    public string CurrentSeasonKey { get; set; } = "";
    public string SubsidizedCropItemId { get; set; } = "";
    public string SubsidizedCropName { get; set; } = "";
    public double SeasonalSubsidyTaxReduction { get; set; }
    public double LastRecoveryRate { get; set; }
    public int ExposureScore { get; set; }
    public string ExposureCategory { get; set; } = "";
    public int ExposureProtectedDays { get; set; }
    public int ExposureConsecutiveBelow60Days { get; set; }
    public int ExposureConsecutiveBelow50Days { get; set; }
    public Dictionary<string, int> BasePricesByItemId { get; set; } = new();
    public Dictionary<string, int> LastCalculatedPricesByItemId { get; set; } = new();
    public Dictionary<string, int> LastSoldQualityByItemId { get; set; } = new();
    public Dictionary<string, int> LastSoldUnitPriceByItemId { get; set; } = new();
    public Dictionary<string, double> MarketPressureByItemId { get; set; } = new();
    public Dictionary<string, int> SeasonSoldCountByItemId { get; set; } = new();
    public Dictionary<string, int> SeasonalProducedRawByItemId { get; set; } = new();
    public Dictionary<string, int> LifetimeSoldByItemId { get; set; } = new();
    public Dictionary<string, double> ActiveRotationBonusByItemId { get; set; } = new();
    public Dictionary<string, CropRotationTileState> CropRotationByTile { get; set; } = new();
    public List<DemandEventState> DemandEvents { get; set; } = new();
    public List<double> RecentNonMainIncomeShares { get; set; } = new();
    public LedgerDaySnapshot LastDay { get; set; } = new();
    public TaxLedger TaxLedger { get; set; } = new();
    public Dictionary<long, PlayerTaxLedger> PlayerTaxLedgers { get; set; } = new();
    public Dictionary<long, List<int>> RecentShippingIncomeByPlayerId { get; set; } = new();
    public bool UsesPlayerTaxLedgers { get; set; }

    public void EnsureValid()
    {
        if (this.SchemaVersion < 3)
            this.SchemaVersion = 3;

        this.CurrentSeasonKey ??= "";
        this.SubsidizedCropItemId ??= "";
        this.SubsidizedCropName ??= "";
        this.ExposureCategory ??= "";
        this.BasePricesByItemId ??= new Dictionary<string, int>();
        this.LastCalculatedPricesByItemId ??= new Dictionary<string, int>();
        this.LastSoldQualityByItemId ??= new Dictionary<string, int>();
        this.LastSoldUnitPriceByItemId ??= new Dictionary<string, int>();
        this.MarketPressureByItemId ??= new Dictionary<string, double>();
        this.SeasonSoldCountByItemId ??= new Dictionary<string, int>();
        this.SeasonalProducedRawByItemId ??= new Dictionary<string, int>();
        this.LifetimeSoldByItemId ??= new Dictionary<string, int>();
        this.ActiveRotationBonusByItemId ??= new Dictionary<string, double>();
        this.CropRotationByTile ??= new Dictionary<string, CropRotationTileState>();
        this.DemandEvents ??= new List<DemandEventState>();
        this.RecentNonMainIncomeShares ??= new List<double>();
        this.LastDay ??= new LedgerDaySnapshot();
        this.LastDay.EnsureValid();
        this.TaxLedger ??= new TaxLedger();
        this.PlayerTaxLedgers ??= new Dictionary<long, PlayerTaxLedger>();
        this.RecentShippingIncomeByPlayerId ??= new Dictionary<long, List<int>>();

        foreach (PlayerTaxLedger playerLedger in this.PlayerTaxLedgers.Values)
            playerLedger.EnsureValid();

        foreach ((long playerId, List<int>? incomeHistory) in this.RecentShippingIncomeByPlayerId.ToArray())
        {
            List<int> validIncomeHistory = incomeHistory?
                .Select(income => Math.Max(0, income))
                .TakeLast(7)
                .ToList() ?? new List<int>();
            this.RecentShippingIncomeByPlayerId[playerId] = validIncomeHistory;
        }

        foreach (DemandEventState demandEvent in this.DemandEvents)
            demandEvent.CategoryBonuses ??= new Dictionary<ItemMarketCategory, double>();

        foreach (CropRotationTileState tileState in this.CropRotationByTile.Values)
        {
            tileState.SeasonKey ??= "";
            tileState.HarvestItemId ??= "";
        }

        this.ExposureScore = Math.Clamp(this.ExposureScore, 0, 14);
        this.SeasonalSubsidyTaxReduction = Math.Clamp(this.SeasonalSubsidyTaxReduction, 0, 0.28);
    }
}
