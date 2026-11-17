using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using SpcStudio.Audio;
using SpcStudio.Controls;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio;

// The channel buttons beside the roll, adding and deleting extra channels, and every audition the window sounds - a press on the grid, a key beside it,
// the instrument panel's play button, the note panel's play button and the play key's auditions.
public partial class MainWindow
{
    private const int InstrumentAuditionSemitone = 0;
    private const int HeldAuditionGateSamples = 0;
    private const string WarmUpInstrumentName = "Warm-up";
    private const int WarmUpChannel = 0;
    private const int WarmUpBufferLength = 1024;
    private static readonly TimeSpan ShortestEndlessAuditionLoop = TimeSpan.FromSeconds(0.25);
    private static readonly TimeSpan LongestSettledEndlessAuditionHold = TimeSpan.FromSeconds(2);

    private readonly List<ChannelButton> _channelButtons = [];
    private readonly Stopwatch _timeSinceEndlessAuditionKeyOn = new();
    private bool _isInstrumentSoundingEndlessly;
    private bool _isAuditionKeyHeld;

    // The stretch the note panel's run is sounding - its channel, its notes' start ticks and whether it starts over at its end - or null while none is.
    private (int Channel, int[] Ticks, bool IsLooping)? _runStretch;

    // The instrument each held audition is holding - its song, its id and the pitch it sounds at - where it is holding one.
    private readonly Dictionary<AuditionHold, (Song Song, int InstrumentId, int Semitone)> _heldInstrumentAuditions = [];

    // How long after its key-on the instrument sounding endlessly settled on its final level, or null while its level still moves.
    private TimeSpan? _endlessAuditionSettledAfter;

    // Points the per-channel lanes under the roll at the selected channel, closing the cell editor where it was open on one of them.
    private void ShowChannel()
    {
        if (_cellLane is ChannelChangeLane)
        {
            CloseCell();
        }

        ChannelVolumes.Channel = Roll.SelectedChannel;
        Pans.Channel = Roll.SelectedChannel;
        ChannelEchoes.Channel = Roll.SelectedChannel;
        ShowChannelPanel();
    }

    // Puts one button per channel of the song beside the roll, each with its own mute and solo toggles and an extra channel's delete, and the add
    // button under them while the song has room for another channel.
    private void BuildChannelButtons()
    {
        ChannelButtons.Children.Clear();
        _channelButtons.Clear();
        for (int channel = 0; channel < _song.ChannelCount; channel++)
        {
            var button = ChannelButton.Create(channel, Tooltips.MuteChannel, Tooltips.PlayChannelSolo, OnMuteToggled, OnSoloToggled, SelectChannel, OnDeleteChannel);
            _channelButtons.Add(button);
            ChannelButtons.Children.Add(button.Button);
        }

        if (!_song.CanAddChannel)
        {
            return;
        }

        var addButton = ChannelButton.CreateIconed(AppIcons.Plus, Palette.SilverGray, OnAddChannel);
        addButton.ShowState(isSelected: false);
        ToolTip.SetTip(addButton.Button, Tooltips.AddExtraChannel);
        ChannelButtons.Children.Add(addButton.Button);
    }

    // Makes a channel the one being edited, and brings the buttons and the per-channel lanes to it, scrolling its button into view once the buttons are laid out.
    private void SelectChannel(int channel)
    {
        Roll.SelectedChannel = channel;
        UpdateChannelButtons();
        ShowChannel();
        Dispatcher.UIThread.Post(_channelButtons[channel].Button.BringIntoView, DispatcherPriority.Loaded);
    }

    // Adds an extra channel after the last and selects it, as an undo step of its own.
    private void OnAddChannel()
    {
        _song.AddChannel();
        MarkModified();
        SelectChannel(_song.ChannelCount - 1);
    }

