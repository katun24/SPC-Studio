using System.Collections;
using System.Collections.Immutable;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using SpcStudio.Audio;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// Edits the current instrument: its name, its sample, its color, its tuning, its envelope bytes and the values every note standing on its rungs plays,
// writing each edit straight into the instrument and reporting it.
public partial class InstrumentPanel : UserControl
{
    private const string NoiseItem = Sample.NoiseName;
    private const int NoiseItemIndex = 0;
    private const int FirstSampleItemIndex = NoiseItemIndex + 1;
    private const int NoSelectionIndex = -1;
    private const int MaxChainTicks = 9999;
    private const int TuningFieldMaxMagnitude = 999;
    private const double TuningMatchTolerance = 1e-9;
    private const double MinTuning = 1 / Instrument.TuningLowStepsPerUnit;

    private static readonly ValueRange TuningFieldRange = new(-TuningFieldMaxMagnitude, TuningFieldMaxMagnitude);
    private readonly DefaultField[] _defaultFields;
    private readonly ToggleField[] _toggleFields;
    private Song? _song;
    private Instrument? _instrument;
    private BrrPitch.Measurement _measurement;
    private bool _isUpdating;
    private Control? _controlBeingEdited;
    private double _playbackPositionTick;
    private EnvelopeChainRows? _envelopeRowsEditor;
    private ChainRows<int>? _volumeRowsEditor;
    private ChainRows<PanState>? _panRowsEditor;
    private PitchBendRows? _bendEditor;
    private SweepRows<NoteVibrato>? _vibratoEditor;
    private SweepRows<NoteTremolo>? _tremoloEditor;

    // Builds the controls and wires every field, the tuning's four controls, the play buttons and the hue strip.
    public InstrumentPanel()
    {
        InitializeComponent();

        _defaultFields =
        [
            new DefaultField(InstrumentValues.GateRow, instrument => instrument.Gate, (instrument, gate) => NoteDefaults.SetQuantizationHalf(instrument, NoteDefault.Gate, gate)),
            new DefaultField(InstrumentValues.VelocityRow, instrument => instrument.Velocity, (instrument, velocity) => NoteDefaults.SetQuantizationHalf(instrument, NoteDefault.Velocity, velocity)),
            new DefaultField(InstrumentValues.DetuneRow, instrument => instrument.FineTune, (instrument, fineTune) => instrument.FineTune = fineTune),
            new DefaultField(InstrumentValues.VolumeRow, instrument => NoteVolume.Lane.LeadState(instrument), (instrument, volume) => NoteVolume.Lane.SetLeadState(instrument, volume)),
            new DefaultField(InstrumentValues.PanRow, instrument => NotePan.Lane.LeadState(instrument).Value,
                (instrument, pan) => NotePan.Lane.SetLeadState(instrument, NotePan.Lane.LeadState(instrument) with { Value = pan })),
            new DefaultField(InstrumentValues.AmplifyRow, instrument => instrument.Amplify, (instrument, amplify) => instrument.Amplify = amplify),
        ];

        _toggleFields =
        [
            new ToggleField(InstrumentValues.LegatoRow.Check, instrument => instrument.IsLegato, (instrument, isLegato) => instrument.IsLegato = isLegato),
            new ToggleField(InstrumentValues.HoldRow.Check, instrument => instrument.IsHeld, (instrument, isHeld) => instrument.IsHeld = isHeld),
            new ToggleField(InstrumentValues.EchoRow.Check, instrument => instrument.IsEchoOn, (instrument, isEchoOn) => instrument.IsEchoOn = isEchoOn),
        ];

        foreach (var field in _defaultFields)
        {
            field.Row.OnTypedOrLeft(OnDefaultFieldChanged, OnDefaultFieldCommitted);
            FieldSteps.OnKeys(field.Row.Box, (_, e) => StepDefaultField(e, field));
            FieldResets.ResetsTo(field.Row.Box, () => field.Read(Instrument.NewInstrumentDefaults));
        }

        foreach (var field in _toggleFields)
        {
            field.Check.IsCheckedChanged += OnToggleFieldChanged;
        }

        HookTuningField(OctaveBox, SnesPitch.CentsPerOctave);
        HookTuningField(SemitoneBox, SnesPitch.CentsPerSemitone);
        HookTuningField(CentsBox, 1);
        TuningSlider.ValueChanged += OnTuningSliderChanged;
        FieldResets.OnReset(TuningSlider, () => ResetTuningToSample(TuningSlider));
        TuningSlider.PointerReleased += (_, _) => Refresh();
        TuningSlider.AddHandler(PointerWheelChangedEvent, (_, _) => Refresh(), RoutingStrategies.Bubble, handledEventsToo: true);

        InstrumentValues.SurroundDropDown.SelectionChanged += OnSurroundChanged;
        InstrumentValues.VolumeSection.OnAddPressed(OnAddVolumeRow);
        InstrumentValues.PanSection.OnAddPressed(OnAddPanRow);
        InstrumentValues.BendSection.OnAddPressed(OnAddBend);
        InstrumentValues.VibratoSection.OnAddPressed(OnAddVibrato);
        InstrumentValues.TremoloSection.OnAddPressed(OnAddTremolo);
        HeldPress.HookPressAndRelease(PlayButton, () => AuditionRequested?.Invoke(), RaiseAuditionReleased);
        DropDownAuditions.Hook(InstrumentBox, Tooltips.PlayInstrument, index => InstrumentRowAuditionRequested?.Invoke(index), RaiseAuditionReleased);
        DropDownAuditions.Hook(SampleBox, Tooltips.PlaySample, AuditionSampleRow, RaiseAuditionReleased);
        ColorPopup.PlacementTarget = ColorButton;
        ColorPopup.HorizontalOffset = Space.Px4;
        ColorPopup.Opened += (_, _) => InstrumentHueStrip.Focus();
        InstrumentHueStrip.HueChanged += OnHueStripChanged;
        InstrumentHueStrip.KeyDown += OnHueStripKeyDown;
        InstrumentValues.EnvelopeRows.RowAddButton.Click += OnAddEnvelopeRow;
        InstrumentPick.NameCommitted += OnInstrumentNameCommitted;

        Show(null, null);
    }

