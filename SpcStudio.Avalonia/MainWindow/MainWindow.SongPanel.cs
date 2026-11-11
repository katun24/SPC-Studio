using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SpcStudio.Audio;
using SpcStudio.Controls;
using SpcStudio.Export;
using SpcStudio.Import;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio;

// The song as a whole: the Song panel's tags, tempo, song volume, options, sample path and sample group, and its ARAM readout.
public partial class MainWindow
{
    public const string AramSamplesLabel = "Samples";
    private const int AramCountPauseMilliseconds = 150;
    private const string RunningAmkStage = "Running AddmusicK";

    private bool _isWritingFieldsFromSong;
    private IReadOnlyList<SampleGroup>? _installGroups;
    private AramInstall? _aramInstall;
    private bool _isAramCounting;
    private bool _isAramRecountWanted;
    private List<bool> _shownSampleGroupPickability = [];

    // The samples SMW's instruments play out of each of the install's groups, by group, read the first time a group is asked for.
    private readonly Dictionary<string, List<Sample>> _smwSamplesByGroup = [];

    // The ids of the samples each of the install's groups holds by its files' sizes, by group, worked out the first time a group is asked for.
    private readonly Dictionary<string, HashSet<int>> _smwSampleIdsByGroup = [];

    // Counts the ARAM rows once the edits have paused.
    private readonly DispatcherTimer _aramCountTimer = new() { Interval = TimeSpan.FromMilliseconds(AramCountPauseMilliseconds) };

    // Writes the song's sample path, sample group, two options and four tags into the Song panel.
    private void ShowSongPanelFields()
    {
        FlagGuard.RunWithFlagSet(ref _isWritingFieldsFromSong, () =>
        {
            SamplePathBox.Text = _song.SamplePath;
            ShowSampleGroupChoices();
            SmwInstrumentsCheck.IsChecked = _song.UsesSmwInstruments;
            ShowSmwInstrumentsSwitch();
            LoopSongCheck.IsChecked = _song.IsLooping;
            TempoImmunityCheck.IsChecked = _song.HasTempoImmunity;
            ShowSpcTags();
        });
    }

    // Fills the Sample Group drop-down from the install, a group lacking a sample of the sample group's the song cannot do without grayed out, and
    // selects the song's group in it.
    private void ShowSampleGroupChoices()
    {
        var choices = SampleGroupChoicesOfInstall();
        _shownSampleGroupPickability = SampleGroupPickability(choices);
        var rows = choices.Select((choice, index) => _shownSampleGroupPickability[index] ? choice : (object)UnpickableRows.Grayed(choice)).ToList();
        SampleGroupBox.ItemsSource = rows;
        SampleGroupBox.SelectedItem = rows.FirstOrDefault(row => row is SampleGroupChoice choice && choice.Name == _song.SampleGroup);
    }

    // Whether each group can be picked: whether it holds every sample of the sample group's the song cannot do without, which every group does where
    // the song needs none.
    private List<bool> SampleGroupPickability(List<SampleGroupChoice> choices)
    {
        var smwSampleIdsNeeded = _song.SmwSampleIdsNeeded;
        if (smwSampleIdsNeeded.Count == 0)
        {
            return [.. choices.Select(_ => true)];
        }

        return [.. choices.Select(choice => smwSampleIdsNeeded.IsSubsetOf(SmwSampleIdsOf(choice.Name)))];
    }

    // Enables the SMW Samples check box where the SMW instruments can be turned off and the box is ticked or a sample group is chosen.
    private void ShowSmwInstrumentsSwitch()
    {
        SmwInstrumentsCheck.IsEnabled = _song.CanTurnOffSmwInstruments && (_song.UsesSmwInstruments || _song.SampleGroup is not null);
    }

    // Brings the SMW Samples check box and the sample groups that can be picked in step with an edit to the notes or the instruments, filling the
    // Sample Group drop-down again only where which groups can be picked has changed.
    private void ShowWhatSmwSampleUseAllows()
    {
        ShowSmwInstrumentsSwitch();
        if (!SampleGroupPickability(SampleGroupChoicesOfInstall()).SequenceEqual(_shownSampleGroupPickability))
        {
            FlagGuard.RunWithFlagSet(ref _isWritingFieldsFromSong, ShowSampleGroupChoices);
        }
    }

