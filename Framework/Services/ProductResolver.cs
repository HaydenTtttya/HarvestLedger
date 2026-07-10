using StardewValley;
using StardewValley.Extensions;
using StardewValley.GameData.Crops;
using StardewValley.GameData.FruitTrees;
using StardewValley.GameData.Machines;
using StardewValley.GameData.Objects;
using StardewValley.TerrainFeatures;
using SObject = StardewValley.Object;

namespace HarvestLedger.Framework.Services;

public static class ProductResolver
{
    private static readonly Dictionary<string, List<string>> MachinesByRequiredQid = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, List<string>> MachinesByRequiredTag = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> UniversalMachines = new(StringComparer.OrdinalIgnoreCase);
    private static bool TriggerIndexBuilt;

    public static void InvalidateMachineCache()
    {
        MachinesByRequiredQid.Clear();
        MachinesByRequiredTag.Clear();
        UniversalMachines.Clear();
        TriggerIndexBuilt = false;
    }

    public static bool TryCreateHarvestFromSeed(Item? item, out SObject harvest)
    {
        harvest = null!;
        if (item is not SObject { Category: SObject.SeedsCategory } seed || seed.ItemId == Crop.mixedSeedsId)
            return false;

        string harvestItemId = "";
        if (seed.IsFruitTreeSapling() && FruitTree.TryGetData(seed.ItemId, out FruitTreeData? treeData))
        {
            harvestItemId = treeData.Fruit?.FirstOrDefault(fruit => !string.IsNullOrWhiteSpace(fruit.ItemId))?.ItemId ?? "";
        }
        else if (Crop.TryGetData(seed.ItemId, out CropData cropData))
        {
            harvestItemId = cropData.HarvestItemId;
        }

        if (string.IsNullOrWhiteSpace(harvestItemId))
            return false;

        try
        {
            harvest = ItemRegistry.Create<SObject>(MarketItemIdentity.NormalizeObjectId(harvestItemId), allowNull: true);
            return harvest is not null;
        }
        catch
        {
            harvest = null!;
            return false;
        }
    }

    public static string GetCropHarvestItemId(Crop crop)
    {
        if (crop.forageCrop.Value)
        {
            string forageCropItemId = crop.whichForageCrop.Value switch
            {
                "1" => "399",
                "2" => "829",
                _ => crop.whichForageCrop.Value
            };
            return MarketItemIdentity.NormalizeObjectId(forageCropItemId);
        }

        string itemId = crop.isWildSeedCrop()
            ? crop.whichForageCrop.Value
            : crop.indexOfHarvest.Value ?? "";

        return string.IsNullOrWhiteSpace(itemId)
            ? ""
            : MarketItemIdentity.NormalizeObjectId(itemId);
    }

    public static IEnumerable<SObject> GetMachineOutputsForInput(SObject inputObject)
    {
        if (inputObject.QualifiedItemId is null)
            yield break;

        Dictionary<string, MachineData> machinesData = GetMachineData();
        if (machinesData.Count == 0)
            yield break;

        HashSet<string> candidates = GetCandidateMachines(inputObject, machinesData);
        if (candidates.Count == 0)
            yield break;

        GameLocation location = Game1.currentLocation ?? Game1.getFarm();
        Item probeInput = inputObject.getOne();
        probeInput.Quality = inputObject.Quality;
        probeInput.Stack = int.MaxValue;

        foreach ((string machineId, MachineData machineData) in machinesData)
        {
            if (!candidates.Contains(machineId))
                continue;

            SObject? machine = TryCreateMachine(machineId);
            if (machine is null)
                continue;

            if (!TryGetMachineOutput(machine, machineData, probeInput, location, out SObject? outputObject))
                continue;

            yield return outputObject;
        }
    }

    private static HashSet<string> GetCandidateMachines(SObject inputObject, Dictionary<string, MachineData> machinesData)
    {
        if (!TriggerIndexBuilt)
            BuildTriggerIndex(machinesData);

        HashSet<string> candidates = new(UniversalMachines, StringComparer.OrdinalIgnoreCase);
        if (MachinesByRequiredQid.TryGetValue(inputObject.QualifiedItemId, out List<string>? byQid))
            candidates.UnionWith(byQid);

        foreach (string tag in inputObject.GetContextTags())
        {
            if (MachinesByRequiredTag.TryGetValue(tag, out List<string>? byTag))
                candidates.UnionWith(byTag);
        }

        return candidates;
    }

