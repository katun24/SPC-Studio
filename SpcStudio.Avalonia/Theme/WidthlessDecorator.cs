using Avalonia;
using Avalonia.Controls;

namespace SpcStudio.Theme;

// Holds a part of a template and asks for its height alone, so what the part holds never widens the control around it.
public sealed class WidthlessDecorator : Decorator
{
    // Measures the part no wider than it was last laid out and asks for none of its width.
    protected override Size MeasureOverride(Size availableSize)
    {
        Size partSize = base.MeasureOverride(availableSize.WithWidth(Math.Min(availableSize.Width, Bounds.Width)));
        return new Size(0, partSize.Height);
    }

    // Measures the part again at the width it is given, so it knows the room it really has, and lays it out across it.
    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Measure(finalSize.Deflate(Padding));
        return base.ArrangeOverride(finalSize);
    }
}
