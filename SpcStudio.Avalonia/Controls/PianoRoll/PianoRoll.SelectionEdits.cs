using System.Collections.Immutable;
using Avalonia.Controls;
using SpcStudio.Model;

namespace SpcStudio.Controls;

// The edits the note panel and the keys make to the selected notes: the values set and stepped, the delete, and the clipboard.
public sealed partial class PianoRoll : Control
{
    // The notes Ctrl+C and Ctrl+X put aside, their start ticks measured from the earliest of them.
    private static readonly List<CopiedNote> ClipboardNotes = [];

    // Empties the note clipboard.
    public static void ClearClipboard()
    {
        ClipboardNotes.Clear();
    }

    // Points every selected note at another instrument, and answers whether any note changed.
    public bool SetSelectedInstrument(int instrumentId)
    {
        if (!WriteEverySelected((channel, note) => note.InstrumentId != instrumentId && _song.SetNoteInstrument(channel, note.StartTick, instrumentId) is not null))
        {
            return false;
        }

        InvalidateVisual();
        return true;
    }

    // Re-pitches every selected note, and answers whether any note changed.
    public bool SetSelectedSemitone(int semitone)
    {
        if (!WriteEverySelected((channel, note) => note.Semitone != semitone && _song.SetNoteSemitone(channel, note.StartTick, semitone) is not null))
        {
            return false;
        }

        InvalidateVisual();
        return true;
    }

    // Gives every selected note a `q` gate of its own, and answers whether any note changed.
    public bool SetSelectedGate(int gate)
    {
        return RewriteEverySelected((channel, note) => _song.SetNoteGate(channel, note.StartTick, gate));
    }

    // Gives every selected note a `q` velocity of its own, and answers whether any note changed.
    public bool SetSelectedVelocity(int velocity)
    {
        return RewriteEverySelected((channel, note) => _song.SetNoteVelocity(channel, note.StartTick, velocity));
    }

    // Gives every selected note a fine tune of its own, and answers whether any note changed.
    public bool SetSelectedFineTune(int fineTune)
    {
        return RewriteEverySelected((channel, note) => _song.SetNoteFineTune(channel, note.StartTick, fineTune));
    }

    // Moves one of every selected note's values onto its own rung or its instrument's, and answers whether any note changed.
    public bool SetSelectedSource(NoteDefault value, NoteSource source)
    {
        return RewriteEverySelected((channel, note) => _song.SetNoteSource(channel, note.StartTick, value, source));
    }

    // Gives every selected note its instrument's value of one of its values as a value of its own, by taking the instrument's rung and leaving it again, and answers whether any note changed.
    public bool SetSelectedToInstrumentValue(NoteDefault value)
    {
        return RewriteEverySelected((channel, note) =>
        {
            _song.SetNoteSource(channel, note.StartTick, value, NoteSource.Instrument);
            return _song.SetNoteSource(channel, note.StartTick, value, NoteSource.Note);
        });
    }

    // Turns legato on or off for every selected note, and answers whether any note changed.
    public bool SetSelectedLegato(bool isLegato)
    {
        return RewriteEverySelected((channel, note) => _song.SetNoteLegato(channel, note.StartTick, isLegato));
    }

    // Hands every selected note's key-off to a `$FC` block or takes it back and redraws, and answers whether any note changed.
    public bool SetSelectedHold(bool isHeld)
    {
        if (!RewriteEverySelected((channel, note) => _song.SetNoteHold(channel, note.StartTick, isHeld)))
        {
            return false;
        }

        Refresh();
        return true;
    }

    // Moves every selected note to a tick, and answers whether any note moved.
    public bool SetSelectedStart(int startTick)
    {
        return RewriteAndReselect(note => note.StartTick == startTick, (channel, note) => _song.SetNoteStart(channel, note.StartTick, startTick));
    }

    // Resizes every selected note from its start and makes that the length the next note placed gets, and answers whether any note changed.
    public bool SetSelectedLength(int lengthTicks)
    {
        if (!RewriteAndReselect(note => note.LengthTicks == lengthTicks, (channel, note) => _song.SetNoteLength(channel, note.StartTick, lengthTicks)))
        {
            return false;
        }

        _placedNoteLengthTicks = lengthTicks;
        return true;
    }

    // Steps every selected note's `q` gate, and answers whether any note changed.
    public bool NudgeSelectedGate(int step)
    {
        return NudgeSelected(step, note => note.Gate, _ => SnesTiming.GateRange, (channel, note, gate) => _song.SetNoteGate(channel, note.StartTick, gate));
    }

