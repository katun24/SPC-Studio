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
    private const string WaitingForAmkMessage = "Waiting for AddmusicK to finish...";

    private bool _isWritingFieldsFromSong;
    private IReadOnlyList<SampleGroup>? _installGroups;
    private AramInstall? _aramInstall;
    private bool _isAramCounting;
    private bool _isAramRecountWanted;

    // Counts the ARAM rows once the edits have paused.
    private readonly DispatcherTimer _aramCountTimer = new() { Interval = TimeSpan.FromMilliseconds(AramCountPauseMilliseconds) };

    // Writes the song's tempo into the tempo field.
    private void ShowTempoField()
    {
        FlagGuard.RunWithFlagSet(ref _isWritingFieldsFromSong, () => TempoBox.Text = FieldInput.TextOf(_song.BaseTempo));
    }

    // Writes the song's song volume into its field.
    private void ShowMasterVolumeField()
    {
        FlagGuard.RunWithFlagSet(ref _isWritingFieldsFromSong, () => VolumeBox.Text = FieldInput.TextOf(_song.BaseMasterVolume));
    }

    // Writes the song's sample path, sample group, two options and four tags into the Song panel.
    private void ShowSongPanelFields()
    {
        FlagGuard.RunWithFlagSet(ref _isWritingFieldsFromSong, () =>
        {
            SamplePathBox.Text = _song.SamplePath;
            ShowSampleGroupChoices();
            LoopSongCheck.IsChecked = _song.IsLooping;
            TempoImmunityCheck.IsChecked = _song.HasTempoImmunity;
            ShowSpcTags();
        });
    }

    // Fills the Sample Group drop-down from the install and selects the song's group in it.
    private void ShowSampleGroupChoices()
    {
        var choices = SampleGroupChoicesOfInstall();
        SampleGroupBox.ItemsSource = choices;
        SampleGroupBox.SelectedItem = choices.FirstOrDefault(choice => choice.Name == _song.SampleGroup);
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
        return _installGroups?.FirstOrDefault(group => group.Name == song.SampleGroup);
    }

    // Sets the group the export's #samples block opens with, as an undo step of its own.
    private void OnSampleGroupSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_isWritingFieldsFromSong || SampleGroupBox.SelectedItem is not SampleGroupChoice choice)
        {
            return;
        }

        _song.SampleGroup = choice.Name;
        MarkModified(sender);
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

    // Ties the tempo's and the song volume's sliders to their fields, and gives each field its lane's default as the value a right-click puts it back at.
    private void WireSongValueFields()
    {
        SliderField.Tie(VolumeSlider, VolumeBox, SnesVolume.MasterVolumeRange);
        SliderField.Tie(TempoSlider, TempoBox, SnesTiming.TempoRange);
        FieldResets.ResetsTo(VolumeBox, () => Volumes.DefaultValue);
        FieldResets.ResetsTo(TempoBox, () => Tempos.DefaultValue);
    }

    // Applies the tempo as it is typed to the tempo lane's first region, a value the driver cannot take changing nothing.
    private void OnTempoChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isWritingFieldsFromSong)
        {
            return;
        }

        if (!FieldInput.TryReadValue(TempoBox.Text, SnesTiming.TempoRange, out int tempo))
        {
            return;
        }

        ApplyFirstRegionValue(Tempos, TempoBox, tempo);
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
        return (long)Math.Round(TempoMap.For(_song).SecondsAt(tick) * DspPoint.SampleRate);
    }

    // Steps the tempo field from the song's tempo by an arrow key or a wheel notch.
    private void OnTempoKeyDown(object? sender, KeyEventArgs e)
    {
        StepSongValueField(TempoBox, _song.BaseTempo, SnesTiming.TempoRange, e);
    }

    // Puts the song's tempo back in its field once the field loses the focus.
    private void OnTempoCommitted(object? sender, RoutedEventArgs e)
    {
        ShowTempoField();
    }

    // Applies the song volume as it is typed to the song volume lane's first region, a value out of range changing nothing.
    private void OnMasterVolumeChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isWritingFieldsFromSong)
        {
            return;
        }

        if (!FieldInput.TryReadValue(VolumeBox.Text, SnesVolume.MasterVolumeRange, out int volume))
        {
            return;
        }

        ApplyFirstRegionValue(Volumes, VolumeBox, volume);
    }

    // Steps the song volume field from the song's song volume by an arrow key or a wheel notch.
    private void OnMasterVolumeKeyDown(object? sender, KeyEventArgs e)
    {
        StepSongValueField(VolumeBox, _song.BaseMasterVolume, SnesVolume.MasterVolumeRange, e);
    }

    // Puts the song's song volume back in its field once the field loses the focus.
    private void OnMasterVolumeCommitted(object? sender, RoutedEventArgs e)
    {
        ShowMasterVolumeField();
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

    // Reads the AddmusicK install's sample groups and ARAM budget, or forgets them where no install is set.
    private void ReadAmkInstall()
    {
        string? directory = AppPaths.AmkDirectory;
        _installGroups = string.IsNullOrEmpty(directory) ? null : SampleGroups.ReadGroups(directory);
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

    // Reads the install's groups and budget afresh and shows the Song panel in step with them.
    private void ReadAndShowInstall()
    {
        ReadAmkInstall();
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

    // Runs AddmusicK over the install with nothing to compile behind a box that waits for it, saying so where AddmusicK could not start; what goes wrong
    // once it has started is its console window's to say.
    private async Task BuildAmkInstall(Window owner)
    {
        string? directory = AppPaths.AmkDirectory;
        try
        {
            await MessageDialog.ShowWhile(owner, WaitingForAmkMessage, WindowTitles.Notices.RunningAmk, () => AmkBuild.BuildDriver(directory));
        }
        catch (AmkRunException exception) when (!exception.HasAmkRun)
        {
            await MessageDialog.Show(owner, exception.Message, WindowTitles.Failures.FailedToRunAmk);
        }
        catch (AmkRunException)
        {
        }
    }

    // Builds the install again and reads its groups and budget afresh.
    private async void OnAramRefreshClick(object? sender, RoutedEventArgs e)
    {
        await BuildAmkInstall(this);
        AdoptInstallGroups(this);
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

        // The ARAM the copy uses and its song data's bytes, or null bytes where the export refuses it, counted off the UI thread.
        var (usage, songDataBytes) = await Task.Run(() =>
        {
            int? countedBytes = SongDataBytesIfExportable(songCopy, exportOptions);
            return (AramBudget.Usage(songCopy, install, countedBytes ?? 0), countedBytes);
        });

        _isAramCounting = false;
        if (_isAramRecountWanted)
        {
            _isAramRecountWanted = false;
            CountAramUsage();
            return;
        }

        ShowAramUsage(usage, songDataBytes is not null, isSampleGroupCounted);
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

    // Writes the five ARAM rows and the count of samples in the Samples row's label, the song data, the sample group and the free room showing the
    // uncounted mark where what they depend on was not counted.
    private void ShowAramUsage(AramUsage usage, bool isSongDataCounted, bool isSampleGroupCounted)
    {
        bool isFreeCounted = isSongDataCounted && isSampleGroupCounted;
        AramSongDataText.Text = isSongDataCounted ? $"{usage.SongDataBytes}" : ValueTexts.NoValue;
        AramSamplesRow.Label = $"{AramSamplesLabel} ({usage.SampleCount})";
        AramSamplesText.Text = $"{usage.SampleBytes}";
        AramGroupText.Text = isSampleGroupCounted ? $"{usage.SampleGroupBytes}" : ValueTexts.NoValue;
        AramEchoText.Text = $"{usage.EchoBufferBytes}";
        AramFreeText.Text = isFreeCounted ? $"{usage.FreeBytes}" : ValueTexts.NoValue;
        AramFreeText.Classes.Set(ThemeClasses.Negative, isFreeCounted && usage.FreeBytes < 0);
    }
}
