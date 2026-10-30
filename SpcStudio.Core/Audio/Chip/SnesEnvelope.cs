using SpcStudio.Model;

namespace SpcStudio.Audio;

// The DSP's envelope generator, run off the three envelope bytes a sample at a time, and walked from key-on to draw the shape it plays.
public sealed class SnesEnvelope
{
    public const int MaxLevel = 0x7FF;
    public const int LevelBits = 11;
    private const int SustainLevelShift = 8;
    private const int ExponentialStepShift = 8;
    private const int ReleaseStep = 8;
    private const int AttackStep = 32;
    private const int FastestAttackStep = 1024;
    private const int LinearGainStep = 32;
    private const int BentGainSlowStep = 8;
    private const int BentGainBendLevel = 0x600;
    private const int DirectGainLevelScale = 16;
    private const int FastestRateIndex = 31;
    private const int MaxTraceSeconds = 120;
    private const int WorkedOutLevelCycleSamples = 2;

    private EnvelopeState _registers;
    private int _samplesToNextTick;

    // The level the last sample worked out, before the clamp and whether or not a tick kept it.
    private int _lastWorkedOutLevel;

    public int Level { get; private set; }
    public EnvelopePhase Phase { get; private set; }

    // Whether the envelope still sounds, which a key-off release ends by running the level down to silence.
    public bool IsActive { get; private set; }

    // Whether nothing more will happen before a key-off: the release is running, or the phase or the GAIN mode steps at no rate.
    private bool HasNoNextTick
    {
        get
        {
            return Phase == EnvelopePhase.Release || CurrentRateIndex() == 0;
        }
    }

    // Whether the level has fallen to silence for good, nothing but a key-on or new registers raising it again.
    public bool IsSilentForGood
    {
        get
        {
            if (Level > 0)
            {
                return false;
            }

            return HasNoNextTick || LevelAfterOneStep() <= 0;
        }
    }

    // Walks an envelope a tick at a time from key-on until it falls silent or settles, the release left out.
    public static EnvelopeTrace TraceFromKeyOn(EnvelopeState registers)
    {
        return TraceThroughChanges([new EnvelopeChange(0, registers)]);
    }

    // Walks an envelope from key-on through a run of register changes, each taking over at its time with the level and the phase carrying on under
    // it, until the last falls silent or settles, the release left out.
    public static EnvelopeTrace TraceThroughChanges(IReadOnlyList<EnvelopeChange> changes)
    {
        var envelope = new SnesEnvelope();
        envelope.KeyOn(changes[0].Registers);
        var corners = new List<EnvelopeCorner> { new(0, envelope.Level, envelope.Phase) };
        var handoverCornerIndices = new List<int> { 0 };
        long samples = 0;
        long maxTraceSamples = (long)(DspPoint.SampleRate * MaxTraceSeconds);
        for (int changeIndex = 1; ; changeIndex++)
        {
            bool isPastLastChange = changeIndex >= changes.Count;
            long nextHandoverSample = isPastLastChange ? maxTraceSamples : Math.Min(maxTraceSamples, (long)Math.Round(changes[changeIndex].Seconds * DspPoint.SampleRate));
            bool isSettled = envelope.TraceUntil(corners, ref samples, nextHandoverSample);
            if (isPastLastChange || samples >= maxTraceSamples)
            {
                return new EnvelopeTrace(corners, isSettled, handoverCornerIndices);
            }

            samples = nextHandoverSample;
            handoverCornerIndices.Add(corners.Count);
            corners.Add(new EnvelopeCorner(samples / DspPoint.SampleRate, envelope.Level, envelope.Phase));
            envelope.SetRegisters(changes[changeIndex].Registers);
        }
    }

    // Keys on from silence, in the attack phase whatever the registers run.
    public void KeyOn(EnvelopeState registers)
    {
        _registers = registers;
        Level = 0;
        _lastWorkedOutLevel = 0;
        Phase = EnvelopePhase.Attack;
        _samplesToNextTick = 1;
        IsActive = true;
    }

    // Takes new envelope registers without restarting, the level and the phase carrying on under the new rates.
    public void SetRegisters(EnvelopeState registers)
    {
        _registers = registers;
    }

    // Starts the release.
    public void KeyOff()
    {
        Phase = EnvelopePhase.Release;
    }

    // Advances one sample at the DSP's rate.
    public void StepOneSample()
    {
        if (Phase == EnvelopePhase.Release)
        {
            StepRelease();
            return;
        }

        if (CurrentRateIndex() == 0 || --_samplesToNextTick > 0)
        {
            WorkOutLevelWithoutTick(1);
            return;
        }

        ReloadCounterAndStep();
    }

    // Skips straight to the next envelope tick and answers how many samples that took, or zero where nothing more will happen before a key-off.
    public int AdvanceToNextTick()
    {
        if (HasNoNextTick)
        {
            return 0;
        }

        int elapsed = _samplesToNextTick;
        WorkOutLevelWithoutTick(elapsed - 1);
        ReloadCounterAndStep();
        return elapsed;
    }