    // Raised when the user has changed the instrument, with the control the edit came from.
    public event Action<object>? InstrumentEdited;

    // Raised with the name typed once a rename of the instrument is finished, for the main window to give it or refuse it.
    public event Action<string>? RenameRequested;

    // Raised by the bin under the header.
    public event Action? DeleteRequested;

    // Raised by the plus under the header.
    public event Action? CreateRequested;

    // Raised by the tray-down button under the header.
    public event Action? ImportRequested;

    // Raised by the tray-up button under the header.
    public event Action? ExportRequested;

    // Raised by the copy button under the header.
    public event Action? DuplicateRequested;

    // Raised by the folder button beside the Instrument Picker button.
    public event Action? ExportAllRequested;

    // Raised by the button left of the Sample drop-down, with the id of the sample the instrument plays.
    public event Action<int>? SampleEditRequested;

    // Raised by the button left of the play button.
    public event Action? SelectNotesRequested;

    // Raised when the play button is pressed, whatever the window then makes of the press.
    public event Action? AuditionRequested;

    // Raised while an instrument drop-down row's play button is held, with the row's index.
    public event Action<int>? InstrumentRowAuditionRequested;

    // Raised while a Sample drop-down row's play button is held, with a copy of the song and the instrument showing in it, pointed at that row's sample.
    public event Action<Song, Instrument>? SampleRowAuditionRequested;

    // Raised when the play button, or a drop-down row's, comes up.
    public event Action? AuditionReleased;

    // Raised by the instrument drop-down, with the item picked.
    public event Action<object?>? InstrumentPicked;

    // Raised by the corner button, both to take the panel out into a window and to put it back.
    public event Action? PopOutToggled;

    // Raised by the Instrument Picker button.
    public event Action? InstrumentPickerRequested;

    // The instrument drop-down's items.
    public IEnumerable? InstrumentItems
    {
        set
        {
            InstrumentBox.ItemsSource = value;
        }
    }

    // Which of the drop-down's items is showing, by index, or none.
    public int SelectedInstrumentIndex
    {
        set
        {
            InstrumentBox.SelectedIndex = value;
        }
    }

    // Whether the panel stands in a window of its own, which sets the corner button's icon and tooltip.
    public bool IsPoppedOut
    {
        set
        {
            DockableFrame.ShowPoppedOut(value);
        }
    }

    // Whether the envelope section is standing open.
    public bool IsEnvelopeOpen
    {
        get
        {
            return InstrumentValues.EnvelopeSection.IsOpen;
        }
        set
        {
            InstrumentValues.EnvelopeSection.IsOpen = value;
        }
    }

    // The envelope section's rows, built on first use.
    private EnvelopeChainRows EnvelopeRowsEditor
    {
        get
        {
            return _envelopeRowsEditor ??= EnvelopeChainRows.Over(InstrumentValues.EnvelopeRows, InstrumentValues.EnvelopeSection,
                () => SubjectsOf<IEnvelopeHost>(new EnvelopeHost(this), InstrumentValues.EnvelopeRows.RowList),
                () => _instrument, EnvelopeGraphTiming);
        }
    }