    // Deletes an extra channel with everything on it, asking first where it holds notes, the selection moving to the channel before it where it was on that channel or after it.
    private async void OnDeleteChannel(int channel)
    {
        if (!await IsChannelDeleteConfirmed(channel))
        {
            return;
        }

        Roll.ClearSelection();

        int channelSelectedAfterDelete = Roll.SelectedChannel >= channel ? Roll.SelectedChannel - 1 : Roll.SelectedChannel;
        _song.DeleteChannel(channel);
        SelectChannel(channelSelectedAfterDelete);
        KeyOffExtraChannels();
        MarkModified();
    }

    // Whether a channel may go: always where it holds no notes, and otherwise where the user answers yes to deleting those notes with it.
    private async Task<bool> IsChannelDeleteConfirmed(int channel)
    {
        int noteCount = _song.Notes(channel).Count;
        if (noteCount == 0)
        {
            return true;
        }

        return await MessageDialog.IsAnsweredYes(this, QuestionsAndNotices.DeleteChannel(Song.ChannelNumberOf(channel), noteCount), WindowTitles.Notices.DeleteChannel);
    }

    // Moves the selected channel to the song's last where the song no longer has it, and brings the per-channel lanes to it.
    private void KeepSelectedChannelInSong()
    {
        if (Roll.SelectedChannel < _song.ChannelCount)
        {
            return;
        }

        Roll.SelectedChannel = _song.ChannelCount - 1;
        ShowChannel();
    }

    // Keys off the voice of every extra channel there can be.
    private void KeyOffExtraChannels()
    {
        for (int channel = Song.DriverChannelCount; channel < Song.MaxChannelCount; channel++)
        {
            _audio?.Mixer.KeyOffChannel(channel);
        }
    }

    // Mutes a channel or unmutes it, from its mute toggle.
    private void OnMuteToggled(int channel, bool isMuted)
    {
        _song.Muting.SetMuted(channel, isMuted);
        ApplyMuteEdit();
    }

    // Solos a channel or takes its solo off, from its solo toggle.
    private void OnSoloToggled(int channel, bool isSoloed)
    {
        _song.Muting.SetSoloed(channel, isSoloed);
        ApplyMuteEdit();
    }

    // Records a mute or solo edit, keys off whatever it has silenced and redraws the buttons.
    private void ApplyMuteEdit()
    {
        MarkModified();
        SilenceMutedVoices();
        UpdateChannelButtons();
    }

    // Keys off the voice of every channel that does not sound as the song now stands.
    private void SilenceMutedVoices()
    {
        if (_audio is null)
        {
            return;
        }

        for (int channel = 0; channel < _song.ChannelCount; channel++)
        {
            if (_song.Muting.IsSilenced(channel))
            {
                _audio.Mixer.KeyOffChannel(channel);
            }
        }
    }

    // Draws each channel button selected or not, its toggles saying the song's mute and solo, building the buttons again where the song's channel count has changed.
    private void UpdateChannelButtons()
    {
        if (_channelButtons.Count != _song.ChannelCount)
        {
            BuildChannelButtons();
        }

        for (int channel = 0; channel < _channelButtons.Count; channel++)
        {
            _channelButtons[channel].ShowColor(ChannelPalette.ColorOfChannel(_song, channel));
            _channelButtons[channel].ShowState(channel == Roll.SelectedChannel, _song.Muting.IsMuted(channel), _song.Muting.IsSoloed(channel));
        }
    }

    // Steps the selected channel by a wheel notch, wrapping at both ends.
    private void CycleChannel(PointerWheelEventArgs e)
    {
        int step = -FieldSteps.StepOf(e);
        if (step == 0)
        {
            return;
        }

        SelectChannel((Roll.SelectedChannel + step + _song.ChannelCount) % _song.ChannelCount);
        e.Handled = true;
    }

    // Sounds a note pressed on the grid at its place in the song, held for as long as the press.
    private void OnNoteAuditioned(Note note)
    {
        AuditionSongNote(note, Roll.SelectedChannel);
    }

