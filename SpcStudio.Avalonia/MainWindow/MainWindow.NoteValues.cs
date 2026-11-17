using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SpcStudio.Controls;
using SpcStudio.Model;

namespace SpcStudio;

// The note panel's `q` gate and velocity, its fine tune, its legato and hold flags and its echo send, each with the switch saying which rung the
// selection takes it from; and what the panel's value rows share - their in-flight guards, their typing, their steps and their inert state.
public partial class MainWindow
{
    private const int InstrumentRungSwitchState = 0;
    private const int NoteRungSwitchState = 1;
    private const int NoInstrumentValue = -1;

    private static readonly NoteDefault[] SliderNoteDefaults = [NoteDefault.Gate, NoteDefault.Velocity, NoteDefault.FineTune];
    private static readonly NoteDefault[] CheckBoxNoteDefaults = [NoteDefault.Legato, NoteDefault.Hold];
    private bool _isUpdatingQuantization;
    private bool _isWritingNoteCheckBoxes;

    // Writes the three fields and their sliders' spans from the selection, unless an edit from one of them is in flight.
    private void ShowQuantization()
    {
        if (_isUpdatingQuantization)
        {
            return;
        }

        FlagGuard.RunWithFlagSet(ref _isUpdatingQuantization, () => WriteQuantization(null));
    }

    // Writes the three fields and their sliders' spans from the selection, all but the field being typed into.
    private void WriteQuantization(TextBox? fieldBeingTyped)
    {
        var notes = Roll.SelectedNotes;
        foreach (var value in SliderNoteDefaults)
        {
            var (slider, box, _, _) = NoteDefaultRow(value);
            if (box == fieldBeingTyped)
            {
                continue;
            }

            box.Text = FieldInput.TextOf(IsQuantizationMixed(value) ? null : CurrentQuantization(value));
            SliderField.ShowSpan(slider, notes.Select(note => NoteDefaults.PlayedValueOf(note, value)));
        }
    }

    // Takes the values the selection agrees on as the ones the next note placed holds.
    private void AdoptQuantization(IReadOnlyList<Note> selected)
    {
        if (FieldInput.SharedValue(selected, note => note.Gate) is int gate)
        {
            Roll.CurrentGate = gate;
        }

        if (FieldInput.SharedValue(selected, note => note.Velocity) is int velocity)
        {
            Roll.CurrentVelocity = velocity;
        }

        if (FieldInput.SharedValue(selected, note => note.FineTune) is int fineTune)
        {
            Roll.CurrentFineTune = fineTune;
        }
    }

    // Whether several notes are selected and disagree about one of the three values.
    private bool IsQuantizationMixed(NoteDefault value)
    {
        return Roll.SelectedNotes is { Count: > 1 } notes && FieldInput.SharedValue(notes, note => NoteDefaults.PlayedValueOf(note, value)) is null;
    }

    // One of the three values as the next note placed takes it.
    private int CurrentQuantization(NoteDefault value)
    {
        return value switch
        {
            NoteDefault.Gate => Roll.CurrentGate,
            NoteDefault.Velocity => Roll.CurrentVelocity,
            _ => Roll.CurrentFineTune,
        };
    }

    // Makes a typed value the one the next note placed takes, bumping the other half of a `q` off 0 where both would be.
    private void SetCurrentQuantization(NoteDefault value, int typed)
    {
        if (value == NoteDefault.FineTune)
        {
            Roll.CurrentFineTune = typed;
            return;
        }

        (Roll.CurrentGate, Roll.CurrentVelocity) = Note.QuantizationWithHalfSet(value, typed, Roll.CurrentGate, Roll.CurrentVelocity);
    }

    // One of the three values' row.
    private NumericRowParts NoteDefaultRow(NoteDefault value)
    {
        return value switch
        {
            NoteDefault.Gate => NotePanelValues.GateRow,
            NoteDefault.Velocity => NotePanelValues.VelocityRow,
            _ => NotePanelValues.DetuneRow,
        };
    }

    // One flag's row.
    private ToggleRowParts NoteFlagRow(NoteDefault value)
    {
        return value == NoteDefault.Legato ? NotePanelValues.LegatoRow : NotePanelValues.HoldRow;
    }

