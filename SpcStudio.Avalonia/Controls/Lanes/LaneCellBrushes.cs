using Avalonia.Media;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The two brushes a lane draws its cells in, plain and selected, and the pairs more than one lane shares: the yellow of the two volume lanes and the blue of the three echo lanes.
public sealed record LaneCellBrushes(Brush Cell, Brush Selected)
{
    public static readonly LaneCellBrushes Volume = Of(Palette.PastelYellow, Palette.PaleYellow);
    public static readonly LaneCellBrushes Echo = Of(Palette.PastelBlue, Palette.PaleBlue);

    // A lane's pair, built from its two colors.
    public static LaneCellBrushes Of(Color cell, Color selected)
    {
        return new LaneCellBrushes(new SolidColorBrush(cell), new SolidColorBrush(selected));
    }
}
