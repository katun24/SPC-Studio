using Avalonia.Controls;
using Avalonia.Threading;
using SpcStudio.Audio;
using SpcStudio.Controls;
using SpcStudio.Model;

namespace SpcStudio;

// The song's samples and instruments: the Sample Editor's edits and its undo, importing samples, the instrument panel's buttons and edits, and the two instrument drop-downs.
public partial class MainWindow
{
    private const string SampleImportFileFilter = "Samples (*.brr;*.wav)|*.brr;*.wav" + DialogFilters.Separator + DialogFilters.BrrSample + DialogFilters.Separator + DialogFilters.WavAudio
        + DialogFilters.Separator + DialogFilters.AllFiles;

    private const string SampleImportStage = "samples imported";
    private const int NoInstrumentIndex = -1;

    private bool _isWritingInstrumentDropDowns;

    // The instrument new notes are given, looked up in the song by its id each time it is asked for.
    private Instrument? CurrentInstrument
    {
        get
        {
            return _currentInstrumentId is int id ? _song.InstrumentById(id) : null;
        }
    }

    // Where the Sample Editor is looking, or nowhere while it is closed.
    private SampleView SampleEditorView
    {
        get
        {
            return _sampleEditor?.CurrentView ?? default;
        }
    }

    // Adds an empty sample to the song and puts the Sample Editor on it.
    private void OnSampleCreateRequested()
    {
        ShowNewSample(_song.CreateSample());
    }

    // Adds a copy of a sample to the song and puts the Sample Editor on it.
    private void OnSampleDuplicateRequested(int sampleId)
    {
        if (_song.SampleById(sampleId) is null)
        {
            return;
        }

        ShowNewSample(_song.DuplicateSample(sampleId));
    }

    // Asks before deleting a sample, every instrument built on it and every note those play, and deletes them all where the answer is yes.
    private async void OnSampleDeleteRequested(int sampleId)
    {
        if (_song.SampleById(sampleId) is not { } sample)
        {
            return;
        }

        int instrumentCount = _song.InstrumentCountUsingSample(sampleId);
        int noteCount = _song.NoteCountUsingSample(sampleId);
        var answer = await MessageDialog.Show((Window?)_sampleEditor ?? this, QuestionsAndNotices.DeleteSample(sample.Name, instrumentCount, noteCount),
            WindowTitles.Notices.DeleteSample, MessageButtons.YesNo);
        if (answer != MessageAnswer.Yes)
        {
            return;
        }

        _song.DeleteSample(sampleId);
        ForgetDeletedNotesAndInstrument(noteCount);
        ShowSongAfterSampleDelete();
    }

    // Drops the selection where a delete took notes with it, and the current instrument, so the refresh falls back to the first one left.
    private void ForgetDeletedNotesAndInstrument(int deletedNoteCount)
    {
        if (deletedNoteCount > 0)
        {
            Roll.ClearSelection();
        }

        _currentInstrumentId = null;
    }

    // Empties both undo histories after a sample's delete and brings everything the delete reached up to date, recording no step.
    private void ShowSongAfterSampleDelete()
    {
        _history.Discard(_song);
        RefreshRollExtent();
        RefreshSequence();
        ShowChangedSampleList();
        ShowUndoState();
    }

    // Discards the Sample Editor's history for a sample list that has grown, brings the window up to date and puts the editor on the sample added.
    private void ShowNewSample(Sample sample)
    {
        ShowChangedSampleList();
        _sampleEditor?.ShowSamples(_song, sample.Id);
    }

    // Discards the Sample Editor's history for a sample list that has grown or shrunk and brings the window up to date.
    private void ShowChangedSampleList()
    {
        _sampleHistory.Discard(_song);
        RefreshInstruments();
        UpdateTitle();
    }

    // Gives a sample the name its rename finished with, as an undo step of the Sample Editor's own, or says in a box why the name is refused.
    private async void OnSampleRenameRequested(int sampleId, string name)
    {
        if (_song.SampleById(sampleId) is not { } sample)
        {
            return;
        }

        if (_song.SampleNameRefusal(sample, name) is { } refusal)
        {
            await MessageDialog.Show((Window?)_sampleEditor ?? this, refusal, WindowTitles.Failures.FailedToRenameSample);
            return;
        }

        EditSample(sampleId, renamed => renamed.WithName(name), SampleEditorView, null);
        Instruments.Show(_song, CurrentInstrument);
        _sampleEditor?.ShowSamples(_song);
    }

