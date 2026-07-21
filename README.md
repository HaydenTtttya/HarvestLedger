# Harvest Ledger

Harvest Ledger is a Stardew Valley economy mod for players who want the farm market to react to what they actually produce. It tracks what you ship, moves prices over time, exposes the state of the market in-game, and adds optional tax and stamina balance systems.

The goal is not to bury the player in accounting. The mod keeps the math behind the scenes and gives you a readable ledger when you want to check what is happening.

## What It Does

### Dynamic prices

Harvest Ledger edits `Data/Objects`, so price changes flow through the normal Stardew Valley item price path instead of living only inside this mod's UI.

Repeatedly shipping the same item increases market pressure for that item. As pressure rises, the sale price is pushed down within the configured minimum and maximum multipliers. Demand recovers over time, and recovery is better when yesterday's sales were more diverse.

Prices are affected by:

- item category base multipliers
- repeated sales pressure
- player skill levels
- seasonal conditions
- short regional demand events
- crop rotation bonuses
- farm income concentration
- processing chains where artisan goods can be traced back to raw ingredients

The default tuning is intentionally conservative. It should make monoculture and single-product spam less dominant without making ordinary farming feel punished every day.

### Product and machine output resolution

The mod resolves products through Stardew Valley's own data where possible.

- Seeds and fruit tree saplings use `Data/Crops` and `Data/FruitTrees`.
- Machine output checks use `Data/Machines` and `MachineDataUtility`.
- Preserved and processed items keep their source ingredient when the game exposes it, so wine, jelly, roe, smoked fish, dried goods, and similar items can be tracked more accurately.
- Machine rule lookups are indexed by required item IDs and context tags, so the ledger does not need to brute-force every machine rule on every display pass.

Some custom machine rules use C# output delegates which cannot be safely inspected ahead of time. Those are skipped rather than guessed.

### In-game ledger

Press `F8` by default to open the ledger menu.

The ledger shows:

- current dynamic price state
- yesterday's shipped item count and shipping income
- the most pressured item
- pending taxes
- active or upcoming demand events
- subsidy and crop rotation policy state
- an optional detailed breakdown of the latest tax bill
- base price, current price, pressure, and trend per item
- filters for farm products, fish, mine goods, and all registered item prices, including compatible mod items
- search across item names, categories, IDs, and processed-ingredient names

The ledger uses actual item icons where possible and includes generated rows for likely processed goods, so artisan outputs are visible even before every variant has been shipped.

### Daily ledger tracking

At the end of each day, Harvest Ledger reads the shipping bin and records:

- shipped stacks and item counts
- gross shipping income
- sold item categories
- last sold quality
- last sold unit prices
- market pressure changes
- main income category and concentration

This data is saved per save file through SMAPI save data.

### Regional demand and subsidies

Each season can generate demand events such as town festivals, pantry restocks, mine supply orders, or fish market shortages. These events temporarily lift related categories.

The mod chooses a seasonal subsidized crop from the base game `Data/Crops`. Candidates must grow in the current season and be plantable in open farm soil, so fruit tree products and indoor-only crops are not eligible. The default target is twice the square root of planted crops, capped at 64; both values are configurable. Keeping that crop meaningfully present in your farm plan can improve demand recovery and reduce taxes over time.

### Taxes

The optional tax system assesses taxes at day end and collects them the next morning.

It currently includes:

- bracketed income tax
- land use tax based on tilled farm tiles
- automation tax based on functional production machines registered in `Data/Machines` and placed on the farm or in its buildings; map-template objects are excluded unless they are the casks unlocked with the player's cellar (never machines elsewhere in the world or in a player's inventory)
- subsidy reductions
- an optional unpaid-tax penalty when the player cannot cover the bill

Tax values are configurable. Late fees are off by default, so unpaid balances stay outstanding instead of compounding every morning. Enable `Taxes.ApplyUnpaidTaxPenalty` if a save specifically wants that pressure. The ledger's **Show tax overview button** setting adds a floating tax bill to the F8 menu, with the latest shipping, income, land, machine, subsidy, outstanding, and late-fee amounts.

### Stamina balancing

The optional stamina system can add extra costs after tool use. The defaults are mild and configurable:

- axe
- pickaxe
- hoe
- watering can
- fishing rod
- scythe
- weapons

Set a rate or cost to `0` to disable that tool's extra cost.

## Compatibility

Harvest Ledger is built for Stardew Valley 1.6 and SMAPI 4.

The dynamic price path is intentionally compatible with display mods that read normal Stardew item prices. For example, UIInfoSuite-style price displays that create items and call `sellToStorePrice()` should see Harvest Ledger's adjusted prices for normal objects and real item instances.

There are still limits:

- Per-ingredient processed prices are more accurate inside Harvest Ledger than in mods that only know the base object ID.
- Machine outputs implemented through non-inspectable C# delegates are skipped in generated ledger rows.
- Mods that replace the game's sale-price calculation may stack with Harvest Ledger depending on load order and implementation.

### Multiplayer

