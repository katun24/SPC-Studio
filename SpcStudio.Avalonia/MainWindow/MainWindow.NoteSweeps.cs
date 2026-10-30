using System.Collections.Immutable;
using Avalonia.Interactivity;
using SpcStudio.Controls;
using SpcStudio.Model;

namespace SpcStudio;

// The note panel's four chain sections, ENVELOPE, PITCH BEND, VIBRATO and TREMOLO, which are the selected notes handed to the shared envelope, bend
// and sweep rows, with the switch on each banner saying whether the notes hold the chain themselves or take it from their instrument.
public partial class MainWindow
{
    private const int NoEnvelopeGraphOwner = 0;

    private EnvelopeChainRows? _noteEnvelopeEditor;
    private PitchBendRows? _noteBendEditor;
    private SweepRows<NoteVibrato>? _noteVibratoEditor;
    private SweepRows<NoteTremolo>? _noteTremoloEditor;

    // The ENVELOPE section's rows, built the first time they are asked for.
    private EnvelopeChainRows NoteEnvelopeEditor
    {
        get
        {
            return _noteEnvelopeEditor ??= EnvelopeChainRows.Over(NotePanelValues.EnvelopeRows, NotePanelValues.EnvelopeSection,
                () => SelectedNoteSubjects<IEnvelopeHost>(selected => new NoteEnvelopeHost(this, selected)), () => EnvelopeGraphOwnerOf(Roll.Selection),
                NoteEnvelopeGraphTiming, ChainClaim(NoteDefault.Envelope));
        }
    }

    // The PITCH BEND section's rows, built the first time they are asked for.
    private PitchBendRows NoteBendEditor
    {
        get
        {
            return _noteBendEditor ??= new PitchBendRows
            {
                SectionParts = NotePanelValues.BendSection,
                Subjects = () => SelectedNoteSubjects<IBendHost>(selected => new NoteBendHost(this, selected)),
                InertClaim = ChainClaim(NoteDefault.Bend),
            };
        }
    }

    // The VIBRATO section's rows, built the first time they are asked for.
    private SweepRows<NoteVibrato> NoteVibratoEditor
    {
        get
        {
            return _noteVibratoEditor ??= SweepFields.VibratoRows(NotePanelValues.VibratoSection,
                () => SelectedNoteSubjects<IListHost<NoteVibrato>>(selected => new NoteListHost<NoteVibrato>(this, selected, note => note.Vibratos, _song.SetNoteVibratos)),
                ChainClaim(NoteDefault.Vibrato));
        }
    }

    // The TREMOLO section's rows, built the first time they are asked for.
    private SweepRows<NoteTremolo> NoteTremoloEditor
    {
        get
        {
            return _noteTremoloEditor ??= SweepFields.TremoloRows(NotePanelValues.TremoloSection,
                () => SelectedNoteSubjects<IListHost<NoteTremolo>>(selected => new NoteListHost<NoteTremolo>(this, selected, note => note.Tremolos, _song.SetNoteTremolos)),
                ChainClaim(NoteDefault.Tremolo));
        }
    }

    // Gives the four banners' switches the instrument's rung and the note's own, and hooks what a press on each does.
    private void HookChainSectionSwitches()
    {
        foreach (var (value, section) in new[]
        {
            (NoteDefault.Envelope, NotePanelValues.EnvelopeSection), (NoteDefault.Bend, NotePanelValues.BendSection.Section), (NoteDefault.Vibrato, NotePanelValues.VibratoSection.Section), (NoteDefault.Tremolo, NotePanelValues.TremoloSection.Section),
        })
        {
            section.SwitchIcons = TwoRungIcons;
            section.SwitchMixedGoesTo = SwitchStateOfRung(NoteSource.Instrument);
            section.SwitchCycled += state => OnChainSourceCycled(value, state, section);
        }
    }

    // Shows the four banners' switches and the four sections for the selection.
    private void ShowChainSections(IReadOnlyList<Note> notes)
    {
        NotePanelValues.EnvelopeSection.SwitchState = NoteDefaultSwitchState(notes, NoteDefault.Envelope);
        NotePanelValues.BendSection.Section.SwitchState = NoteDefaultSwitchState(notes, NoteDefault.Bend);
        NotePanelValues.VibratoSection.Section.SwitchState = NoteDefaultSwitchState(notes, NoteDefault.Vibrato);
        NotePanelValues.TremoloSection.Section.SwitchState = NoteDefaultSwitchState(notes, NoteDefault.Tremolo);
        NoteEnvelopeEditor.Show();
        NoteBendEditor.Show();
        NoteVibratoEditor.Show();
        NoteTremoloEditor.Show();
    }

    // Whether notes are selected and every one of them holds a chain itself rather than taking it from its instrument.
    private bool DoesEverySelectedNoteOwnChain(NoteDefault value)
    {
        var notes = Roll.SelectedNotes;
        return notes.Count > 0 && notes.All(note => NoteDefaults.SourceOf(note, value) == NoteSource.Note);
    }

    // Moves the selection onto a rung for one chain, recording the move under the given undo key.
    private void OnChainSourceCycled(NoteDefault value, int state, object undoKey)
    {
        CommitToSelection(() => Roll.SetSelectedSource(value, RungOfSwitchState(state)), undoKey);
    }

