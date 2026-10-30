using SpcStudio.Model;

namespace SpcStudio.Audio;

// The mixer's auditions, on the voices above the song's: a note a press holds and the note panel's run of notes - each followed through the lanes it was played from for as long as its note lasts.
public sealed partial class BrrMixer
{
    private SnesVoice? _heldVoice;
    private AuditionNote? _heldNote;
    private AuditionWalk? _heldWalk;
    private NoteRun? _noteRun;

    // The tick of the song the note panel's run has reached, part way to the next where the driver loop is between two, or null while none is sounding.
    public double? NoteRunTick
    {
        get
        {
            lock (_lock)
            {
                return _noteRun is { } run ? run.Walk.Tick + _tickAccumulator / (double)(byte.MaxValue + 1) : null;
            }
        }
    }

    // Whether the note a press holds still sounds: keyed on, and not fallen silent for good with nothing left of its lanes to raise it again.
    public bool IsHeldNoteSounding
    {
        get
        {
            lock (_lock)
            {
                if (_heldVoice is not { } voice)
                {
                    return false;
                }

                bool hasLanesAhead = _heldWalk is { } walk && walk.Tick < walk.Lanes.EndTick;
                return voice.IsActive && (hasLanesAhead || !voice.IsSilentForGood);
            }
        }
    }

    // Keys a note on for a press to hold, in place of whatever the press was holding.
    public void PlayNote(AuditionNote note)
    {
        lock (_lock)
        {
            KeyOffHeldVoices();
            HoldNote(note);
        }
    }

    // Starts the note panel's run: its notes keyed on in turn on a voice of its own, each at its own tick, once or starting over every time the last one ends, replacing a run sounding.
    public void StartNoteRun(IReadOnlyList<RunNote> notes, bool isLooping)
    {
        lock (_lock)
        {
            EndNoteRun();
            _noteRun = new NoteRun(new AuditionWalk(FreeAuditionVoice()), notes, isLooping);
            KeyOnRunNote(_noteRun);
        }
    }

    // Points a sounding run at what its notes now say, heard at once on the note sounding and whole from the next note.
    public void ReplaceNoteRun(IReadOnlyList<RunNote> notes)
    {
        lock (_lock)
        {
            if (_noteRun is not { } run)
            {
                return;
            }

            var replacedNote = run.Notes[run.NextNoteIndex - 1].Note;
            run.Notes = notes;
            run.NextNoteIndex = notes.Count(runNote => runNote.OffsetTicks <= run.TicksSounded);

            var soundingNote = notes[run.NextNoteIndex - 1];
            var lanes = soundingNote.Lanes;
            int tickReached = lanes.StartTick + run.TicksSounded - soundingNote.OffsetTicks;
            if (tickReached >= lanes.EndTick)
            {
                run.Walk.Follow(lanes, soundingNote.Note.KeyOn.Level, tickReached);
                return;
            }

            ReaimAudition(run.Walk, replacedNote.KeyOn, soundingNote.Note, lanes, tickReached);
        }
    }

    // Points the note a press holds at what it now says, heard at once, where a press is holding one that follows lanes.
    public void ReplaceHeldNote(AuditionNote note)
    {
        lock (_lock)
        {
            if (_heldWalk is not { } walk || _heldNote is not { } heldNote || note.Lanes is not { } lanes)
            {
                return;
            }

            _heldNote = note;
            ReaimAudition(walk, heldNote.KeyOn, note, lanes, walk.Tick);
        }
    }

    // Keys the run's note off and ends it.
    public void StopNoteRun()
    {
        lock (_lock)
        {
            EndNoteRun();
        }
    }

    // Keys off every voice a press is holding, which is what letting the button up does.
    public void ReleaseAudition()
    {
        lock (_lock)
        {
            KeyOffHeldVoices();
            _heldWalk = null;
        }
    }

    // Points the echo unit at the song's base state, so an auditioned note is heard through it, while the song is not playing.
    public void SetAuditionEcho(EchoState state)
    {
        lock (_lock)
        {
            ApplyAuditionEcho(state);
        }
    }

    // Keys a note on as the one a press holds, following its lanes where it has them.
    private void HoldNote(AuditionNote note)
    {
        var voice = FreeAuditionVoice();
        KeyOnAudition(voice, note);

        _heldVoice = voice;
        _heldNote = note;
        _heldWalk = null;

        if (note.Lanes is { } lanes)
        {
            _heldWalk = new AuditionWalk(voice);
            _heldWalk.Follow(lanes, note.KeyOn.Level, lanes.StartTick);
            FollowAuditionTempo(lanes, lanes.StartTick);
        }
    }

