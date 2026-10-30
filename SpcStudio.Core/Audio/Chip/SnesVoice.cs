using System.Collections.Immutable;
using SpcStudio.Model;

namespace SpcStudio.Audio;

// One SNES DSP voice at the DSP's own rate: gaussian interpolation over a sample's points, the envelope, the VOL registers worked out from the driver's
// volume chain, and the parts of the driver that move a sounding note - its $DD chain, its vibrato and its tremolo.
public sealed class SnesVoice
{
    private readonly GaussianWindow _interpolation = new();
    private readonly Func<int?> _readNextSourcePoint;
    private readonly SnesEnvelope _envelope = new();
    private short[] _points = [];
    private int _loopStartPoint;
    private bool _doesLoop;
    private BrrSample? _sample;
    private BrrSample? _sampleAtNextWrap;
    private double? _tuningAtNextWrap;
    private bool _isNoise;
    private int _sourcePoint;
    private int _pitchRegister;
    private SnesPitch.NotePitch _notePitch;
    private Instrument? _instrument;
    private int _fineTune;
    private ImmutableArray<PitchBend> _bends = [];
    private int _nextBendIndex;
    private int _bendDelayTicks;
    private SnesFade _bendRamp;
    private int _bendTargetNoteValue;
    private bool _isBendRead;
    private int _bendGlide;
    private VibratoState _vibrato = VibratoState.Off;
    private int _vibratoPhase;
    private int _vibratoDelayCounter;
    private int _vibratoFadeSteps;
    private TremoloState _tremolo = TremoloState.Off;
    private int _tremoloPhase;
    private int _tremoloDelayCounter;
    private int _tremoloDuck;
    private VoiceLevel _level;
    private int _volumeLeft;
    private int _volumeRight;
    private int _gateSamples;
    private double _elapsedSongSamples;
    private bool _isGated;
    private int _ticksPlayed;

    // Builds the voice, handing its interpolation the one reader of source points it takes them through.
    public SnesVoice()
    {
        _readNextSourcePoint = ReadNextSourcePoint;
    }

    public bool IsActive { get; private set; }
    public long KeyOnOrder { get; private set; }

    // Whether this voice is fed into the echo buffer, which is its bit in the DSP's EON.
    public bool IsEchoOn { get; set; }

    // Whether the voice has stopped, or its envelope has fallen to silence for good.
    public bool IsSilentForGood
    {
        get
        {
            return !IsActive || _envelope.IsSilentForGood;
        }
    }

    // The DSP PITCH register as this voice last wrote it.
    public int PitchRegister
    {
        get
        {
            return _pitchRegister;
        }
    }

    // The DSP VOL registers as this voice last wrote them.
    public VoiceVolume Volume
    {
        get
        {
            return new(_volumeLeft, _volumeRight);
        }
    }

    // How many of the song's samples the note now sounding has played since it was keyed on, which is what its key-on length is counted against.
    public int ElapsedSamples
    {
        get
        {
            return (int)_elapsedSongSamples;
        }
    }

    // The 8.8 note value the pitch is derived from.
    private int NoteValueWithFraction
    {
        get
        {
            return SnesFade.Value16Of(_notePitch.NoteValue, _notePitch.FineTune);
        }
    }

    // Keys on a note under the envelope in force on its channel, a number of samples into it where an edit re-keys the voice part way through.
    public void KeyOn(VoiceKeyOn note, long order, int elapsedSamples = 0)
    {
        _isNoise = note.Sample is null;
        _instrument = note.Instrument;
        TakeUpSample(note.Sample);
        _interpolation.Clear();
        _sourcePoint = -1;
        StartPitch(note.Pitch, note.Bends);
        _vibratoPhase = SnesVibrato.StartPhase;
        _vibratoDelayCounter = 0;
        _vibratoFadeSteps = 0;
        _tremoloPhase = 0;
        _tremoloDelayCounter = 0;
        _tremoloDuck = 0;
        SetLevel(note.Level);
        _envelope.KeyOn(note.Envelope);
        StartGate(note.GateSamples, elapsedSamples);
        KeyOnOrder = order;
        IsActive = true;
    }