    // Sets or clears a sample's loop flag, as an undo step of its own.
    private void OnSampleLoopChanged(int sampleId, bool doesLoop)
    {
        EditSample(sampleId, sample => sample.WithLoopFlag(doesLoop), SampleEditorView, null);
    }

    // Moves a sample's loop point to the start of a block, a whole loop marker drag being one undo step.
    private void OnSampleLoopBlockChanged(int sampleId, int block, object? undoSource)
    {
        EditSample(sampleId, sample => sample.WithLoopBlock(block), SampleEditorView, undoSource);
    }

    // Deletes, cuts or pastes a run of a sample's points, as an undo step of its own.
    private void OnSamplePointsReplaced(int sampleId, int start, int count, short[] points, SampleView after)
    {
        EditSample(sampleId, sample => sample.WithPointsReplaced(start, count, points), after, null);
    }

    // Trims a sample down to a run of its points, as an undo step of its own.
    private void OnSamplePointsKept(int sampleId, int start, int count, SampleView after)
    {
        EditSample(sampleId, sample => sample.WithOnlyPointsKept(start, count), after, null);
    }

    // Fades a run of a sample's points by writing the faded points over them, as an undo step of its own.
    private void OnSamplePointsOverwritten(int sampleId, int start, short[] points, SampleView after)
    {
        EditSample(sampleId, sample => sample.WithPointsOverwritten(start, points), after, null);
    }

    // Cuts a sample off after the loop Find Loop found, puts the loop's points there and sets it looping from the loop's start, as an undo step of its own.
    private void OnSampleLoopFound(int sampleId, int loopStart, short[] loopPoints, SampleView after)
    {
        EditSample(sampleId, sample => sample.WithLoopFrom(loopStart, loopPoints), after, null);
    }

    // Gives a sample the effects the rack asked for, a whole gesture on one of the rack's controls being one undo step.
    private void OnSampleEffectsChanged(int sampleId, SampleRack effects, object effectControl)
    {
        EditSample(sampleId, sample => sample.WithEffects(effects), SampleEditorView, effectControl);
    }

    // Sets the rate the Sample Editor plays a sample at, or puts it back on the sample's own C4 for no register, a whole gesture on one of the Pitch row's
    // controls being one undo step.
    private void OnSamplePreviewPitchChanged(int sampleId, int? pitchRegister, object pitchControl)
    {
        EditSample(sampleId, sample => pitchRegister is int chosen ? sample.WithPreviewPitchRegister(chosen) : sample.WithPreviewOnOwnC4(), SampleEditorView,
            pitchControl);
    }

    // Converts a sample to a rate for good, as an undo step of its own, and shows the tuning the instrument panel's instrument now sounds with.
    private void OnSampleRateConverted(int sampleId, int rateHz, SampleView after)
    {
        EditSample(sampleId, sample => sample.ConvertedTo(rateHz), after, null);
        Instruments.Show(_song, CurrentInstrument);
    }

    // Puts an edit of a sample in place of the one it was made from, in the song and in every voice sounding it, records the edit with the Sample Editor's
    // view before it and the view given after it, and redraws the editor, where the song still holds the sample and the edit changed anything.
    private void EditSample(int sampleId, Func<Sample, Sample> edit, SampleView after, object? undoSource)
    {
        if (_song.SampleById(sampleId) is not { } sample)
        {
            return;
        }

        var before = SampleEditorView;
        var edited = edit(sample);
        if (ReferenceEquals(edited, sample))
        {
            return;
        }

        SwapSoundingAudio(sample, edited);
        _song.ReplaceSample(edited);
        MarkSamplesModified(before, after, undoSource);
        _sampleEditor?.ShowSelectedSample();
    }

    // Hands every voice sounding a sample the audio that has just replaced it, to be taken up at the voice's own next loop wrap with the tuning that keeps
    // its pitch at the rate the new audio is at.
    private void SwapSoundingAudio(Sample replaced, Sample replacement)
    {
        if (_audio is null)
        {
            return;
        }

        _audio.Mixer.ReplaceVoiceAudio(replaced.Decoded, replacement.Decoded, replacement.RateFractionOfOriginal);
    }

