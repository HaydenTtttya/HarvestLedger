namespace HarvestLedger.Framework;

public sealed class DemandEventState
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int StartDay { get; set; }
    public int Duration { get; set; } = 7;
    public Dictionary<ItemMarketCategory, double> CategoryBonuses { get; set; } = new();

    public bool IsActive(int dayOfMonth)
    {
        return dayOfMonth >= this.StartDay && dayOfMonth < this.StartDay + this.Duration;
    }

    public int GetRemainingDays(int dayOfMonth)
    {
        return Math.Max(0, this.StartDay + this.Duration - dayOfMonth);
    }
}