    // Sounds a key beside the roll as its instrument at that pitch, held for as long as the press.
    private void OnKeyAuditioned(Note note)
    {
        if (_song.InstrumentOf(note) is { } played)
        {
            AuditionInstrument(_song, played, note.Semitone);
        }
    }

    // The instrument panel's play button pressed, or its shortcut: ends the instrument sounding endlessly, or else sounds the current instrument
    // endlessly at its audition pitch.
    private void OnInstrumentPlayPressed()
    {
        if (_isInstrumentSoundingEndlessly)
        {
            StopEndlessAudition();
            return;
        }

        KeyOnCurrentInstrument();
        if (_audio?.Mixer.IsHeldNoteSounding(AuditionHold.Endless) != true)
        {
            StopEndlessAudition();
            return;
        }

        _isInstrumentSoundingEndlessly = true;
        _endlessAuditionTimer.Start();
        ShowInstrumentPlayButton();
    }

    // Keys the instrument sounding endlessly on again, never sooner than the shortest loop after its key-on: once it has died out on its own, or once
    // it has held its final level for the longest a settled sound is held.
    private void FollowEndlessAudition()
    {
        if (_audio is not { } audio)
        {
            StopEndlessAudition();
            return;
        }

        var timeSinceKeyOn = _timeSinceEndlessAuditionKeyOn.Elapsed;
        bool isSettled = audio.Mixer.IsHeldNoteSettled(AuditionHold.Endless);
        _endlessAuditionSettledAfter = isSettled ? _endlessAuditionSettledAfter ?? timeSinceKeyOn : null;
        if (timeSinceKeyOn < ShortestEndlessAuditionLoop)
        {
            return;
        }

        bool hasDiedOut = !audio.Mixer.IsHeldNoteSounding(AuditionHold.Endless);
        bool hasHeldSettledLongest = timeSinceKeyOn - _endlessAuditionSettledAfter >= LongestSettledEndlessAuditionHold;
        if (hasDiedOut || hasHeldSettledLongest)
        {
            KeyOnCurrentInstrument();
        }
    }

    // Keys the instrument the instrument panel now shows on where the instrument sounding endlessly is another one.
    private void FollowCurrentInstrumentWithEndlessAudition()
    {
        if (!_isInstrumentSoundingEndlessly || _heldInstrumentAuditions.TryGetValue(AuditionHold.Endless, out var held) && held.Song == _song && held.InstrumentId == _currentInstrumentId)
        {
            return;
        }

        KeyOnCurrentInstrument();
    }

    // Keys the current instrument on afresh as the endless audition, at its audition pitch, timing its loop from there, and ends the endless audition
    // where there is no instrument for it to sound.
    private void KeyOnCurrentInstrument()
    {
        if (_audio is null || CurrentInstrument is not { } instrument || !TryKeyOnInstrument(_audio.Mixer, AuditionHold.Endless, _song, instrument, InstrumentAuditionSemitone))
        {
            StopEndlessAudition();
            return;
        }

        _timeSinceEndlessAuditionKeyOn.Restart();
        _endlessAuditionSettledAfter = null;
    }

    // Keys the instrument sounding endlessly off, forgets it and puts the instrument panel's play button back.
    private void StopEndlessAudition()
    {
        LetGoOfHeldAudition(AuditionHold.Endless);
        _isInstrumentSoundingEndlessly = false;
        _endlessAuditionTimer.Stop();
        ShowInstrumentPlayButton();
    }

    // Shows the instrument panel's play button's glyph and tooltip, which say what a press on it does.
    private void ShowInstrumentPlayButton()
    {
        var (icon, tooltip) = StopOrPlayFace(_isInstrumentSoundingEndlessly, Tooltips.StopInstrumentPlayback, Tooltips.PlayInstrumentAltSpace);
        Instruments.ShowPlayButton(icon, tooltip);
    }

