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
}
