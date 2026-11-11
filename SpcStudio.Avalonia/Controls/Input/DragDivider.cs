using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The strip between two framed surfaces, with its grip at its middle, that is dragged to resize the surface after it - the one below a strip lying
// flat, the one right of a strip standing on its side.
public sealed class DragDivider : ThemedBorder
{
    private readonly Border _grip = new();
    private Orientation _orientation = Orientation.Horizontal;
    private double _pressPosition;
    private double _sizeAtPress;
    private Func<double> _readSize = () => 0;
    private Action<double> _applySize = _ => { };

    // Builds the strip lying flat, with its grip.
    public DragDivider()
    {
        Child = _grip;
        ShowOrientation();
    }

    // Horizontal for a strip lying flat, which trades height, and vertical for one standing on its side, which trades width.
    public Orientation Orientation
    {
        get
        {
            return _orientation;
        }
        set
        {
            _orientation = value;
            ShowOrientation();
        }
    }

    // Takes how to read the size of the surface after the strip and how to give it a new one, which a drag of the strip then drives.
    public void DriveSizeOf(Func<double> readSize, Action<double> applySize)
    {
        _readSize = readSize;
        _applySize = applySize;
    }

    // Takes hold of the strip, noting where the pointer stood and how large the surface after it was.
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _pressPosition = PositionAlongDrag(e);
        _sizeAtPress = _readSize();
        e.Pointer.Capture(this);
        Classes.Add(ThemeClasses.Dragging);
        e.Handled = true;
    }

    // Gives the surface after the strip its size at the press, less how far the pointer has moved towards it.
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.Pointer.Captured != this)
        {
            return;
        }

        _applySize(_sizeAtPress - (PositionAlongDrag(e) - _pressPosition));
    }

    // Lets go of the strip.
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
        Classes.Remove(ThemeClasses.Dragging);
    }

    // Dresses the strip and its grip for the way the strip lies.
    private void ShowOrientation()
    {
        bool isStanding = _orientation == Orientation.Vertical;
        Classes.Set(ThemeClasses.PanelHandle, !isStanding);
        Classes.Set(ThemeClasses.PanelHandleVertical, isStanding);
        _grip.Classes.Set(ThemeClasses.DividerGrip, !isStanding);
        _grip.Classes.Set(ThemeClasses.DividerGripVertical, isStanding);
    }

    // Where the pointer stands in the window, along the axis the strip is dragged on.
    private double PositionAlongDrag(PointerEventArgs e)
    {
        var position = e.GetPosition(TopLevel.GetTopLevel(this));
        return _orientation == Orientation.Vertical ? position.X : position.Y;
    }
}