    // Takes the next note without keying on, as a legato boundary does, and answers false where there was no sounding note to run into.
    public bool TryRunIntoNote(VoiceKeyOn note)
    {
        if (!IsActive || _envelope.Phase == EnvelopePhase.Release)
        {
            return false;
        }

        _isNoise = note.Instrument.IsNoise;
        _instrument = note.Instrument;
        StartPitch(note.Pitch, note.Bends);
        SetLevel(note.Level);
        _envelope.SetRegisters(note.Envelope);
        RestartGate(note.GateSamples);
        return true;
    }

    // Counts a key-on length afresh from now, the voice carrying on as it is.
    public void RestartGate(int gateSamples)
    {
        StartGate(gateSamples, 0);
    }

    // Whether the voice is playing a note's instrument on the audio the note keys on with, that audio sounding already or staged for the voice's next loop wrap.
    public bool IsPlayingSourceOf(VoiceKeyOn note)
    {
        return _instrument?.Id == note.Instrument.Id && ReferenceEquals(_sampleAtNextWrap ?? _sample, note.Sample);
    }

    // Points a sounding voice at a new pitch and $DD chain alone, the chain walked forward the ticks the voice has already played.
    public void ReaimPitch(SnesPitch.NotePitch pitch, ImmutableArray<PitchBend> bends)
    {
        if (!IsActive)
        {
            return;
        }

        int played = _ticksPlayed;
        StartPitch(pitch, bends);
        for (int i = 0; i < played; i++)
        {
            StepBendOnTick();
        }

        _pitchRegister = _notePitch.Register();
    }

    // Takes the tuning an edited instrument sounds with mid-note, and answers whether this voice was playing that instrument.
    public bool RetuneInstrument(Instrument instrument, double soundingTuning)
    {
        if (!IsActive || _isNoise || !ReferenceEquals(_instrument, instrument))
        {
            return false;
        }

        TakeUpTuning(soundingTuning);
        return true;
    }

    // Takes new envelope registers mid-note, which is what $ED does, the level and the phase carrying on.
    public void SetEnvelope(EnvelopeState envelope)
    {
        _envelope.SetRegisters(envelope);
    }

    // Takes a new song volume mid-note, which is what w does.
    public void SetMasterVolume(int masterVolume)
    {
        SetLevel(_level with { MasterVolume = masterVolume });
    }

    // Takes a new channel volume mid-note, which is what v does.
    public void SetChannelVolume(int channelVolume)
    {
        SetLevel(_level with { ChannelVolume = channelVolume });
    }

    // Takes a new pan mid-note, which is what y does: the table entry, the fraction a pan fade leaves between two entries, and the surround pair.
    public void SetPan(int panIndex, int panFraction, SnesSurround surround)
    {
        SetLevel(_level with { PanIndex = panIndex, PanFraction = panFraction, Surround = surround });
    }

    // Takes a new amplify mid-note, which is what $FA $03 does.
    public void SetAmplify(int amplify)
    {
        SetLevel(_level with { Amplify = amplify });
    }

    // Takes a new velocity mid-note, which only an edit made while the note sounds does.
    public void SetVelocity(int velocity)
    {
        SetLevel(_level with { Velocity = velocity });
    }

    // Takes a new key-on length mid-note, keying the voice off now where the note is already past it.
    public void SetGate(int gateSamples)
    {
        _gateSamples = gateSamples;
        _isGated = _gateSamples > 0 && _elapsedSongSamples >= _gateSamples;
        if (_isGated)
        {
            KeyOff();
        }
    }

    // Takes a new vibrato mid-note, which is what p does, its delay counter left running.
    public void SetVibrato(VibratoState vibrato)
    {
        _vibrato = vibrato;
    }

    // Takes a new tremolo mid-note, which is what $E5 does, its delay counter and the level its last duck wrote left as they are.
    public void SetTremolo(TremoloState tremolo)
    {
        _tremolo = tremolo;
    }

