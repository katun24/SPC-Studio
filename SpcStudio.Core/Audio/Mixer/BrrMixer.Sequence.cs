using System.Collections.Immutable;
using SpcStudio.Model;

namespace SpcStudio.Audio;

// The mixer's song playback: a run of events timed in samples, fired on the channels' voices as the play position reaches them, wrapping to the loop
// point or stopping at the end, and an edit made mid-run taken up from the playhead on.
public sealed partial class BrrMixer
{
    private const int SongWideSlot = -1;

    private static readonly IEqualityComparer<SequenceEvent> SameDriverStateComparer = EqualityComparer<SequenceEvent>.Create(LeaveSameState);
    private IReadOnlyList<SequenceEvent> _sequenceEvents = [];
    private int _nextEventIndex;
    private long _playPosition;
    private double _playPositionCarry;
    private long _sequenceLengthSamples;
    private long? _loopStartSample;
    private bool _isPlaying;

    // Whether the song is playing.
    public bool IsPlaying
    {
        get
        {
            lock (_lock)
            {
                return _isPlaying;
            }
        }
    }

    // The play position in seconds, ahead of what is audible by the output device's buffer latency.
    public double PlayPositionSeconds
    {
        get
        {
            lock (_lock)
            {
                return _playPosition / DspPoint.SampleRate;
            }
        }
    }

    // Starts playing a run of events ordered by sample from a position, those behind it skipped, and going back to a loop point at the end where the run has one before it.
    public void StartSequence(IReadOnlyList<SequenceEvent> events, long lengthSamples, long startSamples = 0, long? loopSamples = null)
    {
        lock (_lock)
        {
            KeyOffAllButNoteRun();
            _heldVoice = null;
            _heldWalk = null;
            _echo.Reset();
            _noise.ClearClock();

            _sequenceEvents = events;
            MovePlayPositionTo(startSamples);
            TakeRunEnds(lengthSamples, loopSamples);
            _isPlaying = true;

            ResetFades();
            SkipEventsTo(startSamples);
        }
    }

    // Swaps the events of a run already under way, so an edit is heard from the playhead on, the position moved where the edit changed the tick rate, and nothing the edit left as it was touched.
    public void ReplaceSequence(IReadOnlyList<SequenceEvent> events, long lengthSamples, long? atSample = null, long? loopSamples = null)
    {
        lock (_lock)
        {
            if (!_isPlaying)
            {
                return;
            }

            var replacedEvents = _sequenceEvents;
            int replacedEventsBehind = _nextEventIndex;
            long replacedPosition = _playPosition;
            _sequenceEvents = events;
            TakeRunEnds(lengthSamples, loopSamples);
            if (atSample is long movedPosition)
            {
                MovePlayPositionTo(movedPosition);
            }

            _nextEventIndex = EventCountBehind(_sequenceEvents, _playPosition);
            TakeUpChangedState(replacedEvents, replacedEventsBehind);
            ReaimChangedVoices(replacedEvents, replacedEventsBehind, replacedPosition);
        }
    }

    // Stops playback and leaves the position where it is, the voices keyed off rather than cut.
    public void PauseSequence()
    {
        lock (_lock)
        {
            _isPlaying = false;
            KeyOffAllButNoteRun();
        }
    }

    // Stops playback and silences the run where it stands: the voices keyed off and the echo's delay line emptied.
    public void StopSequence()
    {
        lock (_lock)
        {
            PauseSequence();
            _sequenceEvents = [];
            _echo.Reset();
        }
    }

    // Keys off one channel's voice, for muting a channel while it is sounding.
    public void KeyOffChannel(int channel)
    {
        lock (_lock)
        {
            _voices[channel].KeyOff();
        }
    }

    // Points one channel's sounding voice at a pitch, which is how a drag still under the button reaches playback.
    public void ReaimVoicePitch(int voice, SnesPitch.NotePitch pitch, ImmutableArray<PitchBend> bends)
    {
        if (voice < 0 || voice >= SequencerVoiceCount)
        {
            return;
        }

        lock (_lock)
        {
            _voices[voice].ReaimPitch(pitch, bends);
        }
    }

    // Fires every event the play position has reached.
    private void FireDueEvents()
    {
        while (_nextEventIndex < _sequenceEvents.Count && _sequenceEvents[_nextEventIndex].SampleTime <= _playPosition)
        {
            var sequenceEvent = _sequenceEvents[_nextEventIndex++];
            if (sequenceEvent is NoteEvent note)
            {
                FireNote(note);
            }
            else
            {
                ApplyLaneEvent(sequenceEvent, 0);
            }
        }
    }

    // Keys a note on - or, where it is legato and the voice is still sounding, hands it to that voice - after writing its noise clock.
    private void FireNote(NoteEvent note)
    {
        WriteNoiseClock(note.KeyOn.NoiseClock);
        var voice = _voices[note.VoiceIndex];
        if (note.IsLegato && voice.TryRunIntoNote(note.KeyOn))
        {
            return;
        }

        voice.KeyOn(note.KeyOn, ++_keyOnCount);
    }

