using HarvestLedger.Framework.Services;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.Objects;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Menus;
using SObject = StardewValley.Object;

namespace HarvestLedger.Framework.Menus;

public sealed class LedgerMenu : IClickableMenu
{
    private static readonly PriceFilter[] FilterOrder = [PriceFilter.Farm, PriceFilter.Fish, PriceFilter.Mine, PriceFilter.All];

    private const int SidePadding = 36;
    private const int SearchBoxHeight = 44;
    private const int StatusLineHeight = 22;
    private const int FilterButtonHeight = 46;
    private const int TableHeaderHeight = 34;
    private const int RowHeight = 54;
    private const int FooterHeight = 42;
    private const int ScrollbarWidth = 14;
    private const int ScrollbarGap = 10;
    private const int TaxIconDrawSize = 60;
    private const int TaxOverviewButtonSize = 68;
    private const int MainIconDrawSize = 64;

    private readonly LedgerSaveData State;
    private readonly ModConfig Config;
    private readonly ITranslationHelper Translation;
    private readonly Texture2D? IconAtlas;
    private readonly Texture2D? PriceUpArrow;
    private readonly Texture2D? PriceDownArrow;
    private readonly Texture2D? TaxIcon;
    private readonly Texture2D? MainIcon;
    private readonly Rectangle[] IconSources;
    private readonly List<PriceRow> Rows;
    private readonly Dictionary<PriceFilter, Rectangle> FilterBounds = new();
    private readonly TextBox SearchBox;

    private PriceFilter CurrentFilter = PriceFilter.Farm;
    private Rectangle PreviousPageBounds;
    private Rectangle NextPageBounds;
    private Rectangle ScrollbarTrackBounds;
    private Rectangle ScrollbarThumbBounds;
    private Rectangle SearchBounds;
    private Rectangle TaxOverviewButtonBounds;
    private Rectangle TaxOverviewWindowBounds;
    private IKeyboardSubscriber? PreviousKeyboardSubscriber;
    private PriceRow? HoveredRow;
    private bool DraggingScrollbar;
    private int ScrollbarDragOffsetY;
    private int Page;
    private string LastSearchText = "";
    private bool TaxOverviewOpen;

    public LedgerMenu(
        LedgerSaveData state,
        ModConfig config,
        Texture2D? iconAtlas,
        Texture2D? priceUpArrow,
        Texture2D? priceDownArrow,
        Texture2D? taxIcon,
        Texture2D? mainIcon,
        ITranslationHelper translation)
        : base(
            x: Math.Max(0, (Game1.uiViewport.Width - GetMenuWidth()) / 2),
            y: Math.Max(0, (Game1.uiViewport.Height - GetMenuHeight()) / 2),
            width: GetMenuWidth(),
            height: GetMenuHeight(),
            showUpperRightCloseButton: true)
    {
        this.State = state;
        this.Config = config;
        this.Translation = translation;
        this.IconAtlas = iconAtlas;
        this.PriceUpArrow = priceUpArrow;
        this.PriceDownArrow = priceDownArrow;
        this.TaxIcon = taxIcon;
        this.MainIcon = mainIcon;
        this.IconSources = BuildIconSources(iconAtlas);
        this.Rows = BuildPriceRows(state, config, translation);
        this.SearchBox = new TextBox(Game1.content.Load<Texture2D>("LooseSprites\\textBox"), Game1.staminaRect, Game1.smallFont, Game1.textColor)
        {
            Height = 44,
            Width = 300,
            textLimit = 40,
            limitWidth = true
        };
        this.SearchBox.OnEnterPressed += _ => this.DeselectSearchBox();
        this.UpdateButtonBounds();
        this.UpdateTaxOverviewButtonBounds();
        this.RepositionCloseButton();
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (this.upperRightCloseButton?.containsPoint(x, y) == true)
        {
            Game1.playSound("bigDeSelect");
            this.exitThisMenu();
            return;
        }

        if (this.TaxOverviewButtonBounds.Contains(x, y))
        {
            this.TaxOverviewOpen = !this.TaxOverviewOpen;
            Game1.playSound("smallSelect");
            return;
        }

        if (this.TaxOverviewOpen && this.TaxOverviewWindowBounds.Contains(x, y))
            return;

        this.TaxOverviewOpen = false;

        if (this.SearchBounds.Contains(x, y))
        {
            this.SelectSearchBox();
            return;
        }

        this.DeselectSearchBox();

        foreach ((PriceFilter filter, Rectangle bounds) in this.FilterBounds)
        {
            if (!bounds.Contains(x, y))
                continue;

            if (this.CurrentFilter != filter)
            {
                this.CurrentFilter = filter;
                this.Page = 0;
                Game1.playSound("smallSelect");
            }

            return;
        }

        if (this.ScrollbarThumbBounds.Contains(x, y))
        {
            this.DraggingScrollbar = true;
            this.ScrollbarDragOffsetY = y - this.ScrollbarThumbBounds.Y;
            return;
        }

        if (this.ScrollbarTrackBounds.Contains(x, y))
        {
            this.SetPageFromScrollbar(y - (this.ScrollbarThumbBounds.Height / 2));
            Game1.playSound("shwip");
            return;
        }

        int maxPage = this.GetMaxPage();
        if (this.PreviousPageBounds.Contains(x, y) && this.Page > 0)
        {
            this.Page--;
            Game1.playSound("shwip");
        }
        else if (this.NextPageBounds.Contains(x, y) && this.Page < maxPage)
        {
            this.Page++;
            Game1.playSound("shwip");
        }
    }

    public override void receiveKeyPress(Keys key)
    {
        if (this.SearchBox.Selected)
        {
            if (key is Keys.Enter or Keys.Escape)
                this.DeselectSearchBox();

            return;
        }

        base.receiveKeyPress(key);
    }

    public override void leftClickHeld(int x, int y)
    {
        if (!this.DraggingScrollbar)
            return;

        this.SetPageFromScrollbar(y - this.ScrollbarDragOffsetY);
    }

    public override void releaseLeftClick(int x, int y)
    {
        this.DraggingScrollbar = false;
    }

    public override void receiveScrollWheelAction(int direction)
    {
        int maxPage = this.GetMaxPage();
        if (direction > 0 && this.Page > 0)
        {
            this.Page--;
            Game1.playSound("shiny4");
        }
        else if (direction < 0 && this.Page < maxPage)
        {
            this.Page++;
            Game1.playSound("shiny4");
        }
    }

    public override void update(GameTime time)
    {
        base.update(time);

        this.SearchBox.Update();
        string currentSearch = this.SearchBox.Text ?? "";
        if (currentSearch == this.LastSearchText)
            return;

        this.LastSearchText = currentSearch;
        this.Page = 0;
    }

    protected override void cleanupBeforeExit()
    {
        this.DeselectSearchBox();
        base.cleanupBeforeExit();
    }

    public override void draw(SpriteBatch b)
    {
        this.RepositionCloseButton();
        this.HoveredRow = null;

        IClickableMenu.drawTextureBox(
            b,
            Game1.menuTexture,
            new Rectangle(0, 256, 60, 60),
            this.xPositionOnScreen,
            this.yPositionOnScreen,
            this.width,
            this.height,
            Color.White);

        this.DrawHeader(b);
        this.DrawPolicyPanels(b);
        this.DrawFilters(b);
        this.DrawPriceTable(b);
        this.DrawFooter(b);
        this.DrawTaxOverviewWindow(b);

        this.upperRightCloseButton?.draw(b);
        this.DrawHoverTooltip(b);
        this.drawMouse(b);
    }

    private void DrawHeader(SpriteBatch b)
    {
        int x = this.xPositionOnScreen + SidePadding;
        int y = this.yPositionOnScreen + 30;

        if (this.MainIcon is not null)
            b.Draw(this.MainIcon, new Rectangle(x, y, MainIconDrawSize, MainIconDrawSize), Color.White);
        else
            this.DrawLedgerIcon(b, LedgerIcon.MarketPressureGauge, x, y + 2, 44);

        int titleX = x + (this.MainIcon is not null ? MainIconDrawSize + 14 : 58);
        int titleY = this.MainIcon is not null ? y + ((MainIconDrawSize - Game1.dialogueFont.LineSpacing) / 2) : y;
        Utility.drawTextWithShadow(b, this.T("menu.title"), Game1.dialogueFont, new Vector2(titleX, titleY), Game1.textColor);

        string[] statusItems = this.GetStatusItems(this.GetTopPressureText());

        this.DrawStatusLine(b, this.GetStatusLines(statusItems), x, y + this.GetHeaderStatusTopOffset());
        this.DrawSearchBox(b, x, this.GetSearchTop());
    }

    private string[] GetStatusItems(string topPressure)
    {
        return
        [
            this.T("menu.status.prices", new { state = this.T(this.Config.EnableDynamicPricing ? "menu.state.on" : "menu.state.off") }),
            this.T("menu.status.sold", new { count = this.State.LastDay.SoldItemCount, gold = this.State.LastDay.GrossShippingIncome }),
            topPressure,
            this.GetTaxStatusText()
        ];
    }