    // One pass of the driver's main loop that carries into a new tick: the $DD chain, the vibrato and the tremolo each stepped.
    public void StepDriverTick()
    {
        if (!IsActive)
        {
            return;
        }

        StepBendOnTick();
        StepVibratoOnTick();
        StepTremoloOnTick();
    }

    // One pass of the driver's main loop between ticks: the bend, the vibrato and the tremolo each read a fraction of a tick past their last step.
    public void GlideBetweenTicks(int tickFraction)
    {
        if (!IsActive)
        {
            return;
        }

        GlideBendBetweenTicks(tickFraction);
        SweepVibratoBetweenTicks(tickFraction);
        SweepTremoloBetweenTicks(tickFraction);
    }

    // Starts the envelope's release.
    public void KeyOff()
    {
        _envelope.KeyOff();
    }

    // Silences the voice at once.
    public void Kill()
    {
        IsActive = false;
    }

    // Mixes a run of samples into the main buses alone, for a voice with nowhere to send an echo.
    public void MixWithoutEcho(float[] left, float[] right, int start, int count)
    {
        Mix(left, right, null, null, null, start, count);
    }

    // Mixes a run of samples into the main buses, and into the echo send where the voice's EON bit is set, at a playback speed and transpose.
    public void Mix(float[] left, float[] right, int[]? echoLeft, int[]? echoRight, short[]? noise, int start, int count, double speed = 1,
                    double pitchScale = 1)
    {
        bool isSendingEcho = IsEchoOn && echoLeft is not null && echoRight is not null;
        int interpolationStep = pitchScale == 1 ? _pitchRegister : (int)Math.Round(_pitchRegister * pitchScale);
        for (int i = 0; i < count; i++)
        {
            if (!IsActive)
            {
                return;
            }

            KeyOffAtGate();
            _envelope.StepOneSample();
            if (!_envelope.IsActive)
            {
                IsActive = false;
                return;
            }

            int point = _isNoise ? (noise is null ? 0 : noise[start + i]) : _interpolation.InterpolatedPoint;
            point = (point * _envelope.Level) >> SnesEnvelope.LevelBits;

            // The point through each side's VOL register.
            int outLeft = (point * _volumeLeft) >> DspPoint.RegisterProductShift;
            int outRight = (point * _volumeRight) >> DspPoint.RegisterProductShift;
            left[start + i] += DspPoint.ToLevel(outLeft);
            right[start + i] += DspPoint.ToLevel(outRight);
            if (isSendingEcho)
            {
                echoLeft![start + i] += outLeft;
                echoRight![start + i] += outRight;
            }

            if (!_isNoise && !_interpolation.TryAdvance(interpolationStep, _readNextSourcePoint))
            {
                IsActive = false;
                return;
            }

            _elapsedSongSamples += speed;
        }
    }

    // Stages new audio to be taken up at this voice's next loop wrap, with the tuning its instrument sounds at the fraction of its original rate the new audio
    // plays at, where the voice is playing, or is about to play, the audio it replaces.
    public void ReplaceAudioAtLoop(BrrSample from, BrrSample to, double toRateFractionOfOriginal)
    {
        if (!IsActive || _isNoise)
        {
            return;
        }

        if (!ReferenceEquals(_sampleAtNextWrap ?? _sample, from))
        {
            return;
        }

        _sampleAtNextWrap = to;
        _tuningAtNextWrap = _instrument?.TuningMultiplierAt(toRateFractionOfOriginal);
    }

    // Takes a new level and works the VOL registers out from it.
    private void SetLevel(VoiceLevel level)
    {
        _level = level;
        WriteVolumeRegisters();
    }

    // Works the DSP VOL registers out from the level's own inputs and the duck the tremolo is holding the note's volume down by.
    private void WriteVolumeRegisters()
    {
        var levelWithTremoloDuck = _tremolo.IsActive ? _level with { Velocity = SnesTremolo.DuckedVolume(_level.Velocity, _tremoloDuck) } : _level;
        var registers = levelWithTremoloDuck.Registers;
        _volumeLeft = registers.Left;
        _volumeRight = registers.Right;
    }

