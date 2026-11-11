using SpcStudio.Model;

namespace SpcStudio.Controls;

// A lane whose values belong to one channel rather than to the song, showing whichever channel the window points it at.
public abstract class ChannelChangeLane : ChangeLane
{
    private int _channel;

    // Takes the two brushes the lane's cells are drawn in.
    protected ChannelChangeLane(LaneCellBrushes cellBrushes) : base(cellBrushes)
    {
    }

    public override bool IsPerChannel
    {
        get
        {
            return true;
        }
    }

    // Which channel's changes the lane shows; setting it redraws.
    public int Channel
    {
        get
        {
            return _channel;
        }
        set
        {
            if (_channel == value)
            {
                return;
            }

            _channel = value;
            Refresh();
        }
    }

    // The notes being dragged right now with the channel each is over, asked for by the drawing; null or empty when nothing is being dragged.
    public Func<IReadOnlyList<(int Channel, Note Note)>>? DraggedNotes { get; set; }

    // Whether any channel holds a cell in this row, not only the one showing.
    public override bool HasChanges
    {
        get
        {
            int shownChannel = _channel;
            try
            {
                for (int channel = 0; channel < (Song?.ChannelCount ?? 0); channel++)
                {
                    _channel = channel;
                    if (base.HasChanges)
                    {
                        return true;
                    }
                }

                return false;
            }
            finally
            {
                _channel = shownChannel;
            }
        }
    }

    // The channel's notes as the drawing shows them, the notes being dragged onto it put where the drag has got to; null where nothing is dragged.
    protected IReadOnlyList<Note>? PreviewedNotes()
    {
        if (Song is null || DraggedNotes?.Invoke() is not { Count: > 0 } draggedNotes)
        {
            return null;
        }

        var notes = new List<Note>(Song.Notes(Channel));
        foreach (var (channel, note) in draggedNotes)
        {
            if (channel == Channel)
            {
                notes.Add(note);
            }
        }

        notes.Sort((first, second) => first.StartTick.CompareTo(second.StartTick));
        return notes;
    }

    // The stretches the notes' own values on a lane of one number decide, each at the value it plays.
    protected IEnumerable<DrawnRegion> FlatOverriddenRegions(NoteFlatLane lane)
    {
        if (Song is not Song song)
        {
            return [];
        }

        var overridingNotes = NoteRungs.OverridingNotes(song, Channel, PreviewedNotes(), note => lane.ResolvedValue(song, note));
        return overridingNotes.Select(overriding => new DrawnRegion(overriding.Note.StartTick, overriding.Note.EndTick(), overriding.Value, false, overriding.Value));
    }
}