    private string GetTopPressureText()
    {
        return string.IsNullOrWhiteSpace(this.State.LastDay.TopPressuredItemId)
            ? this.T("menu.status.top-pressure-none")
            : this.T("menu.status.top-pressure", new { item = GetDisplayNameFromItemId(this.State.LastDay.TopPressuredItemId), pressure = this.State.LastDay.TopPressuredItemPressure.ToString("P0") });
    }

    private string GetTaxStatusText()
    {
        if (!this.Config.EnableTaxSystem)
            return this.T("menu.status.tax-off");

        if (this.State.UsesPlayerTaxLedgers && this.State.PlayerTaxLedgers.TryGetValue(Game1.player.UniqueMultiplayerID, out PlayerTaxLedger? playerLedger))
        {
            int playerDue = playerLedger.PendingTaxes + playerLedger.UnpaidTaxes;
            string personalStatus = playerDue > 0
                ? this.T("menu.status.tax-personal-due", new { gold = playerDue })
                : playerLedger.LastCollectedTaxes > 0
                    ? this.T("menu.status.tax-personal-collected", new { gold = playerLedger.LastCollectedTaxes })
                    : playerLedger.LastAssessedTaxes > 0
                        ? this.T("menu.status.tax-personal-assessed", new { gold = playerLedger.LastAssessedTaxes })
                        : this.T("menu.status.tax-personal-none");

            if (!this.Config.ShowFarmTaxOverview)
                return personalStatus;

            int farmDue = this.State.TaxLedger.PendingTaxes + this.State.TaxLedger.UnpaidTaxes;
            return $"{personalStatus} · {this.T("menu.status.tax-farm-overview", new { gold = farmDue })}";
        }

        TaxLedger taxes = this.State.TaxLedger;
        int taxesDue = taxes.PendingTaxes + taxes.UnpaidTaxes;
        if (taxesDue > 0)
            return this.T("menu.status.tax-due", new { gold = taxesDue });

        if (taxes.LastCollectedTaxes > 0)
            return this.T("menu.status.tax-collected", new { gold = taxes.LastCollectedTaxes });

        if (taxes.LastAssessedTaxes > 0)
            return this.T("menu.status.tax-assessed", new { gold = taxes.LastAssessedTaxes });

        return this.T("menu.status.tax-none");
    }

    private void DrawPolicyPanels(SpriteBatch b)
    {
        int left = this.xPositionOnScreen + SidePadding;
        int top = this.yPositionOnScreen + this.GetHeaderHeight() + 10;
        int gap = 14;
        int panelWidth = (this.width - (SidePadding * 2) - gap) / 2;
        int right = left + panelWidth + gap;
        int panelHeight = this.GetPolicyPanelHeight(panelWidth);

        DrawMutedBox(b, left, top, panelWidth, panelHeight);
        DrawMutedBox(b, right, top, panelWidth, panelHeight);

        int panelHeaderHeight = this.GetPolicyPanelHeaderHeight();
        bool hasTaxOverview = this.Config.EnableTaxSystem && this.Config.ShowFarmTaxOverview;
        int titleY = hasTaxOverview ? top + ((panelHeaderHeight - Game1.smallFont.LineSpacing) / 2) : top + 10;
        int demandIconY = hasTaxOverview ? top + ((panelHeaderHeight - 28) / 2) : top + 8;
        this.DrawLedgerIcon(b, LedgerIcon.RegionalDemandNoticeBoard, left + 14, demandIconY, 28);
        Utility.drawTextWithShadow(b, this.T("menu.demand.title"), Game1.smallFont, new Vector2(left + 50, titleY), Game1.textColor);
        Utility.drawTextWithShadow(b, this.T("menu.policy.title"), Game1.smallFont, new Vector2(right + 14, titleY), Game1.textColor);
        this.DrawTaxOverviewButton(b);

        string[] demandLines = this.GetDemandLines(panelWidth - 28);
        for (int i = 0; i < demandLines.Length; i++)
            Utility.drawTextWithShadow(b, demandLines[i], Game1.smallFont, new Vector2(left + 14, top + panelHeaderHeight + (i * StatusLineHeight)), Game1.textColor * 0.88f);

        string[] policyLines = this.GetWrappedLines(this.GetPolicyItems(), panelWidth - 28);

        for (int i = 0; i < policyLines.Length; i++)
        {
            int lineY = top + panelHeaderHeight + (i * StatusLineHeight);
            Utility.drawTextWithShadow(b, policyLines[i], Game1.smallFont, new Vector2(right + 14, lineY), Game1.textColor * 0.88f);
        }
    }

    private string[] GetPolicyItems()
    {
        string subsidyCrop = string.IsNullOrWhiteSpace(this.State.SubsidizedCropItemId)
            ? this.T("menu.none")
            : GetLocalizedObjectName(this.State.SubsidizedCropItemId);
        int requiredSubsidyCrops = this.State.LastDay.SubsidyRequiredCropCount > 0
            ? this.State.LastDay.SubsidyRequiredCropCount
            : this.Config.DynamicPricing.GetSubsidyCropRequirement(this.State.LastDay.TotalCropCount);
        string progress = $"{this.State.LastDay.SubsidizedCropCount} / {requiredSubsidyCrops}";
        string mainCategory = this.GetDisplayCategoryFromSavedValue(this.State.LastDay.MainIncomeCategory);
        List<string> policyItems =
        [
            this.T("menu.policy.subsidy", new { crop = subsidyCrop, progress }),
            this.T("menu.policy.reduction", new { percent = this.State.SeasonalSubsidyTaxReduction.ToString("P0") }),
            this.T("menu.policy.exposure", new { state = this.GetExposureStateText(this.State.ExposureScore) }),
            this.T("menu.policy.main-income", new { category = mainCategory, percent = this.State.LastDay.MainIncomeCategoryShare.ToString("P0") }),
            this.T("menu.policy.recovery", new { percent = this.State.LastRecoveryRate.ToString("P0") })
        ];

        return policyItems.ToArray();
    }

