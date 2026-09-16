using Avalonia.Controls;
using Avalonia.Layout;
using EveLocalMon.Core.Imaging;
using EveLocalMon.Core.Reading;

namespace EveLocalMon.App.Views;

/// <summary>
/// The manual half of "auto-detect with override": every number the detector guessed, editable.
/// Nothing here is clever — it exists so a wrong guess is a 5-second fix instead of a blocker.
/// </summary>
public sealed class CalibrationPanel : StackPanel
{
    private readonly NumericUpDown _iconX;
    private readonly NumericUpDown _iconSize;
    private readonly NumericUpDown _nameX;
    private readonly NumericUpDown _nameRight;
    private readonly NumericUpDown _rowPitch;
    private readonly NumericUpDown _listTop;
    private readonly NumericUpDown _listHeight;
    private readonly NumericUpDown _headerX;
    private readonly NumericUpDown _headerY;
    private readonly NumericUpDown _headerWidth;
    private readonly NumericUpDown _headerHeight;

    public CalibrationPanel()
    {
        Spacing = 4;

        Children.Add(new TextBlock { Text = "Calibration (region-relative)", FontWeight = Avalonia.Media.FontWeight.SemiBold });

        _iconX = AddField("Icon x");
        _iconSize = AddField("Icon size");
        _nameX = AddField("Name x");
        _nameRight = AddField("Name right");
        _rowPitch = AddField("Row pitch");
        _listTop = AddField("List top");
        _listHeight = AddField("List height");
        _headerX = AddField("Header x");
        _headerY = AddField("Header y");
        _headerWidth = AddField("Header w");
        _headerHeight = AddField("Header h");

        var apply = new Button { Content = "Apply", HorizontalAlignment = HorizontalAlignment.Stretch };
        apply.Click += (_, _) =>
        {
            if (Value is { } calibration)
            {
                Applied?.Invoke(calibration);
            }
        };

        Children.Add(apply);
    }

    public event Action<Calibration>? Applied;

    /// <summary>Null when the fields don't describe a usable calibration (e.g. nothing detected yet).</summary>
    public Calibration? Value
    {
        get
        {
            int width = (int)(_nameRight.Value ?? 0);
            int pitch = (int)(_rowPitch.Value ?? 0);
            int iconSize = (int)(_iconSize.Value ?? 0);
            if (width <= 0 || pitch <= 0 || iconSize <= 0)
            {
                return null;
            }

            PixelRect? header = _headerWidth.Value > 0 && _headerHeight.Value > 0
                ? new PixelRect((int)(_headerX.Value ?? 0), (int)(_headerY.Value ?? 0), (int)(_headerWidth.Value ?? 0), (int)(_headerHeight.Value ?? 0))
                : null;

            return new Calibration(
                new PixelRect(0, (int)(_listTop.Value ?? 0), width, (int)(_listHeight.Value ?? 0)),
                (int)(_iconX.Value ?? 0),
                (int)(_nameX.Value ?? 0),
                width,
                header,
                pitch,
                iconSize);
        }

        set
        {
            if (value is null) return;

            _iconX.Value = value.IconX;
            _iconSize.Value = value.IconSize;
            _nameX.Value = value.NameX;
            _nameRight.Value = value.NameRight;
            _rowPitch.Value = value.RowPitch;
            _listTop.Value = value.ListRect.Y;
            _listHeight.Value = value.ListRect.Height;
            _headerX.Value = value.HeaderCountRect?.X ?? 0;
            _headerY.Value = value.HeaderCountRect?.Y ?? 0;
            _headerWidth.Value = value.HeaderCountRect?.Width ?? 0;
            _headerHeight.Value = value.HeaderCountRect?.Height ?? 0;
        }
    }

    private NumericUpDown AddField(string label)
    {
        var box = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 10000,
            Increment = 1,
            FormatString = "0",
            Width = 96,
            ShowButtonSpinner = true,
        };

        Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = label, Width = 80, VerticalAlignment = VerticalAlignment.Center },
                box,
            },
        });

        return box;
    }
}
