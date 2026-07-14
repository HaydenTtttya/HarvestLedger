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
        api.AddBoolOption(manifest, () => config.ShowFarmTaxOverview, value => config.ShowFarmTaxOverview = value, () => "Show farm tax overview");
        api.AddKeybindList(manifest, () => config.MenuKey, value => config.MenuKey = value, () => "Status hotkey");

        api.AddSectionTitle(manifest, () => "Dynamic pricing");
        api.AddNumberOption(manifest, () => (float)config.DynamicPricing.MinimumPriceMultiplier, value => config.DynamicPricing.MinimumPriceMultiplier = value, () => "Minimum multiplier", null, 0.05f, 1f, 0.05f);
        api.AddNumberOption(manifest, () => (float)config.DynamicPricing.MaximumPriceMultiplier, value => config.DynamicPricing.MaximumPriceMultiplier = value, () => "Maximum multiplier", null, 1f, 5f, 0.05f);
        api.AddNumberOption(manifest, () => (float)config.DynamicPricing.SaturationPoint, value => config.DynamicPricing.SaturationPoint = value, () => "Saturation point", null, 10f, 2000f, 10f);
        api.AddNumberOption(manifest, () => (float)config.DynamicPricing.MaxPenalty, value => config.DynamicPricing.MaxPenalty = value, () => "Max pressure impact", null, 0f, 1f, 0.01f);
        api.AddNumberOption(manifest, () => (float)config.DynamicPricing.BaseRecovery, value => config.DynamicPricing.BaseRecovery = value, () => "Base recovery", null, 0f, 1f, 0.01f);
        api.AddNumberOption(manifest, () => (float)config.DynamicPricing.MaxDiversityRecovery, value => config.DynamicPricing.MaxDiversityRecovery = value, () => "Max diversity recovery", null, 0f, 1f, 0.01f);
        api.AddNumberOption(manifest, () => (float)config.DynamicPricing.SubsidyRecovery, value => config.DynamicPricing.SubsidyRecovery = value, () => "Subsidy recovery", null, 0f, 1f, 0.01f);
        api.AddNumberOption(manifest, () => (float)config.DynamicPricing.SkillBonus, value => config.DynamicPricing.SkillBonus = value, () => "Skill bonus per level", null, 0f, 0.1f, 0.001f);

        api.AddSectionTitle(manifest, () => "Taxes");
        api.AddNumberOption(manifest, () => (float)config.Taxes.FirstIncomeTaxRate, value => config.Taxes.FirstIncomeTaxRate = value, () => "Income tax 0-25k", null, 0f, 0.5f, 0.01f);
        api.AddNumberOption(manifest, () => (float)config.Taxes.SecondIncomeTaxRate, value => config.Taxes.SecondIncomeTaxRate = value, () => "Income tax 25k-75k", null, 0f, 0.5f, 0.01f);
        api.AddNumberOption(manifest, () => (float)config.Taxes.ThirdIncomeTaxRate, value => config.Taxes.ThirdIncomeTaxRate = value, () => "Income tax 75k-150k", null, 0f, 0.5f, 0.01f);
        api.AddNumberOption(manifest, () => (float)config.Taxes.TopIncomeTaxRate, value => config.Taxes.TopIncomeTaxRate = value, () => "Income tax 150k+", null, 0f, 0.5f, 0.01f);
        api.AddNumberOption(manifest, () => (float)config.Taxes.AutomationRate, value => config.Taxes.AutomationRate = value, () => "Automation rate", null, 0f, 100f, 1f);
        api.AddTextOption(manifest, () => config.Taxes.SharedCostAllocation, value => config.Taxes.SharedCostAllocation = value, () => "Shared cost split", () => "Separate-wallet farms only.", ["ShippingIncome", "Equal", "HostPays"]);

        api.AddSectionTitle(manifest, () => "Stamina");
        api.AddNumberOption(manifest, () => (float)config.Stamina.AxeToolRate, value => config.Stamina.AxeToolRate = value, () => "Axe extra rate", null, 0f, 10f, 0.1f);
        api.AddNumberOption(manifest, () => (float)config.Stamina.PickaxeToolRate, value => config.Stamina.PickaxeToolRate = value, () => "Pickaxe extra rate", null, 0f, 10f, 0.1f);
        api.AddNumberOption(manifest, () => (float)config.Stamina.HoeToolRate, value => config.Stamina.HoeToolRate = value, () => "Hoe extra rate", null, 0f, 10f, 0.1f);
        api.AddNumberOption(manifest, () => (float)config.Stamina.WateringCanToolRate, value => config.Stamina.WateringCanToolRate = value, () => "Watering can extra rate", null, 0f, 10f, 0.1f);
        api.AddNumberOption(manifest, () => (float)config.Stamina.FishingRodCost, value => config.Stamina.FishingRodCost = value, () => "Fishing rod flat cost", null, 0f, 10f, 0.1f);
        api.AddNumberOption(manifest, () => (float)config.Stamina.ScytheCost, value => config.Stamina.ScytheCost = value, () => "Scythe flat cost", null, 0f, 10f, 0.1f);
        api.AddNumberOption(manifest, () => (float)config.Stamina.WeaponCost, value => config.Stamina.WeaponCost = value, () => "Weapon flat cost", null, 0f, 10f, 0.1f);
    }
}

public interface IGenericModConfigMenuApi
{
    void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);
    void AddSectionTitle(IManifest mod, Func<string> text, Func<string>? tooltip = null);
    void AddBoolOption(IManifest mod, Func<bool> getValue, Action<bool> setValue, Func<string> name, Func<string>? tooltip = null, string? fieldId = null);
    void AddNumberOption(IManifest mod, Func<float> getValue, Action<float> setValue, Func<string> name, Func<string>? tooltip = null, float? min = null, float? max = null, float? interval = null, Func<float, string>? formatValue = null, string? fieldId = null);
    void AddNumberOption(IManifest mod, Func<int> getValue, Action<int> setValue, Func<string> name, Func<string>? tooltip = null, int? min = null, int? max = null, int? interval = null, Func<int, string>? formatValue = null, string? fieldId = null);
    void AddTextOption(IManifest mod, Func<string> getValue, Action<string> setValue, Func<string> name, Func<string>? tooltip = null, string[]? allowedValues = null, Func<string, string>? formatAllowedValue = null, string? fieldId = null);
    void AddKeybindList(IManifest mod, Func<KeybindList> getValue, Action<KeybindList> setValue, Func<string> name, Func<string>? tooltip = null, string? fieldId = null);
}
