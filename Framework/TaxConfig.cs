namespace HarvestLedger.Framework;

public sealed class TaxConfig
{
    public int FirstIncomeBracket { get; set; } = 25000;
    public int SecondIncomeBracket { get; set; } = 75000;
    public int ThirdIncomeBracket { get; set; } = 150000;
    public double FirstIncomeTaxRate { get; set; } = 0.06;
    public double SecondIncomeTaxRate { get; set; } = 0.09;
    public double ThirdIncomeTaxRate { get; set; } = 0.12;
    public double TopIncomeTaxRate { get; set; } = 0.16;
    public int FreeLandUseTiles { get; set; } = 120;
    public int LowLandUseTileLimit { get; set; } = 300;
    public int MediumLandUseTileLimit { get; set; } = 700;
    public int LowLandUseTaxPerTile { get; set; } = 1;
    public int MediumLandUseTaxPerTile { get; set; } = 2;
    public int HighLandUseTaxPerTile { get; set; } = 3;
    public double AutomationRate { get; set; } = 12;
    public bool ApplyUnpaidTaxPenalty { get; set; }
    public double UnpaidTaxPenalty { get; set; } = 0.05;
    public string SharedCostAllocation { get; set; } = "ShippingIncome";
}
