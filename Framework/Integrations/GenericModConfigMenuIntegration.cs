using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace HarvestLedger.Framework.Integrations;

internal static class GenericModConfigMenuIntegration
{
    public static void Register(IManifest manifest, IModHelper helper, ModConfig config, Action reset, Action save)
    {
        IGenericModConfigMenuApi? api = helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (api is null)
            return;

        api.Register(manifest, reset, save);

        api.AddSectionTitle(manifest, () => "Core");
        api.AddBoolOption(manifest, () => config.EnableDynamicPricing, value => config.EnableDynamicPricing = value, () => "Dynamic pricing");
        api.AddBoolOption(manifest, () => config.EnableDailyLedger, value => config.EnableDailyLedger = value, () => "Daily ledger logging");
        api.AddBoolOption(manifest, () => config.EnableStaminaBalance, value => config.EnableStaminaBalance = value, () => "Stamina balancing");
        api.AddBoolOption(manifest, () => config.EnableTaxSystem, value => config.EnableTaxSystem = value, () => "Tax system");
        api.AddKeybindList(manifest, () => config.MenuKey, value => config.MenuKey = value, () => "Status hotkey");

        api.AddSectionTitle(manifest, () => "Dynamic pricing");
        api.AddNumberOption(manifest, () => (float)config.DynamicPricing.MinimumPriceMultiplier, value => config.DynamicPricing.MinimumPriceMultiplier = value, () => "Minimum multiplier", null, 0.05f, 1f, 0.05f);
        api.AddNumberOption(manifest, () => (float)config.DynamicPricing.MaximumPriceMultiplier, value => config.DynamicPricing.MaximumPriceMultiplier = value, () => "Maximum multiplier", null, 1f, 5f, 0.05f);
        api.AddNumberOption(manifest, () => (float)config.DynamicPricing.DailyDemandRecovery, value => config.DynamicPricing.DailyDemandRecovery = value, () => "Daily demand recovery", null, 0f, 1f, 0.01f);
        api.AddNumberOption(manifest, () => (float)config.DynamicPricing.SalePressurePerItem, value => config.DynamicPricing.SalePressurePerItem = value, () => "Sale pressure per item", null, 0f, 0.1f, 0.001f);
        api.AddNumberOption(manifest, () => (float)config.DynamicPricing.SkillBonus, value => config.DynamicPricing.SkillBonus = value, () => "Skill bonus per level", null, 0f, 0.1f, 0.001f);
        api.AddNumberOption(manifest, () => (float)config.DynamicPricing.QualityPriceWeight, value => config.DynamicPricing.QualityPriceWeight = value, () => "Quality price weight", null, 0f, 0.5f, 0.01f);

        api.AddSectionTitle(manifest, () => "Taxes");
        api.AddNumberOption(manifest, () => config.Taxes.DailyPropertyTax, value => config.Taxes.DailyPropertyTax = value, () => "Daily property tax", null, 0, 1000, 5);
        api.AddNumberOption(manifest, () => config.Taxes.BuildingTax, value => config.Taxes.BuildingTax = value, () => "Building tax", null, 0, 500, 5);
        api.AddNumberOption(manifest, () => config.Taxes.CapitalItemTax, value => config.Taxes.CapitalItemTax = value, () => "Capital item tax", null, 0, 100, 1);
        api.AddNumberOption(manifest, () => config.Taxes.SprinklerTax, value => config.Taxes.SprinklerTax = value, () => "Sprinkler tax", null, 0, 100, 1);
        api.AddNumberOption(manifest, () => (float)config.Taxes.IncomeTaxRate, value => config.Taxes.IncomeTaxRate = value, () => "Income tax rate", null, 0f, 0.5f, 0.01f);
        api.AddNumberOption(manifest, () => (float)config.Taxes.HighIncomeTaxRate, value => config.Taxes.HighIncomeTaxRate = value, () => "High-income tax rate", null, 0f, 0.75f, 0.01f);
        api.AddNumberOption(manifest, () => config.Taxes.HighIncomeThreshold, value => config.Taxes.HighIncomeThreshold = value, () => "High-income threshold", null, 0, 100000, 500);

        api.AddSectionTitle(manifest, () => "Stamina");
        api.AddNumberOption(manifest, () => (float)config.Stamina.AxeCostMultiplier, value => config.Stamina.AxeCostMultiplier = value, () => "Axe cost", null, 0f, 5f, 0.05f);
        api.AddNumberOption(manifest, () => (float)config.Stamina.PickaxeCostMultiplier, value => config.Stamina.PickaxeCostMultiplier = value, () => "Pickaxe cost", null, 0f, 5f, 0.05f);
        api.AddNumberOption(manifest, () => (float)config.Stamina.HoeCostMultiplier, value => config.Stamina.HoeCostMultiplier = value, () => "Hoe cost", null, 0f, 5f, 0.05f);
        api.AddNumberOption(manifest, () => (float)config.Stamina.WateringCanCostMultiplier, value => config.Stamina.WateringCanCostMultiplier = value, () => "Watering can cost", null, 0f, 5f, 0.05f);
        api.AddNumberOption(manifest, () => (float)config.Stamina.FishingRodCostMultiplier, value => config.Stamina.FishingRodCostMultiplier = value, () => "Fishing rod cost", null, 0f, 5f, 0.05f);
    }
}

public interface IGenericModConfigMenuApi
{
    void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);
    void AddSectionTitle(IManifest mod, Func<string> text, Func<string>? tooltip = null);
    void AddBoolOption(IManifest mod, Func<bool> getValue, Action<bool> setValue, Func<string> name, Func<string>? tooltip = null, string? fieldId = null);
    void AddNumberOption(IManifest mod, Func<float> getValue, Action<float> setValue, Func<string> name, Func<string>? tooltip = null, float? min = null, float? max = null, float? interval = null, Func<float, string>? formatValue = null, string? fieldId = null);
    void AddNumberOption(IManifest mod, Func<int> getValue, Action<int> setValue, Func<string> name, Func<string>? tooltip = null, int? min = null, int? max = null, int? interval = null, Func<int, string>? formatValue = null, string? fieldId = null);
    void AddKeybindList(IManifest mod, Func<KeybindList> getValue, Action<KeybindList> setValue, Func<string> name, Func<string>? tooltip = null, string? fieldId = null);
}
