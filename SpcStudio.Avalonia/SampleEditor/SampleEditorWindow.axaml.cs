using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using SpcStudio.Audio;
using SpcStudio.Controls;
using SpcStudio.Model;

namespace SpcStudio;

// The sample editor: a drop-down of the song's samples, the waveform of the one chosen, its loop, the clipboard the editing keys and the Edit menu act
// on, the effects rack and a preview transport - every edit going out to the main window as intent rather than being written here.
public partial class SampleEditorWindow : StandaloneWindow
{
    private const double ScrollSlackPoints = 0.5;
    private const int ScrollSmallChangesPerViewport = 10;
    private const int NoSelectedIndex = -1;

    private readonly SampleEditQueue _sampleEdits;
    private Song? _song;
    private int? _selectedSampleId;
    private CopiedPoints? _copiedPoints;
    private bool _isUpdatingControls;
    private bool _isSyncingScroll;

    // Raised with a sample's id and whether it is to loop, as the Loop box is ticked.
    public event Action<int, bool>? LoopChanged;

    // Raised with a sample's id, the block its loop is to start at and the control the edit came from - or null for a key's - as the loop marker is
    // placed, dragged or sent to either end.
    public event Action<int, int, object?>? LoopBlockChanged;

    // Raised with a sample's id, the first point and the count of points to take out, the points to put in their place, and the view the editor is to be
    // left at - which covers a delete, a cut and a paste.
    public event Action<int, int, int, short[], SampleView>? PointsReplaced;

    // Raised with a sample's id, the first point and the end of the points to take out, the first point and the end of a run inside them to keep, and
    // the view the editor is to be left at, as Trim or Trim Silent Ends cuts.
    public event Action<int, int, int, int, int, SampleView>? PointsKept;

    // Raised with a sample's id, the first point to write over, the points to write there, and the view the editor is to be left at, as a fade changes
    // the points selected.
    public event Action<int, int, short[], SampleView>? PointsOverwritten;

    // Raised with a sample's id, the first point to add onto, the points to add there, and the view the editor is to be left at, as Paste Mixed mixes the
    // copied points into the sample.
    public event Action<int, int, short[], SampleView>? PointsMixedIn;

    // Raised with a sample's id, the loop to fit to whole blocks first or none, the point its loop is to start at, the points from the sample's start to
    // the loop's end, and the view the editor is to be left at, as Find Loop cuts the sample after the loop it found and sets it looping.
    public event Action<int, LoopFit?, int, short[], SampleView>? LoopFound;

    // Raised with a sample's id, its loop as one to fit to whole blocks, and the view the editor is to be left at, as Fit Loop To Blocks lowers the
    // sample's rate.
    public event Action<int, LoopFit, SampleView>? LoopFitted;

    // Raised by the effects rack with a sample's id, the effects it is to have and the control the edit came from.
    public event Action<int, SampleRack, object>? EffectsChanged;

    // Raised with a sample's id, the PITCH register the preview is to play it at - or null for the sample's own C4 - and the control the edit came from,
    // as the Pitch row is moved.
    public event Action<int, int?, object>? PreviewPitchChanged;

    // Raised with a sample's id, the rate in hertz it is to be converted to from its kept audio, and the control the edit came from, as the Rate row is
    // moved.
    public event Action<int, int, object>? RateChanged;

    // Raised by undo's shortcut, for the sample history to be stepped back.
    public event Action? UndoRequested;

    // Raised by redo's shortcuts, for the sample history to be stepped forward.
    public event Action? RedoRequested;

    // Raised by the New Sample button or menu item, for an empty sample to be added to the song.
    public event Action? CreateRequested;

    // Raised by the Import button or menu item, for files to be read into the song as samples.
    public event Action? ImportRequested;

    // Raised with a sample's id, for a second sample holding the same audio to be added to the song.
    public event Action<int>? DuplicateRequested;

    // Raised with the chord the Create Chord Sample window made, for it to be added to the song.
    public event Action<Sample>? ChordSampleCreated;

