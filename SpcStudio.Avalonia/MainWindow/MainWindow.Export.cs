using System.Text;
using Avalonia.Interactivity;
using SpcStudio.Audio;
using SpcStudio.Export;
using SpcStudio.Import;
using SpcStudio.Model;

namespace SpcStudio;

// Writing the song out: the MML and its samples, the SPC and the WAV, and the menu item that reads a file the app does not open as a song - the songs of a ROM.
public partial class MainWindow
{
    private const string MmlFileKind = "MML";
    private const string SpcFileKind = "SPC";
    private const string WavFileKind = "WAV";
    private const string NothingToExportMessage = "Place at least one note first.";
    private const string NoAmkDirectoryMessage = "No AddmusicK folder is set, so the song's samples cannot be written. Choose one in Options > Preferences.";
    private const string ReadingRomStage = "Reading the ROM";
    private const string SongsExtractedStage = "songs extracted";
    private const string ExportWavOpeningLine = "Reading the song";
    private const string SecondsRenderedStage = "seconds rendered";

    // Exports the MML to the .txt the song was imported from or last exported to, asking before overwriting it, or asking for one where there is none.
    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        if (!await CanExportMml(WindowTitles.Failures.FailedToExportToMml))
        {
            return;
        }

        if (_mmlPath is null)
        {
            await ExportMmlAs();
            return;
        }

        var answer = await FolderExport.AskToWrite(this, _mmlPath, QuestionsAndNotices.OverwriteMml(Path.GetFileName(_mmlPath)), MessageButtons.YesNo, WindowTitles.Notices.OverwriteMml);
        if (answer != MessageAnswer.Yes)
        {
            return;
        }

