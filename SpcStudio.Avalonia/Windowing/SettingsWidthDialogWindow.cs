using Avalonia.Controls;
using SpcStudio.Theme;

namespace SpcStudio;

// A modal box as wide as Preferences, sized to what it holds in height.
public abstract class SettingsWidthDialogWindow : DialogWindow
{
    // Gives the box the settings boxes' width and its content's height.
    protected SettingsWidthDialogWindow()
    {
        SizeToContent = SizeToContent.Height;
        Width = ThemeResource.SettingsBoxWidth;
    }

    // Ticks or clears a setting's check box as the setting stands, then reports each state it is ticked or cleared into.
    protected static void ShowCheckBox(CheckBox box, bool isChecked, Action<bool> report)
    {
        box.IsChecked = isChecked;
        box.IsCheckedChanged += (_, _) => report(box.IsChecked == true);
    }
}