    // Rebuilds the sequence and records a sample edit in the Sample Editor's history, with where the editor was looking on either side of it.
    private void MarkSamplesModified(SampleView before, SampleView after, object? undoSource)
    {
        RefreshSequence();
        if (!_sampleHistory.Record(_song, before, after, EditStep.UndoKeyFor(undoSource)))
        {
            return;
        }

        UpdateTitle();
    }

    // Takes back the Sample Editor's last edit.
    private void OnSampleUndo()
    {
        if (!_sampleHistory.CanUndo)
        {
            return;
        }

        RestoreSampleSnapshot(_sampleHistory.Undo());
    }

    // Puts back the Sample Editor's last edit taken back.
    private void OnSampleRedo()
    {
        if (!_sampleHistory.CanRedo)
        {
            return;
        }

        RestoreSampleSnapshot(_sampleHistory.Redo());
    }

    // Puts a state from the Sample Editor's history back, every sounding voice handed the audio that took its sample's place, and the editor with it.
    private void RestoreSampleSnapshot(SampleSnapshot snapshot)
    {
        var samplesBeforeById = _song.Samples.ToDictionary(sample => sample.Id);
        snapshot.Restore(_song);
        foreach (var restored in _song.Samples)
        {
            if (samplesBeforeById.TryGetValue(restored.Id, out var replaced))
            {
                SwapSoundingAudio(replaced, restored);
            }
        }

        RefreshSequence();
        UpdateTitle();
        Instruments.Show(_song, CurrentInstrument);
        _sampleEditor?.ShowRestoredState(_song, snapshot.View);
    }

    // Imports the .brr and .wav files the user picks as samples of the song, each file on its own, and says once what was refused.
    private async Task ImportSamples(Window owner)
    {
        var paths = await FilePickers.OpenFiles(owner, WindowTitles.ImportSamples, SampleImportFileFilter, PickerId.ImportSamples, AppPaths.SamplesFolder);
        if (paths.Count == 0)
        {
            return;
        }

        var refusals = new List<string>();
        var samplesRead = await ProgressDialog.Run(owner, WindowTitles.ImportSamples, "Reading the files", (progress, cancel) => ReadSampleFiles(paths, refusals, progress, cancel), ex => ex.Message);
        if (samplesRead.FailureMessage is { } failure)
        {
            await MessageDialog.Show(owner, failure, WindowTitles.Failures.FailedToImportSamples);
            return;
        }

        if (samplesRead.Value is not { } loaded)
        {
            return;
        }

        AddImportedSamples(loaded);
        await ShowImportRefusals(owner, refusals, NameRefusals.SampleNoun);
    }

    // Reads sample files behind the progress box, a file that cannot be read adding its reason to the refusals rather than stopping the rest.
    private static List<Sample> ReadSampleFiles(IReadOnlyList<string> paths, List<string> refusals, JobProgress progress, CancellationToken cancel)
    {
        var samples = new List<Sample>();
        for (int fileIndex = 0; fileIndex < paths.Count; fileIndex++)
        {
            progress(SampleImportStage, fileIndex, paths.Count);
            try
            {
                samples.Add(Sample.Load(paths[fileIndex], (block, blockCount) => progress(SampleImportStage, fileIndex, paths.Count, blockCount <= 0 ? 0 : (double)block / blockCount), cancel));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                refusals.Add(ex.Message);
            }
        }

        progress(SampleImportStage, paths.Count, paths.Count);
        return samples;
    }

    // Adds the samples read from files to the song, one under a name the song already holds taking the first free copy number, and puts the Sample Editor on the last one added.
    private void AddImportedSamples(IReadOnlyList<Sample> loaded)
    {
        Sample? lastAdded = null;
        foreach (var sample in loaded)
        {
            lastAdded = _song.AddSampleUnderFreeName(sample);
        }

        if (lastAdded is null)
        {
            return;
        }

        ShowNewSample(lastAdded);
    }

