using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The keyboard down the left of the roll: one key per semitone, lined up with the roll's rows, auditioning a key's pitch for as long as it is held and following a held press up and down the keys.
public sealed class PianoKeys : Control
{
    private readonly IBrush _backgroundBrush = DrawnSurface.Background;
    private double _verticalOffset;
    private double _viewportHeight;
    private readonly WindowCursor _windowCursor = new();

    // The left press held on a key, which ends the drag and the audition as it lets go.
    private readonly HeldKeyboardPress _heldPress;

    // Sets the keyboard to the key column's width, clipped to whatever height it is given, ending a held press's drag and audition as it lets go.
    public PianoKeys()
    {
        ClipToBounds = true;
        Width = RollMetrics.KeyWidth;
        _heldPress = new HeldKeyboardPress(this, [MouseButton.Left], () =>
        {
            DragEnded?.Invoke();
            SemitoneReleased?.Invoke();
        });
    }

    // Raised with the semitone of the key pressed, and again for each key a held press is dragged onto.
    public event Action<int>? SemitonePressed;

    // Raised when that press lets go, by whatever route.
    public event Action? SemitoneReleased;

    // Raised with how far down the keys a held press has got to, in the keyboard's own coordinates, on every move.
    public event Action<double>? DragAt;

    // Raised when that press lets go, on the same route as the semitone's release.
    public event Action? DragEnded;

    // Raised with how far a wheel-button drag over the keys has just moved, in the window's coordinates.
    public event Action<Vector>? PanRequested;

    // How far the roll has scrolled down, which is what the keys are drawn against.
    public double VerticalOffset
    {
        get
        {
            return _verticalOffset;
        }
        set
        {
            RedrawOnChange(ref _verticalOffset, value);
        }
    }

    // How tall the roll's content area is, which is how far down the keys are drawn.
    public double ViewportHeight
    {
        get
        {
            return _viewportHeight;
        }
        set
        {
            RedrawOnChange(ref _viewportHeight, value);
        }
    }

    // Whether every key of every keyboard is labeled with its note name rather than only each C, redrawing these keys when it changes.
    public bool IsShowingEveryNoteName
    {
        get
        {
            return PianoKeyLook.IsShowingEveryNoteName;
        }
        set
        {
            if (PianoKeyLook.IsShowingEveryNoteName == value)
            {
                return;
            }

            PianoKeyLook.IsShowingEveryNoteName = value;
            InvalidateVisual();
        }
    }

    // How far down the keys are drawn: the roll's content area, or the keyboard's own height before the roll has said.
    private double DrawnHeight
    {
        get
        {
            return _viewportHeight > 0 ? _viewportHeight : Bounds.Height;
        }
    }

    // Re-runs a held press from where the cursor is now, and answers whether one is still held.
    public bool FollowCursor()
    {
        return _windowCursor.FollowOver(this, _heldPress.IsHeld, cursor => FollowPressTo(cursor.Y));
    }

    // Lays the keys out again and redraws them at a new row height.
    public void RedrawAtRowHeight()
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    // Draws the keys the viewport shows, each C or every key labeled with its note name.
    public override void Render(DrawingContext dc)
    {
        double width = RollMetrics.KeyWidth;
        double height = DrawnHeight;
        dc.DrawRectangle(_backgroundBrush, null, new Rect(0, 0, width, height));

        using var clip = dc.PushClip(new Rect(0, 0, width, height));
        for (int row = 0; row < Song.RollRowCount; row++)
        {
            double y = row * RollMetrics.CellHeight - _verticalOffset;
            if (y + RollMetrics.CellHeight < 0)
            {
                continue;
            }

            if (y > height)
            {
                break;
            }

            DrawKey(dc, RollMetrics.SemitoneOfRow(row), y, width);
        }
    }

    // Starts an audition of the key the left button presses, holding the pointer until the release, or a pan from a wheel-button press.
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _windowCursor.Remember(this, e);

        if (WheelDrag.IsWheelPress(e))
        {
            PressWheelButton(e);
            return;
        }

        if (!_heldPress.CanStart(e) || SemitoneAt(e.GetPosition(this).Y) is not int semitone)
        {
            return;
        }

        _heldPress.Hold(e, semitone);
        SemitonePressed?.Invoke(semitone);
    }

    // Follows a held press to where the pointer has moved.
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _windowCursor.Remember(this, e);

        if (_heldPress.IsHeld)
        {
            FollowPressTo(e.GetPosition(this).Y);
        }
    }

    // As wide as the key column and as tall as the rows add up to.
    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(RollMetrics.KeyWidth, RollMetrics.GridHeight);
    }

    // Starts a pan from a plain wheel-button press, leaving a press with a modifier held, which drags a ruler line over the roll, untaken.
    private void PressWheelButton(PointerPressedEventArgs e)
    {
        if (_heldPress.IsHeld || TimeRuler.DoesWheelPressDragLine(e.KeyModifiers))
        {
            return;
        }

        e.Handled = true;
        WheelDrag.Follow(this, e, moved => PanRequested?.Invoke(moved));
    }

    // Writes one of the two scroll inputs and redraws, where it has moved by more than the viewport's tolerance.
    private void RedrawOnChange(ref double input, double value)
    {
        if (RollMetrics.IsSameViewportValue(input, value))
        {
            return;
        }

        input = value;
        InvalidateVisual();
    }

    // The semitone a vertical position falls on, or null outside the drawn keys.
    private int? SemitoneAt(double y)
    {
        if (y < 0 || y > DrawnHeight)
        {
            return null;
        }

        if (!RollMetrics.TryGetRowAt(y + _verticalOffset, out int row))
        {
            return null;
        }

        return RollMetrics.SemitoneOfRow(row);
    }

    // Reports a held press's position and sounds the key it has moved onto, where that is a different one.
    private void FollowPressTo(double y)
    {
        DragAt?.Invoke(y);

        if (SemitoneAt(Math.Clamp(y, 0, DrawnHeight)) is not int semitone || !_heldPress.HasMovedOnto(semitone))
        {
            return;
        }

        SemitonePressed?.Invoke(semitone);
    }

    // Draws one key at a height, with its note name where it is a C or every key is labeled.
    private void DrawKey(DrawingContext dc, int semitone, double y, double width)
    {
        PianoKeyLook.DrawFace(dc, semitone, new Rect(0, y, width, RollMetrics.CellHeight));
        if (PianoKeyLook.LabelOf(semitone) is not { } label)
        {
            return;
        }

        dc.DrawText(label, new Point(Space.Px4, y + (RollMetrics.CellHeight - label.Height) / 2));
    }
}