    // The samples SMW's instruments play out of an install group, read the first time they are asked for, or none where the install defines no such group.
    private List<Sample> SmwSamplesOf(string? groupName)
    {
        return ReadOnceForGroup(_smwSamplesByGroup, groupName, SmwSamples.Read, []);
    }

    // The ids of the samples an install group's files hold by their sizes, worked out the first time they are asked for without reading a file, or none
    // where the install defines no such group.
    private HashSet<int> SmwSampleIdsOf(string? groupName)
    {
        return ReadOnceForGroup(_smwSampleIdsByGroup, groupName, SmwSamples.IdsHeldByFileSize, []);
    }

    // What a read of an install group's files gives, worked out the first time it is asked for and kept in a cache, or the empty answer where there is no
    // install or the install defines no such group.
    private TAnswer ReadOnceForGroup<TAnswer>(Dictionary<string, TAnswer> cache, string? groupName, Func<string, IReadOnlyList<string>, TAnswer> read, TAnswer emptyAnswer)
    {
        if (AppPaths.AmkDirectory is not { Length: > 0 } amkRoot || InstallGroupNamed(groupName) is not { } group)
        {
            return emptyAnswer;
        }

        if (!cache.TryGetValue(group.Name, out var answer))
        {
            answer = read(amkRoot, [.. group.Entries.Select(entry => entry.Path)]);
            cache[group.Name] = answer;
        }

        return answer;
    }

    // Hands the song the samples its SMW instruments play out of its sample group.
    private void LoadSmwSamples()
    {
        _song.SetSmwSamples(SmwSamplesOf(_song.SampleGroup));
    }

    // Writes the four lines of the song's #spc block into their fields.
    private void ShowSpcTags()
    {
        var tags = _song.SpcTags;
        SongTitleBox.Text = tags.Title;
        SongAuthorBox.Text = tags.Author;
        SongGameBox.Text = tags.Game;
        SongCommentBox.Text = tags.Comment;
    }

    // The Sample Group drop-down's entries: the install's groups with the optimized group first and the rest in the file's order, then none.
    private List<SampleGroupChoice> SampleGroupChoicesOfInstall()
    {
        var installGroupNames = (_installGroups ?? []).Select(group => group.Name).ToList();
        if (installGroupNames.Remove(Song.OptimizedSampleGroup))
        {
            installGroupNames.Insert(0, Song.OptimizedSampleGroup);
        }

        return [.. installGroupNames.Select(name => new SampleGroupChoice(name)), new SampleGroupChoice(null)];
    }

    // The sentence saying a song's sample group is not one the install defines, or null where it is, where the song names none, or where there is no install's groups to ask.
    private string? MissingSampleGroupSentence(Song song)
    {
        if (_installGroups is null || song.SampleGroup is not { } groupName || InstallGroupOf(song) is not null)
        {
            return null;
        }

        return SampleGroups.NotInInstallSentence(groupName);
    }

    // The install's group of the name the song's sample group has, or null where the song names none or the install defines no such group.
    private SampleGroup? InstallGroupOf(Song song)
    {
        return InstallGroupNamed(song.SampleGroup);
    }

    // The install's group of a name, or null where there is no name or the install defines no such group.
    private SampleGroup? InstallGroupNamed(string? groupName)
    {
        return _installGroups?.FirstOrDefault(group => group.Name == groupName);
    }