    // Says in one box every refusal of an import batch, where there is any.
    private static async Task ShowImportRefusals(Window owner, List<string> refusals, string noun)
    {
        if (refusals.Count == 0)
        {
            return;
        }

        await MessageDialog.Show(owner, string.Join(MessageDialog.ParagraphBreak, refusals), WindowTitles.Failures.FailedToImport(refusals.Count, noun));
    }

    // Adds an instrument on the newest sample, or on the noise generator where the song has no sample, and starts editing its name.
    private void OnCreateInstrument()
    {
        SelectAddedInstrumentAndRename(CreateInstrumentOnNewestSampleOrNoise());
    }

    // Adds an instrument on the song's newest sample, or on the noise generator where the song has no sample, under a name no other instrument has.
    private Instrument CreateInstrumentOnNewestSampleOrNoise()
    {
        if (_song.NewestSample is { } sample)
        {
            return _song.CreateInstrumentNamedAfter(sample);
        }

        return _song.CreateNamedNoiseInstrument();
    }

    // Adds the instruments in the .spcins files the user picks to the song, and their samples with them.
    private async void OnImportInstrument()
    {
        var paths = await FilePickers.OpenFiles(this, WindowTitles.ImportInstrumentsPicker, InstrumentFile.DialogFilter, PickerId.ImportInstruments, AppPaths.InstrumentsFolder);
        if (paths.Count == 0)
        {
            return;
        }

        await ImportInstrumentFiles(this, paths);
    }

    // Reads .spcins files into the song, selects the last instrument added and says once what was refused, returning that instrument or null where none was added.
    private async Task<Instrument?> ImportInstrumentFiles(Window owner, IReadOnlyList<string> paths)
    {
        var refusals = new List<string>();
        int sampleCountBefore = _song.Samples.Count;
        var lastAdded = AddInstrumentFiles(paths, refusals);
        if (_song.Samples.Count != sampleCountBefore)
        {
            _sampleHistory.Discard(_song);
        }

        if (lastAdded is not null)
        {
            SelectAddedInstrument(lastAdded);
        }

        await ShowImportRefusals(owner, refusals, NameRefusals.InstrumentNoun);
        return lastAdded;
    }

    // Adds the instrument of each .spcins file to the song, a file that cannot be added adding its reason to the refusals, and returns the last one added.
    private Instrument? AddInstrumentFiles(IReadOnlyList<string> paths, List<string> refusals)
    {
        Instrument? lastAdded = null;
        foreach (string path in paths)
        {
            try
            {
                lastAdded = InstrumentFile.Load(path).AddTo(_song);
            }
            catch (Exception ex)
            {
                refusals.Add(ex.Message);
            }
        }

        return lastAdded;
    }

    // Adds a copy of the current instrument, selects it and starts editing its name.
    private void OnDuplicateInstrument()
    {
        if (CurrentInstrument is not { } instrument)
        {
            return;
        }

        SelectAddedInstrumentAndRename(_song.DuplicateInstrument(instrument.Id));
    }

    // Selects every note in the song playing the current instrument.
    private void OnSelectInstrumentNotes()
    {
        if (CurrentInstrument is not { } instrument)
        {
            return;
        }

        Roll.SelectMatching((_, note) => note.InstrumentId == instrument.Id);
    }

    // Writes the current instrument and the sample it plays to the .spcins file the user picks.
    private async void OnExportInstrument()
    {
        if (CurrentInstrument is not { } instrument)
        {
            return;
        }

        var kind = new FileExportKind(WindowTitles.ExportInstrumentPicker, InstrumentFile.DialogFilter, InstrumentFile.Extension, PickerId.ExportInstrument,
            AppPaths.InstrumentsFolder, WindowTitles.Failures.FailedToExportInstrument);
        await FileExport.WriteOne(this, kind, ExportedFileOf(instrument));
    }

    // Writes every instrument in the song, each with the sample it plays, to a .spcins file in a folder the user picks, and reports what landed and what did not.
    private async void OnExportAllInstruments()
    {
        if (_song.Instruments.Count == 0)
        {
            return;
        }

        var kind = new FolderExportKind(NameRefusals.InstrumentNoun, WindowTitles.SelectExportFolderPicker, PickerId.ExportAllInstruments, AppPaths.InstrumentsFolder,
            WindowTitles.Notices.ExportInstruments, WindowTitles.Successes.InstrumentsExported);
        await FolderExport.WriteAll(this, kind, _song.Instruments.Select(ExportedFileOf).ToList());
    }

