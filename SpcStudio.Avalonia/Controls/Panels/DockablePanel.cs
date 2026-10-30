using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// One of the three right-hand panels: a framed column with a header across its top, the corner button over the header's right end that takes the
// panel out into a window of its own and docks it back, and the rows under the header in a body that scrolls.
public sealed class DockablePanel : ThemedBorder
{
    private readonly Panel _headerRow = new() { Margin = new Thickness(0, 0, Space.Px16, 0) };
    private readonly IconShape _cornerIcon = new() { Fill = ThemeResource.TextSecondaryBrush };
    private readonly Button _cornerButton = new() { Classes = { ThemeClasses.PanelCorner }, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
    private readonly ScrollViewer _scroll = new() { Classes = { ThemeClasses.PanelScroll }, Focusable = false };
    private readonly StackPanel _body = new() { Background = Brushes.Transparent, Margin = new Thickness(0, 0, Space.Px6, 0) };
    private Control? _header;

    // Builds the frame, its header row with the corner button, and its scrolling body.
    public DockablePanel()
    {
        Classes.Add(ThemeClasses.PanelFrame);
        Classes.Add(ThemeClasses.SidePanel);
        _cornerButton.Content = _cornerIcon;
        _cornerButton.Click += (_, e) => PopOutClick?.Invoke(this, e);
        _headerRow.Children.Add(_cornerButton);
        DockPanel.SetDock(_headerRow, Dock.Top);
        _scroll.Content = _body;
        Child = new DockPanel { Children = { _headerRow, _scroll } };
    }

    // Raised by a press on the corner button.
    public event EventHandler<RoutedEventArgs>? PopOutClick;

    // What stands across the top of the panel, under the corner button's right end, or null for the corner button alone.
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
                _headerRow.Children.Insert(0, value);
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

    // The corner button's tooltip while the panel stands in the column.
    public string PopOutTooltip { get; set; } = "";

    // The corner button's tooltip while the panel stands in a window of its own.
    public string DockTooltip { get; set; } = "";

    // Draws the corner button for whether the panel stands in a window of its own.
    public void ShowPoppedOut(bool isPoppedOut)
    {
        PanelCornerButton.ShowState(_cornerButton, _cornerIcon, isPoppedOut, PopOutTooltip, DockTooltip);
    }
}
