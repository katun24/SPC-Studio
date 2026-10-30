using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The piano roll: one row per semitone across the song, where the left button places, selects, moves and resizes notes on the channel being edited, the right button removes one or sweeps a selection,
// and the middle button pans. This part holds the roll's settings, its state, the events it raises and the part of the grid the viewer shows.
public sealed partial class PianoRoll : Control, IShiftResizeSurface
{
    public const int ReferenceSemitone = 12;

    private Song _song = new();
    private int _selectedChannel;
    private int _placedNoteLengthTicks = NoteLength.Default.Ticks;
    private bool _isShowingOtherChannels = true;
    private readonly ViewSpan _horizontalView = new();
    private readonly ViewSpan _verticalView = new();

    // Raised with a note a press on the grid or a drag to another row sounds, at its place in the song.
    public event Action<Note>? NoteAuditioned;

    // Raised with a note standing for a key beside the roll, which is sounded as a pitch and nothing else.
    public event Action<Note>? KeyAuditioned;

    // Raised when the press that started an audition ends, by whatever route.
    public event Action? NoteReleased;

    // Raised when a click would have placed a note but there is no instrument to give it.
    public event Action? PlacementRefused;

    // Raised whenever an edit changes the song.
    public event Action? SongEdited;

    // Raised with where a sweep or a note drag has got to, in the roll's own coordinates, whether or not it reached a different tick.
    public event Action<double, double?>? DragAt;

    // Raised when that drag ends.
    public event Action? DragEnded;

    // Raised with how far a middle-button drag has moved the cursor since the last event.
    public event Action<Vector>? PanRequested;

    // Raised by a middle press with Shift, Ctrl or Alt held, which drags one of the ruler's lines instead of panning.
    public event Action<PointerPressedEventArgs>? WheelDragRequested;

    // Raised with every selected note in time order, and with an empty list when nothing is selected.
    public event Action<IReadOnlyList<Note>>? SelectionChanged;

    // Raised with every selected note in time order after an edit or an undo rewrote, moved or dropped selected notes rather than a gesture choosing them.
    public event Action<IReadOnlyList<Note>>? SelectedNotesEdited;

    // Raised with the channel an Alt+click picked a note on.
    public event Action<int>? ChannelPicked;

    // Raised while a drag makes the roll longer or shorter.
    public event Action? RollResized;

    // Raised when a note drag takes hold of its notes, and at every step of it or of a bend joint drag, with the notes as they now stand.
    public event Action<IReadOnlyList<(int Channel, Note Note)>>? NotesDragged;

    // Makes the roll take the keyboard focus, and lets Shift steer its drags and its preview.
    public PianoRoll()
    {
        Focusable = true;
        _shiftResize = new ShiftResize<NotePlacement>(_placementPreview, this);
    }

    // The song the roll edits; replacing it empties the clipboard and the selection.
    public Song Song
    {
        get
        {
            return _song;
        }
        set
        {
            _song = value;
            ClipboardNotes.Clear();
            ClearSelection();
            Refresh();
        }
    }

    // The instrument a newly placed note is given, or null where the song has none.
    public int? CurrentInstrumentId { get; set; }

    // The `q` gate a newly placed note is given.
    public int CurrentGate { get; set; } = SnesTiming.DefaultGate;

    // The `q` velocity a newly placed note is given.
    public int CurrentVelocity { get; set; } = SnesVolume.DefaultVelocity;

    // The fine tune a newly placed note is given.
    public int CurrentFineTune { get; set; } = SnesPitch.MinFineTune;

    // The channel the pointer edits, redrawing the roll when it changes.
    public int SelectedChannel
    {
        get
        {
            return _selectedChannel;
        }
        set
        {
            if (_selectedChannel == value)
            {
                return;
            }

            _selectedChannel = value;
            InvalidateVisual();
        }
    }

    // Whether the channels not being edited are drawn, redrawing the roll when it changes.
    public bool IsShowingOtherChannels
    {
        get
        {
            return _isShowingOtherChannels;
        }
        set
        {
            if (_isShowingOtherChannels == value)
            {
                return;
            }

            _isShowingOtherChannels = value;
            InvalidateVisual();
        }
    }

    // Whether a press on the grid sounds the note it lands on.
    public bool DoesAuditionNotes { get; set; } = true;

    // How wide the grid is drawn for the song.
    private double GridWidth
    {
        get
        {
            return RollMetrics.GridWidth(_song);
        }
    }

    // Re-measures and redraws, the roll's length following the notes on it.
    public void Refresh()
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    // Tells the window the roll's length changed, where it differs from the length it had before an edit.
    private void ReportRollResize(int rollTicksBefore)
    {
        RollResizes.RaiseIfChanged(_song, rollTicksBefore, RollResized);
    }

    // Takes the part of the grid the roll's viewer shows, redrawing where it moved.
    public void SetViewport(double left, double top, double width, double height)
    {
        if (!_horizontalView.Differs(left, width) && !_verticalView.Differs(top, height))
        {
            return;
        }

        _horizontalView.Set(left, width);
        _verticalView.Set(top, height);
        InvalidateVisual();
    }

    // Asks to be as wide as the roll's surface, and as tall as the grid and the strip below it that the closing line is drawn on.
    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(RollMetrics.SurfaceWidth(_song), RollMetrics.GridHeight + RollMetrics.GridOverhang);
    }
}