    // An instrument as an export writes it: a file named after it, holding it and the sample it plays.
    private ExportedFile ExportedFileOf(Instrument instrument)
    {
        return new ExportedFile(InstrumentFile.FileNameFor(instrument.Name), path => InstrumentFile.Save(path, instrument, _song.SampleFor(instrument)));
    }

    // Selects an instrument just added to the song and starts editing its name.
    private void SelectAddedInstrumentAndRename(Instrument instrument)
    {
        SelectAddedInstrument(instrument);
        Dispatcher.UIThread.Post(() => Instruments.StartRename(), DispatcherPriority.Input);
    }

    // Makes an instrument just added to the song the current one and records the edit, the selected notes keeping the instrument they had.
    private void SelectAddedInstrument(Instrument instrument)
    {
        _currentInstrumentId = instrument.Id;
        ShowChangedInstrumentList();
    }

    // Redraws everything that shows the song's instruments and marks the song modified, after an instrument was added or taken out.
    private void ShowChangedInstrumentList()
    {
        RefreshInstruments();
        MarkModified();
    }

    // Deletes the current instrument and every note that plays it, asking first where any note does.
    private async void OnDeleteInstrument()
    {
        if (CurrentInstrument is not { } instrument)
        {
            return;
        }

        int noteCount = _song.NoteCountUsingInstrument(instrument.Id);
        if (!await IsInstrumentDeleteConfirmed(instrument, noteCount))
        {
            return;
        }

        _song.DeleteInstrument(instrument.Id);
        ForgetDeletedNotesAndInstrument(noteCount);
        ShowChangedInstrumentList();
    }

    // Whether an instrument may go: always where no note plays it, and otherwise where the user answers yes to deleting those notes with it.
    private async Task<bool> IsInstrumentDeleteConfirmed(Instrument instrument, int noteCount)
    {
        if (noteCount == 0)
        {
            return true;
        }

        var answer = await MessageDialog.Show(this, QuestionsAndNotices.DeleteInstrument(instrument.Name, noteCount), WindowTitles.Notices.DeleteInstrument, MessageButtons.YesNo);
        return answer == MessageAnswer.Yes;
    }

    // Rebuilds both instrument drop-downs from the song and points the panels, the roll and the Sample Editor at it, the current instrument falling back
    // to the first one and then to none where it has gone.
    private void RefreshInstruments()
    {
        if (CurrentInstrument is null)
        {
            _currentInstrumentId = _song.Instruments.FirstOrDefault()?.Id;
        }

        FlagGuard.RunWithFlagSet(ref _isWritingInstrumentDropDowns, ShowInstrumentItems);
        ShowInstrumentSelection();
        Roll.CurrentInstrumentId = _currentInstrumentId;
        Roll.Refresh();
        ShowCurrentInstrumentInPanels();
        _sampleEditor?.ShowSamples(_song);
    }

    // Shows the current instrument in the instrument panel and brings the note panel up to date with it.
    private void ShowCurrentInstrumentInPanels()
    {
        Instruments.Show(_song, CurrentInstrument);
        ShowNotePanel();
    }

    // Gives both instrument drop-downs one shared list holding a row per instrument in the song.
    private void ShowInstrumentItems()
    {
        var instrumentItems = _song.Instruments.Select(InstrumentItem.Of).ToArray();
        Instruments.InstrumentItems = instrumentItems;
        NoteInstrumentBox.ItemsSource = instrumentItems;
    }

    // Picking an instrument from the instrument panel's drop-down.
    private void OnInstrumentPicked(object? picked)
    {
        SelectPickedInstrument(picked);
    }

    // Picking an instrument from the note panel's drop-down, which does what the instrument panel's does.
    private void OnNoteInstrumentSelected(object? sender, SelectionChangedEventArgs e)
    {
        SelectPickedInstrument(NoteInstrumentBox.SelectedItem);
    }

