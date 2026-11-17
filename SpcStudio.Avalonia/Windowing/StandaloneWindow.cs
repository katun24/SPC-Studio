using Avalonia;
using Avalonia.Controls;

namespace SpcStudio;

// A window of the app that is not a modal box: placed where the app puts it and with a taskbar entry of its own.
public abstract class StandaloneWindow : AppWindow
{
    // Gives the window the place and taskbar presence every window that is not a box has.
    protected StandaloneWindow()
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowInTaskbar = true;
    }

    // Makes the window as wide as a control standing straight in its content needs, the content's margin included, and lets it be made no narrower.
    protected void FitWidthTo(Control control)
    {
        control.Measure(Size.Infinity);
        var contentMargin = Content is Control content ? content.Margin : default;
        MinWidth = control.DesiredSize.Width + contentMargin.Left + contentMargin.Right;
        Width = MinWidth;
    }
}