        await WriteMml(_mmlPath);
    }

    // Exports the MML to a .txt chosen in a picker.
    private async void OnExportAsClick(object? sender, RoutedEventArgs e)
    {
        if (!await CanExportMml(WindowTitles.Failures.FailedToExportToMml))
        {
            return;
        }

        await ExportMmlAs();
    }

    // Asks everything an MML export needs before its picker - something to export, nothing on an extra channel, an AddmusicK install, a sample group
    // it defines, no more instruments and samples than the driver plays and a sample folder - answering whether it may go on.
    private async Task<bool> CanExportMml(FailureTitle failureTitle)
    {
        if (!await CanExport(failureTitle) || !await FitsDriverChannels(failureTitle) || await AmkDirectoryOrSayMissing(failureTitle) is null || !await HasInstallSampleGroup()
            || !await FitsDriverTables(failureTitle))
        {
            return false;
        }

        return await EnsureSamplePath();
    }

    // Answers whether the install defines the song's sample group, saying so in a box where it does not.
    private async Task<bool> HasInstallSampleGroup()
    {
        if (MissingSampleGroupSentence(_song) is not { } missingMessage)
        {
            return true;
        }

        await MessageDialog.Show(this, missingMessage, WindowTitles.Notices.SampleGroupMissing);
        return false;
    }

    // Answers whether the song has anything to export, saying so in a box where it has not.
    private async Task<bool> CanExport(FailureTitle failureTitle)
    {
        if (!_song.IsEmpty)
        {
            return true;
        }

        await MessageDialog.Show(this, NothingToExportMessage, failureTitle);
        return false;
    }

    // Answers whether every extra channel of the song is bare, saying so in a box where one is not.
    private async Task<bool> FitsDriverChannels(FailureTitle failureTitle)
    {
        if (!_song.HasExtraChannelContent)
        {
            return true;
        }

        await MessageDialog.Show(this, QuestionsAndNotices.ExtraChannelsNotExported(Song.DriverChannelCount), failureTitle);
        return false;
    }

    // Answers whether the song uses no more instruments and samples than the driver can play, saying in one box which it uses too many of where it does.
    private async Task<bool> FitsDriverTables(FailureTitle failureTitle)
    {
        var overflows = new List<string>();

        int instrumentCount = DriverTableLimits.InstrumentEntryCount(_song);
        if (instrumentCount > DriverTableLimits.MaxInstrumentEntries)
        {
            overflows.Add(QuestionsAndNotices.TooManyInstruments(instrumentCount, DriverTableLimits.MaxInstrumentEntries));
        }

        int groupSampleCount = InstallGroupOf(_song)?.Entries.Count ?? 0;
        int sampleCount = DriverTableLimits.SampleSlotCount(_song, groupSampleCount);
        if (sampleCount > DriverTableLimits.MaxSampleSlots)
        {
            overflows.Add(QuestionsAndNotices.TooManySamples(sampleCount, groupSampleCount, DriverTableLimits.MaxSampleSlots));
        }

        if (overflows.Count == 0)
        {
            return true;
        }

        await MessageDialog.Show(this, string.Join(MessageDialog.ParagraphBreak, overflows), failureTitle);
        return false;
    }

    // Exports the MML to a .txt chosen in a picker, where one is chosen.
    private async Task ExportMmlAs()
    {
        if (await AskMmlPath() is { } path)
        {
            await WriteMml(path);
        }
    }

    // Asks where the MML goes, offering the .txt the song last went to, or null where the picker was dismissed.
    private Task<string?> AskMmlPath()
    {
        string suggestedName = _mmlPath is null ? MmlFileNameOfTitle() : Path.GetFileName(_mmlPath);
        return FilePickers.SaveFile(this, WindowTitles.ExportToMmlPicker, MmlImporter.DialogFilter, suggestedName, MmlImporter.Extension, PickerId.ExportMml, AppPaths.MusicFolder);
    }

    // The .txt name the song's title gives with what a file name cannot carry taken out, or nothing where that leaves nothing.
    private string MmlFileNameOfTitle()
    {
        string fileName = DiskNameRules.WithoutForbiddenCharacters(ShownSongTitle);
        return fileName.Length > 0 ? fileName + MmlImporter.Extension : "";
    }

    // Exports the MML and its samples, then opens the .txt or says in a box where it went.
    private async Task WriteMml(string path)
    {
        if (!await TryWriteMml(path))
        {
            return;
        }

        if (_doesOpenMmlAfterExport)
        {
            await SystemOpen.OpenOrSayFailure(this, path, WindowTitles.Failures.FailedToOpenExportedMml);
            return;
        }

        await ShowExportCompleteBox(MmlFileKind, path);
    }

    // The AddmusicK folder Preferences names where it is still there, or null with a box saying the samples have nowhere to go.
    private async Task<string?> AmkDirectoryOrSayMissing(FailureTitle failureTitle)
    {
        string? directory = AppPaths.AmkDirectory;
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            return directory;
        }

        await MessageDialog.Show(this, NoAmkDirectoryMessage, failureTitle);
        return null;
    }

    // Asks for a folder for the song's samples where it has none, as an undo step of its own, answering false where the question was dismissed.
    private async Task<bool> EnsureSamplePath()
    {
        if (DiskNameRules.IsValid(_song.SamplePath))
        {
            return true;
        }

        if (await SamplePathDialog.Show(this, _song.SamplePath) is not { } path)
        {
            return false;
        }

        _song.SamplePath = path;
        ShowSongPanelFields();
        MarkModified();
        return true;
    }

    // The samples' writes for a song in a given folder, asking once before overwriting files holding other bytes, or null where the user backed out.
    private async Task<List<SampleWrite>?> PlanSamples(string amkDirectory, string songFolder)
    {
        var samples = SampleExport.PlanWrites(amkDirectory, songFolder, _song);
        var clashingSampleFileNames = samples.Where(write => write.HasClash).Select(write => write.Sample.FileName).ToList();
        if (clashingSampleFileNames.Count == 0)
        {
            return samples;
        }

        var answer = await MessageDialog.Show(this, QuestionsAndNotices.OverwriteSamples(_song.SamplePath, clashingSampleFileNames), WindowTitles.Notices.ExportSamples, MessageButtons.YesNo);
        return answer == MessageAnswer.Yes ? samples : null;
    }

    // Puts the song's samples where AddmusicK reads them for a song in a given folder, with the clashes settled, answering whether they went.
    private async Task<bool> WriteSamples(string amkDirectory, string songFolder)
    {
        if (await PlanSamples(amkDirectory, songFolder) is not { } samples)
        {
            return false;
        }

        if (SampleExport.WritePlanned(samples) is not { } failure)
        {
            return true;
        }

        await MessageDialog.Show(this, failure, WindowTitles.Failures.FailedToExportSamples);
        return false;
    }

    // Writes the song's samples and then its MML, making the .txt the one the next export goes to, answering whether it went.
    private async Task<bool> TryWriteMml(string path)
    {
        if (await AmkDirectoryOrSayMissing(WindowTitles.Failures.FailedToExportToMml) is not { } amkDirectory
            || !await WriteSamples(amkDirectory, Path.GetDirectoryName(Path.GetFullPath(path))!))
        {
            return false;
        }

        if (!await MessageDialog.TryOrSayFailure(this, () => File.WriteAllText(path, MmlExporter.Build(_song, _exportOptions)), WindowTitles.Failures.FailedToExportToMml))
        {
            return false;
        }

        _mmlPath = path;
        UpdateTitle();
        return true;
    }

    // Says in a box where an export wrote its file.
    private async Task ShowExportCompleteBox(string fileKind, string path)
    {
        await MessageDialog.Show(this, $"Exported {fileKind} to: {path}", WindowTitles.Successes.ExportComplete);
    }

    // Reads every song out of an AddmusicK-patched ROM behind a progress box and writes one song file each into a chosen folder, reporting the songs it skipped.
    private async void OnExtractRomClick(object? sender, RoutedEventArgs e)
    {
        string? romPath = await FilePickers.OpenFile(this, WindowTitles.ExtractRom, RomInsert.DialogFilter, PickerId.ExtractRom, AppPaths.RomFolder);
        if (romPath is null)
        {
            return;
        }

        string? folder = await FilePickers.PickFolder(this, WindowTitles.SelectExtractionFolderPicker, PickerId.ExtractRomDestination, AppPaths.ProjectsFolder);
        if (folder is null)
        {
            return;
        }

        int songCount = 0;

        var extractionOutcome = await ProgressDialog.Run(this, WindowTitles.ExtractingSongsFromRom, ReadingRomStage,
            (progress, cancel) => RomExtract.WriteSongFiles(romPath, folder, IsSongExtracted, (done, count) => progress(SongsExtractedStage, done, songCount = count), cancel),
            ex => ex is RomReadException or IOException ? ex.Message : null);

        if (extractionOutcome.FailureMessage is { } failure)
        {
            await MessageDialog.Show(this, failure, WindowTitles.Failures.FailedToExtractMusic);
            return;
        }

        var results = extractionOutcome.Value ?? [];

        BoxTitle reportTitle = !extractionOutcome.IsCancelled && results.All(result => result.IsWritten)
            ? WindowTitles.Successes.SongsExtractedFromRom
            : WindowTitles.Notices.SongsExtractedFromRom;
        await MessageDialog.Show(this, ExtractReport(results, folder, songCount, extractionOutcome.IsCancelled), reportTitle);
    }

    // Whether a song read out of a ROM is extracted: every song where SMW's original songs are included, and every other song where they are not.
    private bool IsSongExtracted(RomSong song)
    {
        return _doesRomExtractionIncludeSmwOriginals || !SmwOriginalSongs.IsOriginal(song);
    }

    // What the extraction did as one message: how many songs were written, then the ones that were not, grouped by the reason.
    private static string ExtractReport(IReadOnlyList<RomExtractResult> results, string folder, int songCount, bool isCancelled)
    {
        int writtenSongCount = results.Count(result => result.IsWritten);
        var report = new StringBuilder();
        report.Append(isCancelled
            ? $"Extraction was cancelled after {results.Count} of {songCount} songs. {writtenSongCount} were extracted into {folder}."
            : $"{writtenSongCount} of {results.Count} songs were extracted into {folder}.");

        var unwrittenSongs = results.Where(result => !result.IsWritten).ToList();
        AppendBySongs(report, $"{unwrittenSongs.Count} could not be converted:", unwrittenSongs.Select(result => (result.Slot, result.Reason!)));
        return report.ToString();
    }

    // Appends a heading and then each message once, under the songs that gave it, the most often given first, or nothing where there are no messages.
    private static void AppendBySongs(StringBuilder report, string heading, IEnumerable<(int Slot, string Message)> messages)
    {
        var messageGroupsMostGivenFirst = messages.GroupBy(message => message.Message).OrderByDescending(group => group.Count()).ToList();
        if (messageGroupsMostGivenFirst.Count == 0)
        {
            return;
        }

        report.Append(MessageDialog.ParagraphBreak + heading);
        foreach (var messageGroup in messageGroupsMostGivenFirst)
        {
            string songNumbersOfMessage = SentenceList.Of(messageGroup.Select(message => $"${message.Slot:X2}"));
            report.Append(MessageDialog.ParagraphBreak + $"{songNumbersOfMessage}\n{messageGroup.Key}");
        }
    }

    // Has AddmusicK compile the song with its samples behind a progress box and keeps the .spc it writes where a picker says.
    private async void OnExportSpcClick(object? sender, RoutedEventArgs e)
    {
        if (!await CanExportMml(WindowTitles.Failures.FailedToExportToSpc))
        {
            return;
        }

        string? chosen = await FilePickers.SaveFile(this, WindowTitles.ExportToSpc, SpcExport.DialogFilter, Path.ChangeExtension(SongName, SpcExport.DumpExtension),
            SpcExport.DumpExtension, PickerId.ExportSpc, AppPaths.ProjectsFolder);
        if (chosen is null || await AmkDirectoryOrSayMissing(WindowTitles.Failures.FailedToExportToSpc) is not { } amkDirectory
            || !await WriteSamples(amkDirectory, SpcExport.WorkingSongFolder(amkDirectory)))
        {
            return;
        }

        string? mml = null;
        if (!await MessageDialog.TryOrSayFailure(this, () => mml = MmlExporter.Build(_song, _exportOptions), WindowTitles.Failures.FailedToExportToSpc) || mml is null)
        {
            return;
        }

        await RunFileExport(WindowTitles.ExportToSpc, RunningAmkStage, SpcFileKind, WindowTitles.Failures.FailedToExportToSpc, chosen,
            (_, cancel) => SpcExport.Run(amkDirectory, mml, chosen, cancel), ex => ex is AmkRunException ? ex.Message : null);
    }

    // Renders the song to a .wav chosen in a picker behind a progress box.
    private async void OnExportWavClick(object? sender, RoutedEventArgs e)
    {
        if (!await CanExport(WindowTitles.Failures.FailedToExportToWav))
        {
            return;
        }

        string? chosen = await FilePickers.SaveFile(this, WindowTitles.ExportToWav, WavExporter.DialogFilter, Path.ChangeExtension(SongName, Sample.WavExtension),
            Sample.WavExtension, PickerId.ExportWav, AppPaths.ProjectsFolder);
        if (chosen is null)
        {
            return;
        }

        var song = _song;
        await RunFileExport(WindowTitles.ExportToWav, ExportWavOpeningLine, WavFileKind, WindowTitles.Failures.FailedToExportToWav, chosen,
            (progress, cancel) => WavExporter.Write(song, chosen, (done, total) => progress(SecondsRenderedStage, done, total), cancel), ex => ex.Message);
    }

    // Writes a file behind a progress box, reporting a failure under its title, and where the file was written says so in the status bar and a box.
    private async Task RunFileExport(string title, string openingLine, string fileKind, FailureTitle failureTitle, string path, Action<JobProgress, CancellationToken> write,
        Func<Exception, string?> failureMessageOf)
    {
        var writeOutcome = await ProgressDialog.Run(this, title, openingLine,
            (progress, cancel) =>
            {
                write(progress, cancel);
                return true;
            },
            failureMessageOf);

        if (writeOutcome.FailureMessage is { } failure)
        {
            await MessageDialog.Show(this, failure, failureTitle);
            return;
        }

        if (!writeOutcome.Value)
        {
            return;
        }

        await ShowExportCompleteBox(fileKind, path);
    }
}
