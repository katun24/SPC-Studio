using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace SpcStudio;

// One of the main window's right-hand panels, the column's whole deck of them or the SPC Player's side panel, standing in a window of its own; the
// panel is the control that was in its column, moved here and moved back, and the window passes the keys it does not take to the window that opened it.
public partial class PanelWindow : StandaloneWindow
{
    private bool _isClosing;

    // Builds the window.
    public PanelWindow()
    {
        InitializeComponent();
    }

    // Raised when the panel is to go back into its column: the corner button, Escape, or the window's own close box.
    public event Action? DockRequested;

    // Raised by a key going down that the window has not taken, for the window that opened it to read as though the focus were in it.
    public event Action<KeyEventArgs>? KeyPressed;

    // Raised by a key coming up, for the window that opened it to read as though the focus were in it.
    public event Action<KeyEventArgs>? KeyReleased;

    // Whether the dock now being asked for came from the window's own close box, the close already being under way.
    public bool IsClosingItself { get; private set; }

    // A window as tall as a placement left it or as the deck its panels leave, as wide as asked where that is given, and which asks for the dock on
    // Escape and its close box.
    public static PanelWindow Create(string title, PanelPlacement? placement, double deckHeight, double width, Action dock)
    {
        var window = new PanelWindow { Title = title };
        double height = placement is { Height: > 0 } ? placement.Height : deckHeight;
        if (height > 0)
        {
            window.Height = height;
        }

        if (width > 0)
        {
            window.Width = width;
        }

        window.DockRequested += dock;
        return window;
    }

    // Shows the window where a placement left it, or centered on another window.
    public void ShowAt(PanelPlacement? placement, AppWindow besideWindow)
    {
        if (placement is { Left: not 0 } or { Top: not 0 })
        {
            Position = new PixelPoint(placement.Left, placement.Top);
            Show();
            return;
        }

        besideWindow.ShowBeside(this);
    }

    // Where the window stands and how tall it is.
    public PanelPlacement CapturePlacement()
    {
        return new PanelPlacement
        {
            PoppedOut = true,
            Left = Position.X,
            Top = Position.Y,
            Height = (int)Height,
        };
    }

    // Puts the panel in this window.
    public void Take(Control panel)
    {
        PanelHost.Children.Add(panel);
    }

    // Hands the panel back, leaving the window empty and laid out before it returns.
    public Control Release()
    {
        var panel = PanelHost.Children[0];
        PanelHost.Children.Clear();
        UpdateLayout();
        return panel;
    }

    // Closes without asking for the dock again.
    public void CloseWithoutDocking()
    {
        _isClosing = true;
        Close();
    }

    // Closes without asking for the dock again, unless the window's own close is what is running.
    public void CloseUnlessClosingItself()
    {
        if (!IsClosingItself)
        {
            CloseWithoutDocking();
        }
    }

    // Asks for the dock when the close box is pressed, the close carrying on once the window that opened it has taken the panel.
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        IsClosingItself = true;
        DockRequested?.Invoke();
    }

    // Passes a key going down to the window that opened it.
    protected override void HandleWindowKeyDown(KeyEventArgs e)
    {
        KeyPressed?.Invoke(e);
    }

    // Passes a key coming up to the window that opened it.
    protected override void HandleWindowKeyUp(KeyEventArgs e)
    {
        KeyReleased?.Invoke(e);
    }
}
