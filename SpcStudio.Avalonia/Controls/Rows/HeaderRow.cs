using Avalonia.Controls;
using Avalonia.Layout;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// A header's word at the left of a row, in the panel header look, with whatever controls the row is given standing at its right end.
public class HeaderRow : ThemedGrid
{
    private const string ColumnLayout = "*,Auto";
    private const int ControlColumn = 1;

    private readonly TextBlock _header = new() { VerticalAlignment = VerticalAlignment.Center };

    // Builds the row's columns and its word.
    public HeaderRow()
    {
        ColumnDefinitions = new ColumnDefinitions(ColumnLayout);
        _header.Classes.Add(ThemeClasses.PanelHeader);
        Children.Add(_header);
    }

    // The word at the left of the row.
    public string? Header
    {
        get
        {
            return _header.Text;
        }
        set
        {
            _header.Text = value;
        }
    }

    // Stands every control the row was given in its right-hand column.
    protected override void OnInitialized()
    {
        base.OnInitialized();
        foreach (var control in Children.Where(child => child != _header))
        {
            SetColumn(control, ControlColumn);
        }
    }
}
