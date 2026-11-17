using Avalonia.Controls;
using SpcStudio.Theme;

namespace SpcStudio;

// Shows everything a run of AddmusicK printed in a bordered, scrolling frame, with OK.
public partial class AmkOutputWindow : DialogWindow
{
    public const double OpeningWidth = 800;

    // Builds the window.
    public AmkOutputWindow()
    {
        InitializeComponent();
    }

    // Shows AddmusicK's output over a window, and returns once the window is closed.
    public static Task Show(Window owner, string amkOutput)
    {
        var window = new AmkOutputWindow();
        window.ShowBoxTitle(WindowTitles.Plain.AmkOutput);
        window.OutputFrame.RowsPanel.Children.Add(new SelectableTextBlock { Text = amkOutput, Classes = { ThemeClasses.DialogMessage } });
        return window.ShowDialog(owner);
    }
}