    // The glyph and the tooltip of a play button that Shift turns into a loop, saying what a press on it does: a stop while what it started sounds,
    // whatever Shift is doing, a loop while Shift is held, and a play otherwise.
    private (Geometry Icon, string Tooltip) PlayButtonFace(bool isSounding, string stopTooltip, string loopTooltip, string playTooltip)
    {
        if (!isSounding && _heldShiftKey.IsHeld)
        {
            return (AppIcons.Repeat, loopTooltip);
        }

        return StopOrPlayFace(isSounding, stopTooltip, playTooltip);
    }

    // The glyph and the tooltip of a play button, saying what a press on it does: a stop while what it started sounds, and a play otherwise.
    private static (Geometry Icon, string Tooltip) StopOrPlayFace(bool isSounding, string stopTooltip, string playTooltip)
    {
        return isSounding ? (AppIcons.Stop, stopTooltip) : (AppIcons.Play, playTooltip);
    }

    // Sounds the instrument of a row of either instrument drop-down for as long as the row's play button is held.
    private void AuditionInstrumentRow(int index)
    {
        if (_instrumentIdsByRow[index] is not int id || _song.InstrumentById(id) is not { } instrument)
        {
            return;
        }

        AuditionInstrumentAtAuditionPitch(_song, instrument);
    }

    // Sounds an instrument of a song at the pitch an instrument is auditioned at, held until it is let go of.
    private void AuditionInstrumentAtAuditionPitch(Song song, Instrument instrument)
    {
        AuditionInstrument(song, instrument, InstrumentAuditionSemitone);
    }

    // Sounds an instrument of a song at a pitch as the instrument says it, held for as long as the press, beside the instrument sounding endlessly.
    private void AuditionInstrument(Song song, Instrument instrument, int semitone)
    {
        if (MixerForPressAudition() is { } mixer)
        {
            TryKeyOnInstrument(mixer, AuditionHold.Press, song, instrument, semitone);
        }
    }

    // Keys an instrument of a song on as a held audition at a pitch as the instrument says it, held until released, answering whether it could be played.
    private bool TryKeyOnInstrument(BrrMixer mixer, AuditionHold hold, Song song, Instrument instrument, int semitone)
    {
        if (InstrumentAuditionOf(mixer, song, instrument, semitone) is not { } played)
        {
            return false;
        }

        mixer.PlayNote(hold, played);
        _heldInstrumentAuditions[hold] = (song, instrument.Id, semitone);
        return true;
    }

    // Hands each instrument a held audition is holding what the instrument now says, where its song still holds it.
    private void RefreshInstrumentAudition()
    {
        if (_audio is null)
        {
            return;
        }

        foreach (var (hold, (song, instrumentId, semitone)) in _heldInstrumentAuditions)
        {
            if (song.InstrumentById(instrumentId) is { } instrument && InstrumentAuditionOf(_audio.Mixer, song, instrument, semitone) is { } played)
            {
                _audio.Mixer.ReplaceHeldNote(hold, played);
            }
        }
    }

    // What an instrument of a song is auditioned as at a pitch, held for as long as the press, with the mixer's echo put where the song starts, or null where
    // the note cannot be played.
    private AuditionNote? InstrumentAuditionOf(BrrMixer mixer, Song song, Instrument instrument, int semitone)
    {
        var note = AuditionLanes.InstrumentNote(instrument, semitone);
        if (!song.IsNotePlayable(note))
        {
            return null;
        }

        var lanes = AuditionLanes.ForInstrument(song, Roll.SelectedChannel, note);
        PrepareAuditionEchoAt(mixer, lanes, Song.TickBeforeSong);
        var echo = lanes.Echo.StateAt(Song.TickBeforeSong);
        return AuditionNoteOf(song, instrument, note, lanes, instrument.Envelope, echo, HeldAuditionGateSamples);
    }

    // Sounds a note of the song where it sits on a channel, held for as long as the press, beside the instrument sounding endlessly.
    private void AuditionSongNote(Note note, int channel)
    {
        if (MixerForPressAudition() is { } mixer)
        {
            PlaySongNoteAudition(mixer, _song, note, channel);
        }
    }

