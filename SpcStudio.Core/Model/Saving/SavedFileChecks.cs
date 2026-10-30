using System.Collections.Immutable;
using System.IO;

namespace SpcStudio.Model;

// The refusals both saved files make: a value outside its range, and the values and chains a note and an instrument both carry.
internal static class SavedFileChecks
{
    public const string TempoName = "tempo";
    public const string SongVolumeName = "song volume";
    public const string VolumeName = "volume";
    public const string PanName = "pan";
    public const string AmplifyName = "amplify";
    private const string VibratoName = "vibrato";
    private const string TremoloName = "tremolo";
    private const string SweepDelayName = "delay";
    private const string SweepLengthName = "length";
    private const string SweepRateName = "rate";

    // Refuses a value outside a range, the message naming where it was found and what it is.
    public static void RequireInRange(string where, string what, int value, ValueRange range)
    {
        RequireInRange(where, what, value, range.Min, range.Max);
    }

    // Refuses a fractional value outside a range, the message naming where it was found and what it is.
    public static void RequireInRange(string where, string what, double value, double minimum, double maximum)
    {
        if (!(value >= minimum && value <= maximum))
        {
            throw new InvalidDataException($"{where} has {what} {value}, outside {minimum}..{maximum}.");
        }
    }

    // Refuses a q gate, a q velocity, an $EE fine tune or an $FA $03 amplify out of range, and the one q AddmusicK will not take.
    public static void RequirePlayedValues(string where, int gate, int velocity, int fineTune, int amplify)
    {
        RequireInRange(where, "gate", gate, SnesTiming.GateRange);
        RequireInRange(where, "velocity", velocity, SnesVolume.VelocityRange);
        if (Note.QuantizationOf(gate, velocity) < Note.MinQuantization)
        {
            throw new InvalidDataException($"{where} has a gate and a velocity of 0 together, which AddmusicK cannot write.");
        }

        RequireInRange(where, "fine tune", fineTune, SnesPitch.FineTuneRange);
        RequireAmplify(where, amplify);
    }

    // Refuses an amplify out of its range.
    public static void RequireAmplify(string where, int amplify)
    {
        RequireInRange(where, AmplifyName, amplify, SnesVolume.AmplifyRange);
    }

    // Refuses a pan out of the pan table's range.
    public static void RequirePan(string where, int pan)
    {
        RequireInRange(where, PanName, pan, SnesVolume.PanIndexRange);
    }

    // Refuses a packed surround pair that is not one of the four.
    public static void RequireSurround(string where, int packedSurround)
    {
        RequireInRange(where, "surround", packedSurround, SnesSurround.PackedRange);
    }

    // Refuses a channel volume out of its range.
    public static void RequireVolume(string where, int volume)
    {
        RequireInRange(where, VolumeName, volume, SnesVolume.ChannelVolumeRange);
    }

    // Refuses volume rows setting or ramping to a volume out of range.
    public static void RequireVolumeChain(string where, ImmutableArray<NoteVolumeRegion> chain)
    {
        foreach (var row in chain)
        {
            RequireVolume(where, NoteVolume.Lane.ValueOf(row.State));
            RequireVolume(where, NoteVolume.Lane.EndValueOf(row));
        }
    }

    // Refuses pan rows setting or ramping to a pan out of range.
    public static void RequirePanChain(string where, ImmutableArray<NotePanRegion> chain)
    {
        foreach (var row in chain)
        {
            RequirePan(where, NotePan.Lane.ValueOf(row.State));
            RequirePan(where, NotePan.Lane.EndValueOf(row));
        }
    }

    // Refuses a vibrato chain holding a value out of range or breaking either of the driver's two fade rules.
    public static void RequireVibratoChain(string where, ImmutableArray<NoteVibrato> chain)
    {
        RequireSweepValues(where, VibratoName, chain, SnesVibrato.BoundedSettings, NameOf, (sweep, setting) => sweep[setting], SnesVibrato.Range);
        for (int sweepIndex = 0; sweepIndex < chain.Length; sweepIndex++)
        {
            var sweep = chain[sweepIndex];
            if (sweep.FadeTicks == SnesVibrato.MinFadeTicks)
            {
                continue;
            }

            if (sweep.FadeTicks > sweep.Extent)
            {
                throw new InvalidDataException($"{where} has a vibrato fade of {sweep.FadeTicks} ticks over an extent of {sweep.Extent}.");
            }

            if (SnesVibrato.IsFadeTaken(chain, sweepIndex))
            {
                throw new InvalidDataException($"{where} has two vibrato fades on one note.");
            }
        }
    }

