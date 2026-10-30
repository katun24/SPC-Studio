using SpcStudio.Model;

namespace SpcStudio.Audio;

// The mixer's run of the driver's own main loop, at its own rate and free-running: the tempo accumulator whose carries are the song's ticks, the
// voices' bends and sweeps stepped on them, and the driver's five faded bytes with the commands that set and arm them.
public sealed partial class BrrMixer
{
    private const int DriverLoopHz = 500;
    internal const int SamplesPerDriverFrame = (int)DspPoint.SampleRate / DriverLoopHz;

    private readonly SnesFade[] _channelVolumes = new SnesFade[SequencerVoiceCount];
    private readonly SnesFade[] _pans = new SnesFade[SequencerVoiceCount];
    private readonly SnesSurround[] _surrounds = new SnesSurround[SequencerVoiceCount];
    private long _driverClockSamples;
    private double _driverClockCarry;
    private long _nextDriverFrameSample;
    private int _tickAccumulator;
    private SnesFade _tempo;
    private SnesFade _masterVolume;
    private SnesFade _echoVolumeLeft;
    private SnesFade _echoVolumeRight;

    // Puts the tempo, the volumes, the pans and the echo volumes back at the driver's own initial values, countdowns and all.
    private void ResetFades()
    {
        _tempo = SnesFade.HeldAt(SnesTiming.DriverTempo(SnesTiming.DefaultTempo));
        _masterVolume = SnesFade.HeldAt(SnesVolume.DefaultMasterVolume);
        _echoVolumeLeft = SnesFade.HeldAt(SnesEcho.DefaultVolume);
        _echoVolumeRight = SnesFade.HeldAt(SnesEcho.DefaultVolume);

        for (int i = 0; i < SequencerVoiceCount; i++)
        {
            _channelVolumes[i] = SnesFade.HeldAt(SnesVolume.DefaultChannelVolume);
            _pans[i] = SnesFade.HeldAt(SnesVolume.DefaultPanIndex);
            _surrounds[i] = SnesSurround.None;
        }
    }

    // Runs one pass of the driver's main loop where the clock has reached it.
    private void RunDriverFrameWhereDue()
    {
        if (_driverClockSamples < _nextDriverFrameSample)
        {
            return;
        }

        RunDriverFrame();
        _nextDriverFrameSample += SamplesPerDriverFrame;
    }

    // How many samples can be generated before the next pass of the driver's main loop, never fewer than one.
    private double SamplesUntilNextDriverFrame()
    {
        return SamplesToGenerateFor(_nextDriverFrameSample - _driverClockSamples, _driverClockCarry);
    }

    // Moves the driver's clock on by what a run of generated samples covers.
    private void AdvanceDriverClock(int generated)
    {
        _driverClockSamples += SongSamplesCovered(generated, ref _driverClockCarry);
    }

    // One pass of the driver's main loop: the tempo added to the accumulator, a carry out of it being a tick that steps the bends, the sweeps and the
    // fades, and every other pass reading the bends, the sweeps and the pan fades part way to their next step.
    private void RunDriverFrame()
    {
        _tickAccumulator += _tempo.Value;

        bool isTick = _tickAccumulator > byte.MaxValue;
        _tickAccumulator &= byte.MaxValue;

        foreach (var voice in _voices)
        {
            if (isTick)
            {
                voice.StepDriverTick();
            }
            else
            {
                voice.GlideBetweenTicks(_tickAccumulator);
            }
        }

        if (!isTick)
        {
            if (_isPlaying)
            {
                GlideChannelPans();
            }

            GlideWalkPan(_heldWalk);
            GlideWalkPan(_noteRun?.Walk);
            return;
        }

        if (_isPlaying)
        {
            StepFades();
        }

        AdvanceWalk(_heldWalk);
        AdvanceNoteRun();
    }

    // One tick of the driver's fades - the tempo, the song volume, the echo volumes and each channel's volume and pan - a voice's registers rewritten only where its byte moved.
    private void StepFades()
    {
        if (_tempo.IsFading)
        {
            _tempo = _tempo.Stepped();
        }

        if (HasStepMoved(ref _masterVolume, fade => fade.Value))
        {
            PushMasterVolume();
        }

        if (_echoVolumeLeft.IsFading || _echoVolumeRight.IsFading)
        {
            _echoVolumeLeft = _echoVolumeLeft.Stepped();
            _echoVolumeRight = _echoVolumeRight.Stepped();
            _echo.SetVolumes(_echoVolumeLeft.Value, _echoVolumeRight.Value);
        }

        for (int i = 0; i < SequencerVoiceCount; i++)
        {
            if (HasStepMoved(ref _channelVolumes[i], fade => fade.Value))
            {
                _voices[i].SetChannelVolume(_channelVolumes[i].Value);
            }

            if (HasStepMoved(ref _pans[i], fade => fade.Value16))
            {
                _voices[i].SetPan(_pans[i].Value, _pans[i].Fraction, _surrounds[i]);
            }
        }
    }

    // Writes the VOL registers of every channel whose pan is fading from the pan plus the part of its next step a pass between ticks adds.
    private void GlideChannelPans()
    {
        for (int i = 0; i < SequencerVoiceCount; i++)
        {
            if (_pans[i].IsFading)
            {
                GlidePan(_voices[i], _pans[i], _surrounds[i]);
            }
        }
    }