    // The audition voice a new note takes: one that is silent, or else the one keyed on longest ago, never the run's.
    private SnesVoice FreeAuditionVoice()
    {
        SnesVoice? oldest = null;

        for (int i = SequencerVoiceCount; i < _voices.Length; i++)
        {
            var voice = _voices[i];
            if (ReferenceEquals(voice, _noteRun?.Walk.Voice))
            {
                continue;
            }

            if (!voice.IsActive)
            {
                return voice;
            }

            if (oldest is null || voice.KeyOnOrder < oldest.KeyOnOrder)
            {
                oldest = voice;
            }
        }

        return oldest!;
    }

    // Keys one audition note on, on the voice it was given.
    private void KeyOnAudition(SnesVoice voice, AuditionNote note)
    {
        PrimeVoice(voice, note);
        voice.KeyOn(note.KeyOn, ++_keyOnCount);
    }

    // Sets what a note sets on its voice besides keying it on: the noise clock, the echo send and the two sweeps.
    private void PrimeVoice(SnesVoice voice, AuditionNote note)
    {
        WriteNoiseClock(note.KeyOn.NoiseClock);
        voice.IsEchoOn = note.IsEchoOn;
        voice.SetVibrato(note.Vibrato);
        voice.SetTremolo(note.Tremolo);
    }

    // Keys the run's next note on, which is how a run starts and what a note no binding carries it into does.
    private void KeyOnRunNote(NoteRun run)
    {
        KeyOnAudition(run.Walk.Voice, run.Notes[run.NextNoteIndex].Note);
        FollowRunNote(run);
    }

    // Sounds the run's next note: handed to the voice still sounding where the note before binds into it and the voice takes it, keyed on otherwise.
    private void SoundNextRunNote(NoteRun run)
    {
        var runNote = run.Notes[run.NextNoteIndex];
        var note = runNote.Note;
        var voice = run.Walk.Voice;

        if (!runNote.IsBoundFromPrevious || !voice.TryRunIntoNote(note.KeyOn))
        {
            KeyOnRunNote(run);
            return;
        }

        PrimeVoice(voice, note);
        FollowRunNote(run);
    }

    // Walks the lanes of the run's next note from its own start, under the tempo there, and moves the run on to the note after it.
    private void FollowRunNote(NoteRun run)
    {
        var runNote = run.Notes[run.NextNoteIndex++];
        var lanes = runNote.Lanes;
        run.Walk.Follow(lanes, runNote.Note.KeyOn.Level, lanes.StartTick);
        FollowAuditionTempo(lanes, lanes.StartTick);
    }

    // Brings an audition's voice to what its note now says, keyed on afresh where it now sounds another instrument or other audio, and walks the note's
    // lanes on from the tick reached, everything they hold there taken up at once.
    private void ReaimAudition(AuditionWalk walk, VoiceKeyOn replacedKeyOn, AuditionNote note, AuditionLanes lanes, int tickReached)
    {
        var voice = walk.Voice;
        if (voice.IsActive)
        {
            WriteNoiseClock(note.KeyOn.NoiseClock);
            ReaimVoice(voice, replacedKeyOn, note.KeyOn, voice.ElapsedSamples);
            voice.IsEchoOn = note.IsEchoOn;
        }

        walk.Follow(lanes, note.KeyOn.Level, tickReached);
        walk.IsRepointed = true;
        PushWalkTick(walk, Math.Min(tickReached, lanes.EndTick - 1));
    }

    // Keys the run's note off and forgets the run, where one is sounding.
    private void EndNoteRun()
    {
        _noteRun?.Walk.Voice.KeyOff();
        _noteRun = null;
    }

    // Keys off every voice a press is holding and forgets them.
    private void KeyOffHeldVoices()
    {
        _heldVoice?.KeyOff();
        _heldVoice = null;
    }

    // One tick of the lanes an audition is following: the tempo at every tick, and up to its note's end what the lanes hold there pushed onto the voice.
    private void AdvanceWalk(AuditionWalk? walk)
    {
        if (walk is null)
        {
            return;
        }

        int tick = walk.Tick + 1;
        walk.Tick = tick;
        FollowAuditionTempo(walk.Lanes, tick);
        if (tick < walk.Lanes.EndTick)
        {
            PushWalkTick(walk, tick);
        }
    }

