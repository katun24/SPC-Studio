using SpcStudio.Model;

namespace SpcStudio.Import;

// What a MIDI file's channels play, the way a General MIDI player plays it: every note with the span it sounds for, the sustain pedal holding the ones
// released under it, and each channel's loudness, pan, bend and vibrato depth over time.
public sealed class MidiPerformance
{
    public const int ChannelCount = 16;
    public const int DrumChannel = 9;
    public const int MaxValue = 127;
    private const int DefaultVolume = 100;
    private const int DefaultExpression = MaxValue;
    private const int CenterPan = 64;
    private const int CenterBend = 8192;
    private const double DefaultBendRangeSemitones = 2;
    private const double DefaultModulationRangeSemitones = 0.5;
    private const int PedalDownThreshold = 64;
    private const int ModulationController = 1;
    private const int DataEntryController = 6;
    private const int VolumeController = 7;
    private const int PanController = 10;
    private const int ExpressionController = 11;
    private const int DataEntryFineController = 38;
    private const int SustainController = 64;
    private const int ParameterFineController = 100;
    private const int ParameterController = 101;
    private const int AllSoundOffController = 120;
    private const int ResetControllersController = 121;
    private const int AllNotesOffController = 123;
    private const int NoParameter = MaxValue;
    private const int BendRangeParameter = 0;
    private const int ModulationRangeParameter = 5;
    private const double ModulationRangeStepsPerSemitone = 128;

    private readonly List<MidiPlayedNote> _notes = [];
    private readonly ChannelState[] _channels = [.. Enumerable.Range(0, ChannelCount).Select(_ => new ChannelState())];

    private MidiPerformance()
    {
    }

    // Every note the file sounds, in the order each ended.
    public IReadOnlyList<MidiPlayedNote> Notes
    {
        get
        {
            return _notes;
        }
    }

    // A file played through from its first message to its end, every note still sounding there ending with it.
    public static MidiPerformance Of(MidiFile file)
    {
        var performance = new MidiPerformance();
        foreach (var message in file.Messages)
        {
            performance.Play(message);
        }

        for (int channel = 0; channel < ChannelCount; channel++)
        {
            performance.EndEverySoundingNote(channel, file.EndTick);
        }

        return performance;
    }

    // A channel's volume and expression together, as the share of the loudest they reach.
    public MidiCurve LoudnessOf(int channel)
    {
        return _channels[channel].Loudness;
    }

    // A channel's pan, as the controller's own value.
    public MidiCurve PanOf(int channel)
    {
        return _channels[channel].Pan;
    }

    // A channel's pitch bend, in semitones under the bend range in force.
    public MidiCurve BendOf(int channel)
    {
        return _channels[channel].Bend;
    }

    // A channel's vibrato depth from the modulation wheel, as semitones either side of the note under the modulation range in force.
    public MidiCurve VibratoOf(int channel)
    {
        return _channels[channel].Vibrato;
    }

    // Plays one message on its channel.
    private void Play(MidiMessage message)
    {
        var channel = _channels[message.Channel];
        switch (message.Kind)
        {
            case MidiMessageKind.NoteOn when message.SecondValue > 0:
                StrikeKey(message.Channel, message.FirstValue, message.SecondValue, message.Tick);
                break;
            case MidiMessageKind.NoteOn:
            case MidiMessageKind.NoteOff:
                ReleaseKey(message.Channel, message.FirstValue, message.Tick);
                break;
            case MidiMessageKind.ProgramChange:
                channel.Program = message.FirstValue;
                break;
            case MidiMessageKind.PitchBend:
                channel.BendValue = message.FirstValue;
                channel.SetCurves(message.Tick);
                break;
            case MidiMessageKind.ControlChange:
                ChangeController(message.Channel, message.FirstValue, message.SecondValue, message.Tick);
                break;
        }
    }

    // Starts a note on a key, ending one already sounding on it there.
    private void StrikeKey(int channelNumber, int key, int velocity, long tick)
    {
        var channel = _channels[channelNumber];
        EndNote(channelNumber, key, tick);
        channel.Sounding[key] = new SoundingNote(velocity, channel.Program, tick, IsKeyReleased: false);
    }

    // Lets go of a key, its note sounding on under the sustain pedal and ending where the pedal is up.
    private void ReleaseKey(int channelNumber, int key, long tick)
    {
        var channel = _channels[channelNumber];
        if (!channel.Sounding.TryGetValue(key, out var sounding) || sounding.IsKeyReleased)
        {
            return;
        }

        if (channel.IsPedalDown)
        {
            channel.Sounding[key] = sounding with { IsKeyReleased = true };
            return;
        }

        EndNote(channelNumber, key, tick);
    }

    // Ends the note sounding on a key, where one is.
    private void EndNote(int channelNumber, int key, long tick)
    {
        var channel = _channels[channelNumber];
        if (!channel.Sounding.Remove(key, out var sounding))
        {
            return;
        }

        _notes.Add(new MidiPlayedNote(channelNumber, key, sounding.Velocity, sounding.Program, sounding.StartTick, tick));
    }

