using StardewModdingAPI.Utilities;

namespace HarvestLedger.Framework;

public sealed class ModConfig
{
    public bool EnableDynamicPricing { get; set; } = true;
    public bool EnableDailyLedger { get; set; } = true;
    public bool EnableStaminaBalance { get; set; }
    public bool EnableTaxSystem { get; set; }
    public bool ShowFarmTaxOverview { get; set; } = true;
    public KeybindList MenuKey { get; set; } = KeybindList.Parse("F8");
    public DynamicPricingConfig DynamicPricing { get; set; } = new();
    public StaminaConfig Stamina { get; set; } = new();
    public TaxConfig Taxes { get; set; } = new();
}
