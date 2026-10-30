using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using SpcStudio.Controls;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio;

// The note panel's own place in the song - position, tone and length - showing the panel for the selection, opening the Multiselect box, and the panel's wiring.
public partial class MainWindow
{
    private const int NoiseClockDigits = 2;
    private const int NoteNameDigits = 3;
    private const string SpannedChannelsBrushResource = "WarningBrush";
    private const string NoNoteCountText = "no note";
    private const int ThreeRungMixedGoesTo = (int)LaneSource.Instrument;

    private static readonly Geometry[] TwoRungIcons = [AppIcons.Piano, AppIcons.MusicNote];
    private static readonly Geometry[] ThreeRungIcons = [AppIcons.Minus, AppIcons.Piano, AppIcons.MusicNote];

    // Whether every selected note plays the noise generator, which puts the Tone field in clocks rather than note names.
    private bool IsSelectionAllNoise
    {
        get
        {
            return Roll.SelectedNotes is { Count: > 0 } notes && notes.All(_song.IsNoiseNote);
        }
    }

    // The rows the Tone field can move the selection between: the noise band where every note plays noise, the roll otherwise.
    private ValueRange ToneRangeForSelection
    {
        get
        {
            return IsSelectionAllNoise ? SnesNoise.SemitoneRange : Song.SemitoneRange;
        }
    }

    // Follows a selection a gesture chose with the selected notes and the tab in front - the note panel's where notes are selected and the instrument panel's where none are.
    private void OnSelectionChanged(IReadOnlyList<Note> selected)
    {
        FollowSelectedNotes(selected);
        ShowPanel(selected.Count > 0 ? PanelTab.Note : PanelTab.Instrument);
    }

    // Follows the selected notes with the note panel, the current instrument and the lanes of units' selections.
    private void FollowSelectedNotes(IReadOnlyList<Note> selected)
    {
        FollowNoteRun();
        AdoptQuantization(selected);
        if (selected.Count > 0)
        {
            ShowQuantization();
        }

        if (FieldInput.SharedValue(selected, note => note.InstrumentId) is int instrumentId && instrumentId != _currentInstrumentId)
        {
            MakeInstrumentCurrent(instrumentId);
            Instruments.Show(_song, CurrentInstrument);
        }

        ShowInstrumentSelection();
        ShowNotePanel();
        if (selected.Count == 0)
        {
            return;
        }

        foreach (var lane in UnitLanes)
        {
            lane.ClearUnitSelection();
        }
    }

    // Records an edit to the selected notes as an undo step keyed on the control it came from.
    private void MarkSelectionModified(object? undoKey)
    {
        MarkModified((undoKey, Roll.SelectionChannels));
    }

    // Makes an edit to the selected notes, records it where it changed any, and answers whether it did.
    private bool RecordSelectionEdit(object? undoKey, Func<bool> edit)
    {
        if (!edit())
        {
            return false;
        }

        MarkSelectionModified(undoKey);
        return true;
    }

    // Makes an edit to the selected notes, records it where it changed any, and shows the note panel again.
    private void ShowSelectionEdit(object? undoKey, Func<bool> edit)
    {
        RecordSelectionEdit(undoKey, edit);
        ShowNotePanel();
    }

    // Moves every selected note by a step with the place fields' own edits held off, records it where it moved any, and shows the note panel again.
    private void NudgeSelection(object undoKey, Func<bool> nudge)
    {
        FlagGuard.RunWithFlagSet(ref _isUpdatingNote, () => RecordSelectionEdit(undoKey, nudge));
        ShowNotePanel();
    }

    // Steps a note panel field over the selection: hands the value the selection shares and the step to the field's own write, and steps every
    // note's own value where they differ.
    private static void StepSelectionField(KeyEventArgs e, Func<int?> sharedValue, Action<int> stepEveryNote, Action<int, int> writeStepped)
    {
        if (!FieldSteps.TryTakeStep(e, out int step))
        {
            return;
        }

        if (sharedValue() is not int value)
        {
            stepEveryNote(step);
            return;
        }

        writeStepped(value, step);
    }

