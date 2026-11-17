using Avalonia;
using Avalonia.Markup.Xaml;

namespace SpcStudio.Theme;

// A margin or padding put together in markup from the spacing constants, by side, by axis or all around.
public sealed class SpaceThickness : MarkupExtension
{
    public double AllSides { get; set; }
    public double Horizontal { get; set; }
    public double Vertical { get; set; }
    public double Left { get; set; }
    public double Top { get; set; }
    public double Right { get; set; }
    public double Bottom { get; set; }

    // The thickness the given values make.
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return new Thickness(SideOf(Left, Horizontal), SideOf(Top, Vertical), SideOf(Right, Horizontal), SideOf(Bottom, Vertical));
    }

    // One side's value: its own where it was given, else its axis's, else the one all around.
    private double SideOf(double side, double axis)
    {
        if (side != 0)
        {
            return side;
        }

        if (axis != 0)
        {
            return axis;
        }

        return AllSides;
    }
}