    // Raised with a sample's id, for it to be taken out of the song with every instrument built on it and every note those play.
    public event Action<int>? DeleteRequested;

    // Raised with a sample's id and the name its rename finished with, for the main window to give it or refuse it.
    public event Action<int, string>? RenameRequested;

    // Builds the window over the queue its edits are made through, ties the waveform, its ruler, its markers, its scrollbar and the preview's playhead to
    // each other, and puts a play button on the drop-down's rows.
    public SampleEditorWindow(SampleEditQueue sampleEdits)
    {
        _sampleEdits = sampleEdits;
        InitializeComponent();
        Transport.UseTooltips(Tooltips.PlayOrLoopSample, Tooltips.PauseSample, Tooltips.StopSample);
        _heldShiftKey = new HeldShiftKey(this, ShowShiftHeld);
        WireRateRow();
        WirePitchRow();
        WireRack();

        Waveform.LoopBlockDragged += block => AskForLoopBlock(block, Waveform);
        Waveform.EditRequested += OnEditRequested;
        Waveform.MarkerMoved += OnMarkerMoved;
        Waveform.SelectionChanged += OnSelectionChanged;
        Waveform.ViewChanged += () => Ruler.SetView(Waveform.LeftmostPoint, Waveform.PixelsPerPoint);
        Waveform.ViewChanged += SyncScrollToView;
        Ruler.Pressed += Waveform.TakeRulerPress;
        WaveMarkers.ShowOver(Waveform, Ruler, Waveform);
        WaveScroll.Scroll += OnWaveScrolled;

        _playheadTimer.Tick += OnPlayheadTick;
        DropDownAuditions.Hook(SampleBox, Tooltips.PlaySample, AuditionSampleRow, StopPreview);
        SamplePick.NameCommitted += OnSampleNameCommitted;
        Closed += (_, _) => StopPreview();
    }

    // Where the editor is looking: the sample its drop-down is on and the points its waveform has selected.
    public SampleView CurrentView
    {
        get
        {
            return new SampleView(_selectedSampleId, Waveform.SelectionStart, Waveform.SelectionEnd);
        }
    }

    // The sample the drop-down is on, or null where none is.
    private Sample? CurrentSample
    {
        get
        {
            return _selectedSampleId is { } id ? _song?.SampleById(id) : null;
        }
    }

    // Points the window at a song and rebuilds the drop-down from the samples it offers, selecting the preferred sample where one is given and offered.
    public void ShowSamples(Song song, int? preferredSampleId = null)
    {
        if (!ReferenceEquals(song, _song))
        {
            StopPreview();
        }

        _song = song;
        var rows = SampleRows.Of(song, []);
        if (preferredSampleId is { } preferred && SampleRows.IndexOf(rows, preferred) >= 0)
        {
            _selectedSampleId = preferred;
        }

        if (_selectedSampleId is { } id && SampleRows.IndexOf(rows, id) < 0)
        {
            _selectedSampleId = null;
        }

        _selectedSampleId ??= rows.OfType<DropDownRow>().FirstOrDefault()?.Id;

        FlagGuard.RunWithFlagSet(ref _isUpdatingControls, () =>
        {
            SampleBox.ItemsSource = rows;
            SampleBox.SelectedIndex = _selectedSampleId is { } chosen ? SampleRows.IndexOf(rows, chosen) : NoSelectedIndex;
        });

        ShowSelectedSample();
    }

    // Marks the drop-down's rows by whether any note uses their sample.
    public void MarkUnusedRows()
    {
        UsageMarks.MarkSampleRows(_song, SampleBox.Items);
    }

