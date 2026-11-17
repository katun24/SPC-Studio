using Avalonia.Controls;
using SpcStudio.Audio;
using SpcStudio.Import;
using SpcStudio.Model;

namespace SpcStudio;

// The Instruments and Samples menus' extractions and additions, and the SPC Player's: an AddmusicK .txt or zip, a song on SMWCentral or an .spc read for
// its instruments or samples, and the ones chosen in a box either written to a folder as Export All writes them or added to the open song.
public partial class MainWindow
{
    private ExtractionKind<Instrument>? _instrumentExtraction;
    private ExtractionKind<Sample>? _sampleExtraction;

    // Builds the two kinds and gives the twelve extraction and addition items their headers and what each runs.
    private void WireExtractionItems()
    {
        _instrumentExtraction = new ExtractionKind<Instrument>(WindowTitles.InstrumentsWord, QuestionsAndNotices.OnlyOriginalSmwInstruments,
            song => [.. song.Instruments.Where(instrument => !instrument.IsSmw)], instrument => instrument.Name,
            new ExtractionEnding<Instrument>(WindowTitles.ExtractWord, QuestionsAndNotices.SelectInstrumentsForExtraction,
                choice => ExportInstrumentsToFolder(choice.Owner, choice.Source, choice.Chosen)),
            new ExtractionEnding<Instrument>(WindowTitles.AddWord, QuestionsAndNotices.SelectInstrumentsToAdd, AddInstrumentsToOpenSong));
        _sampleExtraction = new ExtractionKind<Sample>(WindowTitles.SamplesWord, QuestionsAndNotices.OnlyOriginalSmwSamples, song => song.Samples, sample => sample.Name,
            new ExtractionEnding<Sample>(WindowTitles.ExtractWord, QuestionsAndNotices.SelectSamplesForExtraction, choice => SampleExports.ExportToFolder(choice.Owner, choice.Chosen)),
            new ExtractionEnding<Sample>(WindowTitles.AddWord, QuestionsAndNotices.SelectSamplesToAdd, AddSamplesToOpenSong));
        WireKindItems(_instrumentExtraction, [ExtractInstrumentsFromMmlItem, ExtractInstrumentsFromSmwcItem, ExtractInstrumentsFromSpcItem],
            [AddInstrumentsFromMmlItem, AddInstrumentsFromSmwcItem, AddInstrumentsFromSpcItem]);
        WireKindItems(_sampleExtraction, [ExtractSamplesFromMmlItem, ExtractSamplesFromSmwcItem, ExtractSamplesFromSpcItem],
            [AddSamplesFromMmlItem, AddSamplesFromSmwcItem, AddSamplesFromSpcItem]);
    }

    // Gives one kind's extraction and addition items, each in the order of the three sources, their headers and what each runs.
    private void WireKindItems<TItem>(ExtractionKind<TItem> kind, IReadOnlyList<MenuItem> extractItems, IReadOnlyList<MenuItem> addItems)
    {
        // The three sources, in the order of the items.
        ExtractionSource[] extractionSources =
        [
            new(WindowTitles.MmlSourceWord, ReadLibraryFromMmlFile),
            new(WindowTitles.SmwcSongSourceWord, ReadLibraryFromSmwc),
            new(WindowTitles.SpcSourceWord, TranscribeForExtraction),
        ];
        for (int sourceIndex = 0; sourceIndex < extractionSources.Length; sourceIndex++)
        {
            var source = extractionSources[sourceIndex];
            WireExtractionItem(extractItems[sourceIndex], WindowTitles.ExtractFrom(kind.KindWord, source.SourceWord), kind, kind.Export, source);
            WireExtractionItem(addItems[sourceIndex], WindowTitles.AddToCurrentSongFrom(kind.KindWord, source.SourceWord), kind, kind.Add, source);
        }
    }

