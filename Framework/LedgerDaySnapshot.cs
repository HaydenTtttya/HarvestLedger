namespace HarvestLedger.Framework;

public sealed class LedgerDaySnapshot
{
    public bool HadSales { get; set; }
    public int TrackedStacks { get; set; }
    public int UpdatedStacks { get; set; }
    public int SoldItemCount { get; set; }
    public int GrossShippingIncome { get; set; }
    public double MarketPressure { get; set; }
    public int TotalCropCount { get; set; }
    public int SubsidizedCropCount { get; set; }
    public int SubsidyRequiredCropCount { get; set; }
    public bool SubsidyConditionMet { get; set; }
    public double DailyRecoveryRate { get; set; }
    public int DistinctSoldCategoryCount { get; set; }
    public string MainIncomeCategory { get; set; } = "";
    public double MainIncomeCategoryShare { get; set; }
    public string TopPressuredItemId { get; set; } = "";
    public double TopPressuredItemPressure { get; set; }
    public Dictionary<long, int> ShippingIncomeByPlayerId { get; set; } = new();
    public Dictionary<string, int> SoldByItemId { get; set; } = new();
    public Dictionary<ItemMarketCategory, int> SoldByCategory { get; set; } = new();
    public Dictionary<ItemMarketCategory, int> IncomeByCategory { get; set; } = new();

    public void EnsureValid()
    {
        this.MainIncomeCategory ??= "";
        this.TopPressuredItemId ??= "";
        this.ShippingIncomeByPlayerId ??= new Dictionary<long, int>();
        this.SoldByItemId ??= new Dictionary<string, int>();
        this.SoldByCategory ??= new Dictionary<ItemMarketCategory, int>();
        this.IncomeByCategory ??= new Dictionary<ItemMarketCategory, int>();
    }

    public void Reset()
    {
        this.EnsureValid();
        this.HadSales = false;
        this.TrackedStacks = 0;
        this.UpdatedStacks = 0;
        this.SoldItemCount = 0;
        this.GrossShippingIncome = 0;
        this.MarketPressure = 0;
        this.TotalCropCount = 0;
        this.SubsidizedCropCount = 0;
        this.SubsidyRequiredCropCount = 0;
        this.SubsidyConditionMet = false;
        this.DailyRecoveryRate = 0;
        this.DistinctSoldCategoryCount = 0;
        this.MainIncomeCategory = "";
        this.MainIncomeCategoryShare = 0;
        this.TopPressuredItemId = "";
        this.TopPressuredItemPressure = 0;
        this.ShippingIncomeByPlayerId.Clear();
        this.SoldByItemId.Clear();
        this.SoldByCategory.Clear();
        this.IncomeByCategory.Clear();
    }
}