    // Takes a note's pitch, and its $DD chain from the top with no tick of the note played yet.
    private void StartPitch(SnesPitch.NotePitch pitch, ImmutableArray<PitchBend> bends)
    {
        _notePitch = pitch;
        _fineTune = pitch.FineTune;
        _ticksPlayed = 0;
        StartBendChain(bends);
        _pitchRegister = _notePitch.Register();
    }

    // Takes a note's $DD chain from the top and arms its first bend, as a note read does.
    private void StartBendChain(ImmutableArray<PitchBend> bends)
    {
        _bends = bends.IsDefault ? [] : bends;
        _nextBendIndex = 0;
        _bendRamp = default;
        _bendDelayTicks = 0;
        _bendGlide = 0;
        _bendTargetNoteValue = _notePitch.NoteValue;
        ArmNextBend();
    }

    // Consumes the next $DD of the chain, its ramp set out from the note value it is read at.
    private void ArmNextBend()
    {
        if (_nextBendIndex >= _bends.Length)
        {
            _bendRamp = default;
            return;
        }

        var nextBend = _bends[_nextBendIndex++];
        _isBendRead = false;
        _bendTargetNoteValue += nextBend.OffsetSemitones;
        _bendDelayTicks = nextBend.DelayTicks;
        _bendRamp = PitchBend.RampFrom(NoteValueWithFraction, _bendTargetNoteValue, nextBend.LengthTicks);
    }

    // Counts the bend's delay down on a tick, or steps its ramp once the delay has run out, arming the next bend where the ramp lands.
    private void StepBendOnTick()
    {
        _isBendRead = true;
        _bendGlide = 0;
        _ticksPlayed++;
        if (_bendDelayTicks > 0)
        {
            _bendDelayTicks--;
            return;
        }

        if (!_bendRamp.IsFading)
        {
            return;
        }

        _bendRamp = _bendRamp.Stepped();
        _notePitch = _bendRamp.IsFading
            ? _notePitch with { NoteValue = _bendRamp.Value, FineTune = _bendRamp.Fraction }
            : _notePitch with { NoteValue = _bendRamp.Target, FineTune = _fineTune };
        _pitchRegister = _notePitch.Register();
        if (!_bendRamp.IsFading)
        {
            ArmNextBend();
        }
    }

    // Writes the pitch a fraction of a tick along the bend's next step, where a read bend is ramping past its delay.
    private void GlideBendBetweenTicks(int tickFraction)
    {
        bool isGliding = _isBendRead && _bendDelayTicks == 0 && _bendRamp.IsFading;
        _bendGlide = isGliding ? _bendRamp.GlideBetweenTicks(tickFraction) : 0;
        if (isGliding)
        {
            _pitchRegister = _notePitch.Register(_bendGlide);
        }
    }

    // Counts the vibrato's delay up on a tick, or steps its fade and its phase once the delay has run out and sweeps the pitch from the new phase.
    private void StepVibratoOnTick()
    {
        if (!_vibrato.IsActive)
        {
            return;
        }

        if (_vibratoDelayCounter != _vibrato.Delay)
        {
            _vibratoDelayCounter = (_vibratoDelayCounter + 1) & byte.MaxValue;
            return;
        }

        if (_vibrato.HasFade && _vibratoFadeSteps < _vibrato.FadeTicks)
        {
            _vibratoFadeSteps++;
        }

        _vibratoPhase = SnesVibrato.PhaseAfter(_vibratoPhase, _vibrato.Rate, steps: 1);
        WriteVibratoPitch(_vibratoPhase);
    }

    // Sweeps the pitch from the vibrato's phase with a fraction of a tick interpolated into it, once its delay has run out.
    private void SweepVibratoBetweenTicks(int tickFraction)
    {
        if (!_vibrato.IsActive || _vibratoDelayCounter != _vibrato.Delay)
        {
            return;
        }

        WriteVibratoPitch(PhaseBetweenTicks(_vibratoPhase, _vibrato.Rate, tickFraction));
    }

