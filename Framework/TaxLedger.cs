namespace HarvestLedger.Framework;

public sealed class TaxLedger
{
    public int PendingTaxes { get; set; }
    public int LastAssessedTaxes { get; set; }
    public int LastCollectedTaxes { get; set; }
    public int LifetimeAssessedTaxes { get; set; }
    public int LifetimeCollectedTaxes { get; set; }
    public int UnpaidTaxes { get; set; }
    public int LastUnpaidTaxPenalty { get; set; }
    public int LastIncomeTax { get; set; }
    public int LastLandUseTax { get; set; }
    public int LastAutomationTax { get; set; }
    public int LastSubsidyReduction { get; set; }
    public int LastUsedTillableTiles { get; set; }
    public int LastAutomationMachineCount { get; set; }
}
