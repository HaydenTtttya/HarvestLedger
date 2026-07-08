using HarvestLedger.Framework.Services;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.GameData.Objects;
using StardewValley.Menus;

namespace HarvestLedger.Framework.Menus;

public sealed class LedgerMenu : IClickableMenu
{
    private static readonly PriceFilter[] FilterOrder = [PriceFilter.Farm, PriceFilter.Fish, PriceFilter.Mine, PriceFilter.All];

    private const int SidePadding = 36;
    private const int HeaderHeight = 128;
    private const int FilterHeight = 56;
    private const int TableHeaderHeight = 34;
    private const int RowHeight = 54;
    private const int FooterHeight = 42;
    private const int ScrollbarWidth = 14;
    private const int ScrollbarGap = 10;
    private const float LabelTextScale = 0.78f;

    private readonly LedgerSaveData State;
    private readonly ModConfig Config;
    private readonly List<PriceRow> Rows;
    private readonly Dictionary<PriceFilter, Rectangle> FilterBounds = new();
    private readonly TextBox SearchBox;

    private PriceFilter CurrentFilter = PriceFilter.Farm;
    private Rectangle PreviousPageBounds;
    private Rectangle NextPageBounds;
    private Rectangle ScrollbarTrackBounds;
    private Rectangle ScrollbarThumbBounds;
    private Rectangle SearchBounds;
    private IKeyboardSubscriber? PreviousKeyboardSubscriber;
    private bool DraggingScrollbar;
    private int ScrollbarDragOffsetY;
    private int Page;
    private string LastSearchText = "";

    public LedgerMenu(LedgerSaveData state, ModConfig config)
        : base(
            x: Math.Max(0, (Game1.uiViewport.Width - GetMenuWidth()) / 2),
            y: Math.Max(0, (Game1.uiViewport.Height - GetMenuHeight()) / 2),
            width: GetMenuWidth(),
            height: GetMenuHeight(),
            showUpperRightCloseButton: true)
    {
        this.State = state;
        this.Config = config;
        this.Rows = BuildPriceRows(state, config);
        this.SearchBox = new TextBox(Game1.content.Load<Texture2D>("LooseSprites\\textBox"), Game1.staminaRect, Game1.smallFont, Game1.textColor)
        {
            Height = 44,
            Width = 300,
            textLimit = 40,
            limitWidth = true
        };
        this.SearchBox.OnEnterPressed += _ => this.DeselectSearchBox();
        this.UpdateButtonBounds();
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
        this.DrawFilters(b);
        this.DrawPriceTable(b);
        this.DrawFooter(b);

        this.upperRightCloseButton?.draw(b);
        this.drawMouse(b);
    }

    private void DrawHeader(SpriteBatch b)
    {
        int x = this.xPositionOnScreen + SidePadding;
        int y = this.yPositionOnScreen + 30;

        Utility.drawTextWithShadow(b, "Harvest Ledger", Game1.dialogueFont, new Vector2(x, y), Game1.textColor);

        TaxLedger taxes = this.State.TaxLedger;
        int taxesDue = taxes.PendingTaxes + taxes.UnpaidTaxes;
        string[] statusItems =
        [
            $"Prices: {(this.Config.EnableDynamicPricing ? "On" : "Off")}",
            $"Sold: {this.State.LastDay.SoldItemCount} / {this.State.LastDay.GrossShippingIncome}g",
            $"Pressure: {this.State.LastDay.MarketPressure:P0}",
            this.Config.EnableTaxSystem ? $"Tax: {taxesDue}g due" : "Tax: Off"
        ];

        this.DrawStatusLine(b, statusItems, x, y + 48, this.width - (SidePadding * 2) - 36);
        this.DrawSearchBox(b, x, y + 78);
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

            string label = FitText(GetFilterLabel(filter), Game1.smallFont, bounds.Width - 14);
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
        int contentRight = Math.Max(left + 240, right - ScrollbarWidth - ScrollbarGap);
        int contentWidth = contentRight - left;

        DrawMutedBox(b, left, top, tableWidth, tableHeight);

        int iconX = left + 18;
        int nameX = left + 78;
        int pressureX = contentRight - 110;
        int currentX = pressureX - 110;
        int baseX = currentX - 96;
        int nameWidth = Math.Max(80, baseX - nameX - 14);

        this.DrawColumnHeader(b, "Item", nameX, top + 8);
        this.DrawColumnHeader(b, "Base", baseX, top + 8);
        this.DrawColumnHeader(b, "Now", currentX, top + 8);
        this.DrawColumnHeader(b, "Pressure", pressureX, top + 8);
        this.DrawScrollbar(b, right - ScrollbarWidth, top + TableHeaderHeight + 4, tableHeight - TableHeaderHeight - 8, visibleRows.Count, rowsPerPage);

        if (visibleRows.Count == 0)
        {
            string empty = this.Rows.Count == 0
                ? "No tracked prices yet. Open a save with dynamic pricing enabled to populate the ledger."
                : "No items match this view.";
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

            string name = FitText(row.DisplayName, Game1.smallFont, nameWidth);
            string category = FitText(row.DisplayCategory, Game1.smallFont, LabelTextScale, nameWidth);
            Utility.drawTextWithShadow(b, name, Game1.smallFont, new Vector2(nameX, rowTop + 8), Game1.textColor);
            DrawScaledTextWithShadow(b, category, Game1.smallFont, new Vector2(nameX, rowTop + 32), Game1.textColor * 0.72f, LabelTextScale);

            Utility.drawTextWithShadow(b, $"{row.BasePrice}g", Game1.smallFont, new Vector2(baseX, rowTop + 14), Game1.textColor);
            Utility.drawTextWithShadow(b, $"{row.CurrentPrice}g", Game1.smallFont, new Vector2(currentX, rowTop + 14), GetPriceColor(row));
            Utility.drawTextWithShadow(b, row.PressureText, Game1.smallFont, new Vector2(pressureX, rowTop + 14), Game1.textColor);

            rowIndex++;
        }
    }