    // The switch in a two-rung value's own row.
    private FieldOverride RungSwitchOf(NoteDefault value)
    {
        return CheckBoxNoteDefaults.Contains(value) ? NoteFlagRow(value).Switch : NoteDefaultRow(value).Switch;
    }

    // The state a two-rung switch shows for a rung, the instrument's first.
    private static int SwitchStateOfRung(NoteSource rung)
    {
        return rung == NoteSource.Instrument ? InstrumentRungSwitchState : NoteRungSwitchState;
    }

    // The rung a state of a two-rung switch stands for.
    private static NoteSource RungOfSwitchState(int state)
    {
        return state == InstrumentRungSwitchState ? NoteSource.Instrument : NoteSource.Note;
    }

    // Shows the three rows' switches, the mark under each slider at the instrument's own value, and each row live only where every note owns it.
    private void ShowNoteDefaults(IReadOnlyList<Note> notes)
    {
        foreach (var value in SliderNoteDefaults)
        {
            var (slider, box, rungSwitch, _) = NoteDefaultRow(value);
            bool isOwned = ShowTwoRungSource(rungSwitch, notes, value);
            ShowInstrumentMark(slider, SharedInstrumentValue(notes, value));
            ShowRowEditable(slider, box, isOwned);
        }
    }

    // The instrument's own value of one of the three that every note's instrument agrees on, or null where they differ.
    private int? SharedInstrumentValue(IReadOnlyList<Note> notes, NoteDefault value)
    {
        int? valueEveryInstrumentAgreesOn = FieldInput.SharedValue(
            notes, note => _song.InstrumentOf(note) is { } instrument ? NoteDefaults.InstrumentValueOf(instrument, value) : NoInstrumentValue);
        return valueEveryInstrumentAgreesOn == NoInstrumentValue ? null : valueEveryInstrumentAgreesOn;
    }

    // Writes a two-rung switch from the notes, and answers whether every one of them holds the value itself.
    private static bool ShowTwoRungSource(FieldOverride rungSwitch, IReadOnlyList<Note> notes, NoteDefault value)
    {
        rungSwitch.State = NoteDefaultSwitchState(notes, value);
        return notes.Count > 0 && notes.All(note => NoteDefaults.SourceOf(note, value) == NoteSource.Note);
    }

    // Moves the selection onto a rung for one value, recording the move under the switch in the value's row.
    private void OnNoteDefaultCycled(NoteDefault value, int state)
    {
        MoveNoteDefaultToRung(value, state, RungSwitchOf(value));
    }

    // Moves the selection onto a rung for one value, recording the move under the given undo key, and shows the three fields again.
    private void MoveNoteDefaultToRung(NoteDefault value, int state, object undoKey)
    {
        OnChainSourceCycled(value, state, undoKey);
        ShowQuantization();
    }