    // The Volume section's rows, built on first use.
    private ChainRows<int> VolumeRowsEditor
    {
        get
        {
            return _volumeRowsEditor ??= FadeRows.Volume(InstrumentValues.VolumeSection,
                () => SubjectsOf<IFadeHost<int>>(new FadeHost<int>(this, NoteVolume.Lane), InstrumentValues.VolumeSection.Rows));
        }
    }

    // The Pan section's rows, built on first use.
    private ChainRows<PanState> PanRowsEditor
    {
        get
        {
            return _panRowsEditor ??= FadeRows.Pan(InstrumentValues.PanSection,
                () => SubjectsOf<IFadeHost<PanState>>(new FadeHost<PanState>(this, NotePan.Lane), InstrumentValues.PanSection.Rows));
        }
    }

    // The pitch bend section's rows, built on first use.
    private PitchBendRows BendEditor
    {
        get
        {
            return _bendEditor ??= new PitchBendRows
            {
                SectionParts = InstrumentValues.BendSection,
                Subjects = () => SubjectsOf<IBendHost>(new BendHost(this), InstrumentValues.BendSection.Rows),
            };
        }
    }

    // The vibrato section's rows, built on first use.
    private SweepRows<NoteVibrato> VibratoEditor
    {
        get
        {
            return _vibratoEditor ??= SweepFields.VibratoRows(InstrumentValues.VibratoSection,
                () => SubjectsOf<IListHost<NoteVibrato>>(new ListHost<NoteVibrato>(this, instrument => instrument.Vibratos, NoteDefaults.SetVibratos), InstrumentValues.VibratoSection.Rows));
        }
    }

    // The tremolo section's rows, built on first use.
    private SweepRows<NoteTremolo> TremoloEditor
    {
        get
        {
            return _tremoloEditor ??= SweepFields.TremoloRows(InstrumentValues.TremoloSection,
                () => SubjectsOf<IListHost<NoteTremolo>>(new ListHost<NoteTremolo>(this, instrument => instrument.Tremolos, NoteDefaults.SetTremolos), InstrumentValues.TremoloSection.Rows));
        }
    }

    // Points the panel at an instrument of a song, or at none, and shows only what there is to edit.
    public void Show(Song? song, Instrument? instrument)
    {
        _song = song;
        _instrument = instrument;

        if (song is not null && instrument is not null && song.SampleFor(instrument) is { } sample)
        {
            _measurement = sample.Pitch;
        }
        else if (instrument is { IsNoise: true })
        {
            _measurement = default;
        }

        bool hasInstrument = instrument is not null;
        ExportAllButton.IsVisible = song is { Instruments.Count: > 0 };
        PickRow.IsVisible = hasInstrument;
        RenameButton.IsVisible = hasInstrument;
        if (!hasInstrument)
        {
            InstrumentPick.EndRename();
        }

        DeleteButton.IsVisible = hasInstrument;
        ExportButton.IsVisible = hasInstrument;
        DuplicateButton.IsVisible = hasInstrument;
        ColorButton.IsVisible = hasInstrument;
        PlayButton.IsVisible = hasInstrument;
        InstrumentFields.IsVisible = hasInstrument;
        ShowWhetherInstrumentHasNotes();

        Refresh();
    }

    // Shows the select button only where the song has notes playing the instrument.
    public void ShowWhetherInstrumentHasNotes()
    {
        SelectNotesButton.IsVisible = _song is { } song && _instrument is { } instrument && song.NoteCountUsingInstrument(instrument.Id) > 0;
    }

    // Shows the glyph and the tooltip the window chose for the play button, which say what a press on it does.
    public void ShowPlayButton(Geometry icon, string tooltip)
    {
        PlayIcon.Data = icon;
        ToolTip.SetTip(PlayButton, tooltip);
    }

    // Takes where the playback position stands, redrawing the envelope's graph where the tempo there differs from the tempo where it stood.
    public void ShowPlaybackPosition(double tick)
    {
        bool isTempoChanged = _song is { } song && TempoMap.For(song) is var tempos && tempos.TempoAt(tick) != tempos.TempoAt(_playbackPositionTick);
        _playbackPositionTick = tick;
        if (isTempoChanged)
        {
            EnvelopeRowsEditor.RedrawGraph();
        }
    }

    // Turns the drop-down into a text field holding the instrument's name, selected whole so it is typed over.
    public void StartRename()
    {
        if (_instrument is null)
        {
            return;
        }

        InstrumentPick.StartRename(_instrument.Name);
    }