    // Re-reads the selected sample out of the song: its waveform, its loop, its size, its rate, its pitch, its rack and which controls have something to act
    // on.
    public void ShowSelectedSample()
    {
        var sample = CurrentSample;
        _askedRateHz = null;
        _hasPitchRowAsked = false;
        _hasRackAsked = false;
        if (_previewSampleId is { } previewed && _selectedSampleId != previewed)
        {
            StopPreview();
        }

        bool hasSample = sample is not null;
        bool isSmwSample = sample is not null && SmwInstruments.IsSampleId(sample.Id);
        bool isEditable = hasSample && !isSmwSample;
        Waveform.IsSampleReadOnly = isSmwSample;

        bool hasSong = _song is not null;
        ExportSampleButton.IsVisible = hasSample;
        RenameButton.IsVisible = hasSample;
        RenameButton.IsEnabled = isEditable;
        if (!isEditable)
        {
            SamplePick.EndRename();
        }

        ExportAllButton.IsVisible = _song is { } song && SampleExports.CanExportAll(song);
        CreateButton.IsEnabled = hasSong;
        ImportButton.IsEnabled = hasSong;
        DuplicateButton.IsVisible = hasSample;
        DeleteButton.IsVisible = hasSample;
        DeleteButton.IsEnabled = isEditable;
        ProcessMenu.IsEnabled = !isSmwSample;
        RateAndPitchRows.IsVisible = isEditable;
        RackSection.IsVisible = isEditable;
        Transport.IsEnabled = hasSample && _previewMixer is not null;
        ShowRate(sample);
        ShowPitch(sample);
        SyncRack(sample);

        FlagGuard.RunWithFlagSet(ref _isUpdatingControls, () =>
        {
            LoopBox.IsChecked = sample?.Decoded.DoesLoop ?? false;
            LoopBox.IsVisible = hasSample;
            LoopBox.IsEnabled = isEditable;
        });

        ShowSampleAudio(sample);
    }

    // Re-reads the selected sample out of the song once an edit has been made: all of it, or, where an edit of it is still waiting, only its audio and
    // its Pitch row and its effects where those are not being moved, so a control still being moved keeps the value it was moved to.
    public void ShowEditedSample()
    {
        if (CurrentSample is { } sample && _sampleEdits.IsWaitingToEdit(sample.Id))
        {
            ShowSampleAudio(sample);
            if (!_hasPitchRowAsked)
            {
                ShowPitch(sample);
            }

            if (!_hasRackAsked)
            {
                SyncRack(sample);
            }

            return;
        }

        ShowSelectedSample();
    }

    // Draws a sample's audio, or none: its waveform, its size, the ruler's time at the rate the preview plays it, and the preview re-aimed at it, at the
    // same moment of it where it is the sample drawn before at another rate.
    private void ShowSampleAudio(Sample? sample)
    {
        var sampleDrawnBefore = Waveform.Sample;
        Waveform.Sample = sample;
        ShowSize(sample);
        ShowRulerTime(sample);
        RetargetPreviewAtRate(Sample.RateChangeBetween(sampleDrawnBefore, sample));
    }

    // Puts the editor back where a restored state was seen from, after an undo or a redo has put its samples back into the song.
    public void ShowRestoredState(Song song, SampleView view)
    {
        StopPreview();
        ShowSamples(song, view.SampleId);
        Waveform.Select(view.SelectionStart, view.SelectionEnd);
    }

