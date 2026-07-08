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

        double multiplier = this.GetMultiplier(this.ToolInUse);
        if (Math.Abs(multiplier - 1.0) < 0.001)
            return;

        float desiredSpent = (float)Math.Max(0, spent * multiplier);
        float adjustment = spent - desiredSpent;
        player.Stamina = Math.Clamp(after + adjustment, 0, player.MaxStamina);
    }

    private double GetMultiplier(Tool? tool)
    {
        return tool switch
        {
            Axe => this.Config.AxeCostMultiplier,
            Pickaxe => this.Config.PickaxeCostMultiplier,
            Hoe => this.Config.HoeCostMultiplier,
            WateringCan => this.Config.WateringCanCostMultiplier,
            FishingRod => this.Config.FishingRodCostMultiplier,
            MeleeWeapon => this.Config.WeaponCostMultiplier,
            _ when tool?.Name.Contains("Scythe", StringComparison.OrdinalIgnoreCase) == true => this.Config.ScytheCostMultiplier,
            _ => 1.0
        };
    }
}
