using Avalonia.Controls;
using Avalonia.Interactivity;
using SpcStudio.Export;

namespace SpcStudio;

// The box that turns each of the MML export's optimizations on and off, reporting the whole set as each box is ticked.
public partial class MmlExportOptionsWindow : DialogWindow
{
    private MmlExportOptions _options = MmlExportOptions.All;

    // Builds the box.
    public MmlExportOptionsWindow()
    {
        InitializeComponent();
    }

    // Builds the box showing the optimizations given.
    public MmlExportOptionsWindow(MmlExportOptions options) : this()
    {
        _options = options;
        LoopsBox.IsChecked = options.Loops;
        KeyOnRemoteCodeBox.IsChecked = options.KeyOnRemoteCode;
        HeldNotesAsLegatoBox.IsChecked = options.HeldNotesAsLegato;
    }

    // Raised with the whole set as a box is ticked.
    public event Action<MmlExportOptions>? OptionsChanged;

    // Reads every box into the set and reports it.
    private void OnOptionBoxChanged(object? sender, RoutedEventArgs e)
    {
        _options = _options with
        {
            Loops = LoopsBox.IsChecked == true,
            KeyOnRemoteCode = KeyOnRemoteCodeBox.IsChecked == true,
            HeldNotesAsLegato = HeldNotesAsLegatoBox.IsChecked == true,
        };
        OptionsChanged?.Invoke(_options);
    }
}
