using Avalonia;
using SpcStudio.Model;

namespace SpcStudio.Controls;

// What a left click on empty grid lays down - one fresh note, or the selection laid down again under the cursor - the preview that shows it there first, and the Shift resize of that preview.
public sealed partial class PianoRoll
{
    // What a click would place, under the cursor: one note where nothing is selected, one per selected note otherwise, and none where nothing would land.
    private readonly PlacementPreview<NotePlacement> _placementPreview = new();

    // A note as a click places it, at a tick already snapped, on its instrument's rung for every value an instrument can give it.
    public static Note FreshNote(Song song, int tick, int length, int semitone, int instrument, int gate, int velocity, int fineTune)
    {
        var instrumentPlayed = song.InstrumentById(instrument);
        if (instrumentPlayed is { IsNoise: true })
        {
            semitone = SnesNoise.ClampToBand(semitone);
        }

        var note = NoteDefaults.OnInstrumentRungsExceptEcho(new Note(tick, length, semitone, instrument, gate, velocity, fineTune)) with { EchoSource = LaneSource.Instrument };
        return NoteDefaults.SyncedWithInstrument(note, instrumentPlayed);
    }

    // The note a click places with nothing selected: snapped, at the length last chosen, on the current instrument and `q`, or null with no instrument.
    private Note? PlainNoteAt(int tick, int semitone)
    {
        if (CurrentInstrumentId is not int instrument)
        {
            return null;
        }

        return FreshNote(_song, _song.SnapDown(tick), _placedNoteLengthTicks, semitone, instrument, CurrentGate, CurrentVelocity, CurrentFineTune);
    }

    // The notes a click on an empty cell would place: the plain note where nothing is selected, and the selection laid down again otherwise.
    private List<NotePlacement> NotesToPlace(int tick, int semitone)
    {
        var selection = Selection;
        if (selection.Count == 0)
        {
            return PlainNoteToPlace(tick, semitone);
        }

        return SelectionToPlace(selection, tick, semitone);
    }

    // The plain note on the edited channel, its row held where the current instrument's bends fit on the roll whole, or nothing with no instrument.
    private List<NotePlacement> PlainNoteToPlace(int tick, int semitone)
    {
        if (PlainNoteAt(tick, SemitoneWhereInstrumentBendsFit(semitone)) is not Note plain)
        {
            return [];
        }

        return [new NotePlacement(_selectedChannel, plain)];
    }

    // A semitone moved in just far enough for the bends the current instrument hands a placed note to stay on the roll.
    private int SemitoneWhereInstrumentBendsFit(int semitone)
    {
        if (CurrentInstrumentId is not int instrumentId || _song.InstrumentById(instrumentId) is not Instrument instrument)
        {
            return semitone;
        }

        return PitchBend.StartsHeldIn(Song.SemitoneRange, PitchBend.Playable(instrument.Bends, _placedNoteLengthTicks, instrument.IsHeld)).Clamp(semitone);
    }

    // Every selected note moved as one group so that the earliest, topmost of them lands at a cell, the anchor first and the group clamped to the roll.
    private List<NotePlacement> SelectionToPlace(IReadOnlyList<(int Channel, Note Note)> selection, int tick, int semitone)
    {
        int anchorIndex = AnchorIndexOf(selection);
        Note anchor = selection[anchorIndex].Note;
        var (earliestTick, lowestReach, highestReach) = ReachOfGroup(selection.Select(member => member.Note));

        int startOffsetTicks = SnapSpan.OffsetToCursor(_song, tick, anchor.StartTick, earliestTick);
        int semitoneOffset = Song.SemitoneRange.HoldingReach(lowestReach, highestReach).Clamp(semitone - anchor.Semitone);
        bool isOnOneChannel = selection.All(member => member.Channel == selection[0].Channel);
        int? uniformLengthTicks = _placementPreview.UniformLength(_placedNoteLengthTicks);
        int[] placingOrder = [anchorIndex, .. Enumerable.Range(0, selection.Count).Where(index => index != anchorIndex)];
        var group = new List<NotePlacement>(selection.Count);
        foreach (int index in placingOrder)
        {
            var (channel, source) = selection[index];
            Note moved = source.WithLength(uniformLengthTicks ?? source.LengthTicks) with
            {
                StartTick = source.StartTick + startOffsetTicks,
                Semitone = source.Semitone + semitoneOffset,
            };
            group.Add(new NotePlacement(isOnOneChannel ? _selectedChannel : channel, _song.NoteAsPlaced(moved)));
        }

        return group;
    }

