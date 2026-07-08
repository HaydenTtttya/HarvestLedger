namespace HarvestLedger.Framework;

public sealed class TaxConfig
{
    public int DailyPropertyTax { get; set; } = 50;
    public int BuildingTax { get; set; } = 25;
    public int CapitalItemTax { get; set; } = 2;
    public int SprinklerTax { get; set; } = 3;
    public double IncomeTaxRate { get; set; } = 0.04;
    public double HighIncomeTaxRate { get; set; } = 0.08;
    public int HighIncomeThreshold { get; set; } = 10000;
    public bool ProgressiveIncomeTax { get; set; } = true;
    public double MarriedReduction { get; set; } = 0.1;
    public double ChildReduction { get; set; } = 0.05;
    public double UnpaidTaxPenalty { get; set; } = 0.05;
}