Harvest Ledger 0.4.7 supports online and split-screen farms. Install the same version on the host and every farmhand; a farmhand without the mod will only see vanilla item prices.

The host owns the economy state. It runs the daily shipping and tax settlement, saves the ledger, and sends the current market state and economy settings when someone joins. Farmhands use that state for their price display and ledger, but never write a competing copy of the save data. This keeps demand, subsidies, crop rotation, and tax totals from being applied once per player.

Shipping from every farmer's shipping bin is included in the day's market pressure. With a shared wallet, taxes stay on the farm ledger and the host pays as before. With separate wallets, the host settles the day but each player's shipping income, tax bill, arrears, and payment stay on that player's account. Land and machine costs are shared by the previous seven days of shipping income by default; the host can switch that to an even split or host-paid in the config. If no one shipped during that period, the split is even.

### Performance notes

Earlier builds walked the current location every second, and honey checks could repeatedly scan every loaded location for flowers. That work has been removed from the update loop. Existing items are refreshed when the day changes, when a player enters a location, or when an item enters a local inventory. Bee-house flower quality is cached and recalculated only after a relevant world change or on a new day.

Generic Mod Config Menu is optional. If installed, it provides an in-game configuration screen for the main systems and common tuning values.

## Installation

1. Install SMAPI.
2. Download or build `HarvestLedger 0.4.7`.
3. Unzip it into your Stardew Valley `Mods` folder.
4. Launch the game through SMAPI.
5. Open a save and press `F8` to open the ledger.

The mod folder should contain:

```text
HarvestLedger/
  HarvestLedger.dll
  manifest.json
  config.json
  i18n/
  icon/
```

## Configuration

The default `config.json` enables all major systems:

```json
{
  "EnableDynamicPricing": true,
  "EnableDailyLedger": true,
  "EnableStaminaBalance": false,
  "EnableTaxSystem": false,
  "MenuKey": "F8"
}
```

Important dynamic pricing settings:

- `MinimumPriceMultiplier`: lowest price floor after pressure.
- `MaximumPriceMultiplier`: highest price ceiling after bonuses.
- `SaturationPoint`: how many adjusted sales it takes for pressure to become noticeable.
- `MaxPenalty`: maximum pressure impact on price.
- `BaseRecovery`: daily pressure recovery.
- `MaxDiversityRecovery`: extra recovery from diverse sales.
- `SubsidyRecovery`: extra recovery when the seasonal subsidy condition is met.
- `SubsidyCropCurveScale` and `SubsidyMaximumCropCount`: the square-root subsidy target and its cap, so early farms have a meaningful goal without large farms facing a linear requirement.
- `ExposurePenaltyCap`: penalty cap for relying too heavily on one income category.
- `MaxProcessingTraceBonus`: bonus for processed goods tied to raw production history.
- `ExemptItemIds`: object IDs that Harvest Ledger should leave alone.

Taxes and stamina have their own sections. Most values are safe to tune mid-save; if you make large pricing changes, let a day pass so pressure and ledger summaries settle naturally.

For farms with separate wallets, `Taxes.SharedCostAllocation` controls how land and machine costs are split: `ShippingIncome` (the default), `Equal`, or `HostPays`. It has no effect while the farm uses a shared wallet.

`Taxes.ApplyUnpaidTaxPenalty` is off by default. When enabled, `Taxes.UnpaidTaxPenalty` sets the late-fee rate applied to any balance the farm cannot pay at the next morning's collection.

## Development

Build from the repository root:

```bash
dotnet build -c Release
```

The project uses `Pathoschild.Stardew.ModBuildConfig`, so a release zip is generated under:

```text
bin/Release/net6.0/
```

For a versioned release, run:

```bash
./package-release.sh
```

It prompts for the release version, updates `HarvestLedger.csproj`, `manifest.json`, and the version references in this README, then writes `releases/HarvestLedger <version>.zip`. It does not copy anything into the live Mods folder.

Main code areas:

- `ModEntry.cs`: SMAPI event wiring, config reload, menu hotkey.
- `Framework/Services/DynamicPricingService.cs`: price calculation, pressure, demand, crop rotation, processed-good tracing.
- `Framework/Services/ProductResolver.cs`: crop, fruit tree, and machine-output resolution.
- `Framework/Services/DailyLedgerService.cs`: shipping-bin day close.
- `Framework/Services/TaxService.cs`: tax assessment and collection.
- `Framework/Services/StaminaService.cs`: extra stamina costs.
- `Framework/Menus/LedgerMenu.cs`: in-game ledger UI.
- `Framework/Integrations/GenericModConfigMenuIntegration.cs`: GMCM options.

## Current Status

Harvest Ledger is playable and builds cleanly, but the economy numbers still need real save-file playtesting. The systems are intentionally modular so tuning can happen without rewriting the whole mod.

Good next areas to test:

- late-game artisan farms
- large modded crop packs
- saves with many machines
- multiplayer behavior
- whether tax pressure feels interesting or just noisy

Bug reports are most useful when they include the SMAPI log, the save season/day, the relevant config values, and what was shipped the previous day.
