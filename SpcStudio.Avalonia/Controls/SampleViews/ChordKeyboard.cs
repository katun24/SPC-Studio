using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The Create Chord Sample window's keyboard: the piano roll's keys on their side, two octaves either side of the root in the middle, every key named at
// its top, each key in the chord covered in a see-through green inside its edge, and a left press or drag taking keys into the chord and a right one
// taking them out.
public sealed class ChordKeyboard : Control
{
    public const int SemitonesEitherSide = OctavesEitherSide * SnesPitch.SemitonesPerOctave;
    private const int OctavesEitherSide = 2;
    private const int KeyCount = SemitonesEitherSide * 2 + 1;
    private const double KeyBreadth = RollMetrics.BaseCellHeight + Space.Px2;
    private const double KeyLength = RollMetrics.KeyWidth;

    private readonly IBrush _inChordBrush = new SolidColorBrush(Palette.ChordKeyFill);
    private IReadOnlySet<int> _semitonesInChord = new HashSet<int>();
    private int _rootSemitone;

    // The left or right press held on a key, followed as it is dragged and reported as it lets go.
    private readonly HeldKeyboardPress _heldPress;

    // Builds the keyboard, reporting each held press's end.
    public ChordKeyboard()
    {
        _heldPress = new HeldKeyboardPress(this, [MouseButton.Left, MouseButton.Right], () => KeyReleased?.Invoke());
    }

    // Raised with the semitones from the root of each key a left press lands on or is dragged onto.
    public event Action<int>? KeyIncluded;

    // Raised with the semitones from the root of each key a right press lands on or is dragged onto.
    public event Action<int>? KeyExcluded;

    // Raised when a held press lets go, by whatever route.
    public event Action? KeyReleased;

    // The note of the middle key, in semitones above C4, which names every key.
    public int RootSemitone
    {
        get
        {
            return _rootSemitone;
        }
        set
        {
            _rootSemitone = value;
            InvalidateVisual();
        }
    }

    // The keys in the chord, in semitones from the root.
    public IReadOnlySet<int> SemitonesInChord
    {
        get
        {
            return _semitonesInChord;
        }
        set
        {
            _semitonesInChord = value;
            InvalidateVisual();
        }
    }

    // Draws every key side by side, the keys in the chord filled inside their edges, each named at its top.
    public override void Render(DrawingContext dc)
    {
        for (int key = 0; key < KeyCount; key++)
        {
            int semitoneFromRoot = key - SemitonesEitherSide;
            int semitone = _rootSemitone + semitoneFromRoot;
            var face = new Rect(key * KeyBreadth, 0, KeyBreadth, KeyLength);
            PianoKeyLook.DrawFace(dc, semitone, face);
            if (_semitonesInChord.Contains(semitoneFromRoot))
            {
                dc.DrawRectangle(_inChordBrush, null, PianoKeyLook.BodyOf(face));
            }

            var label = PianoKeyLook.NameLabelOf(semitone);
            dc.DrawText(label, new Point(face.X + (KeyBreadth - label.Width) / 2, Space.Px4));
        }
    }

    // Reports the key a left or right press lands on, holding the pointer until the release.
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!_heldPress.CanStart(e))
        {
            return;
        }

        int key = KeyAt(e.GetPosition(this).X);
        _heldPress.Hold(e, key);
        ReportHeldKey();
    }

    // Reports each key a held press is dragged onto.
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_heldPress.IsHeld || !_heldPress.HasMovedOnto(KeyAt(e.GetPosition(this).X)))
        {
            return;
        }

        ReportHeldKey();
    }

    // As wide as the keys side by side and as tall as one key is long.
    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(KeyCount * KeyBreadth, KeyLength);
    }

    // The key across from a point along the keyboard, the end key past either end.
    private static int KeyAt(double x)
    {
        return Math.Clamp((int)Math.Floor(x / KeyBreadth), 0, KeyCount - 1);
    }

    // Reports the key the held press is on as taken into the chord where the left button holds it, and taken out where the right one does.
    private void ReportHeldKey()
    {
        int semitoneFromRoot = _heldPress.HeldKey - SemitonesEitherSide;
        if (_heldPress.HeldButton == MouseButton.Left)
        {
            KeyIncluded?.Invoke(semitoneFromRoot);
            return;
        }

        KeyExcluded?.Invoke(semitoneFromRoot);
    }
}