    // Writes the instrument's current state into every control of the panel.
    private void Refresh()
    {
        EnvelopeRowsEditor.Show();
        VolumeRowsEditor.Show();
        PanRowsEditor.Show();
        BendEditor.Show();
        VibratoEditor.Show();
        TremoloEditor.Show();

        if (_instrument is not { } instrument)
        {
            return;
        }

        FlagGuard.RunWithFlagSet(ref _isUpdating, () => ShowInstrumentFields(instrument));
    }

    // Writes an instrument's name, color, sample, value fields, check boxes, surround pair, measurement and tuning into the panel.
    private void ShowInstrumentFields(Instrument instrument)
    {
        ShowColor(instrument);
        ShowSample(instrument);

        foreach (var field in _defaultFields)
        {
            int value = field.Read(instrument);
            field.Row.Box.Text = FieldInput.TextOf(value);
            SliderField.ShowSpan(field.Row.Slider, [value]);
        }

        foreach (var field in _toggleFields)
        {
            field.Check.IsChecked = field.Read(instrument);
        }

        InstrumentValues.SurroundDropDown.SelectedIndex = SurroundField.IndexOf(NotePan.Lane.LeadState(instrument).Surround);
        ShowMeasurement();
        ShowTuning(instrument);
    }

    // The instrument showing as the one subject a section's rows edit, or none where no instrument shows, a write that changed it reported with the
    // control it came from or the section's rows for a press.
    private RowSubjects<THost> SubjectsOf<THost>(THost host, StackPanel rows) where THost : IRowHost
    {
        return new RowSubjects<THost>(_instrument is null ? [] : [host], (writeAll, source) =>
        {
            if (writeAll())
            {
                RefreshAndReportEdit(source ?? rows);
            }
        });
    }

    // Writes every control again and raises the edit, with the control it came from.
    private void RefreshAndReportEdit(object source)
    {
        Refresh();
        InstrumentEdited?.Invoke(source);
    }

    // Opens the rename.
    private void OnRenameClick(object? sender, RoutedEventArgs e)
    {
        StartRename();
    }

    // Hands a finished rename's name on where it differs from the instrument's own.
    private void OnInstrumentNameCommitted(string name)
    {
        if (_instrument is null || _instrument.Name == name)
        {
            return;
        }

        RenameRequested?.Invoke(name);
    }

    // Raises the pick of an item in the instrument drop-down.
    private void OnInstrumentSelected(object? sender, SelectionChangedEventArgs e)
    {
        InstrumentPicked?.Invoke(InstrumentBox.SelectedItem);
    }

    // Raises the corner button's press.
    private void OnPopOutClick(object? sender, RoutedEventArgs e)
    {
        PopOutToggled?.Invoke();
    }

    // Raises the Instrument Picker button's press.
    private void OnInstrumentPickerClick(object? sender, RoutedEventArgs e)
    {
        InstrumentPickerRequested?.Invoke();
    }

    // Raises the plus button's press.
    private void OnCreateClick(object? sender, RoutedEventArgs e)
    {
        CreateRequested?.Invoke();
    }

    // Raises the tray-down button's press.
    private void OnImportClick(object? sender, RoutedEventArgs e)
    {
        ImportRequested?.Invoke();
    }

    // Raises the tray-up button's press.
    private void OnExportClick(object? sender, RoutedEventArgs e)
    {
        ExportRequested?.Invoke();
    }

    // Raises the folder button's press.
    private void OnExportAllClick(object? sender, RoutedEventArgs e)
    {
        ExportAllRequested?.Invoke();
    }

    // Raises the copy button's press.
    private void OnDuplicateClick(object? sender, RoutedEventArgs e)
    {
        DuplicateRequested?.Invoke();
    }

