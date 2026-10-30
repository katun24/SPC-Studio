using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SpcStudio.Controls;
using SpcStudio.Model;

namespace SpcStudio;

// The note panel's volume, pan and amplify rows, each a value with a lane beneath it; and the three-rung switch every such row of the panel shares, the echo send's included.
public partial class MainWindow
{
    private const int NoDropDownItem = -1;

    private bool _isUpdatingNoteLead;
    private bool _isUpdatingNotePan;
    private bool _isUpdatingNoteAmplify;

    // Shows the Volume row and the Volume section under it for the selection.
    private void ShowNoteVolumeRows()
    {
        ShowNoteLead(Roll.Selection);
        NoteVolumeRowsEditor.Show();
    }

    // Shows the Pan row and the Pan section under it for the selection.
    private void ShowNotePanRows()
    {
        ShowNotePan(Roll.Selection);
        NotePanRowsEditor.Show();
    }

    // Writes the volume the selection keys on at into the Volume field and its slider's span, unless an edit from the field is in flight, and shows the row's switch.
    private void ShowNoteLead(IReadOnlyList<(int Channel, Note Note)> selected)
    {
        if (!_isUpdatingNoteLead)
        {
            FlagGuard.RunWithFlagSet(ref _isUpdatingNoteLead, () => WriteFieldAndSpan(NotePanelValues.VolumeRow.Box, NotePanelValues.VolumeRow.Slider, selected, LeadVolumeOf));
        }

        ShowNoteLeadOverride(selected);
    }

    // Shows the Volume row's switch and the Volume section banner's, the mark under its slider at the instrument's own volume, and the row live only where every note owns it.
    private void ShowNoteLeadOverride(IReadOnlyList<(int Channel, Note Note)> selected)
    {
        bool isOwned = ShowLaneSource(NotePanelValues.VolumeRow.Switch, selected, NoteVolume.Lane);
        NotePanelValues.VolumeSection.Section.SwitchState = NotePanelValues.VolumeRow.Switch.State;
        ShowInstrumentMark(NotePanelValues.VolumeRow.Slider, FieldInput.SharedValue(selected, one => InstrumentLeadValueOf(NoteVolume.Lane, one)));
        ShowRowEditable(NotePanelValues.VolumeRow.Slider, NotePanelValues.VolumeRow.Box, isOwned);
    }

    // The volume a note keys on at, whichever rung decides it.
    private int LeadVolumeOf((int Channel, Note Note) one)
    {
        return NoteVolume.Lane.LeadState(_song, one.Channel, one.Note);
    }

    // Moves the selection onto its own volume where the Volume row's inert slider or field was pressed, and lets the press go on.
    private void OnNoteLeadPressed(object? sender, PointerPressedEventArgs e)
    {
        ClaimOnInertPress(sender, e, NoteVolume.Lane);
    }

    // Moves the selection onto a rung of the volume's three.
    private void OnVolumeSourceCycled(int state)
    {
        MoveSelectionToLaneRung(NoteVolume.Lane, (LaneSource)state, NotePanelValues.VolumeRow.Switch);
    }

