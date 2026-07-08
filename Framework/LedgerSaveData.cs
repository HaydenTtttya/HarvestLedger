namespace HarvestLedger.Framework;

public sealed class LedgerSaveData
{
    public int SchemaVersion { get; set; } = 1;
    public string LastUpdatedOn { get; set; } = "";
    public Dictionary<string, int> BasePricesByItemId { get; set; } = new();
    public Dictionary<string, double> MarketPressureByItemId { get; set; } = new();
    public Dictionary<string, int> LifetimeSoldByItemId { get; set; } = new();
    public LedgerDaySnapshot LastDay { get; set; } = new();
    public TaxLedger TaxLedger { get; set; } = new();

    public void EnsureValid()
    {
        if (this.SchemaVersion <= 0)
            this.SchemaVersion = 1;

        this.BasePricesByItemId ??= new Dictionary<string, int>();
        this.MarketPressureByItemId ??= new Dictionary<string, double>();
        this.LifetimeSoldByItemId ??= new Dictionary<string, int>();
        this.LastDay ??= new LedgerDaySnapshot();
        this.TaxLedger ??= new TaxLedger();
    }
}
