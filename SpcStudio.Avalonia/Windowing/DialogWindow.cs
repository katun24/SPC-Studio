using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SpcStudio;

// A modal box of the app: sized to what it holds, fixed in size, centered on the window that opened it and with no taskbar entry of its own.
public abstract class DialogWindow : AppWindow
{
    // Gives the box the size, place and taskbar presence every modal box has.
    protected DialogWindow()
    {
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        CanMinimize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
    }

    // Gives the box a title and the icon its kind of title carries.
    protected void ShowBoxTitle(BoxTitle title)
    {
        Title = title.Text;
        Classes.Add(title.IconClass);
    }

    // Closes the box answering nothing, for its Cancel, Close or OK button.
    protected void OnDismissClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