    // Takes every event that is not a note, a number of samples after it fired: the faded bytes, the echo, and each channel's amplify, sweeps and envelope override.
    private void ApplyLaneEvent(SequenceEvent sequenceEvent, long samplesSince)
    {
        switch (sequenceEvent)
        {
            case MasterVolumeEvent volume:
                ApplyMasterVolume(volume, samplesSince);
                break;
            case ChannelVolumeEvent volume:
                ApplyChannelVolume(volume, samplesSince);
                break;
            case PanEvent pan:
                ApplyPan(pan, samplesSince);
                break;
            case AmplifyEvent amplify:
                _voices[amplify.VoiceIndex].SetAmplify(amplify.Value);
                break;
            case EchoEvent echo:
                ApplyEchoEvent(echo, samplesSince);
                break;
            case VibratoEvent vibrato:
                _voices[vibrato.VoiceIndex].SetVibrato(vibrato.State);
                break;
            case TremoloEvent tremolo:
                _voices[tremolo.VoiceIndex].SetTremolo(tremolo.State);
                break;
            case EnvelopeEvent envelope:
                _voices[envelope.VoiceIndex].SetEnvelope(envelope.State);
                break;
            case TempoEvent tempo:
                ApplyTempo(tempo, samplesSince);
                break;
        }
    }

    // How many samples can be generated before the next event or, where the run loops, its end - never fewer than one.
    private double SamplesUntilSequenceBoundary()
    {
        long untilBoundary = _nextEventIndex < _sequenceEvents.Count ? _sequenceEvents[_nextEventIndex].SampleTime - _playPosition : long.MaxValue;
        if (_loopStartSample is not null)
        {
            untilBoundary = Math.Min(untilBoundary, _sequenceLengthSamples - _playPosition);
        }

        return Math.Max(SamplesToGenerateFor(untilBoundary, _playPositionCarry), 1);
    }

    // Moves the play position on by what a run of generated samples covers, going back to the loop point or stopping at the end.
    private void AdvancePlayPosition(int generated)
    {
        _playPosition += SongSamplesCovered(generated, ref _playPositionCarry);
        if (_playPosition < _sequenceLengthSamples)
        {
            return;
        }

        if (_loopStartSample is long loopStart)
        {
            RewindToLoopPoint(loopStart);
            return;
        }

        _isPlaying = false;
    }

    // Puts the next event at the first one still ahead of a position, taking up on the way every event behind it that is driver state a note never restates.
    private void SkipEventsTo(long position)
    {
        _nextEventIndex = EventCountBehind(_sequenceEvents, position);
        for (int i = 0; i < _nextEventIndex; i++)
        {
            TakeUpEventBehind(_sequenceEvents[i], position);
        }
    }

    // How many of a run's events lie strictly behind a position.
    private static int EventCountBehind(IReadOnlyList<SequenceEvent> events, long position)
    {
        return SortedSearch.CountLeading(events, sequenceEvent => sequenceEvent.SampleTime < position);
    }

    // Takes up the driver state an event behind a position leaves there: a note's noise clock, or a lane event as far into it as the position is.
    private void TakeUpEventBehind(SequenceEvent sequenceEvent, long position)
    {
        if (sequenceEvent is NoteEvent note)
        {
            WriteNoiseClock(note.KeyOn.NoiseClock);
            return;
        }

        ApplyLaneEvent(sequenceEvent, position - sequenceEvent.SampleTime);
    }

    // Takes up again every piece of driver state whose events behind the playhead an edit changed, and leaves every other piece running as it is.
    private void TakeUpChangedState(IReadOnlyList<SequenceEvent> replacedEvents, int replacedEventsBehind)
    {
        var replacedSlots = StateEventsBySlot(replacedEvents, replacedEventsBehind);
        foreach (var (slot, slotEvents) in StateEventsBySlot(_sequenceEvents, _nextEventIndex))
        {
            if (replacedSlots.TryGetValue(slot, out var replacedSlotEvents) && replacedSlotEvents.SequenceEqual(slotEvents, SameDriverStateComparer))
            {
                continue;
            }

            foreach (var sequenceEvent in slotEvents)
            {
                TakeUpEventBehind(sequenceEvent, _playPosition);
            }
        }
    }

    // The first events of a run that set driver state, grouped by the piece of state each one sets, in their order.
    private static Dictionary<(Type Kind, int VoiceIndex), List<SequenceEvent>> StateEventsBySlot(IReadOnlyList<SequenceEvent> events, int count)
    {
        var slots = new Dictionary<(Type Kind, int VoiceIndex), List<SequenceEvent>>();
        for (int i = 0; i < count; i++)
        {
            if (StateSlotOf(events[i]) is not { } slot)
            {
                continue;
            }

            slots.TryAdd(slot, []);
            slots[slot].Add(events[i]);
        }

        return slots;
    }

