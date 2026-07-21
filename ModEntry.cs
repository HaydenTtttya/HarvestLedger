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
    private const string StateRequestMessageType = "economy-state-request";
    private const string StateSyncMessageType = "economy-state-sync";

    private ModConfig Config = null!;
    private LedgerSaveData State = null!;
    private DynamicPricingConfig EffectiveDynamicPricingConfig = null!;
    private StaminaConfig EffectiveStaminaConfig = null!;
    private DynamicPricingService DynamicPricing = null!;
    private DailyLedgerService DailyLedger = null!;
    private TaxService Taxes = null!;
    private StaminaService Stamina = null!;
    private bool HasAuthoritativeState;
    private bool HostDynamicPricingEnabled;
    private bool HostTaxSystemEnabled;
    private bool HostStaminaBalanceEnabled;
    private Texture2D? IconAtlas;
    private Texture2D? PriceUpArrow;
    private Texture2D? PriceDownArrow;
    private Texture2D? TaxIcon;
    private Texture2D? MainIcon;

    // Save data belongs to the game host. Context.IsMainPlayer can be true while a
    // remote client is loading, so use the game-level host flag for all authority checks.
    private bool IsEconomyAuthority => Game1.IsMasterGame;
    private bool IsDynamicPricingEnabled => this.IsEconomyAuthority
        ? this.Config.EnableDynamicPricing
        : this.HasAuthoritativeState && this.HostDynamicPricingEnabled;
    private bool IsStaminaBalanceEnabled => this.IsEconomyAuthority
        ? this.Config.EnableStaminaBalance
        : this.HasAuthoritativeState && this.HostStaminaBalanceEnabled;

    public override void Entry(IModHelper helper)
    {
        this.Config = helper.ReadConfig<ModConfig>();
        this.Config.EnsureValid();
        this.State = new LedgerSaveData();
        this.UseLocalEconomySettings();
        this.RebuildServices();

        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
        helper.Events.GameLoop.Saving += this.OnSaving;
        helper.Events.GameLoop.DayStarted += this.OnDayStarted;
        helper.Events.GameLoop.DayEnding += this.OnDayEnding;
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.Content.AssetRequested += this.OnAssetRequested;
        helper.Events.Player.InventoryChanged += this.OnInventoryChanged;
        helper.Events.Player.Warped += this.OnWarped;
        helper.Events.World.ObjectListChanged += this.OnObjectListChanged;
        helper.Events.World.TerrainFeatureListChanged += this.OnTerrainFeatureListChanged;
        helper.Events.Multiplayer.PeerConnected += this.OnPeerConnected;
        helper.Events.Multiplayer.ModMessageReceived += this.OnModMessageReceived;
        helper.Events.Input.ButtonPressed += this.OnButtonPressed;
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        GenericModConfigMenuIntegration.Register(this.ModManifest, this.Helper, this.Config, this.ResetConfig, this.SaveConfig);
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        this.HasAuthoritativeState = this.IsEconomyAuthority;
        this.State = this.IsEconomyAuthority
            ? this.Helper.Data.ReadSaveData<LedgerSaveData>(SaveDataKey) ?? new LedgerSaveData()
            : new LedgerSaveData();
        this.State.EnsureValid();
        this.UseLocalEconomySettings();
        this.RebuildServices();

        if (this.Config.EnableTaxSystem && this.IsEconomyAuthority)
            this.Taxes.ReconcileAutomationTaxRules();

        if (this.IsDynamicPricingEnabled)
        {
            bool newSeason = this.DynamicPricing.EnsureSeasonState();
            if (newSeason && SDate.Now().Day == 1)
                Game1.addHUDMessage(new HUDMessage(this.GetSeasonStartMessage(), HUDMessage.newQuest_type));

            this.Helper.GameContent.InvalidateCache("Data/Objects");
            this.DynamicPricing.ApplyToPlayerInventory();
        }

        if (this.IsEconomyAuthority)
            this.SendEconomyState();
        else if (Context.IsMultiplayer)
            this.RequestEconomyState();
    }

    private void OnSaving(object? sender, SavingEventArgs e)
    {
        if (!this.IsEconomyAuthority)
            return;

        this.State.LastUpdatedOn = SDate.Now().ToString();
        this.Helper.Data.WriteSaveData(SaveDataKey, this.State);
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        if (!Context.IsWorldReady || !this.IsEconomyAuthority)
            return;

        if (this.IsDynamicPricingEnabled)
        {
            bool newSeason = this.DynamicPricing.EnsureSeasonState();
            if (newSeason)
                Game1.addHUDMessage(new HUDMessage(this.GetSeasonStartMessage(), HUDMessage.newQuest_type));

            this.DynamicPricing.RecoverMarketDemand();
            this.Helper.GameContent.InvalidateCache("Data/Objects");
            int updated = this.DynamicPricing.ApplyToPlayerInventory();
            updated += this.DynamicPricing.ApplyToLocations(this.GetLocationsToRefresh());
            this.State.LastDay.UpdatedStacks = updated;

            if (this.Config.EnableDailyLedger && this.State.LastDay.HadSales)
            {
                string pressure = this.State.LastDay.MarketPressure.ToString("P0");
                this.Monitor.Log(this.Helper.Translation.Get("message.day-summary", new { pressure, stacks = this.State.LastDay.TrackedStacks }), LogLevel.Info);
            }
        }

        if (this.Config.EnableTaxSystem)
            this.Taxes.CollectDueTaxes();

        this.SendEconomyState();
    }

    private void OnDayEnding(object? sender, DayEndingEventArgs e)
    {
        if (!Context.IsWorldReady || !this.IsEconomyAuthority)
            return;

        this.DailyLedger.CloseCurrentDay();

        if (this.Config.EnableTaxSystem)
            this.Taxes.AssessDailyTaxes();

        this.SendEconomyState();
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (e.NameWithoutLocale.IsEquivalentTo("Data/Machines"))
            ProductResolver.InvalidateMachineCache();

        if (!this.IsDynamicPricingEnabled)
            return;

        if (e.NameWithoutLocale.IsEquivalentTo("Data/Objects"))
            e.Edit(asset => this.DynamicPricing.EditObjectData(asset));
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        if (this.IsStaminaBalanceEnabled)
            this.Stamina.Update();
    }

    private void OnInventoryChanged(object? sender, InventoryChangedEventArgs e)
    {
        if (!Context.IsWorldReady || !this.IsDynamicPricingEnabled)
            return;

        if (this.IsEconomyAuthority)
        {
            foreach (Item? item in e.Added)
                this.DynamicPricing.TrackProducedItem(item);
        }

        if (e.IsLocalPlayer)
            this.DynamicPricing.ApplyToItems(e.Added);
    }

    private void OnWarped(object? sender, WarpedEventArgs e)
    {
        if (!Context.IsWorldReady || !e.IsLocalPlayer || !this.IsDynamicPricingEnabled)
            return;

        this.DynamicPricing.ApplyToLocationItems(e.NewLocation);
    }

    private void OnObjectListChanged(object? sender, ObjectListChangedEventArgs e)
    {
        if (Context.IsWorldReady && this.IsDynamicPricingEnabled)
            this.DynamicPricing.InvalidateApiaryQualityCache();
    }

    private void OnTerrainFeatureListChanged(object? sender, TerrainFeatureListChangedEventArgs e)
    {
        if (Context.IsWorldReady && this.IsDynamicPricingEnabled)
            this.DynamicPricing.InvalidateApiaryQualityCache();
    }

    private void OnPeerConnected(object? sender, PeerConnectedEventArgs e)
    {
        if (Context.IsWorldReady && this.IsEconomyAuthority)
            this.SendEconomyState(e.Peer.PlayerID);
    }

    private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
    {
        if (!string.Equals(e.FromModID, this.ModManifest.UniqueID, StringComparison.Ordinal))
            return;

        if (string.Equals(e.Type, StateRequestMessageType, StringComparison.Ordinal))
        {
            if (Context.IsWorldReady && this.IsEconomyAuthority)
                this.SendEconomyState(e.FromPlayerID);

            return;
        }

        if (!string.Equals(e.Type, StateSyncMessageType, StringComparison.Ordinal) || this.IsEconomyAuthority)
            return;

        IMultiplayerPeer? host = this.Helper.Multiplayer.GetConnectedPlayer(e.FromPlayerID);
        if (host is null || !host.IsHost)
            return;

        LedgerStateSyncMessage message = e.ReadAs<LedgerStateSyncMessage>();
        this.ApplyEconomyState(message);
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady || !this.Config.MenuKey.JustPressed())
            return;

        Game1.activeClickableMenu = new LedgerMenu(
            this.State,
            this.GetLedgerConfig(),
            this.LoadIconAtlas(),
            this.LoadPriceUpArrow(),
            this.LoadPriceDownArrow(),
            this.LoadTaxIcon(),
            this.LoadMainIcon(),
            this.Helper.Translation);
    }

    private void ResetConfig()
    {
        this.Config = new ModConfig();
        this.Config.EnsureValid();
        if (this.IsEconomyAuthority || !this.HasAuthoritativeState)
            this.UseLocalEconomySettings();

        this.RebuildServices();
    }

    private void SaveConfig()
    {
        this.Config.EnsureValid();
        this.Helper.WriteConfig(this.Config);
        if (this.IsEconomyAuthority || !this.HasAuthoritativeState)
            this.UseLocalEconomySettings();

        this.RebuildServices();
        if (Context.IsWorldReady && this.IsDynamicPricingEnabled)
        {
            this.DynamicPricing.InvalidateApiaryQualityCache();
            this.Helper.GameContent.InvalidateCache("Data/Objects");
            this.DynamicPricing.ApplyToPlayerInventory();
        }

        if (Context.IsWorldReady && this.IsEconomyAuthority)
            this.SendEconomyState();

        this.Monitor.Log(this.Helper.Translation.Get("message.config-reloaded"), LogLevel.Trace);
    }

    private void RebuildServices()
    {
        this.DynamicPricing = new DynamicPricingService(this.Monitor, this.EffectiveDynamicPricingConfig, this.State);
        this.DailyLedger = new DailyLedgerService(this.Monitor, this.Config, this.State, this.DynamicPricing);
        this.Taxes = new TaxService(this.Monitor, this.Config, this.State);
        this.Stamina = new StaminaService(this.Monitor, this.EffectiveStaminaConfig);
    }

    private void UseLocalEconomySettings()
    {
        this.Config.EnsureValid();
        this.EffectiveDynamicPricingConfig = this.Config.DynamicPricing;
        this.EffectiveStaminaConfig = this.Config.Stamina;
        this.HostDynamicPricingEnabled = this.Config.EnableDynamicPricing;
        this.HostTaxSystemEnabled = this.Config.EnableTaxSystem;
        this.HostStaminaBalanceEnabled = this.Config.EnableStaminaBalance;
    }

    private ModConfig GetLedgerConfig()
    {
        if (this.IsEconomyAuthority || !this.HasAuthoritativeState)
            return this.Config;

        return new ModConfig
        {
            EnableDynamicPricing = this.HostDynamicPricingEnabled,
            EnableDailyLedger = this.Config.EnableDailyLedger,
            EnableStaminaBalance = this.HostStaminaBalanceEnabled,
            EnableTaxSystem = this.HostTaxSystemEnabled,
            ShowFarmTaxOverview = this.Config.ShowFarmTaxOverview,
            MenuKey = this.Config.MenuKey,
            DynamicPricing = this.EffectiveDynamicPricingConfig,
            Stamina = this.EffectiveStaminaConfig,
            Taxes = this.Config.Taxes
        };
    }

    private IEnumerable<GameLocation> GetLocationsToRefresh()
    {
        foreach (GameLocation location in this.Helper.Multiplayer.GetActiveLocations())
            yield return location;

        if (Game1.currentLocation is not null)
            yield return Game1.currentLocation;

        yield return Game1.getFarm();
    }

    private void RequestEconomyState()
    {
        this.Helper.Multiplayer.SendMessage(
            "",
            StateRequestMessageType,
            modIDs: [this.ModManifest.UniqueID]);
    }

    private void SendEconomyState(long? playerId = null)
    {
        if (!this.HasAuthoritativeState)
            return;

        LedgerStateSyncMessage message = new()
        {
            State = this.State,
            EnableDynamicPricing = this.Config.EnableDynamicPricing,
            DynamicPricing = this.Config.DynamicPricing,
            EnableTaxSystem = this.Config.EnableTaxSystem,
            EnableStaminaBalance = this.Config.EnableStaminaBalance,
            Stamina = this.Config.Stamina
        };
        this.Helper.Multiplayer.SendMessage(
            message,
            StateSyncMessageType,
            modIDs: [this.ModManifest.UniqueID],
            playerIDs: playerId is null ? null : [playerId.Value]);
    }

    private void ApplyEconomyState(LedgerStateSyncMessage message)
    {
        this.State = message.State ?? new LedgerSaveData();
        this.State.EnsureValid();
        this.EffectiveDynamicPricingConfig = message.DynamicPricing ?? new DynamicPricingConfig();
        this.EffectiveStaminaConfig = message.Stamina ?? new StaminaConfig();
        this.HostDynamicPricingEnabled = message.EnableDynamicPricing;
        this.HostTaxSystemEnabled = message.EnableTaxSystem;
        this.HostStaminaBalanceEnabled = message.EnableStaminaBalance;
        this.HasAuthoritativeState = true;
        this.RebuildServices();

        if (!this.IsDynamicPricingEnabled)
            return;

        this.Helper.GameContent.InvalidateCache("Data/Objects");
        this.DynamicPricing.ApplyToPlayerInventory();
        if (Game1.currentLocation is not null)
            this.DynamicPricing.ApplyToLocationItems(Game1.currentLocation);
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

    private Texture2D? LoadPriceUpArrow()
    {
        if (this.PriceUpArrow is not null)
            return this.PriceUpArrow;

        try
        {
            this.PriceUpArrow = this.Helper.ModContent.Load<Texture2D>("icon/arrow_up.png");
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Could not load price increase arrow: {ex.Message}", LogLevel.Trace);
        }

        return this.PriceUpArrow;
    }

    private Texture2D? LoadPriceDownArrow()
    {
        if (this.PriceDownArrow is not null)
            return this.PriceDownArrow;

        try
        {
            this.PriceDownArrow = this.Helper.ModContent.Load<Texture2D>("icon/arrow_down.png");
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Could not load price decrease arrow: {ex.Message}", LogLevel.Trace);
        }

        return this.PriceDownArrow;
    }

    private Texture2D? LoadTaxIcon()
    {
        if (this.TaxIcon is not null)
            return this.TaxIcon;

        try
        {
            this.TaxIcon = this.Helper.ModContent.Load<Texture2D>("icon/Tax.png");
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Could not load tax icon: {ex.Message}", LogLevel.Trace);
        }

        return this.TaxIcon;
    }

    private Texture2D? LoadMainIcon()
    {
        if (this.MainIcon is not null)
            return this.MainIcon;

        try
        {
            this.MainIcon = this.Helper.ModContent.Load<Texture2D>("icon/Main.png");
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Could not load main menu icon: {ex.Message}", LogLevel.Trace);
        }

        return this.MainIcon;
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
