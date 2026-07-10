using StardewModdingAPI;
using StardewValley;
using StardewValley.Tools;

namespace HarvestLedger.Framework.Services;

public sealed class StaminaService
{
    private readonly IMonitor Monitor;
    private readonly StaminaConfig Config;
    private bool WasUsingTool;
    private float StaminaBeforeUse;
    private Tool? ToolInUse;

    public StaminaService(IMonitor monitor, StaminaConfig config)
    {
        this.Monitor = monitor;
        this.Config = config;
    }

    public void Update()
    {
        Farmer player = Game1.player;
        bool isUsingTool = player.UsingTool;

        if (isUsingTool && !this.WasUsingTool)
        {
            this.StaminaBeforeUse = player.Stamina;
            this.ToolInUse = player.CurrentTool;
        }

        if (!isUsingTool && this.WasUsingTool)
            this.ApplyCompletedUse(player);

        this.WasUsingTool = isUsingTool;
    }

    private void ApplyCompletedUse(Farmer player)
    {
        float after = player.Stamina;
        float spent = Math.Max(0, this.StaminaBeforeUse - after);
        if (spent <= 0)
            return;

        double extraCost = this.GetExtraCost(this.ToolInUse);
        if (extraCost <= 0)
            return;

        player.Stamina = Math.Clamp(after - (float)extraCost, 0, player.MaxStamina);
    }

    private double GetExtraCost(Tool? tool)
    {
        return tool switch
        {
            Axe axe => this.GetUpgradedToolCost(this.Config.AxeToolRate, axe),
            Pickaxe pickaxe => this.GetUpgradedToolCost(this.Config.PickaxeToolRate, pickaxe),
            Hoe hoe => this.GetUpgradedToolCost(this.Config.HoeToolRate, hoe),
            WateringCan wateringCan => this.GetUpgradedToolCost(this.Config.WateringCanToolRate, wateringCan),
            FishingRod => Math.Max(0, this.Config.FishingRodCost),
            _ when tool?.Name.Contains("Scythe", StringComparison.OrdinalIgnoreCase) == true => Math.Max(0, this.Config.ScytheCost),
            MeleeWeapon => Math.Max(0, this.Config.WeaponCost),
            _ => 0
        };
    }

    private double GetUpgradedToolCost(double configuredToolRate, Tool tool)
    {
        return Math.Max(0, configuredToolRate) * (1 + Math.Max(0, tool.UpgradeLevel));
    }
}