    // Gives an item its header, its title with the mark of an item that opens a box, and runs its extraction or addition over this window when it is
    // clicked.
    private void WireExtractionItem<TItem>(MenuItem item, string title, ExtractionKind<TItem> kind, ExtractionEnding<TItem> ending, ExtractionSource source)
    {
        item.Header = WindowTitles.BoxOpeningItemHeader(title);
        item.Click += async (_, _) => await Extract(this, title, kind, ending, () => source.Read(title, WindowTitles.Failures.FailedTo(title), ending.ActWord));
    }

    // Hooks the SPC Player's four extraction and addition items up to the open SPC's transcription.
    private void WireSpcPlayerExtractions(SpcPlayerWindow player)
    {
        player.ExportInstrumentsRequested += async (title, file, trace) => await ExtractFromSpcPlayer(player, title, _instrumentExtraction!, _instrumentExtraction!.Export, file, trace);
        player.AddInstrumentsRequested += async (title, file, trace) => await ExtractFromSpcPlayer(player, title, _instrumentExtraction!, _instrumentExtraction!.Add, file, trace);
        player.ExportSamplesRequested += async (title, file, trace) => await ExtractFromSpcPlayer(player, title, _sampleExtraction!, _sampleExtraction!.Export, file, trace);
        player.AddSamplesRequested += async (title, file, trace) => await ExtractFromSpcPlayer(player, title, _sampleExtraction!, _sampleExtraction!.Add, file, trace);
    }

    // Transcribes the SPC Player's open dump from what playing it through heard, under a title over the player, and writes or adds the instruments or
    // samples chosen in the box.
    private async Task ExtractFromSpcPlayer<TItem>(SpcPlayerWindow player, string title, ExtractionKind<TItem> kind, ExtractionEnding<TItem> ending, SpcFile file, SpcTrace trace)
    {
        await Extract(player, title, kind, ending, () => TranscribedSong(ProgressHost.OwnBox(player), player, title, WindowTitles.Failures.FailedTo(title),
            (reportDoneFraction, cancel) => TranscribeTraceAsWholeJob(file, trace, reportDoneFraction, cancel)));
    }

    // Reads a source for its instruments or samples under a title, every box over a window, says so where it holds none of its own, and writes or adds
    // the ones chosen in the box.
    private async Task Extract<TItem>(Window owner, string title, ExtractionKind<TItem> kind, ExtractionEnding<TItem> ending, Func<Task<Song?>> readSource)
    {
        var openSong = _song;
        if (await readSource() is not { } song)
        {
            return;
        }

        var items = kind.ItemsOf(song);
        if (items.Count == 0)
        {
            await MessageDialog.Show(owner, kind.OnlyOriginalSmwMessage, WindowTitles.Notices.NoneToTake(kind.KindWord, ending.ActWord));
            return;
        }

        if (await ExtractionDialog.Show(owner, title, ending.SelectMessage, [.. items.Select(kind.NameOf)], ending.ActWord) is not { } chosenPositions)
        {
            return;
        }

        await ending.Take(new ExtractionChoice<TItem>(owner, song, openSong, [.. chosenPositions.Select(position => items[position])]));
    }

    // Adds the instruments chosen in the box, each with the sample it plays, to the song that was open when the source was read, as Import Instrument(s)
    // adds them, adding nothing where another song has been opened since.
    private async Task AddInstrumentsToOpenSong(ExtractionChoice<Instrument> choice)
    {
        var reads = choice.Chosen.Select(instrument => InstrumentFileRead.OfInstrument(instrument, choice.Source.SampleFor(instrument))).ToList();
        await AddPickedReads(choice.Owner, choice.OpenSong, reads);
    }

    // Adds the samples chosen in the box to the song that was open when the source was read, as Import Sample(s) adds them, adding nothing where another
    // song has been opened since.
    private Task AddSamplesToOpenSong(ExtractionChoice<Sample> choice)
    {
        if (_song != choice.OpenSong)
        {
            return Task.CompletedTask;
        }

        AddImportedSamples(choice.Chosen);
        return Task.CompletedTask;
    }