    // The mixer a press sounds its audition on, the instrument the last press held forgotten, or null with no audio.
    private BrrMixer? MixerForPressAudition()
    {
        if (_audio is null)
        {
            return null;
        }

        _heldInstrumentAuditions.Remove(AuditionHold.Press);
        return _audio.Mixer;
    }

    // Sounds a note of a song where it sits on a channel, held for as long as the press, on any mixer.
    private static void PlaySongNoteAudition(BrrMixer mixer, Song song, Note note, int channel)
    {
        if (SongNoteAuditionOf(song, note, channel, HeldAuditionGateSamples) is not { } played)
        {
            return;
        }

        PrepareAuditionEchoAt(mixer, played.Lanes!, note.StartTick);
        mixer.PlayNote(AuditionHold.Press, played);
    }

    // What a note of a song is auditioned as where it sits on a channel, gated after a number of samples or held where that number is none, or null where it has nothing to be played by.
    private static AuditionNote? SongNoteAuditionOf(Song song, Note note, int channel, int gateSamples)
    {
        if (!song.IsNotePlayable(note) || song.InstrumentOf(note) is not { } instrument)
        {
            return null;
        }

        int tick = note.StartTick;
        var lanes = AuditionLanes.For(song, channel, note);
        return AuditionNoteOf(song, instrument, note, lanes, lanes.VoiceLanes.Envelope.StateAt(tick), lanes.Echo.StateAt(tick), gateSamples);
    }

    // What a note keys on with, read off its lanes at its own tick, under the envelope, the echo and the gate its caller resolved.
    private static AuditionNote AuditionNoteOf(Song song, Instrument instrument, Note note, AuditionLanes lanes, EnvelopeState envelope, EchoState echo, int gateSamples)
    {
        int tick = note.StartTick;
        var voiceLanes = lanes.VoiceLanes;
        var keyOn = VoiceKeyOn.Of(song, instrument, note, voiceLanes, envelope, gateSamples);
        return new AuditionNote(keyOn, echo.DoesSendFrom(lanes.Channel), voiceLanes.Vibrato.StateAt(tick), voiceLanes.Tremolo.StateAt(tick), lanes);
    }

    // Puts the mixer's echo where a song's lanes hold it at a tick, which is what an audition is heard under.
    private static void PrepareAuditionEchoAt(BrrMixer mixer, AuditionLanes lanes, int tick)
    {
        mixer.SetAuditionEcho(lanes.Echo.StateAt(tick));
    }

    // Places a note in a throwaway song and auditions it once on a mixer no device reads.
    private void WarmUpAudition()
    {
        if (_audio is null)
        {
            return;
        }

        var song = new Song();
        var instrument = song.CreateInstrument(WarmUpInstrumentName, song.CreateSample());
        var note = PianoRoll.FreshNote(song, tick: 0, length: Song.RollLengthStepTicks, semitone: 0, instrument.Id, SnesTiming.DefaultGate, SnesVolume.DefaultVelocity, SnesPitch.MinFineTune);
        song.PlaceNotes([(WarmUpChannel, note)]);

        var mixer = new BrrMixer(_audio.SampleRate);
        PlaySongNoteAudition(mixer, song, note, WarmUpChannel);
        var buffer = new float[WarmUpBufferLength];
        mixer.Read(buffer, 0, buffer.Length);
    }

    // Keys the audition a press holds off, which is what letting go of the button that started it does.
    private void ReleaseAudition()
    {
        LetGoOfHeldAudition(AuditionHold.Press);
    }

    // Keys a held audition off and forgets the instrument it was holding.
    private void LetGoOfHeldAudition(AuditionHold hold)
    {
        _audio?.Mixer.ReleaseAudition(hold);
        _heldInstrumentAuditions.Remove(hold);
    }

    // Plays the selected stretch once, or over and over while Shift is held, or stops the run sounding - the note panel's play button.
    private void OnPlayNoteClick(object? sender, RoutedEventArgs e)
    {
        ToggleNoteRun(isLooping: _heldShiftKey.IsHeld);
    }