    private static void BuildTriggerIndex(Dictionary<string, MachineData> machinesData)
    {
        InvalidateMachineCache();

        foreach ((string machineId, MachineData machineData) in machinesData)
        {
            if (machineData.OutputRules is null)
                continue;

            foreach (MachineOutputRule? rule in machineData.OutputRules)
            {
                if (rule?.Triggers is null)
                    continue;

                foreach (MachineOutputTriggerRule? trigger in rule.Triggers)
                {
                    if (trigger is null || !trigger.Trigger.HasFlag(MachineOutputTrigger.ItemPlacedInMachine))
                        continue;

                    bool hasFilter = false;
                    if (!string.IsNullOrWhiteSpace(trigger.RequiredItemId))
                    {
                        AddToIndex(MachinesByRequiredQid, QualifyItemId(trigger.RequiredItemId), machineId);
                        hasFilter = true;
                    }

                    if (trigger.RequiredTags is { Count: > 0 })
                    {
                        string? indexTag = trigger.RequiredTags.FirstOrDefault(tag => !string.IsNullOrWhiteSpace(tag) && tag[0] != '!');
                        if (indexTag is not null)
                            AddToIndex(MachinesByRequiredTag, indexTag, machineId);
                        else
                            UniversalMachines.Add(machineId);

                        hasFilter = true;
                    }

                    if (!hasFilter)
                        UniversalMachines.Add(machineId);
                }
            }
        }

        TriggerIndexBuilt = true;
    }

    private static void AddToIndex(Dictionary<string, List<string>> index, string key, string machineId)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        if (!index.TryGetValue(key, out List<string>? machines))
        {
            machines = new List<string>();
            index[key] = machines;
        }

        if (!machines.Contains(machineId))
            machines.Add(machineId);
    }

    private static string QualifyItemId(string itemId)
    {
        try
        {
            return ItemRegistry.QualifyItemId(itemId) ?? itemId;
        }
        catch
        {
            return itemId;
        }
    }

    private static Dictionary<string, MachineData> GetMachineData()
    {
        try
        {
            return DataLoader.Machines(Game1.content);
        }
        catch
        {
            return new Dictionary<string, MachineData>();
        }
    }

    private static SObject? TryCreateMachine(string machineId)
    {
        foreach (string itemId in GetMachineItemIdCandidates(machineId))
        {
            try
            {
                if (ItemRegistry.Create(itemId, 1, 0, true) is SObject machine)
                    return machine;
            }
            catch
            {
                // Try the next candidate format.
            }
        }

        return null;
    }

    private static IEnumerable<string> GetMachineItemIdCandidates(string machineId)
    {
        if (string.IsNullOrWhiteSpace(machineId))
            yield break;

        string trimmed = machineId.Trim();
        yield return trimmed;
        if (!trimmed.StartsWith('('))
        {
            yield return $"(BC){trimmed}";
            yield return $"(O){trimmed}";
        }
    }

    private static bool TryGetMachineOutput(
        SObject machine,
        MachineData machineData,
        Item probeInput,
        GameLocation location,
        out SObject outputObject)
    {
        outputObject = null!;
        try
        {
            if (!MachineDataUtility.TryGetMachineOutputRule(
                    machine,
                    machineData,
                    MachineOutputTrigger.ItemPlacedInMachine,
                    probeInput,
                    Game1.player,
                    location,
                    out MachineOutputRule? rule,
                    out _,
                    out _,
                    out _))
                return false;

            MachineItemOutput? outputData = MachineDataUtility.GetOutputData(machine, machineData, rule, probeInput, Game1.player, location);
            if (outputData is null || outputData.OutputMethod is not null)
                return false;

            if (MachineDataUtility.GetOutputItem(machine, outputData, probeInput, Game1.player, probe: true, out _) is not SObject resolvedOutput)
                return false;

            outputObject = resolvedOutput;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
