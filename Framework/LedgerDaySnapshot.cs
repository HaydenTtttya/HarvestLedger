namespace HarvestLedger.Framework;

public sealed class LedgerDaySnapshot
{
    public bool HadSales { get; set; }
    public int TrackedStacks { get; set; }
    public int UpdatedStacks { get; set; }
    public int SoldItemCount { get; set; }
    public int GrossShippingIncome { get; set; }
    public double MarketPressure { get; set; }
    public Dictionary<string, int> SoldByItemId { get; set; } = new();

    public void Reset()
    {
        this.HadSales = false;
        this.TrackedStacks = 0;
        this.UpdatedStacks = 0;
        this.SoldItemCount = 0;
        this.GrossShippingIncome = 0;
        this.MarketPressure = 0;
        this.SoldByItemId.Clear();
    }
}
