using Avalonia.Controls;
using Avalonia.Interactivity;
using SpcStudio.Theme;

namespace SpcStudio;

// A modal box of the app: sized to what it holds, no narrower than every box's least width, fixed in size, centered on the window that opened it and
// with no taskbar entry of its own.
public abstract class DialogWindow : AppWindow
{
    // Gives the box the size, place and taskbar presence every modal box has.
    protected DialogWindow()
    {
        SizeToContent = SizeToContent.WidthAndHeight;
        MinWidth = ThemeResource.DialogMinWidth;
        CanResize = false;
        CanMinimize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
    }

    // Gives the box a title and the icon its kind of title carries.
    protected void ShowBoxTitle(BoxTitle title)
    {
        ShowTitle(title.Text, title.IconClass);
    }

    // Gives the box a title, and the icon a theme class gives it where one is named, the app's own staying where none is.
    protected void ShowTitle(string text, string? iconClass)
    {
        Title = text;
        if (iconClass is not null)
        {
            Classes.Add(iconClass);
        }
    }

    // Ticks or clears a setting's check box as the setting stands, then reports each state it is ticked or cleared into.
    protected static void ShowCheckBox(CheckBox box, bool isChecked, Action<bool> report)
    {
        box.IsChecked = isChecked;
        box.IsCheckedChanged += (_, _) => report(box.IsChecked == true);
    }

    // Closes the box answering nothing, for its Cancel, Close or OK button.
    protected void OnDismissClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
