using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SpcStudio.Controls;
using SpcStudio.Model;

namespace SpcStudio;

// The note panel's Amplify row and the AMPLIFY section under it: the row is the first row's value, and both the row's switch and the banner's say
// whether the notes hold their amplify rows themselves or take them from their instrument.
public partial class MainWindow
{
    private bool _isUpdatingNoteAmplify;
    private AmplifyChainRows? _noteAmplifyRowsEditor;

    // The AMPLIFY section's rows, built the first time they are asked for.
    private AmplifyChainRows NoteAmplifyRowsEditor
    {
        get
        {
            return _noteAmplifyRowsEditor ??= AmplifyChainRows.Over(NotePanelValues.AmplifySection,
                () => SelectedNoteSubjects<IAmplifyHost>(selected => new NoteAmplifyHost(this, selected)), ChainClaim(NoteDefault.Amplify));
        }
    }

    // Writes the amplify the selection keys on under into the Amplify field and its slider's span, unless an edit from the field is in flight, and shows the row's switch.
    private void ShowNoteAmplify()
    {
        if (!_isUpdatingNoteAmplify)
        {
            FlagGuard.RunWithFlagSet(ref _isUpdatingNoteAmplify, () => WriteFieldAndSpan(NotePanelValues.AmplifyRow.Box, NotePanelValues.AmplifyRow.Slider, Roll.Selection, LeadAmplifyOf));
        }

        ShowNoteAmplifyOverride();
    }

    // Shows the Amplify row's switch, the mark under its slider at the instrument's own first row, and the row live only where every note owns its rows.
    private void ShowNoteAmplifyOverride()
    {
        var notes = Roll.SelectedNotes;
        bool isOwned = ShowTwoRungSource(NotePanelValues.AmplifyRow.Switch, notes, NoteDefault.Amplify);
        ShowInstrumentMark(NotePanelValues.AmplifyRow.Slider, SharedInstrumentValue(notes, NoteDefault.Amplify));
        ShowRowEditable(NotePanelValues.AmplifyRow.Slider, NotePanelValues.AmplifyRow.Box, isOwned);
    }

    // The amplify a note keys on under, whichever rung decides it.
    private int LeadAmplifyOf((int Channel, Note Note) one)
    {
        return NoteDefaults.PlayedValueOf(NoteRungs.LiveNote(_song, one.Channel, one.Note), NoteDefault.Amplify);
    }

    // Gives a note's first amplify row another value, which puts the note on its own rung.
    private void SetLeadAmplify((int Channel, Note Note) one, int amplify)
    {
        var liveNote = NoteRungs.LiveNote(_song, one.Channel, one.Note);
        _song.SetNoteAmplifies(one.Channel, liveNote.StartTick, NoteAmplify.WithLead(liveNote.Amplifies, amplify));
    }

    // Moves the selection onto its own amplify rows where the Amplify row's inert slider or field was pressed, and lets the press go on.
    private void OnNoteAmplifyPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!FieldSteps.IsClaimingPress(sender, e))
        {
            return;
        }

        ClaimChain(NoteDefault.Amplify, EditStep.ClaimSource);
    }

    // Moves the selection onto a rung for its amplify rows, recording the move under the row's switch.
    private void OnAmplifySourceCycled(int state)
    {
        OnChainSourceCycled(NoteDefault.Amplify, state, NotePanelValues.AmplifyRow.Switch);
    }

    // Applies an amplify as it is typed.
    private void OnNoteAmplifyChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingNoteAmplify || !TryReadTypedValue(NotePanelValues.AmplifyRow, out int amplify))
        {
            return;
        }

        FlagGuard.RunWithFlagSet(ref _isUpdatingNoteAmplify, () => ApplyNoteAmplify(_ => amplify));
    }

    // Puts every selected note's first amplify row at a value of its own, coalescing on the Amplify field, and shows the section under the row and the switch again.
    private void ApplyNoteAmplify(Func<(int Channel, Note Note), int> amplifyOf)
    {
        if (!ApplyToSelection(
                NotePanelValues.AmplifyRow.Box, one => IsAmplifyOwned(one) && LeadAmplifyOf(one) == amplifyOf(one), one => SetLeadAmplify(one, amplifyOf(one))))
        {
            return;
        }

        NoteAmplifyRowsEditor.Show();
        ShowNoteAmplifyOverride();
    }

    // Whether a note holds its amplify rows itself.
    private bool IsAmplifyOwned((int Channel, Note Note) one)
    {
        return NoteDefaults.SourceOf(NoteRungs.LiveNote(_song, one.Channel, one.Note), NoteDefault.Amplify) == NoteSource.Note;
    }

    // Gives every selected note its own instrument's first amplify row as a value of its own, for a right-click on the row.
    private void ResetNoteAmplifyToInstrument()
    {
        ApplyNoteAmplify(one => NoteAmplify.LeadOf(_song.InstrumentOf(one.Note)));
        ShowNoteAmplify();
    }

    // Steps the Amplify field with the arrows or the wheel.
    private void OnNoteAmplifyKeyDown(object? sender, KeyEventArgs e)
    {
        StepField(e, NotePanelValues.AmplifyRow, () => FieldInput.SharedValue(Roll.Selection, LeadAmplifyOf), NudgeNoteAmplify);
    }

    // Steps every selected note's first amplify row by the same amount, and shows the row and the section under it again.
    private void NudgeNoteAmplify(int step)
    {
        if (!NudgeOverride(ref _isUpdatingNoteAmplify, NotePanelValues.AmplifyRow, step, LeadAmplifyOf, SetLeadAmplify))
        {
            return;
        }

        ShowNoteAmplify();
        NoteAmplifyRowsEditor.Show();
    }

    // Puts the Amplify field back to what the selection holds once it is left.
    private void OnNoteAmplifyCommitted(object? sender, RoutedEventArgs e)
    {
        ShowNoteAmplify();
    }

    // Splits the last amplify row of every selected note with room for it, moving the notes onto their own rows first in the same undo step.
    private void OnAddNoteAmplify(object? sender, RoutedEventArgs e)
    {
        ClaimAndAddRow(NoteAmplifyRowsEditor);
    }

    // Gives the Amplify row its two-rung switch, and lets a press on its inert slider or field claim the note's own rows.
    private void HookNoteAmplifyRow()
    {
        ShowTwoRungs(NotePanelValues.AmplifyRow.Switch);
        NotePanelValues.AmplifyRow.Switch.Cycled += OnAmplifySourceCycled;
        AddTunneledPressHandler(NotePanelValues.AmplifyRow.Slider, OnNoteAmplifyPressed);
        AddTunneledPressHandler(NotePanelValues.AmplifyRow.Box, OnNoteAmplifyPressed);
    }

    // One selected note as the subject of its amplify rows, whose first row goes back to its instrument's.
    private sealed class NoteAmplifyHost(MainWindow window, (int Channel, Note Note) selected)
        : NoteListHost<AmplifyRow>(window, selected, note => note.Amplifies, window._song.SetNoteAmplifies), IAmplifyHost
    {
        // The amplify the note's own instrument keys on under.
        public int BaseAmplify
        {
            get
            {
                return NoteAmplify.LeadOf(Song.InstrumentOf(Note));
            }
        }
    }
}
