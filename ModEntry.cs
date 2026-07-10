using HarvestLedger.Framework;
using HarvestLedger.Framework.Integrations;
using HarvestLedger.Framework.Menus;
using HarvestLedger.Framework.Services;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;

namespace HarvestLedger;

public sealed class ModEntry : Mod
{
    private const string SaveDataKey = "ledger-state";

    private ModConfig Config = null!;
    private LedgerSaveData State = null!;
    private DynamicPricingService DynamicPricing = null!;
    private DailyLedgerService DailyLedger = null!;
    private TaxService Taxes = null!;
    private StaminaService Stamina = null!;
    private Texture2D? IconAtlas;

    public override void Entry(IModHelper helper)
    {
        this.Config = helper.ReadConfig<ModConfig>();
        this.State = new LedgerSaveData();
        this.DynamicPricing = new DynamicPricingService(this.Monitor, this.Config.DynamicPricing, this.State);
        this.DailyLedger = new DailyLedgerService(this.Monitor, this.Config, this.State, this.DynamicPricing);
        this.Taxes = new TaxService(this.Monitor, this.Config, this.State);
        this.Stamina = new StaminaService(this.Monitor, this.Config.Stamina);

        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
        helper.Events.GameLoop.Saving += this.OnSaving;
        helper.Events.GameLoop.DayStarted += this.OnDayStarted;
        helper.Events.GameLoop.DayEnding += this.OnDayEnding;
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.Content.AssetRequested += this.OnAssetRequested;
        helper.Events.Player.InventoryChanged += this.OnInventoryChanged;
        helper.Events.Input.ButtonPressed += this.OnButtonPressed;
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        GenericModConfigMenuIntegration.Register(this.ModManifest, this.Helper, this.Config, this.ResetConfig, this.SaveConfig);
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        this.State = this.Helper.Data.ReadSaveData<LedgerSaveData>(SaveDataKey) ?? new LedgerSaveData();
        this.State.EnsureValid();
        this.DynamicPricing = new DynamicPricingService(this.Monitor, this.Config.DynamicPricing, this.State);
        this.DailyLedger = new DailyLedgerService(this.Monitor, this.Config, this.State, this.DynamicPricing);
        this.Taxes = new TaxService(this.Monitor, this.Config, this.State);
        this.Stamina = new StaminaService(this.Monitor, this.Config.Stamina);

        if (this.Config.EnableDynamicPricing)
        {
            bool newSeason = this.DynamicPricing.EnsureSeasonState();
            if (newSeason && SDate.Now().Day == 1)
                Game1.addHUDMessage(new HUDMessage(this.GetSeasonStartMessage(), HUDMessage.newQuest_type));

            this.Helper.GameContent.InvalidateCache("Data/Objects");
            this.DynamicPricing.ApplyToPlayerInventory();
            this.DynamicPricing.ApplyToWorldItems();
        }
    }

    private void OnSaving(object? sender, SavingEventArgs e)
    {
        this.State.LastUpdatedOn = SDate.Now().ToString();
        this.Helper.Data.WriteSaveData(SaveDataKey, this.State);
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        if (this.Config.EnableDynamicPricing)
        {
            bool newSeason = this.DynamicPricing.EnsureSeasonState();
            if (newSeason)
                Game1.addHUDMessage(new HUDMessage(this.GetSeasonStartMessage(), HUDMessage.newQuest_type));

            this.DynamicPricing.RecoverMarketDemand();
            this.Helper.GameContent.InvalidateCache("Data/Objects");
            int updated = this.DynamicPricing.ApplyToPlayerInventory();
            updated += this.DynamicPricing.ApplyToWorldItems();
            this.State.LastDay.UpdatedStacks = updated;

            if (this.Config.EnableDailyLedger && this.State.LastDay.HadSales)
            {
                string pressure = this.State.LastDay.MarketPressure.ToString("P0");
                this.Monitor.Log(this.Helper.Translation.Get("message.day-summary", new { pressure, stacks = this.State.LastDay.TrackedStacks }), LogLevel.Info);
            }
        }

        if (this.Config.EnableTaxSystem)
            this.Taxes.CollectDueTaxes();
    }

    private void OnDayEnding(object? sender, DayEndingEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        this.DailyLedger.CloseCurrentDay();

        if (this.Config.EnableTaxSystem)
            this.Taxes.AssessDailyTaxes();
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (e.NameWithoutLocale.IsEquivalentTo("Data/Machines"))
            ProductResolver.InvalidateMachineCache();

        if (!this.Config.EnableDynamicPricing)
            return;

        if (e.NameWithoutLocale.IsEquivalentTo("Data/Objects"))
            e.Edit(asset => this.DynamicPricing.EditObjectData(asset));
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        if (this.Config.EnableDynamicPricing && e.IsMultipleOf(60))
            this.DynamicPricing.ApplyToCurrentLocationItems();

        if (this.Config.EnableStaminaBalance)
            this.Stamina.Update();
    }

    private void OnInventoryChanged(object? sender, InventoryChangedEventArgs e)
    {
        if (!Context.IsWorldReady || !this.Config.EnableDynamicPricing || !e.IsLocalPlayer)
            return;

        foreach (Item? item in e.Added)
            this.DynamicPricing.TrackProducedItem(item);

        this.DynamicPricing.ApplyToItems(e.Added);
        this.DynamicPricing.ApplyToCurrentLocationItems();
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady || !this.Config.MenuKey.JustPressed())
            return;

        Game1.activeClickableMenu = new LedgerMenu(this.State, this.Config, this.LoadIconAtlas(), this.Helper.Translation);
    }

    private void ResetConfig()
    {
        this.Config = new ModConfig();
        this.RebuildServices();
    }

    private void SaveConfig()
    {
        this.Helper.WriteConfig(this.Config);
        this.RebuildServices();
        this.Monitor.Log(this.Helper.Translation.Get("message.config-reloaded"), LogLevel.Trace);
    }

    private void RebuildServices()
    {
        this.DynamicPricing = new DynamicPricingService(this.Monitor, this.Config.DynamicPricing, this.State);
        this.DailyLedger = new DailyLedgerService(this.Monitor, this.Config, this.State, this.DynamicPricing);
        this.Taxes = new TaxService(this.Monitor, this.Config, this.State);
        this.Stamina = new StaminaService(this.Monitor, this.Config.Stamina);
    }

    private Texture2D? LoadIconAtlas()
    {
        if (this.IconAtlas is not null)
            return this.IconAtlas;

        try
        {
            this.IconAtlas = this.Helper.ModContent.Load<Texture2D>("icon/icon_pixel_art.png");
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Could not load ledger icon atlas: {ex.Message}", LogLevel.Trace);
        }

        return this.IconAtlas;
    }

    private string GetSeasonStartMessage()
    {
        return string.IsNullOrWhiteSpace(this.State.SubsidizedCropItemId)
            ? this.Helper.Translation.Get("message.season.no-subsidy")
            : this.Helper.Translation.Get("message.season.subsidy-crop", new { crop = GetLocalizedItemName(this.State.SubsidizedCropItemId, this.State.SubsidizedCropName) });
    }

    private static string GetLocalizedItemName(string itemId, string fallback)
    {
        try
        {
            Item item = ItemRegistry.Create(itemId, 1, 0, true);
            if (!string.IsNullOrWhiteSpace(item.DisplayName))
                return item.DisplayName;
        }
        catch
        {
            // Fall through to the stored name.
        }

        return string.IsNullOrWhiteSpace(fallback) ? itemId : fallback;
    }
}
