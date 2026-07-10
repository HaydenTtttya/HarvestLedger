namespace HarvestLedger.Framework;

public sealed class CropRotationTileState
{
    public string SeasonKey { get; set; } = "";
    public string HarvestItemId { get; set; } = "";
    public CropRotationCategory CropCategory { get; set; } = CropRotationCategory.OtherCrop;
    public int RotationCount { get; set; }
}
