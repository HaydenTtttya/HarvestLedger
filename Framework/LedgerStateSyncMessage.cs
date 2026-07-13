namespace HarvestLedger.Framework;

public sealed class LedgerStateSyncMessage
{
    public LedgerSaveData State { get; set; } = new();
    public bool EnableDynamicPricing { get; set; }
    public DynamicPricingConfig DynamicPricing { get; set; } = new();
    public bool EnableTaxSystem { get; set; }
    public bool EnableStaminaBalance { get; set; }
    public StaminaConfig Stamina { get; set; } = new();
}
