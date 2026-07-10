using HarvestLedger.Framework.Services;
using System.Reflection;
using StardewValley;
using SObject = StardewValley.Object;

namespace HarvestLedger.Framework;

public static class MarketItemIdentity
{
    private const string IngredientSeparator = "|raw=";

    public static string GetMarketItemId(SObject item)
    {
        string baseItemId = NormalizeObjectId(GetStableObjectId(item));
        string ingredientItemId = GetPreservedIngredientItemId(item);

        return !string.IsNullOrWhiteSpace(ingredientItemId) && IsProcessedObject(item)
            ? BuildProcessedItemId(baseItemId, ingredientItemId)
            : baseItemId;
    }

    public static string BuildProcessedItemId(string baseItemId, string ingredientItemId)
    {
        string normalizedBase = NormalizeObjectId(baseItemId);
        string normalizedIngredient = NormalizeObjectId(ingredientItemId);
        return string.IsNullOrWhiteSpace(normalizedBase) || string.IsNullOrWhiteSpace(normalizedIngredient) || string.Equals(normalizedBase, normalizedIngredient, StringComparison.Ordinal)
            ? normalizedBase
            : $"{normalizedBase}{IngredientSeparator}{normalizedIngredient}";
    }

    public static bool IsProcessedItemId(string itemId)
    {
        return itemId.Contains(IngredientSeparator, StringComparison.Ordinal);
    }

    public static string GetBaseItemId(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return "";

        int separatorIndex = itemId.IndexOf(IngredientSeparator, StringComparison.Ordinal);
        string baseItemId = separatorIndex >= 0
            ? itemId[..separatorIndex]
            : itemId;
        return NormalizeObjectId(baseItemId);
    }

    public static string GetIngredientItemId(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return "";

        int separatorIndex = itemId.IndexOf(IngredientSeparator, StringComparison.Ordinal);
        if (separatorIndex < 0)
            return "";

        return NormalizeObjectId(itemId[(separatorIndex + IngredientSeparator.Length)..]);
    }

    public static string GetRawObjectId(string itemId)
    {
        string baseItemId = GetBaseItemId(itemId);
        return baseItemId.StartsWith("(O)", StringComparison.Ordinal)
            ? baseItemId[3..]
            : baseItemId;
    }

    public static bool TrySetPreservedIngredientItemId(SObject item, string ingredientItemId)
    {
        string rawIngredientId = GetRawObjectId(ingredientItemId);
        if (string.IsNullOrWhiteSpace(rawIngredientId))
            return false;

        FieldInfo? field = typeof(SObject).GetField("preservedParentSheetIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        object? raw = field?.GetValue(item);
        if (raw is null)
            return false;

        PropertyInfo? valueProperty = raw.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
        if (valueProperty is not null && valueProperty.CanWrite)
        {
            valueProperty.SetValue(raw, rawIngredientId);
            return true;
        }

        if (field is not null)
        {
            field.SetValue(item, rawIngredientId);
            return true;
        }

        return false;
    }

    public static string NormalizeObjectId(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return "";

        string trimmed = itemId.Trim();
        return trimmed.StartsWith("(O)", StringComparison.Ordinal)
            ? trimmed
            : $"(O){trimmed}";
    }

    private static string GetStableObjectId(SObject item)
    {
        if (!string.IsNullOrWhiteSpace(item.QualifiedItemId))
            return item.QualifiedItemId;

        if (!string.IsNullOrWhiteSpace(item.ItemId))
            return item.ItemId;

        return item.Name;
    }

    private static string GetPreservedIngredientItemId(SObject item)
    {
        try
        {
            string preservedItemId = item.GetPreservedItemId();
            if (!string.IsNullOrWhiteSpace(preservedItemId))
                return NormalizePreservedObjectId(preservedItemId);
        }
        catch
        {
            // Older game builds or unusual item instances may still need the net field fallback.
        }

        object? raw = typeof(SObject).GetField("preservedParentSheetIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(item);
        object? value = UnwrapNetFieldValue(raw);
        string itemId = value switch
        {
            string text => text,
            int number when number > 0 => number.ToString(),
            long number when number > 0 => number.ToString(),
            _ => ""
        };

        return NormalizePreservedObjectId(itemId);
    }

    private static string NormalizePreservedObjectId(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return "";

        string trimmed = itemId.Trim();
        if (int.TryParse(trimmed, out int numericId) && numericId <= 0)
            return "";

        return NormalizeObjectId(trimmed);
    }

    private static bool IsProcessedObject(SObject item)
    {
        if (ItemClassifier.GetCategory(item) is ItemMarketCategory.ArtisanGoods or ItemMarketCategory.Cooking)
            return true;

        string name = item.Name ?? "";
        return ContainsAny(name, "Wine", "Juice", "Jelly", "Pickles", "Honey", "Roe", "Smoked", "Dried");
    }

    private static object? UnwrapNetFieldValue(object? raw)
    {
        if (raw is null)
            return null;

        PropertyInfo? valueProperty = raw.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
        return valueProperty is null ? raw : valueProperty.GetValue(raw);
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        return needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }
}
