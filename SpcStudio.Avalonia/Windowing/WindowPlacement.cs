using Avalonia;
using Avalonia.Controls;

namespace SpcStudio;

// Remembers the main window's position, size and maximized state between sessions, keeping the bounds it last had while neither maximized nor minimized.
public sealed class WindowPlacement
{
    private readonly Window _window;

    // The last position and client size the window had while not maximized, seeded from its starting values.
    private PixelPoint _normalPosition;
    private Size _normalSize;

    // Starts following the window's position and size.
    public WindowPlacement(Window window)
    {
        _window = window;
        _normalPosition = window.Position;
        _normalSize = new Size(window.Width, window.Height);
        window.PositionChanged += (_, _) => RememberNormalBounds();
        window.SizeChanged += (_, _) => RememberNormalBounds();
    }

    // Puts a saved placement back from the window's constructor: the size and the position now, centered on the primary screen where the saved corner
    // is on no screen, and the maximized state once the window has opened.
    public void Apply(AppSettings settings)
    {
        _window.Width = settings.Width;
        _window.Height = settings.Height;

        var savedCorner = new PixelPoint(settings.Left, settings.Top);
        if (_window.Screens.ScreenFromPoint(savedCorner) is not null)
        {
            _window.Position = savedCorner;
        }
        else if (_window.Screens.Primary is { } screen)
        {
            var area = screen.WorkingArea;
            var centered = CenteredOn(area.Position, area.Size.ToSize(screen.Scaling), new Size(settings.Width, settings.Height), screen.Scaling);
            _window.Position = new PixelPoint(Math.Max(area.X, centered.X), Math.Max(area.Y, centered.Y));
        }

        if (settings.Maximized)
        {
            _window.Opened += MaximizeOnOpened;
        }
    }

    // The placement to save, a minimized window being saved as normal.
    public AppSettings Capture()
    {
        return new AppSettings
        {
            Left = _normalPosition.X,
            Top = _normalPosition.Y,
            Width = (int)_normalSize.Width,
            Height = (int)_normalSize.Height,
            Maximized = _window.WindowState == WindowState.Maximized,
        };
    }

    // The screen position that centers a window of a size on an area, both sizes in device-independent pixels and the area's corner in screen ones.
    public static PixelPoint CenteredOn(PixelPoint areaCorner, Size areaSize, Size windowSize, double scaling)
    {
        return new PixelPoint(
            areaCorner.X + (int)((areaSize.Width - windowSize.Width) / 2 * scaling),
            areaCorner.Y + (int)((areaSize.Height - windowSize.Height) / 2 * scaling));
    }

    // Takes the window's position and size as the ones to restore to, while it is neither maximized nor minimized.
    private void RememberNormalBounds()
    {
        if (_window.WindowState != WindowState.Normal)
        {
            return;
        }

        _normalPosition = _window.Position;
        _normalSize = _window.ClientSize;
    }

    // Maximizes the window once, as it opens.
    private void MaximizeOnOpened(object? sender, EventArgs e)
    {
        _window.Opened -= MaximizeOnOpened;
        _window.WindowState = WindowState.Maximized;
    }
}