    private void DrawTaxOverviewButton(SpriteBatch b)
    {
        this.UpdateTaxOverviewButtonBounds();
        if (this.TaxOverviewButtonBounds == Rectangle.Empty)
            return;

        bool hovered = this.TaxOverviewButtonBounds.Contains(Game1.getMouseX(), Game1.getMouseY());
        Color tint = this.TaxOverviewOpen || hovered ? Color.White : Color.White * 0.82f;
        Rectangle bounds = this.TaxOverviewButtonBounds;
        IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), bounds.X, bounds.Y, bounds.Width, bounds.Height, tint);
        Rectangle iconBounds = new(bounds.Center.X - (TaxIconDrawSize / 2), bounds.Center.Y - (TaxIconDrawSize / 2), TaxIconDrawSize, TaxIconDrawSize);
        if (this.TaxIcon is not null)
            b.Draw(this.TaxIcon, iconBounds, Color.White);
        else
            this.DrawLedgerIcon(b, LedgerIcon.TaxLedgerStamp, iconBounds.X, iconBounds.Y, iconBounds.Width);
    }

    private void DrawTaxOverviewWindow(SpriteBatch b)
    {
        if (!this.TaxOverviewOpen || this.TaxOverviewButtonBounds == Rectangle.Empty)
        {
            this.TaxOverviewWindowBounds = Rectangle.Empty;
            return;
        }

        string[] items = this.GetTaxBreakdownItems().Skip(1).ToArray();
        int maxContentWidth = Math.Max(180, Math.Min(420, this.width - 100));
        string[] lines = this.GetWrappedLines(items, maxContentWidth);
        int lineHeight = Math.Max(StatusLineHeight, Game1.smallFont.LineSpacing);
        int widestLine = lines.Length == 0
            ? 180
            : (int)Math.Ceiling(lines.Max(line => Game1.smallFont.MeasureString(line).X));
        int windowWidth = Math.Clamp(widestLine + 42, 250, Math.Min(460, this.width - 36));
        int windowHeight = 54 + (lines.Length * lineHeight) + 18;
        int windowX = Math.Clamp(
            this.TaxOverviewButtonBounds.Right - windowWidth,
            this.xPositionOnScreen + 18,
            this.xPositionOnScreen + this.width - windowWidth - 18);
        int windowY = this.TaxOverviewButtonBounds.Bottom + 8;
        int menuBottom = this.yPositionOnScreen + this.height - 18;
        if (windowY + windowHeight > menuBottom)
            windowY = Math.Max(this.yPositionOnScreen + 18, this.TaxOverviewButtonBounds.Y - windowHeight - 8);

        this.TaxOverviewWindowBounds = new Rectangle(windowX, windowY, windowWidth, windowHeight);
        IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), windowX, windowY, windowWidth, windowHeight, Color.White);
        this.DrawLedgerIcon(b, LedgerIcon.TaxLedgerStamp, windowX + 14, windowY + 12, 26);
        Utility.drawTextWithShadow(b, this.T("menu.tax-window.title"), Game1.smallFont, new Vector2(windowX + 50, windowY + 16), Game1.textColor);
        b.Draw(Game1.staminaRect, new Rectangle(windowX + 14, windowY + 45, windowWidth - 28, 2), Color.Black * 0.22f);

        for (int i = 0; i < lines.Length; i++)
            Utility.drawTextWithShadow(b, lines[i], Game1.smallFont, new Vector2(windowX + 18, windowY + 56 + (i * lineHeight)), Game1.textColor * 0.9f);
    }

    private IEnumerable<string> GetTaxBreakdownItems()
    {
        TaxLedger farmLedger = this.State.TaxLedger;
        if (this.State.UsesPlayerTaxLedgers && this.State.PlayerTaxLedgers.TryGetValue(Game1.player.UniqueMultiplayerID, out PlayerTaxLedger? playerLedger))
        {
            return this.CreateTaxBreakdownItems(
                playerLedger.LastShippingIncome,
                playerLedger.LastIncomeTax,
                playerLedger.LastLandUseTax,
                playerLedger.LastAutomationTax,
                playerLedger.LastSubsidyReduction,
                playerLedger.LastAssessedTaxes,
                playerLedger.PendingTaxes + playerLedger.UnpaidTaxes,
                playerLedger.LastUnpaidTaxPenalty,
                farmLedger.LastUsedTillableTiles,
                farmLedger.LastAutomationMachineCount);
        }

        return this.CreateTaxBreakdownItems(
            this.State.LastDay.GrossShippingIncome,
            farmLedger.LastIncomeTax,
            farmLedger.LastLandUseTax,
            farmLedger.LastAutomationTax,
            farmLedger.LastSubsidyReduction,
            farmLedger.LastAssessedTaxes,
            farmLedger.PendingTaxes + farmLedger.UnpaidTaxes,
            farmLedger.LastUnpaidTaxPenalty,
            farmLedger.LastUsedTillableTiles,
            farmLedger.LastAutomationMachineCount);
    }

    private IEnumerable<string> CreateTaxBreakdownItems(
        int shippingIncome,
        int incomeTax,
        int landUseTax,
        int automationTax,
        int subsidyReduction,
        int assessedTaxes,
        int taxesDue,
        int unpaidTaxPenalty,
        int usedTillableTiles,
        int automationMachineCount)
    {
        List<string> items =
        [
            this.T("menu.policy.tax-title"),
            this.T("menu.policy.tax-income", new { shipping = Math.Max(0, shippingIncome), tax = Math.Max(0, incomeTax) }),
            this.T("menu.policy.tax-land", new { tax = Math.Max(0, landUseTax), tiles = Math.Max(0, usedTillableTiles) }),
            this.T("menu.policy.tax-machines", new { tax = Math.Max(0, automationTax), machines = Math.Max(0, automationMachineCount) }),
            this.T("menu.policy.tax-total", new { subsidy = Math.Max(0, subsidyReduction), total = Math.Max(0, assessedTaxes) }),
            this.T("menu.policy.tax-outstanding", new { due = Math.Max(0, taxesDue) })
        ];

        if (unpaidTaxPenalty > 0)
            items.Add(this.T("menu.policy.tax-penalty", new { penalty = unpaidTaxPenalty }));

        return items;
    }

    private void DrawFilters(SpriteBatch b)
    {
        this.UpdateButtonBounds();

        foreach (PriceFilter filter in FilterOrder)
        {
            Rectangle bounds = this.FilterBounds[filter];
            bool selected = this.CurrentFilter == filter;
            Color tint = selected ? Color.White : Color.White * 0.72f;
            IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), bounds.X, bounds.Y, bounds.Width, bounds.Height, tint);

            string label = FitText(this.GetFilterLabel(filter), Game1.smallFont, bounds.Width - 14);
            Vector2 size = Game1.smallFont.MeasureString(label);
            Color color = selected ? Game1.textColor : Game1.unselectedOptionColor;
            Utility.drawTextWithShadow(
                b,
                label,
                Game1.smallFont,
                new Vector2(bounds.Center.X - (size.X / 2), bounds.Center.Y - (size.Y / 2) + 1),
                color);
        }
    }

    private void DrawPriceTable(SpriteBatch b)
    {
        IReadOnlyList<PriceRow> visibleRows = this.GetVisibleRows();
        int rowsPerPage = this.GetRowsPerPage();
        int maxPage = this.GetMaxPage(visibleRows.Count);
        this.Page = Math.Clamp(this.Page, 0, maxPage);

        int left = this.xPositionOnScreen + SidePadding;
        int top = this.GetTableTop();
        int right = this.xPositionOnScreen + this.width - SidePadding;
        int tableWidth = right - left;
        int tableHeight = TableHeaderHeight + (rowsPerPage * RowHeight);
        int contentRight = Math.Max(left + 100, right - ScrollbarWidth - ScrollbarGap);
        int contentWidth = contentRight - left;

        DrawMutedBox(b, left, top, tableWidth, tableHeight);

        bool compactColumns = tableWidth < 620;
        bool extraCompactColumns = tableWidth < 360;
        bool roomyColumns = tableWidth >= 760;
        int pressureColumnWidth = this.GetPressureColumnWidth(visibleRows, roomyColumns ? 132 : 96, roomyColumns ? 210 : 130);
        int iconX = left + 18;
        int nameX;
        int pressureX;
        int currentX;
        int baseX = 0;
        int nameWidth;

        if (compactColumns)
        {
            iconX = left + 12;
            nameX = left + 58;
            pressureX = extraCompactColumns ? 0 : contentRight - pressureColumnWidth;
            currentX = extraCompactColumns ? contentRight - 64 : pressureX - 108;
            nameWidth = Math.Max(50, currentX - nameX - (extraCompactColumns ? 8 : 38));

            this.DrawColumnHeader(b, this.T("menu.table.item"), nameX, top + 8);
            this.DrawColumnHeader(b, this.T("menu.table.now"), currentX, top + 8);
            if (!extraCompactColumns)
                this.DrawColumnHeader(b, FitText(this.T("menu.table.pressure"), Game1.smallFont, 106), pressureX, top + 8);
        }
        else
        {
            nameX = left + 78;
            pressureX = contentRight - pressureColumnWidth;
            currentX = pressureX - (roomyColumns ? 136 : 110);
            baseX = currentX - (roomyColumns ? 128 : 98);
            nameWidth = Math.Max(80, baseX - nameX - 14);

            this.DrawColumnHeader(b, this.T("menu.table.item"), nameX, top + 8);
            this.DrawColumnHeader(b, this.T("menu.table.base"), baseX, top + 8);
            this.DrawColumnHeader(b, this.T("menu.table.now"), currentX, top + 8);
            this.DrawColumnHeader(b, this.T("menu.table.pressure"), pressureX, top + 8);
        }
        this.DrawScrollbar(b, right - ScrollbarWidth, top + TableHeaderHeight + 4, tableHeight - TableHeaderHeight - 8, visibleRows.Count, rowsPerPage);

        if (visibleRows.Count == 0)
        {
            string empty = this.Rows.Count == 0
                ? this.T("menu.empty.no-tracked")
                : this.T("menu.empty.no-match");
            Utility.drawTextWithShadow(b, Game1.parseText(empty, Game1.smallFont, tableWidth - 40), Game1.smallFont, new Vector2(left + 20, top + 64), Game1.textColor);
            return;
        }

        int start = this.Page * rowsPerPage;
        IEnumerable<PriceRow> pageRows = visibleRows.Skip(start).Take(rowsPerPage);
        int rowIndex = 0;

        foreach (PriceRow row in pageRows)
        {
            int rowTop = top + TableHeaderHeight + (rowIndex * RowHeight);
            Color rowColor = rowIndex % 2 == 0 ? Color.White * 0.10f : Color.White * 0.04f;
            b.Draw(Game1.staminaRect, new Rectangle(left + 8, rowTop + 4, contentWidth - 8, RowHeight - 8), rowColor);

            row.Icon?.drawInMenu(b, new Vector2(iconX, rowTop + 1), 0.68f, 1f, 0.88f, StackDrawType.Hide, Color.White, false);
            Rectangle iconBounds = new(iconX, rowTop + 1, 44, 44);
            if (row.Icon is not null && iconBounds.Contains(Game1.getMouseX(), Game1.getMouseY()))
            {
                this.HoveredRow = row;
                b.Draw(Game1.staminaRect, iconBounds, Color.White * 0.12f);
            }

            string name = FitText(row.DisplayName, Game1.smallFont, nameWidth);
            string category = FitText(row.DisplayCategory, Game1.smallFont, nameWidth);
            Utility.drawTextWithShadow(b, name, Game1.smallFont, new Vector2(nameX, rowTop + 8), Game1.textColor);
            Utility.drawTextWithShadow(b, category, Game1.smallFont, new Vector2(nameX, rowTop + 31), Game1.textColor * 0.72f);

            if (!compactColumns)
                Utility.drawTextWithShadow(b, $"{row.BasePrice}g", Game1.smallFont, new Vector2(baseX, rowTop + 14), Game1.textColor);
            if (!extraCompactColumns)
                this.DrawPriceTrend(b, row, currentX - 28, rowTop + 15, 24);
            Utility.drawTextWithShadow(b, $"{row.CurrentPrice}g", Game1.smallFont, new Vector2(currentX, rowTop + 14), GetPriceColor(row));
            if (!extraCompactColumns)
                Utility.drawTextWithShadow(b, FitText(row.PressureText, Game1.smallFont, contentRight - pressureX - 4), Game1.smallFont, new Vector2(pressureX, rowTop + 14), Game1.textColor);

            rowIndex++;
        }
    }

    private void DrawHoverTooltip(SpriteBatch b)
    {
        if (this.HoveredRow is not PriceRow row)
            return;

        string[] lines =
        [
            this.T("menu.tooltip.quality-prices"),
            this.T("menu.tooltip.normal-price", new { gold = GetSalePriceForDisplay(row.ItemId, row.CurrentUnitPrice, 0) }),
            this.T("menu.tooltip.silver-price", new { gold = GetSalePriceForDisplay(row.ItemId, row.CurrentUnitPrice, 1) }),
            this.T("menu.tooltip.gold-price", new { gold = GetSalePriceForDisplay(row.ItemId, row.CurrentUnitPrice, 2) }),
            this.T("menu.tooltip.iridium-price", new { gold = GetSalePriceForDisplay(row.ItemId, row.CurrentUnitPrice, 4) }),
            this.T("menu.tooltip.pressure", new { pressure = row.Pressure.ToString("P0") }),
            this.T("menu.tooltip.yesterday-sold", new { count = row.LastSold }),
            this.T("menu.tooltip.lifetime-sold", new { count = row.LifetimeSold })
        ];

        float contentWidth = Math.Max(
            Game1.smallFont.MeasureString(row.DisplayName).X,
            lines.Max(line => Game1.smallFont.MeasureString(line).X));
        const int tooltipLineTop = 50;
        const int tooltipBottomPadding = 30;
        int tooltipLineHeight = Math.Max(22, Game1.smallFont.LineSpacing);
        int tooltipWidth = Math.Clamp((int)Math.Ceiling(contentWidth) + 40, 230, 420);
        int tooltipHeight = tooltipLineTop + (lines.Length * tooltipLineHeight) + tooltipBottomPadding;
        int mouseX = Game1.getMouseX();
        int mouseY = Game1.getMouseY();
        int tooltipX = mouseX + 28;
        int tooltipY = mouseY + 22;

        if (tooltipX + tooltipWidth > Game1.uiViewport.Width - 8)
            tooltipX = mouseX - tooltipWidth - 18;
        if (tooltipY + tooltipHeight > Game1.uiViewport.Height - 8)
            tooltipY = mouseY - tooltipHeight - 18;

        tooltipX = Math.Clamp(tooltipX, 8, Math.Max(8, Game1.uiViewport.Width - tooltipWidth - 8));
        tooltipY = Math.Clamp(tooltipY, 8, Math.Max(8, Game1.uiViewport.Height - tooltipHeight - 8));

        IClickableMenu.drawTextureBox(
            b,
            Game1.menuTexture,
            new Rectangle(0, 256, 60, 60),
            tooltipX,
            tooltipY,
            tooltipWidth,
            tooltipHeight,
            Color.White);

        Utility.drawTextWithShadow(b, row.DisplayName, Game1.smallFont, new Vector2(tooltipX + 20, tooltipY + 15), Game1.textColor);
        b.Draw(Game1.staminaRect, new Rectangle(tooltipX + 16, tooltipY + 41, tooltipWidth - 32, 2), Color.Black * 0.22f);

        for (int i = 0; i < lines.Length; i++)
        {
            Color textColor = i == 0 ? Game1.textColor * 0.78f : Game1.textColor;
            Utility.drawTextWithShadow(b, lines[i], Game1.smallFont, new Vector2(tooltipX + 20, tooltipY + tooltipLineTop + (i * tooltipLineHeight)), textColor);
        }
    }

    private void DrawFooter(SpriteBatch b)
    {
        IReadOnlyList<PriceRow> visibleRows = this.GetVisibleRows();
        int maxPage = this.GetMaxPage(visibleRows.Count);
        int y = this.yPositionOnScreen + this.height - FooterHeight - 6;
        int centerX = this.xPositionOnScreen + (this.width / 2);

        string pageText = visibleRows.Count == 0
            ? this.T("menu.footer.no-items")
            : this.T("menu.footer.items", new { count = visibleRows.Count, page = this.Page + 1, maxPage = maxPage + 1 });
        Vector2 pageSize = Game1.smallFont.MeasureString(pageText);
        Utility.drawTextWithShadow(b, pageText, Game1.smallFont, new Vector2(centerX - (pageSize.X / 2), y + 10), Game1.textColor);

        this.DrawPageButton(b, this.PreviousPageBounds, "<", this.Page > 0);
        this.DrawPageButton(b, this.NextPageBounds, ">", this.Page < maxPage);
    }

    private void DrawPageButton(SpriteBatch b, Rectangle bounds, string label, bool enabled)
    {
        Color tint = enabled ? Color.White : Color.White * 0.45f;
        IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), bounds.X, bounds.Y, bounds.Width, bounds.Height, tint);

        Vector2 size = Game1.dialogueFont.MeasureString(label);
        Utility.drawTextWithShadow(
            b,
            label,
            Game1.dialogueFont,
            new Vector2(bounds.Center.X - (size.X / 2), bounds.Center.Y - (size.Y / 2) - 2),
            enabled ? Game1.textColor : Game1.unselectedOptionColor);
    }

    private void DrawPriceTrend(SpriteBatch b, PriceRow row, int x, int y, int size)
    {
        Texture2D? arrow = row.CurrentPrice > row.BasePrice
            ? this.PriceUpArrow
            : row.CurrentPrice < row.BasePrice
                ? this.PriceDownArrow
                : null;

        if (arrow is not null)
        {
            b.Draw(arrow, new Rectangle(x, y, size, size), Color.White);
            return;
        }

        if (row.TrendIcon is not null)
            this.DrawLedgerIcon(b, row.TrendIcon.Value, x, y, size);
    }

    private void DrawColumnHeader(SpriteBatch b, string text, int x, int y)
    {
        Utility.drawTextWithShadow(b, text, Game1.smallFont, new Vector2(x, y), Game1.textColor * 0.72f);
    }

    private void DrawStatusLine(SpriteBatch b, IReadOnlyList<string> lines, int x, int y)
    {
        for (int i = 0; i < lines.Count; i++)
            Utility.drawTextWithShadow(b, lines[i], Game1.smallFont, new Vector2(x, y + (i * StatusLineHeight)), Game1.textColor * 0.86f);
    }

    private void DrawSearchBox(SpriteBatch b, int x, int y)
    {
        string label = this.T("menu.search.label");
        Utility.drawTextWithShadow(b, label, Game1.smallFont, new Vector2(x, y + 8), Game1.textColor * 0.86f);

        int boxX = x + 84;
        int boxWidth = Math.Min(380, this.xPositionOnScreen + this.width - SidePadding - boxX);
        this.SearchBox.X = boxX;
        this.SearchBox.Y = y;
        this.SearchBox.Width = Math.Max(80, boxWidth);
        this.SearchBox.Height = SearchBoxHeight;
        this.SearchBounds = new Rectangle(this.SearchBox.X, this.SearchBox.Y, this.SearchBox.Width, this.SearchBox.Height);

        this.SearchBox.Draw(b, false);

        if (string.IsNullOrWhiteSpace(this.SearchBox.Text) && !this.SearchBox.Selected)
        {
            Utility.drawTextWithShadow(
                b,
                this.T("menu.search.placeholder"),
                Game1.smallFont,
                new Vector2(this.SearchBox.X + 16, this.SearchBox.Y + 10),
                Game1.textColor * 0.48f);
        }
    }

    private void DrawScrollbar(SpriteBatch b, int x, int y, int height, int rowCount, int rowsPerPage)
    {
        this.ScrollbarTrackBounds = new Rectangle(x, y, ScrollbarWidth, height);

        int maxPage = this.GetMaxPage(rowCount);
        int thumbHeight = maxPage <= 0
            ? height
            : Math.Max(28, (int)Math.Round(height * Math.Clamp(rowsPerPage / (double)Math.Max(1, rowCount), 0.12, 1.0)));
        int travel = Math.Max(0, height - thumbHeight);
        int thumbY = maxPage <= 0
            ? y
            : y + (int)Math.Round(travel * (this.Page / (double)maxPage));

        this.ScrollbarThumbBounds = new Rectangle(x + 2, thumbY, ScrollbarWidth - 4, thumbHeight);

        b.Draw(Game1.staminaRect, this.ScrollbarTrackBounds, Color.Black * 0.18f);
        b.Draw(Game1.staminaRect, this.ScrollbarThumbBounds, maxPage <= 0 ? Color.White * 0.22f : Color.White * 0.48f);
        b.Draw(Game1.staminaRect, new Rectangle(this.ScrollbarThumbBounds.X, this.ScrollbarThumbBounds.Y, this.ScrollbarThumbBounds.Width, 2), Color.White * 0.35f);
    }

    private void SetPageFromScrollbar(int thumbY)
    {
        int maxPage = this.GetMaxPage();
        int travel = this.ScrollbarTrackBounds.Height - this.ScrollbarThumbBounds.Height;
        if (maxPage <= 0 || travel <= 0)
            return;

        int minY = this.ScrollbarTrackBounds.Y;
        int maxY = this.ScrollbarTrackBounds.Y + travel;
        double ratio = (Math.Clamp(thumbY, minY, maxY) - minY) / (double)travel;
        this.Page = Math.Clamp((int)Math.Round(ratio * maxPage), 0, maxPage);
    }

    private static void DrawMutedBox(SpriteBatch b, int x, int y, int width, int height)
    {
        b.Draw(Game1.staminaRect, new Rectangle(x, y, width, height), Color.Black * 0.12f);
        b.Draw(Game1.staminaRect, new Rectangle(x, y, width, 3), Color.Black * 0.18f);
        b.Draw(Game1.staminaRect, new Rectangle(x, y + height - 3, width, 3), Color.White * 0.14f);
    }

    private void RepositionCloseButton()
    {
        if (this.upperRightCloseButton is null)
            return;

        const int margin = 8;
        const int buttonSize = 48;
        int preferredX = this.xPositionOnScreen + this.width - buttonSize - margin;
        int preferredY = this.yPositionOnScreen + margin;
        int maxX = Math.Max(margin, Game1.uiViewport.Width - buttonSize - margin);
        int maxY = Math.Max(margin, Game1.uiViewport.Height - buttonSize - margin);

        this.upperRightCloseButton.bounds = new Rectangle(
            Math.Clamp(preferredX, margin, maxX),
            Math.Clamp(preferredY, margin, maxY),
            buttonSize,
            buttonSize);
    }

    private void SelectSearchBox()
    {
        if (!this.SearchBox.Selected)
            Game1.playSound("smallSelect");

        if (Game1.keyboardDispatcher.Subscriber != this.SearchBox)
            this.PreviousKeyboardSubscriber = Game1.keyboardDispatcher.Subscriber;

        Game1.keyboardDispatcher.Subscriber = this.SearchBox;
        this.SearchBox.SelectMe();
    }

    private void DeselectSearchBox()
    {
        if (!this.SearchBox.Selected && Game1.keyboardDispatcher.Subscriber != this.SearchBox)
            return;

        this.SearchBox.Selected = false;
        if (Game1.keyboardDispatcher.Subscriber == this.SearchBox)
            Game1.keyboardDispatcher.Subscriber = this.PreviousKeyboardSubscriber;

        this.PreviousKeyboardSubscriber = null;
    }

    private static int GetMenuWidth()
    {
        int available = Game1.uiViewport.Width - 24;
        return available < 560
            ? Math.Max(320, available)
            : Math.Min(980, available);
    }

    private static int GetMenuHeight()
    {
        int available = Game1.uiViewport.Height - 24;
        return available < 420
            ? Math.Max(320, available)
            : Math.Min(780, available);
    }

    private string[] GetDemandLines(int maxWidth)
    {
        int day = Game1.dayOfMonth;
        List<string> lines = new();
        foreach (DemandEventState demandEvent in this.State.DemandEvents.Where(demandEvent => demandEvent.IsActive(day)).OrderBy(demandEvent => demandEvent.StartDay).Take(2))
        {
            string categories = string.Join(", ", demandEvent.CategoryBonuses.Select(pair => $"{GetDisplayCategory(this.Translation, pair.Key)} +{pair.Value:P0}"));
            lines.AddRange(this.GetWrappedLines([this.GetDemandEventName(demandEvent)], maxWidth));
            lines.AddRange(this.GetWrappedLines([this.T("menu.demand.remaining", new { categories, days = demandEvent.GetRemainingDays(day) })], maxWidth));
        }

        if (lines.Count == 0)
        {
            DemandEventState? next = this.State.DemandEvents
                .Where(demandEvent => demandEvent.StartDay > day)
                .OrderBy(demandEvent => demandEvent.StartDay)
                .FirstOrDefault();

            if (next is null)
                lines.AddRange(this.GetWrappedLines([this.T("menu.demand.no-active")], maxWidth));
            else
            {
                lines.AddRange(this.GetWrappedLines([this.T("menu.demand.next", new { name = this.GetDemandEventName(next) })], maxWidth));
                lines.AddRange(this.GetWrappedLines([this.T("menu.demand.starts", new { day = next.StartDay })], maxWidth));
            }
        }

        return lines.ToArray();
    }

    private string GetExposureStateText(int exposureScore)
    {
        string key = exposureScore switch
        {
            <= 2 => "menu.exposure.stable",
            <= 6 => "menu.exposure.watch",
            <= 13 => "menu.exposure.exposed",
            _ => "menu.exposure.concentrated"
        };
        return this.T(key);
    }

    private string GetDisplayCategoryFromSavedValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return this.T("menu.none");

        return Enum.TryParse(value, out ItemMarketCategory category)
            ? GetDisplayCategory(this.Translation, category)
            : value;
    }

    private string GetDemandEventName(DemandEventState demandEvent)
    {
        string key = $"menu.demand-event.{demandEvent.Id}";
        string translated = this.T(key);
        return string.Equals(translated, key, StringComparison.Ordinal)
            ? demandEvent.Name
            : translated;
    }

    private string T(string key, object? tokens = null)
    {
        return T(this.Translation, key, tokens);
    }

    private static string T(ITranslationHelper translation, string key, object? tokens = null)
    {
        return translation.Get(key, tokens).ToString();
    }

    private static string FitText(string text, SpriteFont font, int maxWidth)
    {
        if (font.MeasureString(text).X <= maxWidth)
            return text;

        const string ellipsis = "...";
        string trimmed = text;
        while (trimmed.Length > 0 && font.MeasureString(trimmed + ellipsis).X > maxWidth)
            trimmed = trimmed[..^1];

        return trimmed.Length == 0 ? ellipsis : trimmed + ellipsis;
    }

    private int GetPressureColumnWidth(IReadOnlyList<PriceRow> rows, int minimum, int maximum)
    {
        float widestText = Game1.smallFont.MeasureString(this.T("menu.table.pressure")).X;
        foreach (PriceRow row in rows)
            widestText = Math.Max(widestText, Game1.smallFont.MeasureString(row.PressureText).X);

        return Math.Clamp((int)Math.Ceiling(widestText) + 14, minimum, maximum);
    }

    private IReadOnlyList<string> GetStatusLines(IReadOnlyList<string> items)
    {
        int maxWidth = Math.Max(80, this.width - (SidePadding * 2) - 36);
        const string gap = "   ";
        List<string> lines = new();
        string currentLine = "";

        foreach (string item in items.Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            if (Game1.smallFont.MeasureString(item).X > maxWidth)
            {
                if (!string.IsNullOrEmpty(currentLine))
                {
                    lines.Add(currentLine);
                    currentLine = "";
                }

                lines.AddRange(this.GetWrappedLines([item], maxWidth));
                continue;
            }

            string candidate = string.IsNullOrEmpty(currentLine) ? item : currentLine + gap + item;
            if (Game1.smallFont.MeasureString(candidate).X <= maxWidth)
            {
                currentLine = candidate;
                continue;
            }

            lines.Add(currentLine);
            currentLine = item;
        }

        if (!string.IsNullOrEmpty(currentLine))
            lines.Add(currentLine);

        return lines.Count > 0 ? lines : [""];
    }

    private string[] GetWrappedLines(IEnumerable<string> items, int maxWidth)
    {
        List<string> lines = new();
        foreach (string item in items)
        {
            string wrapped = Game1.parseText(item, Game1.smallFont, Math.Max(1, maxWidth));
            lines.AddRange(wrapped.Split('\n', StringSplitOptions.None).Select(line => line.TrimEnd('\r')));
        }

        return lines.Count > 0 ? lines.ToArray() : [""];
    }

    private void DrawLedgerIcon(SpriteBatch b, LedgerIcon icon, int x, int y, int size)
    {
        if (this.IconAtlas is null || this.IconAtlas.Width < 4 || this.IconAtlas.Height < 4 || size <= 0)
            return;

        int cellWidth = this.IconAtlas.Width / 4;
        int cellHeight = this.IconAtlas.Height / 4;
        int index = (int)icon;
        Rectangle source = index >= 0 && index < this.IconSources.Length && this.IconSources[index].Width > 0 && this.IconSources[index].Height > 0
            ? this.IconSources[index]
            : new Rectangle((index % 4) * cellWidth, (index / 4) * cellHeight, cellWidth, cellHeight);
        b.Draw(this.IconAtlas, new Rectangle(x, y, size, size), source, Color.White);
    }

    private static Rectangle[] BuildIconSources(Texture2D? atlas)
    {
        Rectangle[] sources = new Rectangle[16];
        if (atlas is null || atlas.Width < 4 || atlas.Height < 4)
            return sources;

        int cellWidth = atlas.Width / 4;
        int cellHeight = atlas.Height / 4;
        for (int i = 0; i < sources.Length; i++)
            sources[i] = new Rectangle((i % 4) * cellWidth, (i / 4) * cellHeight, cellWidth, cellHeight);

        try
        {
            Color[] pixels = new Color[atlas.Width * atlas.Height];
            atlas.GetData(pixels);

            for (int index = 0; index < sources.Length; index++)
            {
                int cellX = (index % 4) * cellWidth;
                int cellY = (index / 4) * cellHeight;
                int minX = cellWidth;
                int minY = cellHeight;
                int maxX = -1;
                int maxY = -1;

                for (int y = 0; y < cellHeight; y++)
                {
                    int sourceY = cellY + y;
                    for (int x = 0; x < cellWidth; x++)
                    {
                        int sourceX = cellX + x;
                        if (pixels[(sourceY * atlas.Width) + sourceX].A <= 8)
                            continue;

                        minX = Math.Min(minX, x);
                        minY = Math.Min(minY, y);
                        maxX = Math.Max(maxX, x);
                        maxY = Math.Max(maxY, y);
                    }
                }

                if (maxX < minX || maxY < minY)
                    continue;

                int padding = Math.Max(2, Math.Min(cellWidth, cellHeight) / 32);
                int left = Math.Max(0, minX - padding);
                int top = Math.Max(0, minY - padding);
                int right = Math.Min(cellWidth - 1, maxX + padding);
                int bottom = Math.Min(cellHeight - 1, maxY + padding);
                sources[index] = new Rectangle(cellX + left, cellY + top, right - left + 1, bottom - top + 1);
            }
        }
        catch
        {
            // Some texture implementations don't allow GetData after loading; the full cells still render correctly.
        }

        return sources;
    }

    private void UpdateButtonBounds()
    {
        int x = this.xPositionOnScreen + SidePadding;
        int headerHeight = this.GetHeaderHeight();
        int panelWidth = (this.width - (SidePadding * 2) - 14) / 2;
        int y = this.yPositionOnScreen + headerHeight + this.GetPolicyPanelHeight(panelWidth) + 26;
        int gap = 10;
        int buttonHeight = FilterButtonHeight;
        int availableWidth = this.width - (SidePadding * 2);
        int buttonWidth = Math.Max(48, (availableWidth - (gap * (FilterOrder.Length - 1))) / FilterOrder.Length);

        for (int i = 0; i < FilterOrder.Length; i++)
        {
            PriceFilter filter = FilterOrder[i];
            this.FilterBounds[filter] = new Rectangle(x + ((buttonWidth + gap) * i), y, buttonWidth, buttonHeight);
        }

        int footerY = this.yPositionOnScreen + this.height - FooterHeight - 6;
        this.PreviousPageBounds = new Rectangle(this.xPositionOnScreen + this.width - SidePadding - 116, footerY, 48, 42);
        this.NextPageBounds = new Rectangle(this.xPositionOnScreen + this.width - SidePadding - 58, footerY, 48, 42);
    }

    private void UpdateTaxOverviewButtonBounds()
    {
        if (!this.Config.EnableTaxSystem || !this.Config.ShowFarmTaxOverview)
        {
            this.TaxOverviewButtonBounds = Rectangle.Empty;
            this.TaxOverviewOpen = false;
            return;
        }

        int panelWidth = (this.width - (SidePadding * 2) - 14) / 2;
        int right = this.xPositionOnScreen + SidePadding + panelWidth + 14;
        int top = this.yPositionOnScreen + this.GetHeaderHeight() + 10;
        this.TaxOverviewButtonBounds = new Rectangle(right + panelWidth - TaxOverviewButtonSize - 8, top + 4, TaxOverviewButtonSize, TaxOverviewButtonSize);
    }

    private int GetTableTop()
    {
        int panelWidth = (this.width - (SidePadding * 2) - 14) / 2;
        return this.yPositionOnScreen + this.GetHeaderHeight() + this.GetPolicyPanelHeight(panelWidth) + this.GetFilterHeight() + 22;
    }

    private int GetHeaderHeight()
    {
        // The panels start ten pixels after this boundary, which leaves a two-pixel gap below the search box.
        return this.GetSearchTop() - this.yPositionOnScreen + SearchBoxHeight - 8;
    }

    private int GetSearchTop()
    {
        int statusLineCount = this.GetStatusLines(this.GetStatusItems(this.GetTopPressureText())).Count;
        return this.yPositionOnScreen + 30 + this.GetHeaderStatusTopOffset() + (statusLineCount * StatusLineHeight) + 6;
    }

    private int GetHeaderStatusTopOffset()
    {
        return this.MainIcon is not null ? MainIconDrawSize + 4 : 48;
    }

    private int GetPolicyPanelHeight(int panelWidth)
    {
        int contentWidth = Math.Max(80, panelWidth - 28);
        int demandLineCount = this.GetDemandLines(contentWidth).Length;
        int policyLineCount = this.GetWrappedLines(this.GetPolicyItems(), contentWidth).Length;
        int lineCount = Math.Max(5, Math.Max(demandLineCount, policyLineCount));
        return this.GetPolicyPanelHeaderHeight() + (lineCount * StatusLineHeight) + 8;
    }

    private int GetPolicyPanelHeaderHeight()
    {
        return this.Config.EnableTaxSystem && this.Config.ShowFarmTaxOverview
            ? TaxOverviewButtonSize + 8
            : 38;
    }

    private int GetFilterHeight()
    {
        return FilterButtonHeight + 8;
    }

    private int GetRowsPerPage()
    {
        int tableTop = this.GetTableTop();
        int footerTop = this.yPositionOnScreen + this.height - FooterHeight - 6;
        return Math.Max(1, (footerTop - tableTop - TableHeaderHeight) / RowHeight);
    }

    private int GetMaxPage()
    {
        return this.GetMaxPage(this.GetVisibleRows().Count);
    }

    private int GetMaxPage(int rowCount)
    {
        return Math.Max(0, (int)Math.Ceiling(rowCount / (double)this.GetRowsPerPage()) - 1);
    }

    private IReadOnlyList<PriceRow> GetVisibleRows()
    {
        IEnumerable<PriceRow> rows = this.CurrentFilter switch
        {
            PriceFilter.Farm => this.Rows.Where(row => row.IsFarmProduct),
            PriceFilter.Fish => this.Rows.Where(row => row.Category == ItemMarketCategory.Fish),
            PriceFilter.Mine => this.Rows.Where(row => row.Category is ItemMarketCategory.Mining or ItemMarketCategory.MonsterLoot),
            _ => this.Rows
        };

        string query = (this.SearchBox.Text ?? "").Trim();
        if (query.Length == 0)
            return rows.ToList();

        string[] terms = SearchTextUtility.GetQueryTerms(query);
        return rows
            .Where(row => terms.All(term => row.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private string GetFilterLabel(PriceFilter filter)
    {
        string key = filter switch
        {
            PriceFilter.Farm => "menu.filter.farm",
            PriceFilter.Fish => "menu.filter.fish",
            PriceFilter.Mine => "menu.filter.mine",
            _ => "menu.filter.all"
        };
        return this.T(key);
    }

    private static Color GetPriceColor(PriceRow row)
    {
        if (row.CurrentPrice > row.BasePrice)
            return new Color(35, 110, 45);

        if (row.CurrentPrice < row.BasePrice)
            return new Color(135, 45, 35);

        return Game1.textColor;
    }

    private static string GetPressureText(ITranslationHelper translation, double pressureValue, int lastSold)
    {
        string pressure = pressureValue <= 0 ? "0%" : pressureValue.ToString("P0");
        string trend = lastSold > 0
            ? T(translation, "menu.pressure.sold")
            : pressureValue > 0
                ? T(translation, "menu.pressure.recover")
                : T(translation, "menu.pressure.flat");

        return lastSold > 0
            ? $"{pressure} {trend} ({lastSold})"
            : $"{pressure} {trend}";
    }

    private static List<PriceRow> BuildPriceRows(LedgerSaveData state, ModConfig config, ITranslationHelper translation)
    {
        List<PriceRow> rows = new();
        HashSet<string> rowIds = new(StringComparer.OrdinalIgnoreCase);

        IEnumerable<(string ItemId, int BasePrice)> priceEntries = state.BasePricesByItemId
            .Select(pair => (ItemId: pair.Key, BasePrice: pair.Value))
            .Concat(Game1.objectData
                .Where(pair => pair.Value.Price > 0)
                .Select(pair => (ItemId: $"(O){pair.Key}", BasePrice: pair.Value.Price)));

        foreach ((string savedItemId, int basePrice) in priceEntries)
        {
            if (basePrice <= 0)
                continue;

            string canonicalItemId = GetCanonicalMarketItemId(savedItemId);
            if (rowIds.Contains(canonicalItemId))
                continue;

            string rawItemId = GetRawObjectId(canonicalItemId);
            string qualifiedItemId = $"(O){rawItemId}";
            if (config.DynamicPricing.ExemptItemIds.Contains(rawItemId) || config.DynamicPricing.ExemptItemIds.Contains(qualifiedItemId))
                continue;

            if (!Game1.objectData.TryGetValue(rawItemId, out ObjectData? objectData))
                continue;

            int rowBasePrice = !string.Equals(savedItemId, canonicalItemId, StringComparison.OrdinalIgnoreCase)
                ? state.BasePricesByItemId.GetValueOrDefault(canonicalItemId, basePrice)
                : basePrice;
            Item? icon = TryCreateDisplayItem(canonicalItemId) ?? TryCreateIcon(qualifiedItemId);
            string displayName = GetMarketDisplayName(canonicalItemId, objectData, icon);

            ItemMarketCategory category = ItemClassifier.GetCategory(objectData);
            int displayQuality = GetDisplayQuality(state, savedItemId, canonicalItemId, qualifiedItemId, rawItemId);
            string displayCategory = AppendQualityLabel(translation, GetDisplayCategory(translation, objectData, category), displayQuality);
            int currentBasePrice = Math.Max(1, GetCurrentPrice(state, savedItemId, canonicalItemId, qualifiedItemId, rawItemId, objectData.Price));
            int displayBasePrice = GetSalePriceForDisplay(canonicalItemId, Math.Max(1, rowBasePrice), displayQuality);
            int currentPrice = GetSalePriceForDisplay(canonicalItemId, currentBasePrice, displayQuality);
            double rawPressure = state.MarketPressureByItemId.TryGetValue(savedItemId, out double exactPressure)
                ? exactPressure
                : GetValue(state.MarketPressureByItemId, canonicalItemId, qualifiedItemId, rawItemId);
            double pricePressure = Math.Clamp(1 - Math.Exp(-rawPressure / Math.Max(1, config.DynamicPricing.SaturationPoint)), 0, 1);
            int lastSold = state.LastDay.SoldByItemId.TryGetValue(savedItemId, out int exactLastSold)
                ? exactLastSold
                : GetValue(state.LastDay.SoldByItemId, canonicalItemId, qualifiedItemId, rawItemId);
            int lifetimeSold = state.LifetimeSoldByItemId.TryGetValue(savedItemId, out int exactLifetimeSold)
                ? exactLifetimeSold
                : GetValue(state.LifetimeSoldByItemId, canonicalItemId, qualifiedItemId, rawItemId);
            string searchText = SearchTextUtility.Build(displayName, objectData.Name, GetIngredientSearchText(canonicalItemId), displayCategory, category.ToString(), rawItemId, qualifiedItemId, canonicalItemId, savedItemId);

            PriceRow row = new(
                canonicalItemId,
                displayName,
                displayCategory,
                category,
                IsFarmProduct(objectData, category),
                searchText,
                icon,
                displayBasePrice,
                currentPrice,
                currentBasePrice,
                pricePressure,
                lastSold,
                lifetimeSold,
                GetPressureText(translation, pricePressure, lastSold));
            rows.Add(row);
            rowIds.Add(canonicalItemId);
        }

        foreach (PriceRow row in BuildGeneratedProcessedRows(state, config, rowIds, translation))
        {
            rows.Add(row);
            rowIds.Add(row.ItemId);
        }

        return rows
            .OrderBy(row => GetCategorySort(row))
            .ThenByDescending(row => row.Pressure)
            .ThenBy(row => row.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static Item? TryCreateIcon(string qualifiedItemId)
    {
        try
        {
            return ItemRegistry.Create(qualifiedItemId, 1, 0, true);
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<PriceRow> BuildGeneratedProcessedRows(LedgerSaveData state, ModConfig config, ISet<string> existingRowIds, ITranslationHelper translation)
    {
        if (Game1.objectData is null)
            yield break;

        foreach (SObject outputObject in GetGeneratedMachineOutputs(config))
        {
            string marketItemId = GetCanonicalMarketItemId(MarketItemIdentity.GetMarketItemId(outputObject));
            string baseItemId = MarketItemIdentity.GetBaseItemId(marketItemId);
            string rawOutputId = GetRawObjectId(marketItemId);
            if (existingRowIds.Contains(marketItemId) || config.DynamicPricing.ExemptItemIds.Contains(baseItemId) || config.DynamicPricing.ExemptItemIds.Contains(rawOutputId))
                continue;

            if (!Game1.objectData.TryGetValue(rawOutputId, out ObjectData? outputData))
                continue;

            int generatedBasePrice = Math.Max(1, outputObject.Price > 0 ? outputObject.Price : outputData.Price);
            int basePrice = state.BasePricesByItemId.TryGetValue(marketItemId, out int trackedBasePrice) && trackedBasePrice > 0
                ? trackedBasePrice
                : generatedBasePrice;
            int currentBasePrice = state.LastCalculatedPricesByItemId.TryGetValue(marketItemId, out int trackedCurrentPrice) && trackedCurrentPrice > 0
                ? trackedCurrentPrice
                : basePrice;
            double rawPressure = state.MarketPressureByItemId.GetValueOrDefault(marketItemId);
            double pricePressure = Math.Clamp(1 - Math.Exp(-rawPressure / Math.Max(1, config.DynamicPricing.SaturationPoint)), 0, 1);
            int lastSold = state.LastDay.SoldByItemId.GetValueOrDefault(marketItemId);
            int lifetimeSold = state.LifetimeSoldByItemId.GetValueOrDefault(marketItemId);
            string displayName = !string.IsNullOrWhiteSpace(outputObject.DisplayName)
                ? outputObject.DisplayName
                : GetMarketDisplayName(marketItemId, outputData, outputObject);
            ItemMarketCategory category = ItemClassifier.GetCategory(outputObject);
            string qualifiedOutputId = $"(O){rawOutputId}";
            int displayQuality = GetDisplayQuality(state, marketItemId, marketItemId, qualifiedOutputId, rawOutputId);
            string displayCategory = AppendQualityLabel(translation, GetDisplayCategory(translation, outputData, category), displayQuality);
            string searchText = SearchTextUtility.Build(displayName, outputData.Name, GetIngredientSearchText(marketItemId), displayCategory, category.ToString(), baseItemId, marketItemId);

            yield return new PriceRow(
                marketItemId,
                displayName,
                displayCategory,
                category,
                IsFarmProduct(outputData, category),
                searchText,
                outputObject,
                GetSalePriceForDisplay(marketItemId, basePrice, displayQuality),
                GetSalePriceForDisplay(marketItemId, currentBasePrice, displayQuality),
                currentBasePrice,
                pricePressure,
                lastSold,
                lifetimeSold,
                GetPressureText(translation, pricePressure, lastSold));
        }
    }

    private static IEnumerable<SObject> GetGeneratedMachineOutputs(ModConfig config)
    {
        if (Game1.objectData is null)
            yield break;

        HashSet<string> seenOutputIds = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string rawIngredientId, ObjectData ingredientData) in Game1.objectData)
        {
            string ingredientItemId = $"(O){rawIngredientId}";
            if (ingredientData.Price <= 0 || config.DynamicPricing.ExemptItemIds.Contains(rawIngredientId) || config.DynamicPricing.ExemptItemIds.Contains(ingredientItemId))
                continue;

            if (!IsPotentialMachineInput(ingredientData))
                continue;

            if (TryCreateIcon(ingredientItemId) is not SObject inputObject)
                continue;

            inputObject.Stack = int.MaxValue;
            foreach (SObject outputObject in ProductResolver.GetMachineOutputsForInput(inputObject))
            {
                if (outputObject.bigCraftable.Value || outputObject.Price <= 0)
                    continue;

                if (!ShouldShowGeneratedOutput(ingredientData, outputObject))
                    continue;

                string marketItemId = GetCanonicalMarketItemId(MarketItemIdentity.GetMarketItemId(outputObject));
                if (string.IsNullOrWhiteSpace(marketItemId) || !seenOutputIds.Add(marketItemId))
                    continue;

                yield return outputObject;
            }
        }
    }

    private static bool IsPotentialMachineInput(ObjectData ingredientData)
    {
        ItemMarketCategory category = ItemClassifier.GetCategory(ingredientData);
        return IsFarmProduct(ingredientData, category)
            || category is ItemMarketCategory.Forage or ItemMarketCategory.Fish or ItemMarketCategory.Mining or ItemMarketCategory.MonsterLoot;
    }

    private static bool ShouldShowGeneratedOutput(ObjectData ingredientData, SObject outputObject)
    {
        ItemMarketCategory inputCategory = ItemClassifier.GetCategory(ingredientData);
        ItemMarketCategory outputCategory = ItemClassifier.GetCategory(outputObject);
        if (MarketItemIdentity.IsProcessedItemId(MarketItemIdentity.GetMarketItemId(outputObject)))
            return true;

        return outputCategory is ItemMarketCategory.ArtisanGoods or ItemMarketCategory.Cooking
            || (IsFarmProduct(ingredientData, inputCategory) && outputCategory is ItemMarketCategory.AnimalProduct or ItemMarketCategory.Seed);
    }

    private static string GetRawObjectId(string itemId)
    {
        return MarketItemIdentity.GetRawObjectId(itemId);
    }

    private static int GetCurrentPrice(LedgerSaveData state, string savedItemId, string canonicalItemId, string qualifiedItemId, string rawItemId, int fallbackPrice)
    {
        if (state.LastCalculatedPricesByItemId.TryGetValue(savedItemId, out int exactPrice))
            return exactPrice;

        return GetValue(state.LastCalculatedPricesByItemId, canonicalItemId, qualifiedItemId, rawItemId) is int price && price > 0
            ? price
            : fallbackPrice;
    }

    private static int GetDisplayQuality(LedgerSaveData state, string savedItemId, string canonicalItemId, string qualifiedItemId, string rawItemId)
    {
        if (state.LastSoldQualityByItemId.TryGetValue(savedItemId, out int exactQuality))
            return NormalizeQuality(exactQuality);

        return NormalizeQuality(GetValue(state.LastSoldQualityByItemId, canonicalItemId, qualifiedItemId, rawItemId));
    }

    private static int GetSalePriceForDisplay(string marketItemId, int unitPrice, int quality)
    {
        if (TryCreateDisplayItem(marketItemId) is SObject obj)
        {
            obj.Stack = 1;
            obj.Price = Math.Max(1, unitPrice);
            obj.Quality = NormalizeQuality(quality);
            return Math.Max(1, obj.sellToStorePrice());
        }

        return Math.Max(1, unitPrice);
    }

    private static string AppendQualityLabel(ITranslationHelper translation, string category, int quality)
    {
        string key = NormalizeQuality(quality) switch
        {
            1 => "menu.quality.silver",
            2 => "menu.quality.gold",
            4 => "menu.quality.iridium",
            _ => ""
        };

        return string.IsNullOrWhiteSpace(key)
            ? category
            : $"{category} {T(translation, key)}";
    }

    private static int NormalizeQuality(int quality)
    {
        if (quality >= 4)
            return 4;

        if (quality >= 2)
            return 2;

        return quality >= 1 ? 1 : 0;
    }

    private static int GetValue(IReadOnlyDictionary<string, int> values, string canonicalItemId, string qualifiedItemId, string rawItemId)
    {
        if (values.TryGetValue(canonicalItemId, out int canonicalValue))
            return canonicalValue;

        if (values.TryGetValue(qualifiedItemId, out int qualifiedValue))
            return qualifiedValue;

        return values.GetValueOrDefault(rawItemId);
    }

    private static double GetValue(IReadOnlyDictionary<string, double> values, string canonicalItemId, string qualifiedItemId, string rawItemId)
    {
        if (values.TryGetValue(canonicalItemId, out double canonicalValue))
            return canonicalValue;

        if (values.TryGetValue(qualifiedItemId, out double qualifiedValue))
            return qualifiedValue;

        return values.GetValueOrDefault(rawItemId);
    }

    private static string GetDisplayCategory(ITranslationHelper translation, ObjectData objectData, ItemMarketCategory category)
    {
        return objectData.Category switch
        {
            -27 => T(translation, "menu.category.syrup"),
            _ => GetDisplayCategory(translation, category)
        };
    }

    private static string GetDisplayCategory(ITranslationHelper translation, ItemMarketCategory category)
    {
        string key = category switch
        {
            ItemMarketCategory.Seed => "menu.category.seed",
            ItemMarketCategory.Vegetable => "menu.category.vegetable",
            ItemMarketCategory.Fruit => "menu.category.fruit",
            ItemMarketCategory.Flower => "menu.category.flower",
            ItemMarketCategory.Forage => "menu.category.forage",
            ItemMarketCategory.Fish => "menu.category.fish",
            ItemMarketCategory.AnimalProduct => "menu.category.animal-product",
            ItemMarketCategory.ArtisanGoods => "menu.category.artisan",
            ItemMarketCategory.Cooking => "menu.category.cooking",
            ItemMarketCategory.Mining => "menu.category.mining",
            ItemMarketCategory.MonsterLoot => "menu.category.monster-loot",
            _ => "menu.category.other"
        };
        return T(translation, key);
    }

    private static string GetDisplayNameFromItemId(string itemId)
    {
        string canonicalItemId = GetCanonicalMarketItemId(itemId);
        string rawItemId = GetRawObjectId(canonicalItemId);
        if (Game1.objectData.TryGetValue(rawItemId, out ObjectData? objectData))
            return GetMarketDisplayName(canonicalItemId, objectData, TryCreateDisplayItem(canonicalItemId) ?? TryCreateIcon(MarketItemIdentity.GetBaseItemId(canonicalItemId)));

        return rawItemId;
    }

    private static string GetMarketDisplayName(string marketItemId, ObjectData objectData, Item? baseIcon)
    {
        string baseName = baseIcon?.DisplayName ?? objectData.Name ?? GetRawObjectId(marketItemId);
        if (!MarketItemIdentity.IsProcessedItemId(marketItemId))
            return baseName;

        SObject? flavoredItem = TryCreateOfficialFlavoredItem(marketItemId);
        return !string.IsNullOrWhiteSpace(flavoredItem?.DisplayName)
            ? flavoredItem.DisplayName
            : baseName;
    }

    private static string GetCanonicalMarketItemId(string marketItemId)
    {
        if (!MarketItemIdentity.IsProcessedItemId(marketItemId))
            return MarketItemIdentity.GetBaseItemId(marketItemId);

        return TryCreateOfficialFlavoredItem(marketItemId) is not null
            ? marketItemId
            : MarketItemIdentity.GetBaseItemId(marketItemId);
    }

    private static Item? TryCreateDisplayItem(string marketItemId)
    {
        if (MarketItemIdentity.IsProcessedItemId(marketItemId) && TryCreateOfficialFlavoredItem(marketItemId) is SObject flavoredItem)
            return flavoredItem;

        return TryCreateIcon(MarketItemIdentity.GetBaseItemId(marketItemId));
    }

    private static SObject? TryCreateOfficialFlavoredItem(string marketItemId)
    {
        string ingredientItemId = MarketItemIdentity.GetIngredientItemId(marketItemId);
        if (string.IsNullOrWhiteSpace(ingredientItemId) || TryCreateIcon(ingredientItemId) is not SObject ingredientObject)
            return null;

        string baseItemId = MarketItemIdentity.GetBaseItemId(marketItemId);
        ObjectDataDefinition objectDefinition = ItemRegistry.GetObjectTypeDefinition();
        foreach (SObject.PreserveType preserveType in Enum.GetValues<SObject.PreserveType>())
        {
            foreach (string ingredientCandidateId in new[] { ingredientItemId, GetRawObjectId(ingredientItemId) }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    string flavoredBaseItemId = MarketItemIdentity.NormalizeObjectId(objectDefinition.GetBaseItemIdForFlavoredItem(preserveType, ingredientCandidateId));
                    if (!string.Equals(flavoredBaseItemId, baseItemId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    return objectDefinition.CreateFlavoredItem(preserveType, ingredientObject);
                }
                catch
                {
                    // Some preserve types don't apply to some ingredients.
                }
            }
        }

        return null;
    }

    private static string GetIngredientSearchText(string marketItemId)
    {
        string ingredientItemId = MarketItemIdentity.GetIngredientItemId(marketItemId);
        return string.IsNullOrWhiteSpace(ingredientItemId)
            ? ""
            : GetLocalizedObjectName(ingredientItemId);
    }

    private static string GetLocalizedObjectName(string itemId)
    {
        try
        {
            Item item = ItemRegistry.Create(itemId, 1, 0, true);
            if (!string.IsNullOrWhiteSpace(item.DisplayName))
                return item.DisplayName;
        }
        catch
        {
            // Fall back to object data below.
        }

        string rawItemId = GetRawObjectId(itemId);
        return Game1.objectData.TryGetValue(rawItemId, out ObjectData? objectData)
            ? objectData.Name ?? rawItemId
            : rawItemId;
    }

    private static bool IsFarmProduct(ObjectData objectData, ItemMarketCategory category)
    {
        return category is ItemMarketCategory.Seed
                or ItemMarketCategory.Vegetable
                or ItemMarketCategory.Fruit
                or ItemMarketCategory.Flower
                or ItemMarketCategory.AnimalProduct
                or ItemMarketCategory.ArtisanGoods
                or ItemMarketCategory.Cooking
            || objectData.Category is -5 or -6 or -27;
    }

    private static int GetCategorySort(PriceRow row)
    {
        if (row.IsFarmProduct)
            return row.Category switch
            {
                ItemMarketCategory.Vegetable => 0,
                ItemMarketCategory.Fruit => 1,
                ItemMarketCategory.Flower => 2,
                ItemMarketCategory.AnimalProduct => 3,
                ItemMarketCategory.ArtisanGoods => 4,
                ItemMarketCategory.Cooking => 5,
                ItemMarketCategory.Seed => 6,
                _ => 6
            };

        return row.Category switch
        {
            ItemMarketCategory.Fish => 20,
            ItemMarketCategory.Forage => 30,
            ItemMarketCategory.Mining => 40,
            ItemMarketCategory.MonsterLoot => 41,
            _ => 90
        };
    }

    private enum PriceFilter
    {
        Farm,
        Fish,
        Mine,
        All
    }

    private sealed record PriceRow(
        string ItemId,
        string DisplayName,
        string DisplayCategory,
        ItemMarketCategory Category,
        bool IsFarmProduct,
        string SearchText,
        Item? Icon,
        int BasePrice,
        int CurrentPrice,
        int CurrentUnitPrice,
        double Pressure,
        int LastSold,
        int LifetimeSold,
        string PressureText)
    {
        public LedgerIcon? TrendIcon
        {
            get
            {
                if (this.CurrentPrice > this.BasePrice)
                    return LedgerIcon.PriceTrendUpArrow;

                if (this.CurrentPrice < this.BasePrice)
                    return LedgerIcon.PriceTrendDownArrow;

                return null;
            }
        }
    }
}