    // Sets the group the export's #samples block opens with and the SMW instruments play out of, as an undo step of its own.
    private void OnSampleGroupSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_isWritingFieldsFromSong || SampleGroupBox.SelectedItem is not SampleGroupChoice choice)
        {
            return;
        }

        _song.SampleGroup = choice.Name;
        LoadSmwSamples();
        ShowChangedInstrumentList(sender);
    }

    // Turns the SMW instruments on or off, as an undo step of its own, asking first where SMW instruments with changes would go and ticking the box
    // again where the answer is no.
    private async void OnSmwInstrumentsToggled(object? sender, RoutedEventArgs e)
    {
        if (_isWritingFieldsFromSong)
        {
            return;
        }

        bool isTurningOn = SmwInstrumentsCheck.IsChecked == true;
        if (!isTurningOn && !await IsLosingSmwInstrumentChangesAgreed())
        {
            FlagGuard.RunWithFlagSet(ref _isWritingFieldsFromSong, () => SmwInstrumentsCheck.IsChecked = true);
            return;
        }

        if (isTurningOn)
        {
            _song.TurnOnSmwInstruments();
        }
        else
        {
            _song.TurnOffSmwInstruments();
        }

        ShowChangedInstrumentList(sender);
    }

    // Whether the SMW instruments' changes may be lost: asked where any SMW instrument has changes, and agreed where none has.
    private async Task<bool> IsLosingSmwInstrumentChangesAgreed()
    {
        int changedInstrumentCount = _song.ChangedSmwInstrumentCount;
        if (changedInstrumentCount == 0)
        {
            return true;
        }

        return await MessageDialog.IsAnsweredYes(this, QuestionsAndNotices.LoseSmwInstrumentChanges(changedInstrumentCount), WindowTitles.Notices.RemoveOriginalSmwInstruments);
    }

    // Turns tempo immunity on or off, as an undo step of its own.
    private void OnTempoImmunityToggled(object? sender, RoutedEventArgs e)
    {
        if (_isWritingFieldsFromSong)
        {
            return;
        }

        _song.HasTempoImmunity = TempoImmunityCheck.IsChecked == true;
        MarkModified();
    }

    // Applies the four lines of the song's #spc block, trimmed, when one of their fields loses the focus.
    private void OnSongTagCommitted(object? sender, RoutedEventArgs e)
    {
        if (_isWritingFieldsFromSong)
        {
            return;
        }

        _song.SpcTags = new SpcTags(TrimmedText(SongTitleBox), TrimmedText(SongAuthorBox), TrimmedText(SongGameBox), TrimmedText(SongCommentBox));
        MarkModified();
    }

    // Writes the title into the title bar as it is typed.
    private void OnSongTitleTyped(object? sender, TextChangedEventArgs e)
    {
        ShowTitleBar();
    }

    // A field's text without the spaces around it, empty where it holds nothing.
    private static string TrimmedText(TextBox box)
    {
        return box.Text?.Trim() ?? "";
    }

    // Applies the sample path, normalized and written back into its field, when the field loses the focus.
    private void OnSamplePathCommitted(object? sender, RoutedEventArgs e)
    {
        if (_isWritingFieldsFromSong)
        {
            return;
        }

        string path = DiskNameRules.Normalize(SamplePathBox.Text);
        FlagGuard.RunWithFlagSet(ref _isWritingFieldsFromSong, () => SamplePathBox.Text = path);
        _song.SamplePath = path;
        MarkModified();
    }

    // Turns looping on or off, redrawing the loop marker, as an undo step of its own.
    private void OnLoopToggled(object? sender, RoutedEventArgs e)
    {
        if (_isWritingFieldsFromSong)
        {
            return;
        }

        _song.IsLooping = LoopSongCheck.IsChecked == true;
        ShowMarkerSwitch();
    }

    // Makes an edit that changes how ticks map to time and records it, keeping a running playhead on the tick it had reached.
    private void ApplyTimingEdit(object? source, Action edit)
    {
        double? reachedTick = PlayingTick();
        edit();
        MarkModified(source, reachedTick is double tick ? SamplesAtTick(tick) : null);
    }

    // The tick a running song has reached under the tempo map as it stands, or null where nothing is playing.
    private double? PlayingTick()
    {
        if (_audio is not { } audio || !audio.Mixer.IsPlaying)
        {
            return null;
        }

        return TickAtMixerPosition(audio);
    }

    // When a tick falls, in output samples, under the song as it now stands.
    private long SamplesAtTick(double tick)
    {
        return TempoMap.For(_song).SampleAt(tick);
    }

    // Writes a song value moved by a key's step and held to its range into the value's field, which applies it as typing would.
    private static void StepSongValueField(TextBox field, int songValue, ValueRange range, KeyEventArgs e)
    {
        StepSongValueField(field, step => range.Clamp(songValue + step), e);
    }

    // Writes the value a key's step moves a song value to into the value's field, which applies it as typing would.
    private static void StepSongValueField(TextBox field, Func<int, int> valueSteppedBy, KeyEventArgs e)
    {
        if (!FieldSteps.TryTakeStep(e, out int step))
        {
            return;
        }

        FieldInput.WriteValue(field, valueSteppedBy(step));
    }

    // Reads the AddmusicK install's sample groups and ARAM budget, or forgets them where no install is set, and forgets the SMW samples and sample ids
    // read out of its groups.
    private void ReadAmkInstall()
    {
        string? directory = AppPaths.AmkDirectory;
        _installGroups = string.IsNullOrEmpty(directory) ? null : SampleGroups.ReadGroups(directory);
        _smwSamplesByGroup.Clear();
        _smwSampleIdsByGroup.Clear();
        _aramInstall = AramBudget.ReadInstall(directory);
        ReadGlobalSongsEchoBuffer(directory);
    }

    // Reads the largest echo buffer of the install's global songs off the UI thread into the budget and counts the ARAM rows again with it, unless the
    // install was read again meanwhile.
    private async void ReadGlobalSongsEchoBuffer(string? directory)
    {
        if (string.IsNullOrEmpty(directory) || _aramInstall is not { } install)
        {
            return;
        }

        int globalSongsEchoBufferBytes = await Task.Run(() => AramBudget.GlobalSongsEchoBufferBytes(directory));
        if (!ReferenceEquals(_aramInstall, install))
        {
            return;
        }

        _aramInstall = install with { GlobalSongsEchoBufferBytes = globalSongsEchoBufferBytes };
        ShowAramTotals();
    }

    // Reads the install's groups and budget afresh and shows the song's SMW instruments and the Song panel in step with them.
    private void ReadAndShowInstall()
    {
        ReadAmkInstall();
        LoadSmwSamples();
        RefreshInstruments();
        ShowSongPanelFields();
        ShowAramTotals();
    }

    // Reads the install afresh and brings the Song panel in step with it, saying in a box over the owner where the song's group is not one of its groups.
    private async void AdoptInstallGroups(Window owner)
    {
        ReadAndShowInstall();
        if (MissingSampleGroupSentence(_song) is { } missingMessage)
        {
            await MessageDialog.Show(owner, missingMessage, WindowTitles.Notices.SampleGroupMissing);
        }
    }

    // Runs AddmusicK over the install with nothing to compile behind a box that waits for it, saying so where AddmusicK could not start and showing what
    // it printed where it ran and failed.
    private async Task BuildAmkInstall(Window owner)
    {
        string? directory = AppPaths.AmkDirectory;
        try
        {
            await MessageDialog.ShowWhile(owner, QuestionsAndNotices.WaitingForAmk, WindowTitles.Notices.RunningAmk, () => AmkBuild.BuildDriver(directory));
        }
        catch (AmkRunException exception) when (exception.AmkOutput is { } amkOutput)
        {
            await AmkOutputWindow.Show(owner, amkOutput);
        }
        catch (AmkRunException exception) when (!exception.HasAmkRun)
        {
            await MessageDialog.Show(owner, exception.Message, WindowTitles.Failures.FailedToRunAmk);
        }
        catch (AmkRunException)
        {
        }
    }

    // Builds the install again and reads its groups and budget afresh, its boxes standing over a window.
    private async Task RebuildAmkInstall(Window owner)
    {
        await BuildAmkInstall(owner);
        AdoptInstallGroups(owner);
    }

    // Builds the install again and reads its groups and budget afresh, from the ARAM USAGE header's button.
    private async void OnAramRefreshClick(object? sender, RoutedEventArgs e)
    {
        await RebuildAmkInstall(this);
    }

    // Shows the rows that need an install and the ARAM readout where there is one to read, counting the readout again once the edits pause.
    private void ShowAramTotals()
    {
        InstallRows.IsVisible = !string.IsNullOrEmpty(AppPaths.AmkDirectory);
        AramSection.IsVisible = InstallRows.IsVisible && _aramInstall is not null;
        if (!AramSection.IsVisible)
        {
            return;
        }

        _aramCountTimer.Stop();
        _aramCountTimer.Start();
    }

    // Counts a copy of the song against the install off the UI thread and writes the ARAM rows, counting again instead where the song changed meanwhile.
    private async void CountAramUsage()
    {
        _aramCountTimer.Stop();
        if (!AramSection.IsVisible || _aramInstall is not { } install)
        {
            return;
        }

        if (_isAramCounting)
        {
            _isAramRecountWanted = true;
            return;
        }

        _isAramCounting = true;
        var songCopy = _song.Copy();
        var exportOptions = _exportOptions;
        bool isSampleGroupCounted = MissingSampleGroupSentence(_song) is null;

        // The ARAM the song the export writes for the copy uses and its song data's bytes, or null bytes where the export refuses it, counted off the UI thread.
        var (usage, songDataBytes) = await Task.Run(() =>
        {
            var writtenSong = SampleTrim.SongToExport(songCopy, exportOptions);
            int? countedBytes = SongDataBytesIfExportable(writtenSong, exportOptions);
            return (AramBudget.Usage(writtenSong, install, countedBytes ?? 0), countedBytes);
        });

        _isAramCounting = false;
        if (_isAramRecountWanted)
        {
            _isAramRecountWanted = false;
            CountAramUsage();
            return;
        }

        ShowAramUsage(install.ProgramParts, usage, songDataBytes, isSampleGroupCounted);
    }

    // The bytes a song's compiled data takes under the export's options, or null where the export refuses the song.
    private static int? SongDataBytesIfExportable(Song song, MmlExportOptions exportOptions)
    {
        try
        {
            return SongDataSize.BytesOfSong(song, exportOptions);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    // Writes the nine ARAM rows, the song data's own size after its rounded one and the count of samples in the Samples row's label, the song data, the
    // sample table, the sample group and the free room showing the uncounted mark where what they depend on was not counted.
    private void ShowAramUsage(AmkProgramParts programParts, AramUsage usage, int? songDataBytes, bool isSampleGroupCounted)
    {
        bool isFreeCounted = songDataBytes is not null && isSampleGroupCounted;
        AramAddmusicKText.Text = $"{programParts.AddmusicKBytes}";
        AramSoundEffectsText.Text = $"{programParts.SoundEffectBytes}";
        AramGlobalSongsText.Text = $"{programParts.GlobalSongBytes}";
        AramSongDataText.Text = songDataBytes is int ownBytes ? WithBracketedFigure($"{usage.SongDataBytes}", ownBytes) : ValueTexts.NoValue;
        AramSampleTableText.Text = isSampleGroupCounted ? $"{usage.SampleTableBytes}" : ValueTexts.NoValue;
        AramGroupText.Text = isSampleGroupCounted ? $"{usage.SampleGroupBytes}" : ValueTexts.NoValue;
        AramSamplesRow.Label = WithBracketedFigure(AramSamplesLabel, usage.SampleCount);
        AramSamplesText.Text = $"{usage.SampleBytes}";
        AramEchoText.Text = $"{usage.EchoBufferBytes}";
        AramFreeText.Text = isFreeCounted ? $"{usage.FreeBytes}" : ValueTexts.NoValue;
        AramFreeText.Classes.Set(ThemeClasses.Negative, isFreeCounted && usage.FreeBytes < 0);
    }

    // An ARAM row's text with a second figure in brackets after it.
    private static string WithBracketedFigure(string text, int figure)
    {
        return $"{text} ({figure})";
    }
}
