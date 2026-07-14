namespace HarvestLedger.Framework;

/// <summary>Tax amounts and the most recent assessment for one farmhand wallet.</summary>
public sealed class PlayerTaxLedger
{
    public string LastKnownPlayerName { get; set; } = "";
    public int PendingTaxes { get; set; }
    public int UnpaidTaxes { get; set; }
    public int LastShippingIncome { get; set; }
    public int LastIncomeTax { get; set; }
    public int LastLandUseTax { get; set; }
    public int LastAutomationTax { get; set; }
    public int LastSubsidyReduction { get; set; }
    public int LastAssessedTaxes { get; set; }
    public int LastCollectedTaxes { get; set; }
    public int LifetimeAssessedTaxes { get; set; }
    public int LifetimeCollectedTaxes { get; set; }

    public void EnsureValid()
    {
        this.LastKnownPlayerName ??= "";
    }
}