    // The selected notes as a stretch the note panel can play - one channel's, in time order, with no note of that channel between the first and the last left unselected - or null.
    private (int Channel, List<Note> Notes)? SelectedStretch()
    {
        var selection = Roll.Selection;
        if (selection.Count == 0)
        {
            return null;
        }

        int channel = selection[0].Channel;
        if (selection.Any(selected => selected.Channel != channel))
        {
            return null;
        }

        var selectedNotesInTimeOrder = selection.Select(selected => selected.Note).OrderBy(note => note.StartTick).ToList();
        var selectedStartTicks = selectedNotesInTimeOrder.Select(note => note.StartTick).ToHashSet();

        bool hasUnselectedNoteBetween = _song.Notes(channel).Any(placed =>
            placed.StartTick > selectedNotesInTimeOrder[0].StartTick && placed.StartTick < selectedNotesInTimeOrder[^1].StartTick && !selectedStartTicks.Contains(placed.StartTick));

        return hasUnselectedNoteBetween ? null : (channel, selectedNotesInTimeOrder);
    }

    // Starts the selected stretch's run, once or over and over, or stops the run where one is sounding.
    private void ToggleNoteRun(bool isLooping)
    {
        if (_runStretch is not null)
        {
            StopNoteRun();
            return;
        }

        if (_audio is null || SelectedStretch() is not (int channel, var notes) || RunNotesOf(_audio.Mixer, channel, notes, isLooping) is not { } runNotes)
        {
            return;
        }

        _audio.Mixer.StartNoteRun(runNotes, isLooping);
        _runStretch = RunStretchOf(channel, notes, isLooping);
        _noteRunTimer.Start();
        ShowNoteRunPosition();
        ShowPlayNoteButton();
    }

    // The notes of a stretch of a channel as the run sounds them - each gated as the song gates it, or as the loop does, and bound where the note before
    // runs into it - with the mixer's echo put where the first sits, or null where a note of it has nothing to be played by.
    private List<RunNote>? RunNotesOf(BrrMixer mixer, int channel, IReadOnlyList<Note> stretch, bool isLooping)
    {
        var runNotes = new List<RunNote>(stretch.Count);
        for (int index = 0; index < stretch.Count; index++)
        {
            var note = stretch[index];
            int samplesUntilKeyOff = isLooping ? SequenceBuilder.LoopedStretchGateSamples(_song, _exportOptions, channel, stretch, index) : SequenceBuilder.AuditionGateSamples(_song, _exportOptions, channel, note);
            if (SongNoteAuditionOf(_song, note, channel, samplesUntilKeyOff) is not { } played)
            {
                return null;
            }

            runNotes.Add(new RunNote(played, note.StartTick - stretch[0].StartTick, LegatoToggles.IsBoundFromPreviousInLoop(stretch, index)));
        }

        PrepareAuditionEchoAt(mixer, runNotes[0].Lanes, stretch[0].StartTick);
        return runNotes;
    }

    // Ends the note panel's run, where one is sounding, its note keyed off.
    private void StopNoteRun()
    {
        if (_runStretch is null)
        {
            return;
        }

        _audio?.Mixer.StopNoteRun();
        ShowNoteRunEnded();
    }

    // Moves the audition marker with the run, or takes it away once the run has played to its end.
    private void ShowNoteRunPosition()
    {
        if (_audio?.Mixer.NoteRunTick is not double tick)
        {
            ShowNoteRunEnded();
            return;
        }

        Markers.AuditionStep = tick / Song.GridTicks;
    }

    // Forgets the run, takes the audition marker away and puts the play button back.
    private void ShowNoteRunEnded()
    {
        _runStretch = null;
        _noteRunTimer.Stop();
        Markers.AuditionStep = null;
        ShowPlayNoteButton();
    }

