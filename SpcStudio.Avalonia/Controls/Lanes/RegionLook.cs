using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace SpcStudio.Controls;

// What a lane's region is drawn with, all from one cell brush: the band under its line, the line, the second value's line and the value's text.
public sealed record RegionLook(IBrush Band, Pen Line, Pen SecondLine, IBrush Text)
{
    public const double BandOpacity = 0.22;
    private const double LineThickness = 1.4;
    private const double SecondLineThickness = 0.7;
    private const double LineOpacity = 0.75;

    // The look of a region drawn in a cell brush.
    public static RegionLook Of(IBrush cell)
    {
        var lineBrush = WithOpacity(cell, LineOpacity);
        return new RegionLook(WithOpacity(cell, BandOpacity), new Pen(lineBrush, LineThickness), new Pen(lineBrush, SecondLineThickness), cell);
    }

    // The same brush's color at a lower opacity.
    private static IBrush WithOpacity(IBrush brush, double opacity)
    {
        return new ImmutableSolidColorBrush(brush is ISolidColorBrush solid ? solid.Color : Colors.Transparent, opacity);
    }
}