    // Pushes onto a walk's voice what its lanes hold at a tick: the volume chain where its value moved and the sweeps and the envelope where their commands
    // sit - every one of them where the walk has just been pointed at new lanes - and the echo where its command sits.
    private void PushWalkTick(AuditionWalk walk, int laneTick)
    {
        var lanes = walk.Lanes;
        var voice = walk.Voice;
        bool isRepointed = walk.IsRepointed;
        walk.IsRepointed = false;

        var voiceLanes = lanes.VoiceLanes;
        int masterVolume = voiceLanes.MasterVolume.InForceAt(laneTick);
        if (isRepointed || masterVolume != walk.MasterVolume)
        {
            walk.MasterVolume = masterVolume;
            voice.SetMasterVolume(masterVolume);
        }

        int channelVolume = voiceLanes.ChannelVolume.InForceAt(laneTick);
        if (isRepointed || channelVolume != walk.ChannelVolume)
        {
            walk.ChannelVolume = channelVolume;
            voice.SetChannelVolume(channelVolume);
        }

        var pan = voiceLanes.Pan.StateAt(laneTick);
        var surround = SnesSurround.FromPacked(voiceLanes.Surround.InForceAt(laneTick));
        if (isRepointed || pan.Value16 != walk.Pan16 || surround != walk.Surround)
        {
            walk.Pan16 = pan.Value16;
            walk.Surround = surround;
            voice.SetPan(pan.Value, pan.Fraction, surround);
        }

        walk.PanState = pan;

        int amplify = voiceLanes.Amplify.InForceAt(laneTick);
        if (isRepointed || amplify != walk.Amplify)
        {
            walk.Amplify = amplify;
            voice.SetAmplify(amplify);
        }

        if (isRepointed || lanes.VibratoChangeTicks.Contains(laneTick))
        {
            voice.SetVibrato(voiceLanes.Vibrato.StateAt(laneTick));
        }

        if (isRepointed || lanes.TremoloChangeTicks.Contains(laneTick))
        {
            voice.SetTremolo(voiceLanes.Tremolo.StateAt(laneTick));
        }

        if (isRepointed || lanes.EnvelopeChangeTicks.Contains(laneTick))
        {
            voice.SetEnvelope(voiceLanes.Envelope.StateAt(laneTick));
        }

        if (lanes.EchoChangeTicks.Contains(laneTick))
        {
            var echo = lanes.Echo.StateAt(laneTick);
            ApplyAuditionEcho(echo);
            voice.IsEchoOn = echo.DoesSendFrom(lanes.Channel);
        }
    }

    // One tick of the run: its next note sounded where the run has reached it, the run ended or started over where its length has run out, and otherwise a tick of the lanes the sounding note walks.
    private void AdvanceNoteRun()
    {
        if (_noteRun is not { } run)
        {
            return;
        }

        if (++run.TicksSounded >= run.LengthTicks)
        {
            if (!run.IsLooping)
            {
                _noteRun = null;
                return;
            }

            run.TicksSounded = 0;
            run.NextNoteIndex = 0;
        }

        if (run.NextNoteIndex < run.Notes.Count && run.Notes[run.NextNoteIndex].OffsetTicks <= run.TicksSounded)
        {
            SoundNextRunNote(run);
            return;
        }

        AdvanceWalk(run.Walk);
    }

    // Points the echo unit at an echo state an audition is heard under, while the song is not playing.
    private void ApplyAuditionEcho(EchoState state)
    {
        if (!_isPlaying)
        {
            ApplyEchoState(state);
        }
    }

    // Points the driver loop at the tempo a walk's lanes hold at a tick, so an audition's ticks and sweeps run at the song's rate there, while the song is not playing.
    private void FollowAuditionTempo(AuditionLanes lanes, int tick)
    {
        if (!_isPlaying)
        {
            _tempo = SnesFade.HeldAt(SnesTiming.DriverTempo(lanes.Tempo.TempoAt(tick)));
        }
    }

    // Glides an audition's pan between ticks off the state its walk has reached, while the walk has a tick ahead of it.
    private void GlideWalkPan(AuditionWalk? walk)
    {
        if (walk is null || !walk.PanState.IsFading || walk.Tick + 1 >= walk.Lanes.EndTick)
        {
            return;
        }

        GlidePan(walk.Voice, walk.PanState, walk.Surround);
    }
}