    private void DrawFooter(SpriteBatch b)
    {
        IReadOnlyList<PriceRow> visibleRows = this.GetVisibleRows();
        int maxPage = this.GetMaxPage(visibleRows.Count);
        int y = this.yPositionOnScreen + this.height - FooterHeight - 16;
        int centerX = this.xPositionOnScreen + (this.width / 2);

        string pageText = visibleRows.Count == 0
            ? "0 items"
            : $"{visibleRows.Count} items   page {this.Page + 1} / {maxPage + 1}";
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

    private void DrawColumnHeader(SpriteBatch b, string text, int x, int y)
    {
        DrawScaledTextWithShadow(b, text, Game1.smallFont, new Vector2(x, y), Game1.textColor * 0.72f, LabelTextScale);
    }

    private void DrawStatusLine(SpriteBatch b, IReadOnlyList<string> items, int x, int y, int maxWidth)
    {
        int cursorX = x;
        int cursorY = y;
        int gap = 20;
        int lineHeight = 22;

        foreach (string item in items)
        {
            Vector2 size = Game1.smallFont.MeasureString(item);
            if (cursorX > x && cursorX + size.X > x + maxWidth)
            {
                cursorX = x;
                cursorY += lineHeight;
            }

            Utility.drawTextWithShadow(b, item, Game1.smallFont, new Vector2(cursorX, cursorY), Game1.textColor * 0.86f);
            cursorX += (int)size.X + gap;
        }
    }

    private void DrawSearchBox(SpriteBatch b, int x, int y)
    {
        string label = "Search";
        Utility.drawTextWithShadow(b, label, Game1.smallFont, new Vector2(x, y + 8), Game1.textColor * 0.86f);

        int boxX = x + 84;
        int boxWidth = Math.Min(380, this.xPositionOnScreen + this.width - SidePadding - boxX);
        this.SearchBox.X = boxX;
        this.SearchBox.Y = y;
        this.SearchBox.Width = Math.Max(180, boxWidth);
        this.SearchBox.Height = 44;
        this.SearchBounds = new Rectangle(this.SearchBox.X, this.SearchBox.Y, this.SearchBox.Width, this.SearchBox.Height);

        this.SearchBox.Draw(b, false);

        if (string.IsNullOrWhiteSpace(this.SearchBox.Text) && !this.SearchBox.Selected)
        {
            Utility.drawTextWithShadow(
                b,
                "Chinese or English item name",
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
            : Math.Min(640, available);
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

    private static string FitText(string text, SpriteFont font, float scale, int maxWidth)
    {
        if (font.MeasureString(text).X * scale <= maxWidth)
            return text;

        const string ellipsis = "...";
        string trimmed = text;
        while (trimmed.Length > 0 && font.MeasureString(trimmed + ellipsis).X * scale > maxWidth)
            trimmed = trimmed[..^1];

        return trimmed.Length == 0 ? ellipsis : trimmed + ellipsis;
    }

    private static void DrawScaledTextWithShadow(SpriteBatch b, string text, SpriteFont font, Vector2 position, Color color, float scale)
    {
        b.DrawString(font, text, position + Vector2.One, Color.Black * 0.35f, 0f, Vector2.Zero, scale, SpriteEffects.None, 0.86f);
        b.DrawString(font, text, position, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0.87f);
    }

    private void UpdateButtonBounds()
    {
        int x = this.xPositionOnScreen + SidePadding;
        int y = this.yPositionOnScreen + HeaderHeight + 34;
        int gap = 10;
        int buttonHeight = 46;
        int availableWidth = this.width - (SidePadding * 2);
        int buttonWidth = Math.Max(64, (availableWidth - (gap * (FilterOrder.Length - 1))) / FilterOrder.Length);

        for (int i = 0; i < FilterOrder.Length; i++)
        {
            PriceFilter filter = FilterOrder[i];
            this.FilterBounds[filter] = new Rectangle(x + ((buttonWidth + gap) * i), y, buttonWidth, buttonHeight);
        }

        int footerY = this.yPositionOnScreen + this.height - FooterHeight - 10;
        this.PreviousPageBounds = new Rectangle(this.xPositionOnScreen + this.width - SidePadding - 116, footerY, 48, 42);
        this.NextPageBounds = new Rectangle(this.xPositionOnScreen + this.width - SidePadding - 58, footerY, 48, 42);
    }

    private int GetTableTop()
    {
        return this.yPositionOnScreen + HeaderHeight + FilterHeight + 48;
    }

    private int GetRowsPerPage()
    {
        int tableTop = this.GetTableTop();
        int footerTop = this.yPositionOnScreen + this.height - FooterHeight - 22;
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

        string[] terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return rows
            .Where(row => terms.All(term => row.SearchText.Contains(term, StringComparison.CurrentCultureIgnoreCase)))
            .ToList();
    }

    private static string GetFilterLabel(PriceFilter filter)
    {
        return filter switch
        {
            PriceFilter.Farm => "Crops & goods",
            PriceFilter.Fish => "Fish",
            PriceFilter.Mine => "Mine",
            _ => "All prices"
        };
    }

    private static Color GetPriceColor(PriceRow row)
    {
        if (row.CurrentPrice > row.BasePrice)
            return new Color(35, 110, 45);

        if (row.CurrentPrice < row.BasePrice)
            return new Color(135, 45, 35);

        return Game1.textColor;
    }

    private static List<PriceRow> BuildPriceRows(LedgerSaveData state, ModConfig config)
    {
        List<PriceRow> rows = new();

        foreach ((string savedItemId, int basePrice) in state.BasePricesByItemId)
        {
            if (basePrice <= 0)
                continue;

            string rawItemId = GetRawObjectId(savedItemId);
            string qualifiedItemId = $"(O){rawItemId}";
            if (config.DynamicPricing.ExemptItemIds.Contains(rawItemId) || config.DynamicPricing.ExemptItemIds.Contains(qualifiedItemId))
                continue;

            if (!Game1.objectData.TryGetValue(rawItemId, out ObjectData? objectData))
                continue;

            Item? icon = TryCreateIcon(qualifiedItemId);
            string? displayName = icon?.DisplayName;
            if (string.IsNullOrWhiteSpace(displayName))
                displayName = objectData.Name ?? rawItemId;

            ItemMarketCategory category = ItemClassifier.GetCategory(objectData);
            string displayCategory = GetDisplayCategory(objectData, category);
            int currentPrice = Math.Max(1, objectData.Price);
            double rawPressure = GetValue(state.MarketPressureByItemId, qualifiedItemId, rawItemId);
            double pricePressure = Math.Min(0.85, rawPressure * config.DynamicPricing.SalePressurePerItem);
            int lastSold = GetValue(state.LastDay.SoldByItemId, qualifiedItemId, rawItemId);
            int lifetimeSold = GetValue(state.LifetimeSoldByItemId, qualifiedItemId, rawItemId);
            string searchText = string.Join(
                ' ',
                new[] { displayName, objectData.Name ?? "", displayCategory, category.ToString(), rawItemId, qualifiedItemId });

            rows.Add(new PriceRow(
                qualifiedItemId,
                displayName,
                displayCategory,
                category,
                IsFarmProduct(objectData, category),
                searchText,
                icon,
                basePrice,
                currentPrice,
                pricePressure,
                lastSold,
                lifetimeSold));
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

    private static string GetRawObjectId(string itemId)
    {
        return itemId.StartsWith("(O)", StringComparison.Ordinal)
            ? itemId[3..]
            : itemId;
    }

    private static int GetValue(IReadOnlyDictionary<string, int> values, string qualifiedItemId, string rawItemId)
    {
        if (values.TryGetValue(qualifiedItemId, out int qualifiedValue))
            return qualifiedValue;

        return values.GetValueOrDefault(rawItemId);
    }

    private static double GetValue(IReadOnlyDictionary<string, double> values, string qualifiedItemId, string rawItemId)
    {
        if (values.TryGetValue(qualifiedItemId, out double qualifiedValue))
            return qualifiedValue;

        return values.GetValueOrDefault(rawItemId);
    }

    private static string GetDisplayCategory(ObjectData objectData, ItemMarketCategory category)
    {
        return objectData.Category switch
        {
            -5 => "Egg",
            -6 => "Milk",
            -27 => "Syrup",
            _ => category switch
            {
                ItemMarketCategory.ArtisanGoods => "Artisan",
                ItemMarketCategory.MonsterLoot => "Monster loot",
                _ => category.ToString()
            }
        };
    }

    private static bool IsFarmProduct(ObjectData objectData, ItemMarketCategory category)
    {
        return category is ItemMarketCategory.Seed
                or ItemMarketCategory.Vegetable
                or ItemMarketCategory.Fruit
                or ItemMarketCategory.Flower
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
                ItemMarketCategory.ArtisanGoods => 3,
                ItemMarketCategory.Cooking => 4,
                ItemMarketCategory.Seed => 5,
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
        double Pressure,
        int LastSold,
        int LifetimeSold)
    {
        public string PressureText
        {
            get
            {
                string pressure = this.Pressure <= 0 ? "0%" : this.Pressure.ToString("P0");
                return this.LastSold > 0
                    ? $"{pressure} ({this.LastSold})"
                    : pressure;
            }
        }
    }
}
