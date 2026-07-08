namespace HarvestLedger.Framework;

public sealed class TaxLedger
{
    public int PendingTaxes { get; set; }
    public int LastAssessedTaxes { get; set; }
    public int LastCollectedTaxes { get; set; }
    public int LifetimeAssessedTaxes { get; set; }
    public int LifetimeCollectedTaxes { get; set; }
    public int UnpaidTaxes { get; set; }
    public int LastIncomeTax { get; set; }
    public int LastPropertyTax { get; set; }
    public int LastCapitalTax { get; set; }
    public int LastSprinklerTax { get; set; }
}