    // Moves the selected notes to a tick, the Position field's edit.
    private void ApplyPosition(int startTick)
    {
        ShowSelectionEdit(PositionBox, () => Roll.SetSelectedStart(startTick));
    }

    // Moves every selected note by the same number of ticks, keeping the group's shape.
    private void NudgeSelectedPositions(int ticks)
    {
        ShowSelectionEdit(PositionBox, () => Roll.NudgeSelectedStart(ticks));
    }

    // Applies what the Position field holds, and puts the note's own tick back where it holds nothing.
    private void CommitPosition()
    {
        if (_isUpdatingNote)
        {
            return;
        }

        FlagGuard.RunWithFlagSet(ref _isUpdatingNote, () =>
        {
            if (FieldInput.TryReadValue(PositionBox.Text, NotePlaceLimits.PositionRange, out int tick))
            {
                ApplyPosition(tick);
            }
        });
        ShowNotePanel();
    }

    // Commits the Position field when it loses the focus.
    private void OnPositionCommitted(object? sender, RoutedEventArgs e)
    {
        CommitPosition();
    }

    // Commits the Position field on Enter, and moves the selection by the snap size on an arrow or a notch.
    private void OnPositionKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitPosition();
            e.Handled = true;
            return;
        }

        int step = FieldSteps.StepOf(e);
        var notes = Roll.SelectedNotes;
        if (step == 0 || notes.Count == 0)
        {
            return;
        }

        int ticks = step * _song.SnapTicks;
        if (notes.Count > 1)
        {
            FlagGuard.RunWithFlagSet(ref _isUpdatingNote, () => NudgeSelectedPositions(ticks));
        }
        else
        {
            FlagGuard.RunWithFlagSet(ref _isUpdatingNote, () => ApplyPosition(NotePlaceLimits.PositionRange.Clamp(notes[0].StartTick + ticks)));
        }

        ShowNotePanel();
        PositionBox.CaretIndex = PositionBox.Text?.Length ?? 0;
        e.Handled = true;
    }

    // Re-pitches the selected notes to a row, the Tone field's edit.
    private void ApplyTone(int semitone)
    {
        ShowSelectionEdit(ToneBox, () => Roll.SetSelectedSemitone(semitone));
    }

    // Reads the Tone field as a row: from a clock where the selection plays noise, from a note name otherwise.
    private bool TryReadTone(string? text, out int semitone)
    {
        if (!IsSelectionAllNoise)
        {
            return PitchLabel.TryParse(text ?? "", out semitone);
        }

        semitone = 0;
        if (!FieldInput.TryReadHex(text, SnesNoise.ClockRange, out int clock))
        {
            return false;
        }

        semitone = SnesNoise.SemitoneForClock(clock);
        return true;
    }

    // What the Tone field shows for a row: its clock where the selection plays noise, its name otherwise.
    private string ToneTextForSelection(int semitone)
    {
        return IsSelectionAllNoise ? FieldInput.HexTextOf(SnesNoise.ClockForSemitone(semitone)) : PitchLabel.NameOf(semitone);
    }

    // Whether every selected note can sit on a row without one of its bends leaving the roll.
    private bool CanSelectionTakeTone(int semitone)
    {
        return Roll.SelectedNotes.All(note => PitchBend.StartsHeldIn(Song.SemitoneRange, note.Bends).Contains(semitone));
    }

    // Re-pitches the selection as a row is typed into the Tone field.
    private void OnToneChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingNote || IsBlankForMixedSelection(ToneBox) || !TryReadTone(ToneBox.Text, out int semitone) || !CanSelectionTakeTone(semitone))
        {
            return;
        }

        FlagGuard.RunWithFlagSet(ref _isUpdatingNote, () => ApplyTone(semitone));
    }

    // Steps the Tone field a row, or moves every selected note a row where they sit on different rows.
    private void OnToneKeyDown(object? sender, KeyEventArgs e)
    {
        StepSelectionField(e, () => FieldInput.SharedValue(Roll.SelectedNotes, note => note.Semitone), NudgeTone, WriteSteppedTone);
    }

    // Writes the row a step moves the selection's shared row to into the Tone field, held to the band and kept where a bend would leave the roll.
    private void WriteSteppedTone(int semitone, int step)
    {
        int moved = ToneRangeForSelection.Clamp(semitone + step);
        if (!CanSelectionTakeTone(moved))
        {
            moved = semitone;
        }

        FieldInput.WriteText(ToneBox, ToneTextForSelection(moved));
    }

    // Moves every selected note by a row, keeping the intervals between them.
    private void NudgeTone(int step)
    {
        NudgeSelection(ToneBox, () => Roll.NudgeSelectedSemitone(step, ToneRangeForSelection));
    }

    // Puts the selection's row back into the Tone field when it loses the focus.
    private void OnToneCommitted(object? sender, RoutedEventArgs e)
    {
        ShowNotePanel();
    }

    // Resizes the selected notes, the Length field's edit.
    private void ApplyLength(int ticks)
    {
        ShowSelectionEdit(LengthBox, () => Roll.SetSelectedLength(ticks));
    }

    // Resizes every selected note by the same number of ticks, keeping the differences between them.
    private void NudgeSelectedLengths(int ticks)
    {
        NudgeSelection(LengthBox, () => Roll.NudgeSelectedLength(ticks, NotePlaceLimits.LengthRange));
    }

    // Resizes the selection as a length is typed into the Length field.
    private void OnLengthChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingNote || IsBlankForMixedSelection(LengthBox) || !FieldInput.TryReadValue(LengthBox.Text, NotePlaceLimits.LengthRange, out int ticks))
        {
            return;
        }

        FlagGuard.RunWithFlagSet(ref _isUpdatingNote, () => ApplyLength(ticks));
    }

    // Steps the Length field by the snap size, or resizes every selected note by it where their lengths differ.
    private void OnLengthKeyDown(object? sender, KeyEventArgs e)
    {
        StepSelectionField(e, () => FieldInput.SharedValue(Roll.SelectedNotes, note => note.LengthTicks), step => NudgeSelectedLengths(step * _song.SnapTicks),
            (length, step) => FieldInput.WriteStepped(LengthBox, length, step * _song.SnapTicks, NotePlaceLimits.LengthRange));
    }

    // Puts the selection's length back into the Length field when it loses the focus.
    private void OnLengthCommitted(object? sender, RoutedEventArgs e)
    {
        ShowNotePanel();
    }

    // Shows the note panel's fields for the selection, or takes them out of the panel while nothing is selected.
    private void ShowNotePanel()
    {
        var notes = Roll.SelectedNotes;
        NoteStack.IsVisible = notes.Count > 0;
        ShowPlayNoteButton();
        ShowSelectedNoteCount(notes.Count);
        ShowNoteDefaults(notes);
        ShowNoteFlags(notes);
        ShowNoteEcho();
        ShowNoteVolumeRows();
        ShowNotePanRows();
        ShowNoteAmplify(Roll.Selection);
        ShowChainSections(notes);
        if (notes.Count == 0 || _isUpdatingNote)
        {
            return;
        }

        FlagGuard.RunWithFlagSet(ref _isUpdatingNote, () => ShowNotePlaceFields(notes));
    }

    // Writes how many notes are selected, or that none is, and on how many channels where they span more than one, marked in the warning color.
    private void ShowSelectedNoteCount(int noteCount)
    {
        int channelCount = Roll.SelectionChannelCount;
        string spannedChannels = channelCount > 1 ? $" in {channelCount} channels" : "";
        string countedNotes = noteCount == 0 ? NoNoteCountText : CountedNoun.Of(noteCount, CountedNoun.NoteNoun);
        NoteCountText.Text = $"{countedNotes} selected{spannedChannels}";
        NoteCountText.Foreground = channelCount > 1 ? ThemeResource.Of<IBrush>(SpannedChannelsBrushResource) : ThemeResource.TextSecondaryBrush;
    }

    // Writes the selection's position, row and length into their fields, the Tone field taking clocks or note names as the selection asks.
    private void ShowNotePlaceFields(IReadOnlyList<Note> notes)
    {
        PositionBox.Text = FieldInput.TextOf(FieldInput.SharedValue(notes, note => note.StartTick));
        bool isAllNoise = IsSelectionAllNoise;
        ToneBox.MaxLength = isAllNoise ? NoiseClockDigits : NoteNameDigits;
        FieldInput.SetAccepts(ToneBox, isAllNoise ? FieldChars.NoiseClock : FieldChars.NoteName);
        ToneBox.Text = FieldInput.SharedValue(notes, note => note.Semitone) is int semitone ? ToneTextForSelection(semitone) : "";
        LengthBox.Text = FieldInput.TextOf(FieldInput.SharedValue(notes, note => note.LengthTicks));
    }

    // Whether a field is blank because the selection holds no one value for it, which is not a value to apply.
    private bool IsBlankForMixedSelection(TextBox box)
    {
        return Roll.SelectionCount > 1 && string.IsNullOrEmpty(box.Text);
    }

    // Opens the Multiselect box over the song as it stands, and selects what its filter takes, which is not an edit.
    private async void OnMultiselectClick(object? sender, RoutedEventArgs e)
    {
        if (await MultiselectDialog.Show(this, _song) is not { } filter)
        {
            return;
        }

        Roll.SelectMatching(filter);
    }

    // Ties the note panel's sliders to their fields, gives the fields their resets and hooks up its rung switches and the presses that claim a value for the note.
    private void WireNotePanel()
    {
        HookNoteValueEdits();
        WireNoteResets();
        DropDownAuditions.Hook(NoteInstrumentBox, Tooltips.PlayInstrument, AuditionInstrumentRow, ReleaseAudition);
        NotePanelValues.EnvelopeRows.RowAddButton.Click += OnAddNoteEnvelope;
        HookNoteDefaultSwitches();
        HookChainSectionSwitches();
        HookLaneSourceSwitches();
    }

    // Hooks what typing into and leaving each value field, pressing each check box and drop-down, and each list section's '+' do.
    private void HookNoteValueEdits()
    {
        NotePanelValues.GateRow.OnTypedOrLeft(OnGateChanged, OnQuantizationFieldCommitted);
        NotePanelValues.VelocityRow.OnTypedOrLeft(OnVelocityChanged, OnQuantizationFieldCommitted);
        NotePanelValues.DetuneRow.OnTypedOrLeft(OnFineTuneChanged, OnQuantizationFieldCommitted);
        NotePanelValues.VolumeRow.OnTypedOrLeft(OnNoteLeadChanged, OnNoteLeadCommitted);
        NotePanelValues.AmplifyRow.OnTypedOrLeft(OnNoteAmplifyChanged, OnNoteAmplifyCommitted);
        NotePanelValues.PanRow.OnTypedOrLeft(OnNotePanChanged, OnNotePanCommitted);
        NotePanelValues.SurroundDropDown.SelectionChanged += OnNoteSurroundToggled;
        NotePanelValues.LegatoRow.Check.IsCheckedChanged += OnNoteLegatoToggled;
        NotePanelValues.HoldRow.Check.IsCheckedChanged += OnNoteHoldToggled;
        NotePanelValues.EchoRow.Check.IsCheckedChanged += OnNoteEchoToggled;
        NotePanelValues.VolumeSection.OnAddPressed(OnAddNoteVolume);
        NotePanelValues.PanSection.OnAddPressed(OnAddNotePan);
        NotePanelValues.BendSection.OnAddPressed(OnAddBend);
        NotePanelValues.VibratoSection.OnAddPressed(OnAddVibrato);
        NotePanelValues.TremoloSection.OnAddPressed(OnAddTremolo);
    }

    // Gives each of the note panel's value fields the reset a right-click on it runs: every selected note takes its own instrument's value and keeps it as its own.
    private void WireNoteResets()
    {
        foreach (var value in SliderNoteDefaults)
        {
            var (_, box, _, _) = NoteDefaultRow(value);
            FieldResets.OnReset(box, () => ResetQuantizationToInstrument(value));
        }

        FieldResets.OnReset(NotePanelValues.VolumeRow.Box, () => ResetLeadsToInstrument(NoteVolume.Lane, NotePanelValues.VolumeRow.Box));
        FieldResets.OnReset(NotePanelValues.PanRow.Box, () => ResetLeadsToInstrument(NotePan.Lane, NotePanelValues.PanRow.Box));
        FieldResets.OnReset(NotePanelValues.AmplifyRow.Box, ResetNoteAmplifyToInstrument);
    }

    // Gives the gate, velocity, tune, legato and hold rows their two-rung switches, and lets a press on an inert control claim the note's own rung.
    private void HookNoteDefaultSwitches()
    {
        foreach (var value in SliderNoteDefaults.Concat(CheckBoxNoteDefaults))
        {
            var rungSwitch = RungSwitchOf(value);
            ShowTwoRungs(rungSwitch);
            rungSwitch.Cycled += state => OnNoteDefaultCycled(value, state);
        }

        foreach (var value in SliderNoteDefaults)
        {
            var (slider, box, _, _) = NoteDefaultRow(value);
            AddTunneledPressHandler(slider, OnNoteDefaultPressed);
            AddTunneledPressHandler(box, OnNoteDefaultPressed);
        }

        foreach (var value in CheckBoxNoteDefaults)
        {
            AddTunneledPressHandler(NoteFlagRow(value).Check, OnNoteFlagPressed);
        }
    }

    // Gives the volume, pan, amplify and echo rows and the Volume and Pan sections' banners their three-rung switches, and lets a press on an inert control claim the note's own rung.
    private void HookLaneSourceSwitches()
    {
        foreach (var row in new[] { NotePanelValues.PanRow.Switch, NotePanelValues.VolumeRow.Switch, NotePanelValues.AmplifyRow.Switch, NotePanelValues.EchoRow.Switch })
        {
            row.Icons = ThreeRungIcons;
            row.MixedGoesTo = ThreeRungMixedGoesTo;
        }

        foreach (var section in new[] { NotePanelValues.VolumeSection.Section, NotePanelValues.PanSection.Section })
        {
            section.SwitchIcons = ThreeRungIcons;
            section.SwitchMixedGoesTo = ThreeRungMixedGoesTo;
        }

        NotePanelValues.PanRow.Switch.Cycled += state => OnPanSourceCycled(state);
        NotePanelValues.VolumeRow.Switch.Cycled += state => OnVolumeSourceCycled(state);
        NotePanelValues.PanSection.Section.SwitchCycled += OnPanSourceCycled;
        NotePanelValues.VolumeSection.Section.SwitchCycled += OnVolumeSourceCycled;
        NotePanelValues.AmplifyRow.Switch.Cycled += state => OnAmplifySourceCycled(state);
        NotePanelValues.EchoRow.Switch.Cycled += state => OnEchoSourceCycled(state);
        AddTunneledPressHandler(NotePanelValues.VolumeRow.Slider, OnNoteLeadPressed);
        AddTunneledPressHandler(NotePanelValues.VolumeRow.Box, OnNoteLeadPressed);
        AddTunneledPressHandler(NotePanelValues.PanRow.Slider, OnNotePanPressed);
        AddTunneledPressHandler(NotePanelValues.PanRow.Box, OnNotePanPressed);
        AddTunneledPressHandler(NotePanelValues.SurroundDropDown, OnNotePanPressed);
        AddTunneledPressHandler(NotePanelValues.AmplifyRow.Slider, OnNoteAmplifyPressed);
        AddTunneledPressHandler(NotePanelValues.AmplifyRow.Box, OnNoteAmplifyPressed);
        AddTunneledPressHandler(NotePanelValues.EchoRow.Check, OnNoteEchoPressed);
    }

    // Gives a row's switch the two rungs' glyphs, a press out of a mixed selection going to the instrument's.
    private static void ShowTwoRungs(FieldOverride row)
    {
        row.Icons = TwoRungIcons;
        row.MixedGoesTo = SwitchStateOfRung(NoteSource.Instrument);
    }

    // Hangs a press handler on a control ahead of the control's own.
    private static void AddTunneledPressHandler(Control target, EventHandler<PointerPressedEventArgs> handler)
    {
        target.AddHandler(PointerPressedEvent, handler, RoutingStrategies.Tunnel);
    }
}
