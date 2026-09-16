namespace EveLocalMon.Core.Reading;

/// <param name="SlotTop">Frame y coordinate of the top of this row's icon slot.</param>
/// <param name="IsPartial">True if the slot is cut off by the top or bottom of the list viewport (not classified).</param>
/// <param name="TemplateName">The closest-matching template's name, whenever an icon is present —
/// even if <see cref="Category"/> came out Unknown because the score missed the threshold.
/// Kept for tuning; only trust it as an actual match when Category is Friendly/Enemy/Neutral.</param>
/// <param name="BestScore">Mean masked colour difference (0-255 scale) of the closest-matching template.</param>
/// <param name="SecondBestScore">Best score among templates other than the winner; useful when tuning thresholds.</param>
public sealed record RowResult(
    int SlotTop,
    bool IsPartial,
    RowCategory Category,
    string? TemplateName,
    double BestScore,
    double SecondBestScore,
    int TextPixels,
    int IconPixels);