    // The piece of driver state an event sets - its kind and the voice it reaches, or none for the song's own - or null for a note without a noise clock.
    private static (Type Kind, int VoiceIndex)? StateSlotOf(SequenceEvent sequenceEvent)
    {
        return sequenceEvent switch
        {
            NoteEvent { KeyOn.NoiseClock: null } => null,
            NoteEvent => (typeof(NoteEvent), SongWideSlot),
            IChannelEvent channelEvent => (sequenceEvent.GetType(), channelEvent.VoiceIndex),
            _ => (sequenceEvent.GetType(), SongWideSlot),
        };
    }

    // Whether two events leave the same driver state behind them: the same noise clock at the same moment for two notes, and everything alike otherwise.
    private static bool LeaveSameState(SequenceEvent? first, SequenceEvent? second)
    {
        if (first is NoteEvent firstNote && second is NoteEvent secondNote)
        {
            return firstNote.SampleTime == secondNote.SampleTime && firstNote.KeyOn.NoiseClock == secondNote.KeyOn.NoiseClock;
        }

        return Equals(first, second);
    }

    // Brings every sounding channel voice to the note the edited song has under the playhead, writing only what the edit changed about it.
    private void ReaimChangedVoices(IReadOnlyList<SequenceEvent> replacedEvents, int replacedEventsBehind, long replacedPosition)
    {
        for (int i = 0; i < SequencerVoiceCount; i++)
        {
            var voice = _voices[i];
            if (!voice.IsActive)
            {
                continue;
            }

            var replacedNote = NoteSoundingAt(replacedEvents, replacedEventsBehind, i, replacedPosition);
            var note = NoteSoundingAt(_sequenceEvents, _nextEventIndex, i, _playPosition);
            if (note is null)
            {
                if (replacedNote is not null)
                {
                    voice.KeyOff();
                }

                continue;
            }

            ReaimVoice(voice, replacedNote?.KeyOn, note.KeyOn, (int)(_playPosition - note.SampleTime));
        }
    }

    // The note a voice would be inside at a position, among the events of a run behind it, or null where the playhead has reached a rest.
    private static NoteEvent? NoteSoundingAt(IReadOnlyList<SequenceEvent> events, int eventsBehind, int voice, long position)
    {
        for (int i = eventsBehind - 1; i >= 0; i--)
        {
            if (events[i] is not NoteEvent note || note.VoiceIndex != voice)
            {
                continue;
            }

            return note.SampleTime <= position && position < note.EndSample ? note : null;
        }

        return null;
    }

    // Goes back to the loop point for the next pass, the delay line left ringing and the channels' voices keyed off, but for each one whose note is still
    // keyed on at the end and whose channel ties into the loop point, which sounds on as the tie.
    private void RewindToLoopPoint(long loopStart)
    {
        for (int i = 0; i < SequencerVoiceCount; i++)
        {
            if (GateLeftPastLoopPoint(i, loopStart) is int gateSamples)
            {
                _voices[i].RestartGate(gateSamples);
                continue;
            }

            _voices[i].KeyOff();
        }

        MovePlayPositionTo(loopStart);
        SkipEventsTo(_playPosition);
    }

    // How long a voice stays keyed on past the loop point where its note is keyed on up to the end and the loop point falls inside a note of its channel,
    // which is up to where that note is let go, or null where the voice is let go at the wrap.
    private int? GateLeftPastLoopPoint(int voice, long loopStart)
    {
        var endNote = NoteSoundingAt(_sequenceEvents, EventCountBehind(_sequenceEvents, _sequenceLengthSamples), voice, _sequenceLengthSamples - 1);
        if (endNote is null || endNote.KeyOn.GateSamples < endNote.EndSample - endNote.SampleTime)
        {
            return null;
        }

        var tiedNote = NoteSoundingAt(_sequenceEvents, EventCountBehind(_sequenceEvents, loopStart), voice, loopStart);
        if (tiedNote is null)
        {
            return null;
        }

        long gateSamplesLeft = tiedNote.SampleTime + tiedNote.KeyOn.GateSamples - loopStart;
        return gateSamplesLeft > 0 ? (int)gateSamplesLeft : null;
    }

    // Puts the play position at a sample, with no part of one carried into it.
    private void MovePlayPositionTo(long position)
    {
        _playPosition = position;
        _playPositionCarry = 0;
    }

    // Takes the sample a run ends at and the loop point it goes back to there, where it has one before the end.
    private void TakeRunEnds(long lengthSamples, long? loopSamples)
    {
        _sequenceLengthSamples = lengthSamples;
        _loopStartSample = LoopStartWithinLength(loopSamples, lengthSamples);
    }

    // Keys off every voice but the note panel's run's.
    private void KeyOffAllButNoteRun()
    {
        foreach (var voice in _voices)
        {
            if (!ReferenceEquals(voice, _noteRun?.Walk.Voice))
            {
                voice.KeyOff();
            }
        }
    }

    // The loop point a run actually has, which is none where it is not before the end.
    private static long? LoopStartWithinLength(long? loopSamples, long lengthSamples)
    {
        return loopSamples is long loopStart && loopStart < lengthSamples ? loopStart : null;
    }
}