    // The note panel's rows over a chain: inert while any selected note takes it from its instrument, and claimed by moving the selection onto its own.
    private InertClaim ChainClaim(NoteDefault value)
    {
        return new InertClaim(() => !DoesEverySelectedNoteOwnChain(value), undoKey => ClaimChain(value, undoKey));
    }

    // Moves the selected notes onto their own rung for one chain where any is not there already, keeping the chain each plays.
    private void ClaimChain(NoteDefault value, object? undoKey)
    {
        if (Roll.SelectionCount == 0 || DoesEverySelectedNoteOwnChain(value))
        {
            return;
        }

        OnChainSourceCycled(value, SwitchStateOfRung(NoteSource.Note), undoKey ?? value);
    }

    // What the Envelope section's graph takes as the thing being edited.
    private static object EnvelopeGraphOwnerOf(IReadOnlyList<(int Channel, Note Note)> selected)
    {
        if (selected.Count == 0)
        {
            return NoEnvelopeGraphOwner;
        }

        return (selected.Count, selected[0].Channel, selected[0].Note.StartTick);
    }

    // The song's tempo map and the start of the earliest selected note, which the Envelope section's graph turns ticks into seconds from.
    private (TempoMap Tempos, int KeyOnTick) NoteEnvelopeGraphTiming()
    {
        return (TempoMap.For(_song), Roll.SelectedNotes.Select(note => note.StartTick).DefaultIfEmpty().Min());
    }

    // Splits the last envelope row of every selected note with room for it, moving the notes onto their own rows first in the same undo step.
    private void OnAddNoteEnvelope(object? sender, RoutedEventArgs e)
    {
        ClaimAndAddRow(NoteEnvelopeEditor);
    }

    // Adds a bend at the end of every selected note's chain with room for one, moving the notes onto their own chain first in the same undo step.
    private void OnAddBend(object? sender, RoutedEventArgs e)
    {
        ClaimAndAddRow(NoteBendEditor);
    }

    // Adds a vibrato stretch at the end of every selected note's chain with room for one, moving the notes onto their own chain first in the same undo step.
    private void OnAddVibrato(object? sender, RoutedEventArgs e)
    {
        ClaimAndAddRow(NoteVibratoEditor);
    }

    // Adds a tremolo stretch at the end of every selected note's chain with room for one, moving the notes onto their own chain first in the same undo step.
    private void OnAddTremolo(object? sender, RoutedEventArgs e)
    {
        ClaimAndAddRow(NoteTremoloEditor);
    }

    // The state a two-rung switch shows for the notes: the rung every one stands on, none where they differ, and the instrument's with no notes.
    private static int? NoteDefaultSwitchState(IReadOnlyList<Note> notes, NoteDefault value)
    {
        if (notes.Count == 0)
        {
            return SwitchStateOfRung(NoteSource.Instrument);
        }

        return FieldInput.SharedValue(notes, note => SwitchStateOfRung(NoteDefaults.SourceOf(note, value)));
    }

    // One selected note as the subject of a chain it holds entry for entry: its vibrato, its tremolo or its envelope rows.
    private class NoteListHost<T>(MainWindow window, (int Channel, Note Note) selected, Func<Note, ImmutableArray<T>> chainOf,
        Func<int, int, ImmutableArray<T>, Note?> writeChain) : NoteChainHost(window, selected), IListHost<T>
        where T : struct
    {
        // The chain the note holds.
        public ImmutableArray<T> Chain
        {
            get
            {
                return chainOf(Note);
            }
        }

        // Writes the chain onto the note, which puts the note on its own rung.
        public bool Write(ImmutableArray<T> chain)
        {
            writeChain(Channel, Note.StartTick, chain);
            return true;
        }
    }

    // One selected note as the subject of its envelope rows, whose last row ends with the note and whose base is its instrument's row in force at the same tick.
    private sealed class NoteEnvelopeHost(MainWindow window, (int Channel, Note Note) selected)
        : NoteListHost<EnvelopeRow>(window, selected, note => note.Envelopes, window._song.SetNoteEnvelopes), IEnvelopeHost
    {
        // The instrument's row in force a number of ticks after key-on, or the note's own where it has no instrument.
        public EnvelopeState BaseEnvelopeAt(int offsetTicks)
        {
            var baseEnvelopeRows = Song.InstrumentOf(Note) is { } instrument ? EnvelopeRows.OfInstrument(instrument) : Note.Envelopes;
            return EnvelopeRows.StateAt(baseEnvelopeRows, offsetTicks);
        }
    }

    // One selected note as the subject of a pitch bend chain, whose offsets may not carry the note off the roll.
    private sealed class NoteBendHost(MainWindow window, (int Channel, Note Note) selected) : NoteChainHost(window, selected), IBendHost
    {
        // The note's bends.
        public ImmutableArray<PitchBend> Chain
        {
            get
            {
                return Note.Bends;
            }
        }

        // The offsets a bend can hold without carrying the note off the roll.
        public ValueRange OffsetRange(ImmutableArray<PitchBend> chain, int index)
        {
            return PitchBendRows.OffsetRangeOn(chain, index, Note.Semitone);
        }

        // Writes the chain onto the note, which puts the note on its own rung.
        public bool Write(ImmutableArray<PitchBend> chain)
        {
            Song.SetNoteBends(Channel, Note.StartTick, chain);
            return true;
        }
    }
}
