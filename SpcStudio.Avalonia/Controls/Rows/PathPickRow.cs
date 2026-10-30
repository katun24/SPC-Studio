using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SpcStudio.Controls;

// A chosen path's readout beside a Browse button, what a press on the button picks being the window's.
public sealed class PathPickRow : Grid
{
    private const string BrowseText = "Browse...";
    private const string ColumnLayout = "*,Auto";
    private const int ButtonColumn = 1;

    private readonly SelectableTextBlock _readout = new();
    private string? _shownPath;
    private string? _noPathText;

    // Builds the readout in the column that stretches and the button in the one beside it.
    public PathPickRow()
    {
        ColumnDefinitions = new ColumnDefinitions(ColumnLayout);
        var browseButton = new Button { Content = BrowseText };
        browseButton.Click += (_, e) => BrowseClick?.Invoke(this, e);
        SetColumn(browseButton, ButtonColumn);
        Children.Add(_readout);
        Children.Add(browseButton);
    }

    // Raised by a press on the Browse button.
    public event EventHandler<RoutedEventArgs>? BrowseClick;

    // The path the readout shows, or null where none is set, which the readout then says.
    public string? ShownPath
    {
        get
        {
            return _shownPath;
        }
        set
        {
            _shownPath = value;
            ShowPathOrNone();
        }
    }

    // What the readout says where no path is set.
    public string? NoPathText
    {
        get
        {
            return _noPathText;
        }
        set
        {
            _noPathText = value;
            ShowPathOrNone();
        }
    }

    // Shows the path in the readout, or the text for no path where none is set.
    private void ShowPathOrNone()
    {
        _readout.Text = string.IsNullOrEmpty(_shownPath) ? _noPathText : _shownPath;
    }
}