    // Walks the envelope a tick at a time up to a sample, adding a corner at each tick, and answers whether it settled on the way; it stops early where
    // it settles or falls silent, and otherwise stops on the sample with the countdown to its next tick carried across it.
    private bool TraceUntil(List<EnvelopeCorner> corners, ref long samples, long untilSample)
    {
        while (samples < untilSample)
        {
            int samplesLeftToUntilSample = (int)(untilSample - samples);
            if (HasNoNextTick)
            {
                WorkOutLevelWithoutTick(samplesLeftToUntilSample);
                return true;
            }

            if (_samplesToNextTick > samplesLeftToUntilSample)
            {
                WorkOutLevelWithoutTick(samplesLeftToUntilSample);
                _samplesToNextTick -= samplesLeftToUntilSample;
                samples = untilSample;
                return false;
            }

            var levelAndPhaseBeforeTick = (Level, Phase);
            samples += AdvanceToNextTick();
            if ((Level, Phase) == levelAndPhaseBeforeTick)
            {
                return true;
            }

            corners.Add(new EnvelopeCorner(samples / DspPoint.SampleRate, Level, Phase));
            if (Level == 0)
            {
                return false;
            }
        }

        return false;
    }

    // Takes one release step down toward silence, ending the envelope there.
    private void StepRelease()
    {
        Level -= ReleaseStep;
        if (Level > 0)
        {
            return;
        }

        Level = 0;
        IsActive = false;
    }

    // Reloads the counter at the rate of the phase running before the step, and takes one step.
    private void ReloadCounterAndStep()
    {
        _samplesToNextTick = SnesDspTables.RatePeriods[CurrentRateIndex()];
        StepLevel();
        _lastWorkedOutLevel = Level;
        Level = Math.Clamp(Level, 0, MaxLevel);
    }

    // Works out the level on each of a run of samples no tick falls on, keeping only what the last one worked out.
    private void WorkOutLevelWithoutTick(int sampleCount)
    {
        int samplesToWorkOut = sampleCount <= WorkedOutLevelCycleSamples + 1 ? sampleCount : WorkedOutLevelCycleSamples + sampleCount % WorkedOutLevelCycleSamples;
        for (int sample = 0; sample < samplesToWorkOut; sample++)
        {
            _lastWorkedOutLevel = LevelAfterOneStep();
        }
    }

    // The rate index the running phase, or the GAIN mode, steps at.
    private int CurrentRateIndex()
    {
        if (!_registers.UsesAdsr)
        {
            return GainByte.ModeOf(_registers.Gain) == GainMode.Direct ? FastestRateIndex : GainByte.ValueOf(_registers.Gain);
        }

        return Phase switch
        {
            EnvelopePhase.Attack => _registers.AttackRate * 2 + 1,
            EnvelopePhase.Decay => _registers.DecayRate * 2 + 16,
            _ => _registers.SustainRate,
        };
    }

    // The level one step of the running phase, or of the GAIN mode, would leave, the envelope itself left as it was.
    private int LevelAfterOneStep()
    {
        var (levelBeforeStep, phaseBeforeStep) = (Level, Phase);
        StepLevel();
        int levelAfterStep = Level;
        (Level, Phase) = (levelBeforeStep, phaseBeforeStep);
        return levelAfterStep;
    }

    // Takes one step of the running phase, or of the GAIN mode.
    private void StepLevel()
    {
        if (!_registers.UsesAdsr)
        {
            StepGain(_registers.Gain);
            return;
        }

        switch (Phase)
        {
            case EnvelopePhase.Attack:
                StepAttack();
                break;
            case EnvelopePhase.Decay:
                StepDecay();
                break;
            default:
                Level -= ExponentialStep();
                break;
        }
    }

    // Rises one attack step, moving on to the decay at full scale.
    private void StepAttack()
    {
        Level += _registers.AttackRate < EnvelopeState.MaxAttackRate ? AttackStep : FastestAttackStep;
        if (Level < MaxLevel)
        {
            return;
        }

        Level = MaxLevel;
        Phase = EnvelopePhase.Decay;
    }

    // Falls one exponential step, or moves on to the sustain where the level has reached the sustain level.
    private void StepDecay()
    {
        if ((Level >> SustainLevelShift) == _registers.SustainLevel)
        {
            Phase = EnvelopePhase.Sustain;
            return;
        }

        Level -= ExponentialStep();
    }

    // Takes one step of a GAIN byte's mode, or jumps to its level in Direct mode.
    private void StepGain(int gain)
    {
        switch (GainByte.ModeOf(gain))
        {
            case GainMode.Direct:
                Level = GainByte.ValueOf(gain) * DirectGainLevelScale;
                break;
            case GainMode.LinearDecrease:
                Level -= LinearGainStep;
                break;
            case GainMode.ExponentialDecrease:
                Level -= ExponentialStep();
                break;
            case GainMode.LinearIncrease:
                Level += LinearGainStep;
                break;
            default:
                Level += (uint)_lastWorkedOutLevel < BentGainBendLevel ? LinearGainStep : BentGainSlowStep;
                break;
        }
    }

    // How far one exponential step falls from the current level.
    private int ExponentialStep()
    {
        return ((Level - 1) >> ExponentialStepShift) + 1;
    }
}