    // Writes the pitch the vibrato's offset at a phase puts over the note value and the bend's glide.
    private void WriteVibratoPitch(int phase)
    {
        int fadedExtent = _vibrato.HasFade ? SnesVibrato.FadedExtent(_vibrato.Extent, _vibrato.FadeTicks, _vibratoFadeSteps) : _vibrato.Extent;
        _pitchRegister = _notePitch.Register(_bendGlide + SnesVibrato.OffsetAt(phase, fadedExtent));
    }

    // Counts the tremolo's delay up on a tick, or steps its phase once the delay has run out and ducks the volume by the new phase.
    private void StepTremoloOnTick()
    {
        if (!_tremolo.IsActive)
        {
            return;
        }

        if (_tremoloDelayCounter != _tremolo.Delay)
        {
            _tremoloDelayCounter = (_tremoloDelayCounter + 1) & byte.MaxValue;
            return;
        }

        _tremoloPhase = SnesTremolo.NextPhase(_tremoloPhase, _tremolo.Rate, _tremolo.Amount);
        DuckVolumeAt(_tremoloPhase);
    }

    // Ducks the volume by the tremolo's phase with a fraction of a tick interpolated into it, once its delay has run out.
    private void SweepTremoloBetweenTicks(int tickFraction)
    {
        if (!_tremolo.IsActive || _tremoloDelayCounter != _tremolo.Delay)
        {
            return;
        }

        DuckVolumeAt(PhaseBetweenTicks(_tremoloPhase, _tremolo.Rate, tickFraction));
    }

    // Holds the note's volume down by the tremolo's duck at a phase, and rewrites the VOL registers.
    private void DuckVolumeAt(int phase)
    {
        _tremoloDuck = SnesTremolo.DuckAt(phase, _tremolo.Amount);
        WriteVolumeRegisters();
    }

    // A sweep's phase a fraction of a tick past where its last tick left it.
    private static int PhaseBetweenTicks(int phase, int rate, int tickFraction)
    {
        return (phase + ((rate * tickFraction) >> SnesFade.FractionBits)) & byte.MaxValue;
    }

    // Starts counting a key-on length a number of samples into it.
    private void StartGate(int gateSamples, int elapsedSamples)
    {
        _gateSamples = gateSamples;
        _elapsedSongSamples = elapsedSamples;
        _isGated = elapsedSamples > gateSamples;
    }

    // Keys the voice off the first time its gate is reached.
    private void KeyOffAtGate()
    {
        if (_isGated || _gateSamples <= 0 || _elapsedSongSamples < _gateSamples)
        {
            return;
        }

        KeyOff();
        _isGated = true;
    }

    // The next source point the interpolation takes in, wrapping to the loop point and taking up staged audio there, or null at a one-shot's end.
    private int? ReadNextSourcePoint()
    {
        _sourcePoint++;
        if (_sourcePoint >= _points.Length && !WrapToLoopPoint())
        {
            return null;
        }

        return _points[_sourcePoint];
    }

    // Puts the source point back at the loop point, taking up any staged audio, and answers false where the audio does not loop.
    private bool WrapToLoopPoint()
    {
        if (!_doesLoop)
        {
            return false;
        }

        if (_sampleAtNextWrap is { } next)
        {
            TakeUpSample(next);
        }

        if (_tuningAtNextWrap is double nextTuning)
        {
            TakeUpTuning(nextTuning);
        }

        if (!_doesLoop || _points.Length == 0)
        {
            return false;
        }

        _sourcePoint = Math.Clamp(_loopStartPoint, 0, _points.Length - 1);
        return true;
    }

    // Takes a new tuning under the note's own row and fine tune, the register following.
    private void TakeUpTuning(double tuningMultiplier)
    {
        _notePitch = _notePitch with { TuningMultiplier = tuningMultiplier };
        _pitchRegister = _notePitch.Register();
    }

    // Takes up the audio a note keys on with, or nothing on a noise note, and the audio staged for the wrap there.
    private void TakeUpSample(BrrSample? sample)
    {
        _sample = sample;
        _sampleAtNextWrap = null;
        _tuningAtNextWrap = null;
        _points = sample?.Points ?? [];
        _loopStartPoint = sample?.LoopStartPoint ?? 0;
        _doesLoop = sample?.DoesLoop ?? false;
    }
}