    // Applies a volume as it is typed.
    private void OnNoteLeadChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingNoteLead || !TryReadTypedValue(NotePanelValues.VolumeRow, out int volume))
        {
            return;
        }

        FlagGuard.RunWithFlagSet(ref _isUpdatingNoteLead, () => ApplyNoteLead(volume));
    }

    // Puts every selected note at a lead volume, coalescing on the Volume field, and shows the section under the row and the switch again.
    private void ApplyNoteLead(int volume)
    {
        if (!ApplyLeadToSelection(NoteVolume.Lane, (_, _) => volume, NotePanelValues.VolumeRow.Box))
        {
            return;
        }

        NoteVolumeRowsEditor.Show();
        ShowNoteLeadOverride(Roll.Selection);
    }

    // Steps the Volume field with the arrows or the wheel.
    private void OnNoteLeadKeyDown(object? sender, KeyEventArgs e)
    {
        StepField(e, NotePanelValues.VolumeRow, () => FieldInput.SharedValue(Roll.Selection, LeadVolumeOf), NudgeNoteLead);
    }

    // Steps every selected note's lead volume by the same amount, and shows the row and the section under it again.
    private void NudgeNoteLead(int step)
    {
        if (!NudgeOverride(
                ref _isUpdatingNoteLead, NotePanelValues.VolumeRow, step, LeadVolumeOf,
                (one, volume) => NoteVolume.Lane.SetLeadState(_song, one.Channel, one.Note, volume)))
        {
            return;
        }

        ShowNoteVolumeRows();
    }

    // Puts the Volume field back to what the selection holds once it is left.
    private void OnNoteLeadCommitted(object? sender, RoutedEventArgs e)
    {
        ShowNoteLead(Roll.Selection);
    }

    // Writes the pan the selection keys on at into the Pan field, its slider's span and the Surround drop-down, unless an edit from either is in flight, and shows the row's switch.
    private void ShowNotePan(IReadOnlyList<(int Channel, Note Note)> selected)
    {
        if (_isUpdatingNotePan)
        {
            return;
        }

        FlagGuard.RunWithFlagSet(ref _isUpdatingNotePan, () =>
        {
            WriteFieldAndSpan(NotePanelValues.PanRow.Box, NotePanelValues.PanRow.Slider, selected, LeadPanValueOf);
            NotePanelValues.SurroundDropDown.SelectedIndex =
                FieldInput.SharedValue(selected, one => LeadPanOf(one).Surround.Packed) is int packed ? SurroundField.IndexOf(SnesSurround.FromPacked(packed)) : NoDropDownItem;
        });

        ShowNotePanOverride(selected);
    }

    // Shows the Pan row's switch and the Pan section banner's, the mark under its slider at the instrument's own pan, and the row and the Surround drop-down live only where every note owns its pan.
    private void ShowNotePanOverride(IReadOnlyList<(int Channel, Note Note)> selected)
    {
        bool isOwned = ShowLaneSource(NotePanelValues.PanRow.Switch, selected, NotePan.Lane);
        NotePanelValues.PanSection.Section.SwitchState = NotePanelValues.PanRow.Switch.State;
        ShowInstrumentMark(NotePanelValues.PanRow.Slider, FieldInput.SharedValue(selected, one => InstrumentLeadValueOf(NotePan.Lane, one)));
        ShowRowEditable(NotePanelValues.PanRow.Slider, NotePanelValues.PanRow.Box, isOwned);
        FieldSteps.SetInertOutOfTabOrder(NotePanelValues.SurroundDropDown, !isOwned);
    }

    // The pan and surround pair a note keys on at, whichever rung decides it.
    private PanState LeadPanOf((int Channel, Note Note) one)
    {
        return NotePan.Lane.LeadState(_song, one.Channel, one.Note);
    }

    // The pan a note keys on at, whichever rung decides it.
    private int LeadPanValueOf((int Channel, Note Note) one)
    {
        return LeadPanOf(one).Value;
    }

    // Moves the selection onto its own pan where the Pan row's inert slider, field or drop-down was pressed, and lets the press go on.
    private void OnNotePanPressed(object? sender, PointerPressedEventArgs e)
    {
        ClaimOnInertPress(sender, e, NotePan.Lane);
    }

    // Moves the selection onto a rung of the pan's three.
    private void OnPanSourceCycled(int state)
    {
        MoveSelectionToLaneRung(NotePan.Lane, (LaneSource)state, NotePanelValues.PanRow.Switch);
    }

    // Applies a pan as it is typed, keeping each note's surround pair.
    private void OnNotePanChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingNotePan || !TryReadTypedValue(NotePanelValues.PanRow, out int pan))
        {
            return;
        }

        FlagGuard.RunWithFlagSet(ref _isUpdatingNotePan, () => ApplyNotePan(state => state with { Value = pan }, NotePanelValues.PanRow.Box));
    }

    // Puts the whole selection under one surround pair, keeping each note's pan.
    private void OnNoteSurroundToggled(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingNotePan || Roll.Selection.Count == 0 || NotePanelValues.SurroundDropDown.SelectedIndex == NoDropDownItem)
        {
            return;
        }

        var surround = SurroundField.PairAt(NotePanelValues.SurroundDropDown.SelectedIndex);
        FlagGuard.RunWithFlagSet(ref _isUpdatingNotePan, () => ApplyNotePan(state => state with { Surround = surround }, NotePanelValues.SurroundDropDown));
        ShowNotePan(Roll.Selection);
    }

    // Changes every selected note's lead pan, coalescing on the control it came from, and shows the section under the row and the switch again.
    private void ApplyNotePan(Func<PanState, PanState> change, object source)
    {
        if (!ApplyLeadToSelection(NotePan.Lane, (_, lead) => change(lead), source))
        {
            return;
        }

        NotePanRowsEditor.Show();
        ShowNotePanOverride(Roll.Selection);
    }

    // Steps the Pan field with the arrows or the wheel.
    private void OnNotePanKeyDown(object? sender, KeyEventArgs e)
    {
        StepField(e, NotePanelValues.PanRow, () => FieldInput.SharedValue(Roll.Selection, LeadPanValueOf), NudgeNotePan);
    }

    // Steps every selected note's lead pan by the same amount, each keeping its own surround pair, and shows the row and the section under it again.
    private void NudgeNotePan(int step)
    {
        var leadPansBeforeStep = Roll.Selection.ToDictionary(one => (one.Channel, one.Note.StartTick), LeadPanOf);
        if (!NudgeOverride(
                ref _isUpdatingNotePan, NotePanelValues.PanRow, step, one => leadPansBeforeStep[(one.Channel, one.Note.StartTick)].Value,
                (one, pan) => NotePan.Lane.SetLeadState(_song, one.Channel, one.Note, leadPansBeforeStep[(one.Channel, one.Note.StartTick)] with { Value = pan })))
        {
            return;
        }

        ShowNotePanRows();
    }

    // Puts the Pan field back to what the selection holds once it is left.
    private void OnNotePanCommitted(object? sender, RoutedEventArgs e)
    {
        ShowNotePan(Roll.Selection);
    }

    // Writes the selection's amplify into the Amplify field and its slider's span, unless an edit from the field is in flight, and shows the row's switch.
    private void ShowNoteAmplify(IReadOnlyList<(int Channel, Note Note)> selected)
    {
        if (!_isUpdatingNoteAmplify)
        {
            FlagGuard.RunWithFlagSet(ref _isUpdatingNoteAmplify, () => WriteFieldAndSpan(NotePanelValues.AmplifyRow.Box, NotePanelValues.AmplifyRow.Slider, selected, AmplifyOf));
        }

        ShowNoteAmplifyOverride(selected);
    }

    // Shows the Amplify row's switch, the mark under its slider at the instrument's own amplify, and the row live only where every note owns it.
    private void ShowNoteAmplifyOverride(IReadOnlyList<(int Channel, Note Note)> selected)
    {
        bool isOwned = ShowLaneSource(NotePanelValues.AmplifyRow.Switch, selected, NoteAmplify.Lane);
        ShowInstrumentMark(NotePanelValues.AmplifyRow.Slider, FieldInput.SharedValue(selected, InstrumentAmplifyOf));
        ShowRowEditable(NotePanelValues.AmplifyRow.Slider, NotePanelValues.AmplifyRow.Box, isOwned);
    }

    // The amplify a note plays, whichever rung decides it.
    private int AmplifyOf((int Channel, Note Note) one)
    {
        return NoteAmplify.Lane.ValuePlayedBy(_song, one.Channel, one.Note);
    }

    // Moves the selection onto its own amplify where the Amplify row's inert slider or field was pressed, and lets the press go on.
    private void OnNoteAmplifyPressed(object? sender, PointerPressedEventArgs e)
    {
        ClaimOnInertPress(sender, e, NoteAmplify.Lane);
    }

    // Moves the selection onto a rung of the amplify's three.
    private void OnAmplifySourceCycled(int state)
    {
        MoveSelectionToLaneRung(NoteAmplify.Lane, (LaneSource)state, NotePanelValues.AmplifyRow.Switch);
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

    // Gives every selected note an amplify of its own, coalescing on the Amplify field, and shows the switch again.
    private void ApplyNoteAmplify(Func<(int Channel, Note Note), int> amplifyOf)
    {
        if (!ApplyToSelection(
                NotePanelValues.AmplifyRow.Box, one => IsOnOwnRung(NoteAmplify.Lane, one) && AmplifyOf(one) == amplifyOf(one),
                one => NoteAmplify.Lane.ApplyOverride(_song, one.Channel, one.Note, amplifyOf(one))))
        {
            return;
        }

        ShowNoteAmplifyOverride(Roll.Selection);
    }

    // The amplify a note's own instrument holds.
    private int InstrumentAmplifyOf((int Channel, Note Note) one)
    {
        return NoteAmplify.Lane.InstrumentValue(_song.InstrumentById(one.Note.InstrumentId));
    }

    // Gives every selected note its own instrument's amplify as an amplify of its own, for a right-click on the row.
    private void ResetNoteAmplifyToInstrument()
    {
        ApplyNoteAmplify(InstrumentAmplifyOf);
        ShowNoteAmplify(Roll.Selection);
    }

    // Steps the Amplify field with the arrows or the wheel.
    private void OnNoteAmplifyKeyDown(object? sender, KeyEventArgs e)
    {
        StepField(e, NotePanelValues.AmplifyRow, () => FieldInput.SharedValue(Roll.Selection, AmplifyOf), NudgeNoteAmplify);
    }

    // Steps every selected note's amplify by the same amount, and shows the row again.
    private void NudgeNoteAmplify(int step)
    {
        if (!NudgeOverride(
                ref _isUpdatingNoteAmplify, NotePanelValues.AmplifyRow, step, AmplifyOf,
                (one, amplify) => NoteAmplify.Lane.ApplyOverride(_song, one.Channel, one.Note, amplify)))
        {
            return;
        }

        ShowNoteAmplify(Roll.Selection);
    }

    // Puts the Amplify field back to what the selection holds once it is left.
    private void OnNoteAmplifyCommitted(object? sender, RoutedEventArgs e)
    {
        ShowNoteAmplify(Roll.Selection);
    }

    // Writes a three-rung switch from the notes - the rung they agree on, mixed where they differ, the lane's with none - and answers whether every one of them stands on its own.
    private bool ShowLaneSource(FieldOverride rungSwitch, IReadOnlyList<(int Channel, Note Note)> notes, ILaneRungs lane)
    {
        rungSwitch.State = notes.Count == 0 ? (int)LaneSource.None : FieldInput.SharedValue(notes, one => (int)lane.LiveSourceOf(_song, one.Channel, one.Note));
        return IsEveryNoteOnOwnRung(notes, lane);
    }

    // Whether there are notes and every one of them stands on its own rung over a lane.
    private bool IsEveryNoteOnOwnRung(IReadOnlyList<(int Channel, Note Note)> notes, ILaneRungs lane)
    {
        return notes.Count > 0 && notes.All(one => IsOnOwnRung(lane, one));
    }

    // The note panel's rows over a lane: inert while any selected note is off its own rung, and claimed by moving the selection onto it under the
    // row's switch where the press brings no key of its own.
    private InertClaim LaneClaim(ILaneRungs lane, FieldOverride rowSwitch)
    {
        return new InertClaim(() => !IsEveryNoteOnOwnRung(Roll.Selection, lane), undoKey => MoveSelectionToLaneRung(lane, LaneSource.Note, undoKey ?? rowSwitch));
    }

    // Whether a note stands on its own rung over a lane.
    private bool IsOnOwnRung(ILaneRungs lane, (int Channel, Note Note) one)
    {
        return lane.LiveSourceOf(_song, one.Channel, one.Note) == LaneSource.Note;
    }

    // Moves the selection onto its own rung over a lane where the press landed on an inert control and claims it, as a claim.
    private void ClaimOnInertPress(object? sender, PointerPressedEventArgs e, ILaneRungs lane)
    {
        if (!FieldSteps.IsClaimingPress(sender, e))
        {
            return;
        }

        MoveSelectionToLaneRung(lane, LaneSource.Note, EditStep.ClaimSource);
    }

    // Moves the selection onto a rung over a lane, recording the move under the given undo key where any note moved, and shows the note panel again.
    private void MoveSelectionToLaneRung(ILaneRungs lane, LaneSource rung, object undoKey)
    {
        ShowSelectionEdit(undoKey, () => MoveEverySelectedNoteToRung(lane, rung));
    }

    // Moves every selected note onto a rung over a lane without changing what it plays, and answers whether any note was elsewhere.
    private bool MoveEverySelectedNoteToRung(ILaneRungs lane, LaneSource rung)
    {
        var notes = Roll.Selection;
        if (notes.All(one => lane.LiveSourceOf(_song, one.Channel, one.Note) == rung))
        {
            return false;
        }

        foreach (var (channel, note) in notes)
        {
            lane.SetSource(_song, channel, note, rung);
        }

        return true;
    }

    // Changes every selected note's lead over a fading lane where any would change, records it under the control it came from, and answers whether it did.
    private bool ApplyLeadToSelection<TState>(NoteFadeLane<TState> lane, Func<(int Channel, Note Note), TState, TState> change, object source) where TState : struct
    {
        return ApplyToSelection(
            source,
            one =>
            {
                var lead = lane.LeadState(_song, one.Channel, one.Note);
                return EqualityComparer<TState>.Default.Equals(change(one, lead), lead);
            },
            one => lane.SetLeadState(_song, one.Channel, one.Note, change(one, lane.LeadState(_song, one.Channel, one.Note))));
    }

    // The byte a note's own instrument's lead holds over a fading lane.
    private int InstrumentLeadValueOf<TState>(NoteFadeLane<TState> lane, (int Channel, Note Note) one) where TState : struct
    {
        return lane.ValueOf(lane.LeadState(_song.InstrumentById(one.Note.InstrumentId)));
    }

    // Puts every selected note's lead over a fading lane at its own instrument's byte, keeping what rides beside it, for a right-click on the row's field, and shows the note panel again.
    private void ResetLeadsToInstrument<TState>(NoteFadeLane<TState> lane, TextBox box) where TState : struct
    {
        ApplyLeadToSelection(lane, (one, lead) => lane.WithValue(lead, InstrumentLeadValueOf(lane, one)), box);
        ShowNotePanel();
    }

    // Writes onto every selected note where any would change, records it under the control it came from, and answers whether it did.
    private bool ApplyToSelection(object source, Func<(int Channel, Note Note), bool> isUnchanged, Action<(int Channel, Note Note)> write)
    {
        var notes = Roll.Selection;
        if (notes.All(isUnchanged))
        {
            return false;
        }

        foreach (var one in notes)
        {
            write(one);
        }

        MarkSelectionModified(source);
        return true;
    }

    // Steps a lane's value on every selected note by the same amount, as far as all of them can go, with the field's own edits held off, recording the step under the field; false where nothing moved.
    private bool NudgeOverride(
        ref bool isUpdatingField, NumericRowParts row, int step, Func<(int Channel, Note Note), int> read, Action<(int Channel, Note Note), int> write)
    {
        var notes = Roll.Selection;
        if (notes.Count == 0)
        {
            return false;
        }

        var valuesBeforeStep = notes.Select(read).ToList();
        int groupStepWithinRange = GroupStep.ClampedToEveryMember(step, valuesBeforeStep.Select(value => (value, row.Range)));
        if (groupStepWithinRange == 0)
        {
            return false;
        }

        FlagGuard.RunWithFlagSet(ref isUpdatingField, () =>
        {
            for (int i = 0; i < notes.Count; i++)
            {
                write(notes[i], valuesBeforeStep[i] + groupStepWithinRange);
            }

            MarkSelectionModified(row.Box);
        });
        return true;
    }

    // Writes the value the notes agree on into a field, blank where they differ, and every note's value onto the slider beside it.
    private static void WriteFieldAndSpan(TextBox box, Slider slider, IReadOnlyList<(int Channel, Note Note)> notes, Func<(int Channel, Note Note), int> valueOf)
    {
        box.Text = FieldInput.TextOf(FieldInput.SharedValue(notes, valueOf));
        SliderField.ShowSpan(slider, notes.Select(valueOf));
    }
}