    // The instruments and samples declared by an AddmusicK .txt or a shared song's zip picked under a title, or null where none was picked or it could not
    // be read.
    private async Task<Song?> ReadLibraryFromMmlFile(string title, FailureTitle failureTitle, string actWord)
    {
        if (await FilePickers.OpenFile(this, title, SongZip.MmlOrZipDialogFilter, PickerId.ImportMml, AppPaths.MusicFolder) is not { } pickedPath)
        {
            return null;
        }

        return await ReadLibraryBehindProgress(title, failureTitle, MmlOrZip.StageCountOf(pickedPath),
            (amkRoot, steps, _, cancel) => MmlOrZip.MmlPathOf(pickedPath, amkRoot, this, steps.Report, cancel));
    }

    // The instruments and samples declared by a song on SMWCentral whose number or page's address is typed into a box titled as the extraction or
    // addition, its button saying the act, or null where none was typed or it could not be fetched or read.
    private async Task<Song?> ReadLibraryFromSmwc(string title, FailureTitle failureTitle, string actWord)
    {
        if (await SmwcSong.AskForNumber(this, SmwcSong.Prompt(title, actWord)) is not { } songNumber)
        {
            return null;
        }

        return await ReadLibraryBehindProgress(title, failureTitle, SmwcSong.StageCount,
            (amkRoot, steps, _, cancel) => SmwcSong.MmlPathOf(songNumber, amkRoot, this, steps, downloadFailureTitle => { }, cancel));
    }

    // The instruments and samples declared by an AddmusicK .txt a step of a number of stages finds, read behind one progress box under a title, every
    // failure shown under one title, or null where it was cancelled or failed.
    private async Task<Song?> ReadLibraryBehindProgress(
        string title, FailureTitle failureTitle, int findingStageCount, Func<string?, EqualStepsProgress, Action<FailureTitle>, CancellationToken, string> findMmlPath)
    {
        var reading = new MmlReading(MmlImporter.LibraryStageCount, failureTitle, (mmlPath, amkRoot, progress, cancel) => MmlImporter.LoadLibrary(mmlPath, amkRoot, progress, cancel));
        return (await ReadMmlBehindProgress(title, findingStageCount, failureTitle, findMmlPath, reading))?.Song;
    }

    // The song an .spc picked under a title transcribes into, holding the instruments and samples the transcription hears, or null where none was picked
    // or it could not be read or transcribed.
    private async Task<Song?> TranscribeForExtraction(string title, FailureTitle failureTitle, string actWord)
    {
        if (await SpcFileOpening.PickPath(this, title) is not { } spcPath)
        {
            return null;
        }

        return await TranscribedSpc(this, ProgressHost.OwnBox(this), spcPath, title, failureTitle, failureTitle);
    }

    // What an extraction or an addition takes out of a source's song: the word its titles name them by, the message where the song holds none of its own,
    // the ones the song holds, each one's name in the box, and the two endings - written to a folder, or added to the open song.
    private sealed record ExtractionKind<TItem>(
        string KindWord, string OnlyOriginalSmwMessage, Func<Song, IReadOnlyList<TItem>> ItemsOf, Func<TItem, string> NameOf, ExtractionEnding<TItem> Export,
        ExtractionEnding<TItem> Add);

    // What becomes of the ones chosen: the word naming the act, which the box's button and the notice where there are none say, the box's message, and
    // the taking of the ones chosen.
    private sealed record ExtractionEnding<TItem>(string ActWord, string SelectMessage, Func<ExtractionChoice<TItem>, Task> Take);

    // The ones chosen in the box: the window the boxes stand over, the source's song, the song open when the source was read, and the chosen.
    private sealed record ExtractionChoice<TItem>(Window Owner, Song Source, Song OpenSong, IReadOnlyList<TItem> Chosen);

    // Where an extraction or an addition reads from: the word its title names it by, and the reading, handed the title, the failure title and the act's
    // word and answering the source's song, or null where there is none.
    private sealed record ExtractionSource(string SourceWord, Func<string, FailureTitle, string, Task<Song?>> Read);
}