    // Ends every note sounding on a channel, held or released under the pedal.
    private void EndEverySoundingNote(int channelNumber, long tick)
    {
        foreach (int key in _channels[channelNumber].Sounding.Keys.ToList())
        {
            EndNote(channelNumber, key, tick);
        }
    }

    // Ends every note on a channel whose key was let go under the pedal.
    private void EndPedalHeldNotes(int channelNumber, long tick)
    {
        foreach (var (key, _) in _channels[channelNumber].Sounding.Where(pair => pair.Value.IsKeyReleased).ToList())
        {
            EndNote(channelNumber, key, tick);
        }
    }

    // Plays one controller message on a channel.
    private void ChangeController(int channelNumber, int controller, int value, long tick)
    {
        var channel = _channels[channelNumber];
        switch (controller)
        {
            case ModulationController:
                channel.Modulation = value;
                break;
            case VolumeController:
                channel.Volume = value;
                break;
            case ExpressionController:
                channel.Expression = value;
                break;
            case PanController:
                channel.PanValue = value;
                break;
            case SustainController when value >= PedalDownThreshold:
                channel.IsPedalDown = true;
                break;
            case SustainController:
                ReleasePedal(channelNumber, tick);
                break;
            case ParameterController:
                channel.Parameter = (value, channel.Parameter.Fine);
                break;
            case ParameterFineController:
                channel.Parameter = (channel.Parameter.Coarse, value);
                break;
            case DataEntryController:
                channel.DataEntry = (value, 0);
                SetParameter(channel);
                break;
            case DataEntryFineController:
                channel.DataEntry = (channel.DataEntry.Coarse, value);
                SetParameter(channel);
                break;
            case ResetControllersController:
                channel.ResetControllers();
                ReleasePedal(channelNumber, tick);
                break;
            case AllNotesOffController:
                foreach (int key in channel.Sounding.Keys.ToList())
                {
                    ReleaseKey(channelNumber, key, tick);
                }

                break;
            case AllSoundOffController:
                EndEverySoundingNote(channelNumber, tick);
                break;
        }

        channel.SetCurves(tick);
    }

    // Lets the sustain pedal up, every note it was holding ending.
    private void ReleasePedal(int channelNumber, long tick)
    {
        _channels[channelNumber].IsPedalDown = false;
        EndPedalHeldNotes(channelNumber, tick);
    }

    // Writes the data entry onto the parameter it selects: the bend range in semitones and cents, or the modulation range in semitones and fractions of one.
    private static void SetParameter(ChannelState channel)
    {
        if (channel.Parameter.Coarse != 0)
        {
            return;
        }

        if (channel.Parameter.Fine == BendRangeParameter)
        {
            channel.BendRangeSemitones = channel.DataEntry.Coarse + channel.DataEntry.Fine / (double)SnesPitch.CentsPerSemitone;
        }
        else if (channel.Parameter.Fine == ModulationRangeParameter)
        {
            channel.ModulationRangeSemitones = channel.DataEntry.Coarse + channel.DataEntry.Fine / ModulationRangeStepsPerSemitone;
        }
    }

    // One note still sounding on a key: its velocity, its program, where it started, and whether its key was let go under the pedal.
    private readonly record struct SoundingNote(int Velocity, int Program, long StartTick, bool IsKeyReleased);

    // What one channel is set to and sounding, and its four curves.
    private sealed class ChannelState
    {
        public int Program { get; set; }
        public int Volume { get; set; } = DefaultVolume;
        public int Expression { get; set; } = DefaultExpression;
        public int PanValue { get; set; } = CenterPan;
        public int BendValue { get; set; } = CenterBend;
        public double BendRangeSemitones { get; set; } = DefaultBendRangeSemitones;
        public int Modulation { get; set; }
        public double ModulationRangeSemitones { get; set; } = DefaultModulationRangeSemitones;
        public bool IsPedalDown { get; set; }
        public (int Coarse, int Fine) Parameter { get; set; } = (NoParameter, NoParameter);
        public (int Coarse, int Fine) DataEntry { get; set; }
        public Dictionary<int, SoundingNote> Sounding { get; } = [];
        public MidiCurve Loudness { get; } = new(LoudnessShare(DefaultVolume, DefaultExpression));
        public MidiCurve Pan { get; } = new(CenterPan);
        public MidiCurve Bend { get; } = new(0);
        public MidiCurve Vibrato { get; } = new(0);

        // Puts back what General MIDI's reset of all controllers resets: the expression, the modulation wheel, the bend and the parameter selected.
        public void ResetControllers()
        {
            Expression = DefaultExpression;
            Modulation = 0;
            BendValue = CenterBend;
            Parameter = (NoParameter, NoParameter);
        }

        // Writes what the channel is set to onto its four curves from a tick on.
        public void SetCurves(long tick)
        {
            Loudness.Set(tick, LoudnessShare(Volume, Expression));
            Pan.Set(tick, PanValue);
            Bend.Set(tick, (double)(BendValue - CenterBend) / CenterBend * BendRangeSemitones);
            Vibrato.Set(tick, (double)Modulation / MaxValue * ModulationRangeSemitones);
        }

        // A volume and an expression together, as the share of the loudest they reach.
        private static double LoudnessShare(int volume, int expression)
        {
            return (double)volume / MaxValue * expression / MaxValue;
        }
    }
}