    // Moves the selection onto its own rung for a value whose inert slider or field was pressed, as a claim, and lets the press go on.
    private void OnNoteDefaultPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!FieldSteps.IsClaimingPress(sender, e))
        {
            return;
        }

        foreach (var value in SliderNoteDefaults)
        {
            var (slider, box, _, _) = NoteDefaultRow(value);
            if (sender != slider && sender != box)
            {
                continue;
            }

            MoveNoteDefaultToRung(value, SwitchStateOfRung(NoteSource.Note), EditStep.ClaimSource);
            return;
        }
    }

    // Applies a gate as it is typed.
    private void OnGateChanged(object? sender, TextChangedEventArgs e)
    {
        ApplyTypedQuantization(NoteDefault.Gate, Roll.SetSelectedGate);
    }

    // Applies a velocity as it is typed.
    private void OnVelocityChanged(object? sender, TextChangedEventArgs e)
    {
        ApplyTypedQuantization(NoteDefault.Velocity, Roll.SetSelectedVelocity);
    }

    // Applies a fine tune as it is typed.
    private void OnFineTuneChanged(object? sender, TextChangedEventArgs e)
    {
        ApplyTypedQuantization(NoteDefault.FineTune, Roll.SetSelectedFineTune);
    }

    // Applies what one of the three fields holds to the next note placed and to the selection, coalescing on the field, and restates the others.
    private void ApplyTypedQuantization(NoteDefault value, Func<int, bool> setSelected)
    {
        if (_isUpdatingQuantization)
        {
            return;
        }

        var row = NoteDefaultRow(value);
        if (!TryReadTypedValue(row, out int typed))
        {
            return;
        }

        FlagGuard.RunWithFlagSet(ref _isUpdatingQuantization, () =>
        {
            SetCurrentQuantization(value, typed);
            RecordSelectionEdit(row.Box, () => setSelected(typed));
            WriteQuantization(row.Box);
            ShowNotePanel();
        });
    }

    // Steps the gate with the arrows or the wheel.
    private void OnGateKeyDown(object? sender, KeyEventArgs e)
    {
        StepQuantizationField(e, NoteDefault.Gate, Roll.NudgeSelectedGate);
    }

    // Steps the velocity with the arrows or the wheel.
    private void OnVelocityKeyDown(object? sender, KeyEventArgs e)
    {
        StepQuantizationField(e, NoteDefault.Velocity, Roll.NudgeSelectedVelocity);
    }

    // Steps the fine tune with the arrows or the wheel.
    private void OnFineTuneKeyDown(object? sender, KeyEventArgs e)
    {
        StepQuantizationField(e, NoteDefault.FineTune, Roll.NudgeSelectedFineTune);
    }

    // Steps one of the three fields: its value where the selection agrees, and every note's own by the same amount where it does not.
    private void StepQuantizationField(KeyEventArgs e, NoteDefault value, Func<int, bool> nudgeSelected)
    {
        var row = NoteDefaultRow(value);
        StepField(e, row, () => IsQuantizationMixed(value) ? null : CurrentQuantization(value), step => EditSelectedQuantization(row.Box, () => nudgeSelected(step)));
    }

    // Gives every selected note its own instrument's value of one of the three, for a right-click on the row.
    private void ResetQuantizationToInstrument(NoteDefault value)
    {
        var (_, box, _, _) = NoteDefaultRow(value);
        EditSelectedQuantization(box, () => Roll.SetSelectedToInstrumentValue(value));
    }

    // Edits every selected note's own value of one of the three straight in the song, coalescing on the field, and shows the fields again.
    private void EditSelectedQuantization(TextBox source, Func<bool> editSelected)
    {
        FlagGuard.RunWithFlagSet(ref _isUpdatingQuantization, () => RecordSelectionEdit(source, editSelected));
        ShowQuantization();
        ShowNotePanel();
    }

    // Puts one of the three fields back to what the selection holds once it is left.
    private void OnQuantizationFieldCommitted(object? sender, RoutedEventArgs e)
    {
        ShowQuantization();
    }

    // Writes the legato and hold boxes and their switches from the selection, each box live only where every note holds that flag itself.
    private void ShowNoteFlags(IReadOnlyList<Note> notes)
    {
        foreach (var value in CheckBoxNoteDefaults)
        {
            var (box, rungSwitch) = NoteFlagRow(value);
            WriteNoteCheckBox(box, notes, note => IsNoteFlagOn(note, value));
            FieldSteps.SetInert(box, !ShowTwoRungSource(rungSwitch, notes, value));
        }
    }

    // Whether a note's legato or hold flag is on.
    private static bool IsNoteFlagOn(Note note, NoteDefault value)
    {
        return value == NoteDefault.Legato ? note.IsLegato : note.IsHeld;
    }

    // Turns legato on or off for the whole selection.
    private void OnNoteLegatoToggled(object? sender, RoutedEventArgs e)
    {
        ToggleNoteFlag(NoteDefault.Legato, Roll.SetSelectedLegato);
    }

    // Turns the hold on or off for the whole selection.
    private void OnNoteHoldToggled(object? sender, RoutedEventArgs e)
    {
        ToggleNoteFlag(NoteDefault.Hold, Roll.SetSelectedHold);
    }

    // Sets a flag on every selected note as its box was pressed, as an undo step keyed on the box.
    private void ToggleNoteFlag(NoteDefault value, Func<bool, bool> setSelected)
    {
        if (_isWritingNoteCheckBoxes)
        {
            return;
        }

        var (box, _) = NoteFlagRow(value);
        ShowSelectionEdit(box, () => setSelected(FieldInput.IsAskedOn(box, Roll.SelectedNotes, note => IsNoteFlagOn(note, value))));
    }

    // Moves the selection onto its own rung for the flag whose inert box was pressed, as a claim, and lets the press go on.
    private void OnNoteFlagPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!FieldSteps.IsClaimingPress(sender, e))
        {
            return;
        }

        foreach (var value in CheckBoxNoteDefaults)
        {
            var (box, _) = NoteFlagRow(value);
            if (box == sender)
            {
                MoveNoteDefaultToRung(value, SwitchStateOfRung(NoteSource.Note), EditStep.ClaimSource);
            }
        }
    }

    // Writes the selection's echo send into its box and its switch, the box live only where every note holds the send itself.
    private void ShowNoteEcho()
    {
        var notes = Roll.Selection;
        WriteNoteCheckBox(NotePanelValues.EchoRow.Check, notes, IsEchoOn);
        FieldSteps.SetInert(NotePanelValues.EchoRow.Check, !ShowLaneSource(NotePanelValues.EchoRow.Switch, notes, NoteEchoSend.Lane));
    }

    // Whether a note is fed into the echo buffer, whichever rung decides it.
    private bool IsEchoOn((int Channel, Note Note) one)
    {
        return NoteEchoSend.IsEchoOn(_song, one.Channel, one.Note);
    }

    // Feeds the whole selection into the echo buffer or takes it out as the box was pressed, which puts each note on its own rung.
    private void OnNoteEchoToggled(object? sender, RoutedEventArgs e)
    {
        var notes = Roll.Selection;
        if (_isWritingNoteCheckBoxes || notes.Count == 0)
        {
            return;
        }

        bool isWet = FieldInput.IsAskedOn(NotePanelValues.EchoRow.Check, notes, IsEchoOn);
        ApplyToSelection(
            NotePanelValues.EchoRow.Check, one => IsOnOwnRung(NoteEchoSend.Lane, one) && IsEchoOn(one) == isWet, one => NoteEchoSend.ApplyOverride(_song, one.Channel, one.Note, isWet));
        ShowNotePanel();
    }

    // Moves the selection onto its own echo send where the inert box was pressed, and lets the press go on.
    private void OnNoteEchoPressed(object? sender, PointerPressedEventArgs e)
    {
        ClaimOnInertPress(sender, e, NoteEchoSend.Lane);
    }

    // Moves the selection onto a rung of the echo send's three.
    private void OnEchoSourceCycled(int state)
    {
        MoveSelectionToLaneRung(NoteEchoSend.Lane, (LaneSource)state, NotePanelValues.EchoRow.Switch);
    }

    // Writes a check box from the notes: checked where every one is on, mixed where they differ, and clear where none is or there are none.
    private void WriteNoteCheckBox<T>(CheckBox box, IReadOnlyList<T> notes, Func<T, bool> isOn)
    {
        FlagGuard.RunWithFlagSet(ref _isWritingNoteCheckBoxes, () => box.IsChecked = FieldInput.SharedFlagOrOff(notes, isOn));
    }

    // Reads a value typed into a live field, refusing a blank that only says the selection disagrees.
    private bool TryReadTypedValue(NumericRowParts row, out int value)
    {
        value = 0;
        if (IsBlankForMixedSelection(row.Box) || FieldSteps.IsInert(row.Box))
        {
            return false;
        }

        return FieldInput.TryReadValue(row.Box.Text, row.Range, out value);
    }

    // Steps a value row's field by a key's or a notch's step: writes the stepped value where the selection shares one, and steps every note's own otherwise.
    private static void StepField(KeyEventArgs e, NumericRowParts row, Func<int?> sharedValue, Action<int> stepEveryNote)
    {
        StepSelectionField(e, sharedValue, stepEveryNote, (value, step) => FieldInput.WriteStepped(row.Box, value, step, row.Range));
    }

    // Makes a row's slider and field live, or inert, read-only and out of the tab order.
    private static void ShowRowEditable(Slider slider, TextBox box, bool isOwned)
    {
        FieldSteps.SetInertOutOfTabOrder(slider, !isOwned);
        FieldSteps.SetInert(box, !isOwned);
        box.IsReadOnly = !isOwned;
    }

    // Marks a slider's rail at the instrument's own value, or marks nothing.
    private static void ShowInstrumentMark(Slider slider, int? instrumentValue)
    {
        SliderMark.SetAt(slider, instrumentValue is int value ? value : double.NaN);
    }
}
