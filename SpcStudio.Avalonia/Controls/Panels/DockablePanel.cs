using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// One of the three right-hand panels: a framed column with a header across its top and the rows under the header in a body that scrolls.
public sealed class DockablePanel : ThemedBorder
{
    private readonly Panel _headerRow = new() { Margin = new Thickness(0, 0, Space.Px16, 0) };
    private readonly ScrollViewer _scroll = new() { Classes = { ThemeClasses.PanelScroll, ThemeClasses.FadedEdges }, Focusable = false };
    private readonly StackPanel _body = new() { Background = Brushes.Transparent, Margin = new Thickness(0, 0, Space.Px6, 0) };
    private Control? _header;

    // Builds the frame, its header row and its scrolling body.
    public DockablePanel()
    {
        Classes.Add(ThemeClasses.PanelFrame);
        Classes.Add(ThemeClasses.SidePanel);
        DockPanel.SetDock(_headerRow, Dock.Top);
        _scroll.Content = _body;
        Child = new DockPanel { Children = { _headerRow, _scroll } };
    }

    // What stands across the top of the panel, or null for nothing.
    public Control? Header
    {
        get
        {
            return _header;
        }
        set
        {
            if (_header is not null)
            {
                _headerRow.Children.Remove(_header);
            }

            _header = value;
            if (value is not null)
            {
                _headerRow.Children.Add(value);
            }
        }
    }

    // The rows under the header, in the order they stand.
    public Avalonia.Controls.Controls Body
    {
        get
        {
            return _body.Children;
        }
    }
}