    // Writes one voice's VOL registers from a fading pan glided part way to its next step, the pan itself left where the last tick put it.
    private void GlidePan(SnesVoice voice, SnesFade pan, SnesSurround surround)
    {
        int glided = pan.Value16GlidedBetweenTicks(_tickAccumulator);
        voice.SetPan(SnesFade.ValueOf(glided), SnesFade.FractionOf(glided), surround);
    }

    // Takes a tempo command - $E2 and, where it ramps, $E3 - which is what the driver loop counts a tick by.
    private void ApplyTempo(TempoEvent tempo, long samplesSince = 0)
    {
        var fade = tempo.Fade is { } ramp ? ramp with { Target = SnesTiming.DriverTempo(ramp.Target) } : null;

        _tempo = FadeAfterCommand(_tempo, tempo.DoesSetValue ? SnesTiming.DriverTempo(tempo.Value) : null, fade, samplesSince);
    }

    // Takes a song volume command - $E0 and, where it fades, $E1 - for every channel's voice.
    private void ApplyMasterVolume(MasterVolumeEvent volume, long samplesSince = 0)
    {
        _masterVolume = FadeAfterCommand(_masterVolume, volume.DoesSetValue ? volume.Value : null, volume.Fade, samplesSince);

        PushMasterVolume();
    }

    // Takes a channel volume command - $E7 and $E8 - for that channel's voice alone.
    private void ApplyChannelVolume(ChannelVolumeEvent volume, long samplesSince = 0)
    {
        int voice = volume.VoiceIndex;

        _channelVolumes[voice] = FadeAfterCommand(_channelVolumes[voice], volume.DoesSetValue ? volume.Value : null, volume.Fade, samplesSince);

        _voices[voice].SetChannelVolume(_channelVolumes[voice].Value);
    }

    // Takes a pan command - $DB and $DC - for that channel's voice alone, the surround pair travelling on the set.
    private void ApplyPan(PanEvent pan, long samplesSince = 0)
    {
        int voice = pan.VoiceIndex;

        _pans[voice] = FadeAfterCommand(_pans[voice], pan.DoesSetValue ? pan.Value : null, pan.Fade, samplesSince);

        if (pan.DoesSetValue)
        {
            _surrounds[voice] = pan.Surround;
        }

        _voices[voice].SetPan(_pans[voice].Value, _pans[voice].Fraction, _surrounds[voice]);
    }

    // Takes an echo state outright: the unit's registers, its two volumes and the channel voices' sends.
    private void ApplyEchoState(EchoState state)
    {
        _echo.SetState(state);
        _echoVolumeLeft = _echoVolumeLeft.WithValueSet(state.VolumeLeft);
        _echoVolumeRight = _echoVolumeRight.WithValueSet(state.VolumeRight);
        SetChannelEchoSends(state);
    }

    // Takes one $EF/$F1/$F5 pass and the $F2 that starts on its tick, the two volumes through their fade states.
    private void ApplyEchoEvent(EchoEvent echo, long samplesSince = 0)
    {
        var state = echo.State;
        _echo.SetState(state);

        _echoVolumeLeft = FadeAfterCommand(_echoVolumeLeft, echo.DoesSetVolume ? state.VolumeLeft : null, echo.FadeLeft, samplesSince);
        _echoVolumeRight = FadeAfterCommand(_echoVolumeRight, echo.DoesSetVolume ? state.VolumeRight : null, echo.FadeRight, samplesSince);

        _echo.SetVolumes(_echoVolumeLeft.Value, _echoVolumeRight.Value);
        SetChannelEchoSends(state);
    }

    // Sets every channel voice's echo send from its bit in an echo state, the audition voices keeping their own.
    private void SetChannelEchoSends(EchoState state)
    {
        for (int i = 0; i < SequencerVoiceCount; i++)
        {
            _voices[i].IsEchoOn = state.DoesSendFrom(i);
        }
    }

    // Writes the song volume into every channel's voice.
    private void PushMasterVolume()
    {
        for (int i = 0; i < SequencerVoiceCount; i++)
        {
            _voices[i].SetMasterVolume(_masterVolume.Value);
        }
    }

    // One of the faded bytes after a command: where it sets a value, the fade in front run out and the value written, then the fade it arms and however
    // much of that fade a position already inside it has used up.
    private static SnesFade FadeAfterCommand(SnesFade state, int? setValue, SequenceFade? fade, long samplesSince)
    {
        var written = setValue is int value ? state.Landed().WithValueSet(value) : state;
        return FadeArmed(written, fade, samplesSince);
    }

    // Steps a fading byte one tick, and answers whether a reading of it moved, which a byte holding still never does.
    private static bool HasStepMoved(ref SnesFade fade, Func<SnesFade, int> reading)
    {
        if (!fade.IsFading)
        {
            return false;
        }

        var stepped = fade.Stepped();
        bool hasMoved = reading(stepped) != reading(fade);
        fade = stepped;
        return hasMoved;
    }

    // One of the faded bytes after a command that arms a fade, the fade in front run out first.
    private static SnesFade FadeArmed(SnesFade state, SequenceFade? fade, long samplesSince)
    {
        if (fade is null)
        {
            return state;
        }

        var armed = state.Landed().FadingTo(fade.Target, fade.LengthTicks);
        if (samplesSince <= 0)
        {
            return armed;
        }

        // The ticks of the fade a position this many samples into it has passed, all of them for a fade of one tick, which spans no samples.
        int elapsedTicks = fade.LengthSamples > 0 ? (int)(samplesSince * fade.LengthTicks / fade.LengthSamples) : fade.LengthTicks;

        return armed.Advanced(elapsedTicks);
    }
}
