namespace Pharmacy.Web.Common;

/// <summary>The manual operations offered on a batch. "StockActions.{name}" is the localization key of each label.</summary>
public enum StockAction
{
    AdjustIncrease = 0,
    AdjustDecrease = 1,
    Quarantine = 2,
    Release = 3,
    WriteOff = 4,
    WriteOffQuarantined = 5,
}