    // Refuses a vibrato chain running past a note of the given length.
    public static void RequireVibratoChainFitsNote(string where, ImmutableArray<NoteVibrato> chain, int noteLengthTicks)
    {
        RequireSweepChainFitsNote(where, VibratoName, chain, noteLengthTicks);
    }

    // Refuses a tremolo chain holding a value out of range.
    public static void RequireTremoloChain(string where, ImmutableArray<NoteTremolo> chain)
    {
        RequireSweepValues(where, TremoloName, chain, SnesTremolo.BoundedSettings, NameOf, (sweep, setting) => sweep[setting], SnesTremolo.Range);
    }

    // Refuses a tremolo chain running past a note of the given length.
    public static void RequireTremoloChainFitsNote(string where, ImmutableArray<NoteTremolo> chain, int noteLengthTicks)
    {
        RequireSweepChainFitsNote(where, TremoloName, chain, noteLengthTicks);
    }

    // Refuses a pitch bend chain holding a delay below its floor or a ramp length outside the range its byte holds.
    public static void RequireBendChain(string where, ImmutableArray<PitchBend> chain)
    {
        foreach (var bend in chain)
        {
            if (bend.DelayTicks < PitchBend.MinDelayTicks)
            {
                throw new InvalidDataException($"{where} has a pitch bend delayed {bend.DelayTicks} ticks, which is below {PitchBend.MinDelayTicks}.");
            }

            RequireInRange(where, "a pitch bend length of", bend.LengthTicks, PitchBend.LengthTicksRange);
        }
    }

    // Refuses a note's level bend node ticks where one is before the note's start or not after the one in front of it.
    public static void RequireFlatBendNodeTicks(string where, IReadOnlyList<int> ticks)
    {
        for (int index = 0; index < ticks.Count; index++)
        {
            int earliestTick = index == 0 ? 0 : ticks[index - 1] + 1;
            RequireInRange(where, "a level bend node at tick", ticks[index], new ValueRange(earliestTick, int.MaxValue));
        }
    }

    // Refuses a chain of sweeps holding a value out of the range its own command takes.
    private static void RequireSweepValues<TSweep, TSetting>(
        string where,
        string sweepName,
        ImmutableArray<TSweep> chain,
        TSetting[] boundedSettings,
        Func<TSetting, string> nameOf,
        Func<TSweep, TSetting, int> valueOf,
        Func<TSetting, ValueRange> rangeOf)
        where TSweep : struct, INoteSweep<TSweep>
    {
        foreach (var sweep in chain)
        {
            foreach (var setting in boundedSettings)
            {
                RequireInRange(where, $"a {sweepName} {nameOf(setting)} of", valueOf(sweep, setting), rangeOf(setting));
            }
        }
    }

    // Refuses a chain of sweeps running past a note of the given length.
    private static void RequireSweepChainFitsNote<TSweep>(string where, string sweepName, ImmutableArray<TSweep> chain, int noteLengthTicks)
        where TSweep : struct, INoteSweep<TSweep>
    {
        if (!NoteSweep.FitToNoteLength(chain, noteLengthTicks).SequenceEqual(chain))
        {
            throw new InvalidDataException($"{where} has a {sweepName} past the end of a note {noteLengthTicks} ticks long.");
        }
    }

    // What a vibrato value is called in a message about a bad file.
    private static string NameOf(VibratoSetting setting)
    {
        return setting switch
        {
            VibratoSetting.Delay => SweepDelayName,
            VibratoSetting.Length => SweepLengthName,
            VibratoSetting.Rate => SweepRateName,
            VibratoSetting.Fade => "fade",
            _ => "extent",
        };
    }

    // What a tremolo value is called in a message about a bad file.
    private static string NameOf(TremoloSetting setting)
    {
        return setting switch
        {
            TremoloSetting.Delay => SweepDelayName,
            TremoloSetting.Length => SweepLengthName,
            TremoloSetting.Rate => SweepRateName,
            _ => "amount",
        };
    }
}