    // Selects the instrument of a drop-down row the user picked, ignoring a selection written into the drop-downs from the song.
    private void SelectPickedInstrument(object? picked)
    {
        if (_isWritingInstrumentDropDowns || picked is not InstrumentItem item)
        {
            return;
        }

        SelectInstrument(item.Id);
    }

    // Makes an instrument the current one, re-points the selected notes at it as an undo step of its own, and shows it in both panels.
    private void SelectInstrument(int instrumentId)
    {
        MakeInstrumentCurrent(instrumentId);
        if (Roll.SetSelectedInstrument(instrumentId))
        {
            MarkModified();
        }

        ShowInstrumentSelection();
        ShowCurrentInstrumentInPanels();
    }

    // Makes an instrument the current one, and the one the roll places notes with.
    private void MakeInstrumentCurrent(int instrumentId)
    {
        _currentInstrumentId = instrumentId;
        Roll.CurrentInstrumentId = instrumentId;
    }

    // Points the instrument panel's drop-down at the current instrument and the note panel's at the selected notes' own.
    private void ShowInstrumentSelection()
    {
        int currentInstrumentIndex = _currentInstrumentId is int id ? IndexOfInstrument(id) : NoInstrumentIndex;
        int noteIndex = SelectedNotesInstrumentIndex(currentInstrumentIndex);
        FlagGuard.RunWithFlagSet(ref _isWritingInstrumentDropDowns, () =>
        {
            Instruments.SelectedInstrumentIndex = currentInstrumentIndex;
            NoteInstrumentBox.SelectedIndex = noteIndex;
        });
    }

    // Where the selected notes' shared instrument sits in the drop-downs: nowhere where they play more than one, and the current instrument's place where nothing is selected.
    private int SelectedNotesInstrumentIndex(int currentIndex)
    {
        var notes = Roll.SelectedNotes;
        if (notes.Count == 0)
        {
            return currentIndex;
        }

        return FieldInput.SharedValue(notes, note => note.InstrumentId) is int sharedId ? IndexOfInstrument(sharedId) : NoInstrumentIndex;
    }

    // Where an instrument sits in the drop-downs, or nowhere where the song does not hold it.
    private int IndexOfInstrument(int instrumentId)
    {
        return _song.Instruments.ToList().FindIndex(instrument => instrument.Id == instrumentId);
    }

    // Refuses a note for want of an instrument, bringing the instrument panel forward with the message.
    private async void OnPlacementRefused()
    {
        ShowPanel(PanelTab.Instrument);
        await MessageDialog.Show(this, "Create an instrument before placing notes.", WindowTitles.Notices.NoInstrument);
    }

    // Gives the current instrument the name its rename finished with, as an undo step of its own, or says in a box why the name is refused.
    private async void OnInstrumentRenameRequested(string name)
    {
        if (CurrentInstrument is not { } instrument)
        {
            return;
        }

        if (_song.InstrumentNameRefusal(instrument, name) is { } refusal)
        {
            await MessageDialog.Show(this, refusal, WindowTitles.Failures.FailedToRenameInstrument);
            return;
        }

        instrument.Name = name;
        OnInstrumentEdited(Instruments);
    }

    // Brings everything an instrument panel edit reaches up to date and records it, a run of edits from one field being one undo step.
    private void OnInstrumentEdited(object source)
    {
        if (_song.SyncNotesWithInstruments())
        {
            AdoptQuantization(Roll.SelectedNotes);
            ShowQuantization();
        }

        MarkModified(source);
        RetuneSoundingInstrument();
        RefreshInstruments();
        RefreshInstrumentDefaultLanes();
        ShowNotePanel();
    }

    // Puts the current instrument's tuning into the notes already sounding it.
    private void RetuneSoundingInstrument()
    {
        if (_audio is null || CurrentInstrument is not { } instrument)
        {
            return;
        }

        _audio.Mixer.RetuneInstrument(instrument, _song.SoundingTuningOf(instrument));
    }

    // Redraws the lanes whose override lines follow an instrument's defaults: the channel volume, the pan, the amplify and the channel echo.
    private void RefreshInstrumentDefaultLanes()
    {
        ChannelVolumes.Refresh();
        Pans.Refresh();
        Amplifies.Refresh();
        ChannelEchoes.Refresh();
    }
}