    // Shows the sample the drop-down was moved to.
    private void OnSampleSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingControls)
        {
            return;
        }

        _selectedSampleId = (SampleBox.SelectedItem as DropDownRow)?.Id;
        ShowSelectedSample();
    }

    // Asks for the sample showing to loop or not, as the Loop box says.
    private void OnLoopChanged(object? sender, RoutedEventArgs e)
    {
        if (_isUpdatingControls || _selectedSampleId is not { } id)
        {
            return;
        }

        LoopChanged?.Invoke(id, LoopBox.IsChecked == true);
    }

    // Asks for the sample showing's loop to start at a block, the edit coming from a control or, where that is null, from a key.
    private void AskForLoopBlock(int block, object? undoSource)
    {
        if (_selectedSampleId is { } id)
        {
            LoopBlockChanged?.Invoke(id, block, undoSource);
        }
    }

    // Asks for the sample showing's loop to start at its first block or its last, where it has points to loop.
    private void MoveLoopMarkerToEdge(ViewEdge edge)
    {
        if (Waveform.LoopBlockAt(edge) is int block)
        {
            AskForLoopBlock(block, null);
        }
    }

    // Carries out one of the editing keys or Edit menu items where it has something to act on, once every edit asked for before it has been made: the
    // clipboard here, and the edit to the song as intent.
    private async void OnEditRequested(SampleEditKind kind)
    {
        await _sampleEdits.WhenEmpty();
        if (!CanApply(kind) || _selectedSampleId is not { } id || CurrentSample is not { } sample)
        {
            return;
        }

        int start = Waveform.SelectionStart;
        int count = Waveform.SelectedPointCount;
        switch (kind)
        {
            case SampleEditKind.Copy:
                _copiedPoints = sample.CopiedPointsInRange(start, count);
                break;

            case SampleEditKind.Cut:
                _copiedPoints = sample.CopiedPointsInRange(start, count);
                Splice(id, start, count, [], start, start);
                break;

            case SampleEditKind.Delete:
                Splice(id, start, count, [], start, start);
                break;

            case SampleEditKind.Paste when CopiedPointsAtRateOf(sample) is { } points:
                // Where the paste goes in and how many points it replaces: all of an empty sample's silence, or none at the marker.
                var (pasteStart, replacedCount) = sample.IsEmpty ? (0, Waveform.PointCount) : (Waveform.MarkerPoint, 0);
                Splice(id, pasteStart, replacedCount, points, pasteStart, pasteStart + points.Length);
                break;

            case SampleEditKind.PasteMixed when CopiedPointsAtRateOf(sample) is { } points:
                int mixStart = Waveform.MarkerPoint;
                EditPoints(after => PointsMixedIn?.Invoke(id, mixStart, points, after), new SampleView(id, mixStart, mixStart + points.Length));
                break;

            case SampleEditKind.Trim:
                KeepRun(id, 0, Waveform.PointCount, start, start + count);
                break;

            case SampleEditKind.FadeIn:
                Overwrite(id, start, PointFades.FadedIn(sample.PointsInRange(start, count)));
                break;

            case SampleEditKind.FadeOut:
                Overwrite(id, start, PointFades.FadedOut(sample.PointsInRange(start, count)));
                break;

            case SampleEditKind.TrimSilentEnds:
                var (trimmedStart, trimmedEnd) = SelectionOrWholeSample();
                var (soundingStart, soundingEnd) = sample.SoundingRunWithin(trimmedStart, trimmedEnd);
                KeepRun(id, trimmedStart, trimmedEnd, soundingStart, soundingEnd);
                break;

            case SampleEditKind.Reverse:
                Overwrite(id, start, sample.PointsInRange(start, count).Reverse().ToArray());
                break;

            case SampleEditKind.SelectLoop:
                Waveform.Select(sample.Decoded.LoopStartPoint, Waveform.PointCount);
                break;

            case SampleEditKind.FindLoop:
                FindLoop(id, sample, start, count);
                break;

            case SampleEditKind.FitLoopToBlocks when OwnLoopFit(sample) is { } fit:
                FitLoop(id, sample, fit);
                break;

            case SampleEditKind.FlattenVolume:
                Overwrite(id, start, FlattenedVolume(sample.PointsInRange(start, count), sample.PitchAtRate));
                break;

            case SampleEditKind.CrossfadeLoop:
                int loopStart = sample.Decoded.LoopStartPoint;
                Overwrite(id, loopStart, CrossfadedLoop(sample.PointsInRange(loopStart, Waveform.PointCount - loopStart), sample.PointsInRange(0, loopStart)));
                break;

            case SampleEditKind.SmoothCycles:
                Overwrite(id, start, SmoothedCycles(sample.PointsInRange(start, count), sample.PitchAtRate));
                break;
        }
    }

    // Finds the loop in a run of a sample's points that Loop Options asks for, fits it to whole blocks where Fit Loop To Blocks is ticked and it can be
    // fitted, runs Flatten Volume, Crossfade Loop and Smooth Cycles over its points where Loop Options has them ticked and they can run, and asks for the
    // sample to be cut off after it and looping, leaving the loop selected and the marker at the sample's start.
    private void FindLoop(int sampleId, Sample sample, int firstPoint, int pointCount)
    {
        var playedPoints = sample.Decoded.Points;
        int endPoint = firstPoint + pointCount;
        int startStepPoints = LoopOptions.FindLoopStartStepPoints;
        var found = LoopOptions.IsShortestLoopPreferred ? LoopFinder.FindShortestAboutAsSmooth(playedPoints, firstPoint, endPoint, startStepPoints)
            : LoopFinder.FindSmoothest(playedPoints, firstPoint, endPoint, startStepPoints);

        if (LoopOptions.DoesFindLoopFitToBlocks && sample.CanFitLoop(found.Fit, LoopOptions.FitLoopMaxRateLoss.Fraction))
        {
            var fitted = sample.FittedLoopOf(found.Fit);
            RunFindLoopSteps(sampleId, fitted.Points, fitted.LoopStartPoint, sample.PitchAt(fitted.RateHz), found.Fit);
            return;
        }

        var (pointsToLoopEnd, loopStart) = sample.LoopOnBlockStartOf(found.StartPoint, found.EndPoint);
        RunFindLoopSteps(sampleId, pointsToLoopEnd, loopStart, sample.PitchAtRate, null);
    }

    // Runs Flatten Volume, Crossfade Loop and Smooth Cycles over the points of a loop, counting the cycles by the pitch given, where Loop Options has them
    // ticked and they can run, blends the points just before the loop into its end, and asks for the sample, fitted first where a fit is given, to be cut
    // off after the loop and looping, leaving the loop selected and the marker at the sample's start; the points run from the sample's start to the loop's
    // end, and none of them has been encoded since the loop was found.
    private void RunFindLoopSteps(int sampleId, short[] pointsToLoopEnd, int loopStart, BrrPitch.Measurement pitch, LoopFit? fit)
    {
        var loopPoints = pointsToLoopEnd[loopStart..];
        if (LoopOptions.DoesFindLoopFlattenVolume)
        {
            loopPoints = FlattenedVolume(loopPoints, pitch);
        }

        if (LoopOptions.DoesFindLoopCrossfade && LoopCrossfade.CanCrossfade(loopStart))
        {
            loopPoints = CrossfadedLoop(loopPoints, pointsToLoopEnd[..loopStart]);
        }

        if (LoopOptions.DoesFindLoopSmoothCycles && CanSmoothCycles(loopPoints.Length, pitch))
        {
            loopPoints = SmoothedCycles(loopPoints, pitch);
        }

        short[] pointsFound = [.. LoopCrossfade.LedIn(pointsToLoopEnd[..loopStart], loopPoints), .. loopPoints];
        EditPoints(after => LoopFound?.Invoke(sampleId, fit, loopStart, pointsFound, after), new SampleView(sampleId, loopStart, pointsToLoopEnd.Length));
        Waveform.MoveMarkerTo(0);
    }

    // A sample's own loop as one to fit to whole blocks, where it can be measured and fitted within the Loop Options' rate loss, or none.
    private static LoopFit? OwnLoopFit(Sample sample)
    {
        var decoded = sample.Decoded;
        if (!decoded.DoesLoop || LoopFinder.RepeatPointsOf(decoded.Points, decoded.LoopStartPoint, decoded.Points.Length) is not double repeatPoints)
        {
            return null;
        }

        var fit = new LoopFit(decoded.LoopStartPoint, repeatPoints);
        return sample.CanFitLoop(fit, LoopOptions.FitLoopMaxRateLoss.Fraction) ? fit : null;
    }

    // Asks for a sample's loop to be fitted to whole blocks, leaving the fitted loop selected.
    private void FitLoop(int sampleId, Sample sample, LoopFit fit)
    {
        var fitted = sample.FittedLoopOf(fit);
        EditPoints(after => LoopFitted?.Invoke(sampleId, fit, after), new SampleView(sampleId, fitted.LoopStartPoint, fitted.Points.Length));
    }

    // A run of a sample's points with its steady fade taken out, measured over whole cycles of the pitch given, as Flatten Volume makes it.
    private static short[] FlattenedVolume(short[] points, BrrPitch.Measurement pitch)
    {
        return VolumeFlattening.Flattened(points, pitch);
    }

    // A loop's points with their end blended into the points before the loop's start, over the Loop Options' length, as Crossfade Loop makes it.
    private static short[] CrossfadedLoop(short[] loopPoints, short[] pointsBeforeLoop)
    {
        return LoopCrossfade.Crossfaded(loopPoints, pointsBeforeLoop, LoopOptions.CrossfadeLength.Fraction);
    }

    // A run of a sample's points with its cycles of the pitch given evened out at the Loop Options' effect, as Smooth Cycles makes it.
    private static short[] SmoothedCycles(short[] points, BrrPitch.Measurement pitch)
    {
        return CycleSmoothing.Smoothed(points, pitch, LoopOptions.SmoothCyclesEffect.Fraction);
    }

    // Whether a run of a sample's points holds enough cycles of the pitch given for Smooth Cycles to even out.
    private static bool CanSmoothCycles(int pointCount, BrrPitch.Measurement pitch)
    {
        return CycleSmoothing.CanSmooth(pointCount, pitch);
    }

    // Opens the Loop Options box, applying each setting as it is chosen.
    private async void OnLoopOptionsClick(object? sender, RoutedEventArgs e)
    {
        var loopOptionsWindow = new LoopOptionsWindow(LoopOptions.IsShortestLoopPreferred, LoopOptions.DoesFindLoopTryStartsBetweenBlocks, LoopOptions.DoesFindLoopFitToBlocks,
            LoopOptions.DoesFindLoopFlattenVolume, LoopOptions.DoesFindLoopCrossfade, LoopOptions.DoesFindLoopSmoothCycles, LoopOptions.SmoothCyclesEffect,
            LoopOptions.CrossfadeLength, LoopOptions.FitLoopMaxRateLoss);
        loopOptionsWindow.ShortestLoopPreferredChanged += isPreferred => LoopOptions.IsShortestLoopPreferred = isPreferred;
        loopOptionsWindow.FindLoopStartsBetweenBlocksChanged += doesTry => LoopOptions.DoesFindLoopTryStartsBetweenBlocks = doesTry;
        loopOptionsWindow.FindLoopFitToBlocksChanged += doesFit => LoopOptions.DoesFindLoopFitToBlocks = doesFit;
        loopOptionsWindow.FindLoopFlattenVolumeChanged += doesFlatten => LoopOptions.DoesFindLoopFlattenVolume = doesFlatten;
        loopOptionsWindow.FindLoopCrossfadeChanged += doesCrossfade => LoopOptions.DoesFindLoopCrossfade = doesCrossfade;
        loopOptionsWindow.FindLoopSmoothCyclesChanged += doesSmooth => LoopOptions.DoesFindLoopSmoothCycles = doesSmooth;
        loopOptionsWindow.SmoothCyclesEffectChanged += percent => LoopOptions.SmoothCyclesEffect.Percentage = percent;
        loopOptionsWindow.CrossfadeLengthChanged += percent => LoopOptions.CrossfadeLength.Percentage = percent;
        loopOptionsWindow.FitLoopMaxRateLossChanged += percent => LoopOptions.FitLoopMaxRateLoss.Percentage = percent;
        await loopOptionsWindow.ShowDialog(this);
    }

    // Whether an edit has something to act on: a sample showing, which must not be read-only but for a copy or Select Loop, and points copied for a paste,
    // a loop for Select Loop, a loop with points before it for Crossfade Loop, a loop that can be measured and fitted for Fit Loop To Blocks, points
    // selected for the rest, for a trim a selection short of the whole sample, for a fade or Flatten Volume enough points selected to run between their two
    // ends, for Trim Silent Ends a silent point at either end of the selection or of the whole sample, for Find Loop room selected for a loop of one block
    // between two of the starts it tries, and for Smooth Cycles two of a pitched sound's cycles selected.
    private bool CanApply(SampleEditKind kind)
    {
        if (CurrentSample is not { } sample)
        {
            return false;
        }

        if (Waveform.IsSampleReadOnly && kind is not (SampleEditKind.Copy or SampleEditKind.SelectLoop))
        {
            return false;
        }

        int selectedCount = Waveform.SelectedPointCount;
        return kind switch
        {
            SampleEditKind.Paste or SampleEditKind.PasteMixed => _copiedPoints is { Points.Length: > 0 },
            SampleEditKind.Trim => selectedCount > 0 && selectedCount < Waveform.PointCount,
            SampleEditKind.FadeIn or SampleEditKind.FadeOut or SampleEditKind.FlattenVolume => selectedCount >= PointFades.MinimumPointCount,
            SampleEditKind.TrimSilentEnds => SelectionOrWholeSample() is var (start, end) && sample.SoundingRunWithin(start, end) != (start, end),
            SampleEditKind.SelectLoop => sample.Decoded.DoesLoop,
            SampleEditKind.CrossfadeLoop => sample.Decoded.DoesLoop && LoopCrossfade.CanCrossfade(sample.Decoded.LoopStartPoint),
            SampleEditKind.FindLoop => LoopFinder.CanFindIn(Waveform.SelectionStart, Waveform.SelectionEnd, LoopOptions.FindLoopStartStepPoints),
            SampleEditKind.FitLoopToBlocks => OwnLoopFit(sample) is not null,
            SampleEditKind.SmoothCycles => CanSmoothCycles(selectedCount, sample.PitchAtRate),
            _ => selectedCount > 0,
        };
    }

    // The copied points converted to a sample's rate, or none where nothing has been copied.
    private short[]? CopiedPointsAtRateOf(Sample sample)
    {
        return _copiedPoints?.PointsAt(sample.RateHz);
    }

    // Asks for a run of a sample's points to be replaced, and then selects the points the replacement leaves selected.
    private void Splice(int sampleId, int firstPoint, int pointCount, short[] replacement, int selectionStart, int selectionEnd)
    {
        EditPoints(after => PointsReplaced?.Invoke(sampleId, firstPoint, pointCount, replacement, after), new SampleView(sampleId, selectionStart, selectionEnd));
    }

    // The first point and the end of the selection, or of the whole sample where nothing is selected.
    private (int Start, int End) SelectionOrWholeSample()
    {
        return Waveform.HasSelection ? (Waveform.SelectionStart, Waveform.SelectionEnd) : (0, Waveform.PointCount);
    }

    // Asks for a sample's points between a start and an end to be taken out but for a run inside them, leaving what is left of them selected.
    private void KeepRun(int sampleId, int start, int end, int keptStart, int keptEnd)
    {
        EditPoints(after => PointsKept?.Invoke(sampleId, start, end, keptStart, keptEnd, after), new SampleView(sampleId, start, start + keptEnd - keptStart));
    }

    // Asks for a run of a sample's points to be written over by as many others, leaving those points selected.
    private void Overwrite(int sampleId, int firstPoint, short[] replacement)
    {
        EditPoints(after => PointsOverwritten?.Invoke(sampleId, firstPoint, replacement, after), new SampleView(sampleId, firstPoint, firstPoint + replacement.Length));
    }

    // Stops the preview, raises an edit of a sample's points with the view the editor is to be left at, and once every edit asked for has been made selects
    // the points that view holds, where the editor is still on that sample.
    private async void EditPoints(Action<SampleView> raiseEdit, SampleView after)
    {
        StopPreview();
        raiseEdit(after);
        await _sampleEdits.WhenEmpty();
        if (_selectedSampleId != after.SampleId)
        {
            return;
        }

        Waveform.Select(after.SelectionStart, after.SelectionEnd);
    }

    // Grays out each item of the opened Edit or Process menu that has nothing to act on, as the menu opens.
    private void OnSampleEditMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menu)
        {
            return;
        }

        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (item.Tag is SampleEditKind kind)
            {
                item.IsEnabled = CanApply(kind);
            }
        }
    }

    // Carries out the edit an item of the Edit or Process menu names.
    private void OnEditItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: SampleEditKind kind })
        {
            OnEditRequested(kind);
        }
    }

    // Scrolls the waveform to where its scrollbar was dragged.
    private void OnWaveScrolled(object? sender, ScrollEventArgs e)
    {
        if (!_isSyncingScroll)
        {
            Waveform.LeftmostPoint = WaveScroll.Value;
        }
    }

    // Puts the scrollbar where the waveform's view is, and hides it where the whole sample is in view.
    private void SyncScrollToView()
    {
        FlagGuard.RunWithFlagSet(ref _isSyncingScroll, () =>
        {
            double viewportPoints = Waveform.ViewportPoints;
            WaveScroll.Maximum = Waveform.FurthestLeftmostPoint;
            WaveScroll.ViewportSize = viewportPoints;
            WaveScroll.LargeChange = viewportPoints;
            WaveScroll.SmallChange = viewportPoints / ScrollSmallChangesPerViewport;
            WaveScroll.Value = Math.Clamp(Waveform.LeftmostPoint, 0, WaveScroll.Maximum);
            WaveScroll.IsVisible = WaveScroll.Maximum > ScrollSlackPoints;
        });
    }

    // Writes how many bytes of ARAM the sample showing costs, at the rate the Rate row last asked for where it has asked for one since the sample was last
    // shown whole.
    private void ShowSize(Sample? sample)
    {
        SampleSizeText.Text = sample is null ? string.Empty : CountedNoun.Of(_askedRateHz is int rateHz ? sample.AramBytesAt(rateHz) : sample.AramBytes, CountedNoun.ByteNoun);
    }

    // The window's own keys besides Escape, ahead of whatever has the focus: undo's and redo's shortcuts step the sample history, select all's selects
    // everything, the play key plays or pauses and its selection shortcut plays the selection, either looping with Shift, and Home and End pan the
    // waveform to either end - resizing the selection there with Shift and moving the loop marker there with Ctrl - where no field has the focus.
    protected override void HandleWindowKeyDown(KeyEventArgs e)
    {
        var focused = FocusManager?.GetFocusedElement();
        if (WindowKeys.StepHistory(e, focused, () => UndoRequested?.Invoke(), () => RedoRequested?.Invoke()))
        {
            return;
        }

        if (Shortcuts.IsPressed(e, Shortcuts.SelectAll) && !WindowKeys.IsOnSelectableText(focused))
        {
            Waveform.SelectAll();
            e.Handled = true;
            return;
        }

        if (Shortcuts.IsPressed(e, Shortcuts.PlaySelection, Shortcuts.PlaySelectionLooping))
        {
            PlaySelection(isLooping: Shortcuts.IsPressed(e, Shortcuts.PlaySelectionLooping));
            e.Handled = true;
            return;
        }

        if (Shortcuts.IsPressed(e, Shortcuts.Play, Shortcuts.PlayLooping))
        {
            TogglePreview(isLooping: Shortcuts.IsPressed(e, Shortcuts.PlayLooping));
            e.Handled = true;
            return;
        }

        if (focused is not TextBox && ViewEdges.TryPanToEdgeOfKey(Waveform, e, EdgeActionOf))
        {
            e.Handled = true;
        }
    }

    // What Home or End does at its edge with modifiers held: Shift resizes the selection there and Ctrl moves the loop marker there, where the sample is
    // not read-only.
    private Action<ViewEdge>? EdgeActionOf(KeyModifiers modifiers)
    {
        return modifiers switch
        {
            KeyModifiers.Shift => edge => Waveform.ResizeSelectionTo(ViewEdges.ValueAt(edge, 0, Waveform.PointCount)),
            KeyModifiers.Control when !Waveform.IsSampleReadOnly => MoveLoopMarkerToEdge,
            _ => null,
        };
    }
}
