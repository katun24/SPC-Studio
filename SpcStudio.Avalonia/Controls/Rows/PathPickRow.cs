using Avalonia.Controls;
using Avalonia.Interactivity;
using SpcStudio.Model;

namespace SpcStudio.Controls;

// A path's field beside a Browse button, what a press on the button picks and what a typed path is checked against being the window's.
public sealed class PathPickRow : Grid
{
    private const string BrowseText = "Browse...";
    private const string ColumnLayout = "*,Auto";
    private const int ButtonColumn = 1;

    private readonly TextBox _pathField = new();
    private string? _shownPath;

    // Builds the field in the column that stretches and the button in the one beside it, the field offering what is typed on Enter or on losing the focus.
    public PathPickRow()
    {
        ColumnDefinitions = new ColumnDefinitions(ColumnLayout);
        FieldInput.SetAccepts(_pathField, FieldChars.DiskPath);
        FieldCommit.OnEnterOrLeave(_pathField, OfferTypedPath);
        var browseButton = new Button { Content = BrowseText };
        browseButton.Click += (_, e) => BrowseClick?.Invoke(this, e);
        SetColumn(browseButton, ButtonColumn);
        Children.Add(_pathField);
        Children.Add(browseButton);
    }

    // Raised by a press on the Browse button.
    public event EventHandler<RoutedEventArgs>? BrowseClick;

    // Raised by Enter or the field losing the focus where the field holds another path than the one shown, with the path typed, empty where the field was emptied.
    public event Action<string>? PathTyped;

    // The path the field shows, or null where none is set, which leaves the field empty.
    public string? ShownPath
    {
        get
        {
            return _shownPath;
        }
        set
        {
            _shownPath = value;
            ShowPathAgain();
        }
    }

    // Writes the path shown into the field, over whatever was typed there.
    public void ShowPathAgain()
    {
        _pathField.Text = _shownPath;
    }

    // Raises the path typed, without its outer spaces, where it is not the path shown.
    private void OfferTypedPath()
    {
        string typedPath = DiskNameRules.Normalize(_pathField.Text);
        if (typedPath == DiskNameRules.Normalize(_shownPath))
        {
            return;
        }

        PathTyped?.Invoke(typedPath);
    }
}