    // Where in a selection the earliest note stands, the topmost of those sharing its tick.
    private static int AnchorIndexOf(IReadOnlyList<(int Channel, Note Note)> selection)
    {
        int anchorIndex = 0;
        for (int index = 1; index < selection.Count; index++)
        {
            Note anchor = selection[anchorIndex].Note;
            Note candidate = selection[index].Note;
            if (candidate.StartTick < anchor.StartTick || (candidate.StartTick == anchor.StartTick && candidate.Semitone > anchor.Semitone))
            {
                anchorIndex = index;
            }
        }

        return anchorIndex;
    }

    // The notes a click on an empty cell places: the preview as a Shift resize has left it, and otherwise what the cell would place.
    private List<NotePlacement> PlacementsForClick(int tick, int semitone)
    {
        return _placementPreview.ResizedMembers() ?? NotesToPlace(tick, semitone);
    }

    // Draws what a left click at a point would place, or nothing where the point is off the grid's rows, left of it or on a note of the edited channel.
    private void ShowPreview(Point? cursor)
    {
        if (_placementPreview.Show(PlacementsUnder(cursor)))
        {
            InvalidateVisual();
        }
    }

    // What a left click at a point would place, or nothing where it would place nothing.
    private List<NotePlacement> PlacementsUnder(Point? cursor)
    {
        if (cursor is not Point point || !TryGetCellAt(point, out int tick, out int semitone))
        {
            return [];
        }

        if (NoteDrawnAt(_selectedChannel, point, tick, semitone) is not null)
        {
            return [];
        }

        return NotesToPlace(tick, semitone);
    }

    // Takes the note preview, the joint preview and the hovered note's joints off the roll.
    private void HidePreview()
    {
        HideNotePreview();
        ShowNodeInsertPreview(null);
        ShowBendNodesOfNoteAt(null);
    }

    // Ends a Shift resize of the note preview and takes the note preview off the roll.
    private void HideNotePreview()
    {
        _placementPreview.EndResize();
        ShowPreview(null);
    }

    // Redraws the preview for where the cursor now is: the joints of the note under it, and the joint preview alone while B is held, and otherwise the
    // note preview, resized while Shift resizes it and following the cursor otherwise.
    private void UpdatePreviewAt(Point cursor)
    {
        ShowBendNodesOfNoteAt(cursor);
        ShowNodeInsertPreview(_isNodeInsertKeyHeld ? cursor : null);
        if (_isNodeInsertKeyHeld)
        {
            HideNotePreview();
            return;
        }

        if (_placementPreview.IsResizing)
        {
            ResizePreviewTo(cursor);
            return;
        }

        ShowPreview(cursor);
    }

    // Stretches every member of the preview to one length from its end at the cursor, which becomes the length the next click places.
    private void ResizePreviewTo(Point cursor)
    {
        if (_placementPreview.Count == 0)
        {
            return;
        }

        var (startOffsetTicks, lengthChangeTicks, rowsDown) = ClampedResizeDeltas(cursor);
        int resizedLengthTicks = WithDeltas(_grabbedBaseline, startOffsetTicks, lengthChangeTicks, rowsDown).LengthTicks;
        var resized = new List<NotePlacement>(_placementPreview.Count);
        for (int index = 0; index < _placementPreview.Count; index++)
        {
            Note baseline = _dragBaselines[index].Note;
            Note stretched = WithDeltas(baseline, startOffsetTicks, 0, rowsDown).WithLength(resizedLengthTicks);
            resized.Add(_placementPreview.Members[index] with { Note = _song.NoteAsPlaced(stretched) });
        }

        if (!_placementPreview.ShowResized(resized))
        {
            return;
        }

        _placedNoteLengthTicks = resizedLengthTicks;
        InvalidateVisual();
    }

    // One note a click would place: the channel it lands on and the note.
    private readonly record struct NotePlacement(int Channel, Note Note)
    {
        // The channel and the note together, as the song and the selection take them.
        public (int Channel, Note Note) ChannelAndNote
        {
            get
            {
                return (Channel, Note);
            }
        }
    }
}