    // Shows the note panel's play button where one channel's notes are selected, its glyph and tooltip saying what a press on it does: a stop while a
    // run sounds, a loop while Shift is held, and a play otherwise.
    private void ShowPlayNoteButton()
    {
        var stretch = SelectedStretch();
        PlayNoteButton.IsVisible = stretch is not null;
        bool isOneNote = stretch?.Notes.Count == 1;
        (PlayNoteIcon.Data, string tooltip) = PlayButtonFace(_runStretch is not null, Tooltips.StopNotePlayback, isOneNote ? Tooltips.LoopNote : Tooltips.LoopNotes,
            isOneNote ? Tooltips.PlayNote : Tooltips.PlayNotes);
        ToolTip.SetTip(PlayNoteButton, tooltip);
    }

    // Hands a sounding run what its stretch now says.
    private void RefreshNoteRun()
    {
        if (_audio is null || _runStretch is not { } run || SelectedStretch() is not (int channel, var notes))
        {
            return;
        }

        if (RunNotesOf(_audio.Mixer, channel, notes, run.IsLooping) is { } runNotes)
        {
            _audio.Mixer.ReplaceNoteRun(runNotes);
        }
    }

    // Keeps the run on its stretch as the selection changes, and ends it once the stretch is no longer what is selected.
    private void FollowNoteRun()
    {
        if (_runStretch is not { } run)
        {
            return;
        }

        if (SelectedStretch() is not (int channel, var notes) || !IsRunStretch(channel, notes, run.Channel, run.Ticks))
        {
            StopNoteRun();
            return;
        }

        _runStretch = RunStretchOf(channel, notes, run.IsLooping);
        RefreshNoteRun();
    }

    // What the window keeps of a stretch the run sounds: its channel, its notes' start ticks and whether the run starts over at its end.
    private static (int Channel, int[] Ticks, bool IsLooping) RunStretchOf(int channel, IReadOnlyList<Note> notes, bool isLooping)
    {
        return (channel, [.. notes.Select(note => note.StartTick)], isLooping);
    }

    // Whether a selected stretch is the run's: as many notes, and every place a note of the run stood holding one of them or nothing at all.
    private bool IsRunStretch(int channel, IReadOnlyList<Note> notes, int runChannel, int[] runTicks)
    {
        if (notes.Count != runTicks.Length)
        {
            return false;
        }

        var runChannelNotes = _song.Notes(runChannel);
        return runTicks.All(tick =>
            (channel == runChannel && notes.Any(note => note.StartTick == tick)) || !runChannelNotes.Any(placed => placed.StartTick == tick));
    }

    // Starts or stops an audition from the play key held with a modifier - the selected stretch once or over and over, or the instrument panel's play
    // button - answering whether the key was one of those; the key's own repeats while it is held do nothing.
    private bool TryHandleAuditionKey(KeyEventArgs e, IInputElement? focused)
    {
        bool isOnInstrument = Shortcuts.IsPressed(e, Shortcuts.PlayInstrument);
        if (!WindowKeys.IsPlayKey(e, focused) || !isOnInstrument && !Shortcuts.IsPressed(e, Shortcuts.PlaySelection, Shortcuts.PlaySelectionLooping))
        {
            return false;
        }

        e.Handled = true;
        if (_isAuditionKeyHeld)
        {
            return true;
        }

        _isAuditionKeyHeld = true;
        if (isOnInstrument)
        {
            OnInstrumentPlayPressed();
        }
        else
        {
            ToggleNoteRun(isLooping: Shortcuts.IsPressed(e, Shortcuts.PlaySelectionLooping));
        }

        return true;
    }

    // Lets go of the play key an audition was started or stopped from, so its next press starts or stops one again.
    private void ReleaseAuditionSpace()
    {
        _isAuditionKeyHeld = false;
    }

    // Ends every audition: the note panel's run, whatever a press or a key is holding and the instrument sounding endlessly.
    private void StopAllAuditions()
    {
        StopNoteRun();
        ReleaseAudition();
        StopEndlessAudition();
    }
}