    // Raises the bin's press.
    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        DeleteRequested?.Invoke();
    }

    // Raises the select button's press.
    private void OnSelectNotesClick(object? sender, RoutedEventArgs e)
    {
        SelectNotesRequested?.Invoke();
    }

    // Fills the Sample drop-down with the noise generator and the song's samples, selects the instrument's, and enables what the sample allows.
    private void ShowSample(Instrument instrument)
    {
        SampleBox.ItemsSource = _song is null ? [] : new[] { NoiseItem }.Concat(_song.Samples.Select(sample => sample.Name)).ToArray();
        SampleBox.SelectedIndex = SampleItemIndexOf(instrument);
        TuningSection.IsEnabled = !instrument.IsNoise;
        SampleEditButton.IsEnabled = !instrument.IsNoise && _song?.SampleFor(instrument) is not null;
    }

    // The Sample drop-down's index of what an instrument plays: the noise generator, one of the song's samples, or none.
    private int SampleItemIndexOf(Instrument instrument)
    {
        if (_song is null)
        {
            return NoSelectionIndex;
        }

        if (instrument.IsNoise)
        {
            return NoiseItemIndex;
        }

        return _song.Samples.ToList().FindIndex(sample => sample.Id == instrument.SampleId) + FirstSampleItemIndex;
    }

    // Raises the release of a play button.
    private void RaiseAuditionReleased()
    {
        AuditionReleased?.Invoke();
    }

    // Raises the audition of the instrument showing as it would play a Sample drop-down row, on a copy of the song.
    private void AuditionSampleRow(int itemIndex)
    {
        if (_song is null || _instrument is null)
        {
            return;
        }

        var songCopy = _song.Copy();
        if (songCopy.InstrumentById(_instrument.Id) is not { } instrumentCopy)
        {
            return;
        }

        PointAtSample(songCopy, instrumentCopy, SampleIdOfItem(songCopy, itemIndex));
        SampleRowAuditionRequested?.Invoke(songCopy, instrumentCopy);
    }

    // The id of what a Sample drop-down row stands for: the noise generator, or one of a song's samples.
    private static int SampleIdOfItem(Song song, int itemIndex)
    {
        return itemIndex == NoiseItemIndex ? Sample.NoiseId : song.Samples[itemIndex - FirstSampleItemIndex].Id;
    }

    // Points an instrument at the noise generator or at one of a song's samples, at the tuning that sample measured.
    private static void PointAtSample(Song song, Instrument instrument, int sampleId)
    {
        instrument.SampleId = sampleId;
        instrument.TuningMultiplier = song.SampleById(sampleId) is { } sample ? sample.OriginalRateC4Tuning : Instrument.NoiseTuningMultiplier;
    }

    // Raises the request to open the instrument's sample in the sample editor.
    private void OnSampleEditClick(object? sender, RoutedEventArgs e)
    {
        if (_instrument is not { IsNoise: false } instrument)
        {
            return;
        }

        SampleEditRequested?.Invoke(instrument.SampleId);
    }

    // Points the instrument at the noise generator or another sample, reseeding the tuning from the sample's own measurement.
    private void OnSampleChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdating || _song is null || _instrument is null || SampleBox.SelectedIndex < 0)
        {
            return;
        }

        int sampleId = SampleIdOfItem(_song, SampleBox.SelectedIndex);
        if (sampleId == _instrument.SampleId)
        {
            return;
        }

        PointAtSample(_song, _instrument, sampleId);
        _measurement = _song.SampleById(sampleId)?.Pitch ?? default;

        RefreshAndReportEdit(SampleBox);
    }

    // Writes the instrument's color into the swatch button and the hue strip.
    private void ShowColor(Instrument instrument)
    {
        ColorButton.Background = new SolidColorBrush(ChannelPalette.FromPackedRgb(instrument.ColorRgb));
        InstrumentHueStrip.ColorRgb = instrument.ColorRgb;
    }

    // Opens the hue strip, or shuts it where it is open.
    private void OnColorClick(object? sender, RoutedEventArgs e)
    {
        ColorPopup.IsOpen = !ColorPopup.IsOpen;
    }

    // Takes the color of the hue under the strip's bar.
    private void OnHueStripChanged()
    {
        if (_isUpdating || _instrument is null || _instrument.ColorRgb == InstrumentHueStrip.ColorRgb)
        {
            return;
        }

        _instrument.ColorRgb = InstrumentHueStrip.ColorRgb;
        RefreshAndReportEdit(InstrumentHueStrip);
    }

    // Shuts the hue strip on Escape.
    private void OnHueStripKeyDown(object? sender, KeyEventArgs e)
    {
        if (!WindowKeys.IsBareEscape(e))
        {
            return;
        }

        ColorPopup.IsOpen = false;
        e.Handled = true;
    }

    // Writes one of the value rows' fields into the instrument as it is typed.
    private void OnDefaultFieldChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdating || _instrument is null)
        {
            return;
        }

        var field = _defaultFields.First(field => ReferenceEquals(field.Row.Box, sender));
        if (!FieldInput.TryReadValue(field.Row.Box.Text, field.Row.Range, out int value) || field.Read(_instrument) == value)
        {
            return;
        }

        field.Write(_instrument, value);
        RefreshAndReportEdit(field.Row.Box);
    }

    // Puts the instrument's own values back into the value rows when one of their fields loses the focus.
    private void OnDefaultFieldCommitted(object? sender, RoutedEventArgs e)
    {
        Refresh();
    }

    // Steps one of the value rows' fields by an arrow key or a wheel notch, through the field's own text.
    private void StepDefaultField(KeyEventArgs e, DefaultField field)
    {
        int step = FieldSteps.StepOf(e);
        if (step == 0 || _instrument is null)
        {
            return;
        }

        e.Handled = true;
        field.Row.Box.Text = FieldInput.TextOf(field.Row.Range.Clamp(field.Read(_instrument) + step));
    }

    // Writes one of the on-or-off rows into the instrument.
    private void OnToggleFieldChanged(object? sender, RoutedEventArgs e)
    {
        if (_isUpdating || _instrument is null)
        {
            return;
        }

        var field = _toggleFields.First(field => ReferenceEquals(field.Check, sender));
        bool isOn = field.Check.IsChecked == true;
        if (field.Read(_instrument) == isOn)
        {
            return;
        }

        field.Write(_instrument, isOn);
        RefreshAndReportEdit(field.Check);
    }

    // Writes the surround pair the instrument's pan opens with.
    private void OnSurroundChanged(object? sender, SelectionChangedEventArgs e)
    {
        var surroundDropDown = InstrumentValues.SurroundDropDown;
        if (_isUpdating || _instrument is null || surroundDropDown.SelectedIndex < 0)
        {
            return;
        }

        var surround = SurroundField.PairAt(surroundDropDown.SelectedIndex);
        var lead = NotePan.Lane.LeadState(_instrument);
        if (lead.Surround == surround)
        {
            return;
        }

        NotePan.Lane.SetLeadState(_instrument, lead with { Surround = surround });
        RefreshAndReportEdit(surroundDropDown);
    }

    // A flat tempo map at the tempo where the playback position stands, and the key-on at its start, which the envelope's graph turns ticks into seconds from.
    private (TempoMap Tempos, int KeyOnTick) EnvelopeGraphTiming()
    {
        return (TempoMap.Flat(TempoMap.For(_song!).TempoAt(_playbackPositionTick)), OffsetRows.FirstOffsetTicks);
    }

    // Adds an envelope row behind the last one.
    private void OnAddEnvelopeRow(object? sender, RoutedEventArgs e)
    {
        EnvelopeRowsEditor.AddRow();
    }

    // Adds a volume ramp behind the chain.
    private void OnAddVolumeRow(object? sender, RoutedEventArgs e)
    {
        VolumeRowsEditor.AddRow();
    }

    // Adds a pan ramp behind the chain.
    private void OnAddPanRow(object? sender, RoutedEventArgs e)
    {
        PanRowsEditor.AddRow();
    }

    // Adds a pitch bend behind the chain.
    private void OnAddBend(object? sender, RoutedEventArgs e)
    {
        BendEditor.AddRow();
    }

    // Adds a vibrato stretch behind the chain.
    private void OnAddVibrato(object? sender, RoutedEventArgs e)
    {
        VibratoEditor.AddRow();
    }

    // Adds a tremolo stretch behind the chain.
    private void OnAddTremolo(object? sender, RoutedEventArgs e)
    {
        TremoloEditor.AddRow();
    }

    // Writes the measurement into its hidden readout.
    private void ShowMeasurement()
    {
        MeasuredText.Text = _measurement.IsPitched
            ? $"Measured {_measurement.FrequencyHz:F1} Hz (confidence {_measurement.Confidence:F2})"
            : $"Unpitched: confidence {_measurement.Confidence:F2}, nothing measured to tune to";
    }

    // Measures the instrument's sample again and takes the tuning it asks for.
    private void OnMeasureClick(object? sender, RoutedEventArgs e)
    {
        if (_song is null || _instrument is null || _song.SampleFor(_instrument) is not { } sample)
        {
            return;
        }

        _measurement = sample.MeasuredPitch();
        _instrument.TuningMultiplier = _measurement.TuningMultiplier;

        RefreshAndReportEdit(MeasureButton);
    }

    // Puts the instrument back on the tuning its sample measured, for a right-click on one of the tuning's controls.
    private void ResetTuningToSample(Control source)
    {
        if (_song is null || _instrument is null || _song.SampleFor(_instrument) is not { } sample)
        {
            return;
        }

        SetTuning(sample.C4Tuning, source);
    }

    // The interval a tuning multiplier plays the sample at, in cents.
    private static double CentsOfTuning(double multiplier)
    {
        return SnesPitch.SemitonesForFrequencyRatio(multiplier) * SnesPitch.CentsPerSemitone;
    }

    // The tuning multiplier an interval in cents makes, held to the range the instrument's bytes hold.
    private static double TuningAtCents(double cents)
    {
        return HeldToTuningRange(SnesPitch.FrequencyRatioForSemitones(cents / SnesPitch.CentsPerSemitone));
    }

    // A tuning multiplier held to the range the instrument's bytes hold.
    private static double HeldToTuningRange(double multiplier)
    {
        return Math.Clamp(multiplier, MinTuning, Instrument.MaxTuningMultiplier);
    }

    // Hangs one of the three tuning fields on the tuning: it applies as typed, steps by its own interval, goes back to the sample's tuning on a
    // right-click and puts the tidy spelling back on Enter and on losing the focus.
    private void HookTuningField(TextBox box, int stepCents)
    {
        box.TextChanged += (_, _) => ApplyTuningFields(box);
        box.LostFocus += (_, _) => Refresh();
        FieldResets.OnReset(box, () => ResetTuningToSample(box));
        FieldSteps.OnStepsAndEnter(box, step => StepTuning(box, stepCents, step), Refresh);
    }

    // Writes the tuning the instrument sounds with into the rail and the three fields, leaving alone whichever of them the edit is coming from.
    private void ShowTuning(Instrument instrument)
    {
        int cents = (int)Math.Round(CentsOfTuning(HeldToTuningRange(SoundingTuningOf(instrument))));
        int sign = cents < 0 ? -1 : 1;
        int magnitude = Math.Abs(cents);

        WriteTuningField(OctaveBox, sign * (magnitude / SnesPitch.CentsPerOctave));
        WriteTuningField(SemitoneBox, sign * (magnitude % SnesPitch.CentsPerOctave / SnesPitch.CentsPerSemitone));
        WriteTuningField(CentsBox, sign * (magnitude % SnesPitch.CentsPerSemitone));

        if (!ReferenceEquals(TuningSlider, _controlBeingEdited))
        {
            TuningSlider.Value = SliderField.HeldToRail(TuningSlider, cents / (double)SnesPitch.CentsPerSemitone);
        }
    }

    // Writes one of the three tuning fields, unless one of them is being typed into.
    private void WriteTuningField(TextBox box, int value)
    {
        if (_controlBeingEdited is TextBox)
        {
            return;
        }

        FieldInput.WriteChangedText(box, FieldInput.TextOf(value));
    }

    // Takes the three tuning fields together as one interval.
    private void ApplyTuningFields(TextBox source)
    {
        if (_isUpdating || _instrument is null)
        {
            return;
        }

        int cents = ReadTuningField(OctaveBox) * SnesPitch.CentsPerOctave + ReadTuningField(SemitoneBox) * SnesPitch.CentsPerSemitone + ReadTuningField(CentsBox);
        SetTuning(TuningAtCents(cents), source, source);
    }

    // One tuning field's number, or nothing where it will not parse.
    private static int ReadTuningField(TextBox box)
    {
        return FieldInput.TryReadSigned(box.Text, TuningFieldRange, out int value) ? value : 0;
    }

    // Moves the tuning by a number of one field's intervals, or by one count of the stored value where the interval is finer than that.
    private void StepTuning(TextBox source, int stepCents, int step)
    {
        if (_instrument is null)
        {
            return;
        }

        double multiplier = SoundingTuningOf(_instrument);
        int steppedCounts = SnesPitch.CountSteppedByCents(multiplier * Instrument.TuningLowStepsPerUnit, CentsOfTuning(HeldToTuningRange(multiplier)), step, stepCents);
        SetTuning(HeldToTuningRange(steppedCounts / Instrument.TuningLowStepsPerUnit), source);
    }

    // Takes the rail's semitones as the tuning.
    private void OnTuningSliderChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdating || _instrument is null)
        {
            return;
        }

        SetTuning(TuningAtCents(TuningSlider.Value * SnesPitch.CentsPerSemitone), TuningSlider, TuningSlider);
    }

    // Makes the instrument sound with a tuning where it differs from the one it sounds with, held at its sample's original rate, leaving one control alone in
    // the refresh that follows.
    private void SetTuning(double soundingMultiplier, Control source, Control? controlToLeaveAlone = null)
    {
        if (_instrument is null || Math.Abs(soundingMultiplier - SoundingTuningOf(_instrument)) < TuningMatchTolerance)
        {
            return;
        }

        _instrument.TuningMultiplier = soundingMultiplier / RateFractionOf(_instrument);

        FlagGuard.RunWithControlMarked(ref _controlBeingEdited, controlToLeaveAlone, () => RefreshAndReportEdit(source));
    }

    // The tuning an instrument sounds with, worked out for the rate its sample plays at.
    private double SoundingTuningOf(Instrument instrument)
    {
        return _song?.SoundingTuningOf(instrument) ?? instrument.TuningMultiplier;
    }

    // How much of its original rate an instrument's sample is at.
    private double RateFractionOf(Instrument instrument)
    {
        return _song?.RateFractionOf(instrument) ?? Sample.OriginalRateFraction;
    }

    // One of the value rows: its parts, and how it reads and writes the instrument.
    private sealed record DefaultField(NumericRowParts Row, Func<Instrument, int> Read, Action<Instrument, int> Write);

    // One of the on-or-off rows: its check box, and how it reads and writes the instrument.
    private sealed record ToggleField(CheckBox Check, Func<Instrument, bool> Read, Action<Instrument, bool> Write);

    // What the instrument as the subject of any of the panel's chains shares: how far a chain may run, that it has no length of its own, and the instrument itself.
    private abstract class InstrumentChainHost(InstrumentPanel panel)
    {
        public int CapacityTicks
        {
            get
            {
                return MaxChainTicks;
            }
        }

        public bool HasOwnLength
        {
            get
            {
                return false;
            }
        }

        // The instrument showing, or none.
        protected Instrument? ShownInstrument
        {
            get
            {
                return panel._instrument;
            }
        }

        // Writes a chain onto the instrument showing, answering whether there was one to write it onto.
        protected bool WriteToInstrument<T>(ImmutableArray<T> chain, Action<Instrument, ImmutableArray<T>> writeChain)
        {
            if (panel._instrument is not { } instrument)
            {
                return false;
            }

            writeChain(instrument, chain);
            return true;
        }
    }

    // The instrument as the subject of its volume or pan rows, whose first row goes back to a new instrument's.
    private sealed class FadeHost<TState>(InstrumentPanel panel, NoteFadeLane<TState> lane)
        : ListHost<NoteChainRegion<TState>>(panel, lane.InstrumentRows, lane.SetInstrumentRows), IFadeHost<TState>
        where TState : struct
    {
        public ImmutableArray<NoteChainRegion<TState>> InstrumentRows
        {
            get
            {
                return Chain;
            }
        }

        // The state a new instrument's first row holds.
        public TState BaseLeadState
        {
            get
            {
                return lane.LeadState(Instrument.NewInstrumentDefaults);
            }
        }
    }

    // The instrument as a pitch bend chain's subject.
    private sealed class BendHost(InstrumentPanel panel) : InstrumentChainHost(panel), IBendHost
    {
        // The instrument's pitch bends, or none where no instrument shows.
        public ImmutableArray<PitchBend> Chain
        {
            get
            {
                return ShownInstrument?.Bends ?? [];
            }
        }

        // The offsets a bend can hold with no note pitch to carry them from.
        public ValueRange OffsetRange(ImmutableArray<PitchBend> chain, int index)
        {
            return PitchBendRows.OffsetRangeAnywhere(chain, index);
        }

        // Writes the chain onto the instrument.
        public bool Write(ImmutableArray<PitchBend> chain)
        {
            return WriteToInstrument(chain, NoteDefaults.SetBends);
        }
    }

    // The instrument as the subject of a chain it holds entry for entry: its vibrato, its tremolo or its envelope rows.
    private class ListHost<T>(InstrumentPanel panel, Func<Instrument, ImmutableArray<T>> chainOf, Action<Instrument, ImmutableArray<T>> writeChain)
        : InstrumentChainHost(panel), IListHost<T>
        where T : struct
    {
        // The instrument's chain, or none where no instrument shows.
        public ImmutableArray<T> Chain
        {
            get
            {
                return ShownInstrument is { } instrument ? chainOf(instrument) : [];
            }
        }

        // Writes the chain onto the instrument.
        public bool Write(ImmutableArray<T> chain)
        {
            return WriteToInstrument(chain, writeChain);
        }
    }

    // The instrument as the subject of its envelope rows, whose last row runs on to the end of every note and whose base is a new instrument's.
    private sealed class EnvelopeHost(InstrumentPanel panel) : ListHost<EnvelopeRow>(panel, EnvelopeRows.OfInstrument, NoteDefaults.SetEnvelopes), IEnvelopeHost
    {
        // A new instrument's row in force a number of ticks after key-on.
        public EnvelopeState BaseEnvelopeAt(int offsetTicks)
        {
            return EnvelopeRows.StateAt(EnvelopeRows.OfInstrument(Instrument.NewInstrumentDefaults), offsetTicks);
        }
    }
}