    // Steps every selected note's `q` velocity, and answers whether any note changed.
    public bool NudgeSelectedVelocity(int step)
    {
        return NudgeSelected(
            step, note => note.Velocity, _ => SnesVolume.VelocityRange, (channel, note, velocity) => _song.SetNoteVelocity(channel, note.StartTick, velocity));
    }

    // Steps every selected note's fine tune, and answers whether any note changed.
    public bool NudgeSelectedFineTune(int step)
    {
        return NudgeSelected(
            step, note => note.FineTune, _ => SnesPitch.FineTuneRange, (channel, note, fineTune) => _song.SetNoteFineTune(channel, note.StartTick, fineTune));
    }

    // Steps every selected note's pitch inside a band its own bends pull in further, and answers whether any note changed.
    public bool NudgeSelectedSemitone(int step, ValueRange band)
    {
        return NudgeSelected(
            step,
            note => note.Semitone,
            note => PitchBend.StartsHeldIn(band, note.Bends),
            (channel, note, semitone) => _song.SetNoteSemitone(channel, note.StartTick, semitone));
    }

    // Steps every selected note's length inside a range, and answers whether any note changed.
    public bool NudgeSelectedLength(int stepTicks, ValueRange lengthRange)
    {
        return NudgeSelected(
            stepTicks, note => note.LengthTicks, _ => lengthRange, (channel, note, lengthTicks) => _song.SetNoteLength(channel, note.StartTick, lengthTicks));
    }

    // Moves every selected note along by a tick count as one group held off the song's start, and answers whether any note moved.
    public bool NudgeSelectedStart(int stepTicks)
    {
        if (IsDraggingNotes || stepTicks == 0)
        {
            return false;
        }

        var selection = Selection;
        if (selection.Count == 0)
        {
            return false;
        }

        int earliestTick = EarliestStartTickOf(selection);
        int heldStepTicks = GroupStep.HeldOffSongStart(stepTicks, earliestTick);
        if (heldStepTicks == 0)
        {
            return false;
        }

        int rollTicksBefore = _song.RollLengthTicks;
        foreach (var (channel, note) in selection)
        {
            _song.LiftNote(channel, note.StartTick);
        }

        var movedNotes = selection.Select(channelNote => (channelNote.Channel, Note: channelNote.Note with { StartTick = channelNote.Note.StartTick + heldStepTicks })).ToList();
        _song.PlaceNotes(movedNotes);
        ReselectEdited(movedNotes, rollTicksBefore);
        return true;
    }

    // Removes every selected note, and answers whether any note was removed.
    public bool DeleteSelected()
    {
        if (IsDraggingNotes)
        {
            return false;
        }

        bool isRemoved = false;
        foreach (var (channel, startTick) in _selectedKeys.OrderByDescending(key => key.Tick))
        {
            if (_song.RemoveNoteAt(channel, startTick))
            {
                isRemoved = true;
            }
        }

        if (!isRemoved)
        {
            return false;
        }

        ClearSelection();
        Refresh();
        return true;
    }

    // Puts every selected note on the clipboard, and answers whether anything was selected.
    public bool CopySelected()
    {
        if (IsDraggingNotes)
        {
            return false;
        }

        var selection = Selection;
        if (selection.Count == 0)
        {
            return false;
        }

        int earliestTick = EarliestStartTickOf(selection);
        ClipboardNotes.Clear();
        foreach (var (channel, note) in selection)
        {
            ClipboardNotes.Add(new CopiedNote(channel, note with { StartTick = note.StartTick - earliestTick }));
        }

        return true;
    }

    // Copies and then removes every selected note, and answers whether the song changed.
    public bool CutSelected()
    {
        return CopySelected() && DeleteSelected();
    }

    // Lays the clipboard down from a tick and selects what was laid down, and answers whether anything was.
    public bool Paste(int atTick)
    {
        if (IsDraggingNotes || _isPanning || ClipboardNotes.Count == 0)
        {
            return false;
        }

        foreach (var copied in ClipboardNotes)
        {
            if (_song.InstrumentOf(copied.Note) is null)
            {
                return false;
            }
        }

        int anchorTick = Math.Max(0, atTick);
        bool isFromOneChannel = ClipboardNotes.All(copied => copied.Channel == ClipboardNotes[0].Channel);
        var pastedNotes = new List<(int Channel, Note Note)>(ClipboardNotes.Count);
        foreach (var copied in ClipboardNotes)
        {
            pastedNotes.Add((isFromOneChannel ? _selectedChannel : copied.Channel, copied.Note with { StartTick = anchorTick + copied.Note.StartTick }));
        }

        _song.PlaceNotes(pastedNotes);
        Reselect(pastedNotes);
        Refresh();
        return true;
    }

    // Runs a write on each selected note and raises the edit of the selected notes where any of them changed one, and answers whether any did.
    private bool WriteEverySelected(Func<int, Note, bool> doesWriteChangeNote)
    {
        if (IsDraggingNotes)
        {
            return false;
        }

        bool isChanged = false;
        foreach (var (channel, note) in Selection)
        {
            if (doesWriteChangeNote(channel, note))
            {
                isChanged = true;
            }
        }

        if (!isChanged)
        {
            return false;
        }

        RaiseSelectedNotesEdited();
        return true;
    }

    // Rewrites each selected note, a note counting as changed where what the write answers differs from it.
    private bool RewriteEverySelected(Func<int, Note, Note?> rewrite)
    {
        return WriteEverySelected((channel, note) => rewrite(channel, note) is { } rewritten && rewritten != note);
    }

    // Writes each selected note not already holding a value, and answers every note the song still has as it now stands, or null where no write found one.
    private List<(int Channel, Note Note)>? RewrittenSelection(Func<Note, bool> doesAlreadyHold, Func<int, Note, Note?> write)
    {
        bool isChanged = false;
        var rewrittenNotes = new List<(int Channel, Note Note)>();
        foreach (var (channel, note) in Selection)
        {
            if (doesAlreadyHold(note))
            {
                rewrittenNotes.Add((channel, note));
                continue;
            }

            if (write(channel, note) is Note rewritten)
            {
                rewrittenNotes.Add((channel, rewritten));
                isChanged = true;
            }
        }

        return isChanged ? rewrittenNotes : null;
    }

    // Writes each selected note not already holding a value, re-keys the selection onto the notes as they now stand and reports a change in the roll's
    // length, and answers whether any note changed; a drag holding notes leaves them alone.
    private bool RewriteAndReselect(Func<Note, bool> doesAlreadyHold, Func<int, Note, Note?> write)
    {
        if (IsDraggingNotes)
        {
            return false;
        }

        int rollTicksBefore = _song.RollLengthTicks;
        if (RewrittenSelection(doesAlreadyHold, write) is not { } rewrittenNotes)
        {
            return false;
        }

        ReselectEdited(rewrittenNotes, rollTicksBefore);
        return true;
    }

    // Tells the panel that a note was rewritten where it stands, which leaves the selection as it was, and redraws.
    private void ShowNoteRewrittenInPlace()
    {
        RaiseSelectedNotesEdited();
        Refresh();
    }

    // Steps a value every selected note carries by one amount held inside every note's own range, and answers whether any note changed.
    private bool NudgeSelected(int step, Func<Note, int> readValue, Func<Note, ValueRange> rangeOf, Func<int, Note, int, Note?> writeValue)
    {
        if (IsDraggingNotes || step == 0)
        {
            return false;
        }

        var selection = Selection;
        if (selection.Count == 0)
        {
            return false;
        }

        int heldStep = GroupStep.ClampedToEveryMember(step, selection.Select(member => (readValue(member.Note), rangeOf(member.Note))));
        if (heldStep == 0)
        {
            return false;
        }

        int rollTicksBefore = _song.RollLengthTicks;
        bool isChanged = false;
        var writtenNotes = new List<(int Channel, Note Note)>(selection.Count);
        foreach (var (channel, note) in selection)
        {
            var written = writeValue(channel, note, readValue(note) + heldStep);
            writtenNotes.Add((channel, written ?? note));
            if (written is not null)
            {
                isChanged = true;
            }
        }

        if (!isChanged)
        {
            return false;
        }

        ReselectEdited(writtenNotes, rollTicksBefore);
        return true;
    }

    // Re-keys the selection onto notes an edit moved or resized, reports a change in the roll's length and redraws.
    private void ReselectEdited(IEnumerable<(int Channel, Note Note)> editedNotes, int rollTicksBefore)
    {
        Rekey(KeysOf(editedNotes));
        ReportRollResize(rollTicksBefore);
        Refresh();
    }

    // The tick the earliest of a group of notes starts at.
    private static int EarliestStartTickOf(IReadOnlyList<(int Channel, Note Note)> notes)
    {
        return notes.Min(channelNote => channelNote.Note.StartTick);
    }

    // One note on the clipboard: the channel it was copied from and the note.
    private readonly record struct CopiedNote(int Channel, Note Note);
}
