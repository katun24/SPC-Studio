using SpcStudio.Export;

namespace SpcStudio.Model;

// Conversions between MML ticks and real time, and the driver's two key-off cuts that decide how much of a note's length it is keyed on for.
public static class SnesTiming
{
    public const int TicksPerWholeNote = 192;
    public const int TicksPerHalfNote = 96;
    public const int TicksPerQuarterNote = 48;
    public const int TicksPerEighthNote = 24;
    public const int TicksPerSixteenthNote = 12;
    public const double TicksPerSecondPerTempoUnit = 125.0 / 64.0;
    public const double TempoUnitsPerBpm = 0.4096;
    public const int MinTempo = 1;
    public const int MaxTempo = 254;
    public const int DefaultTempo = 60;
    public const int MinGate = 0;
    public const int MaxGate = KeyOnRatioCount - 1;
    public const int DefaultGate = MaxGate;
    public const int DefaultWaitTime = 2;
    public const int StaccatoWaitTime = 1;
    public const int MinStaccato = 0;
    public const int MaxStaccato = 1;
    public const int DefaultStaccato = MinStaccato;
    private const int KeyOnRatioCount = 8;
    private const int KeyOnRatioBits = 8;
    private const int KeyOnRatioDenominator = 1 << KeyOnRatioBits;
    private const int MinGateCounterTicks = 1;
    private const int DriverTempoIncrement = 1;

    public static readonly ValueRange TempoRange = new(MinTempo, MaxTempo);
    public static readonly ValueRange GateRange = new(MinGate, MaxGate);
    public static readonly ValueRange StaccatoRange = new(MinStaccato, MaxStaccato);

    // How much of a note's length each of q's gates keys it on for, as a fraction of the ratio denominator.
    private static readonly byte[] KeyOnRatios = [0x33, 0x66, 0x80, 0x99, 0xB3, 0xCC, 0xE6, 0xFF];

    // The tempo byte the driver runs on for the t argument the song carries: one more than it, wrapped to a byte.
    public static int DriverTempo(int tempo)
    {
        return (tempo + DriverTempoIncrement) & byte.MaxValue;
    }

    // How many MML ticks a second a tempo plays.
    public static double TicksPerSecond(int tempo)
    {
        return DriverTempo(tempo) * TicksPerSecondPerTempoUnit;
    }

    // How many seconds a tick count lasts at a tempo, the count fractional.
    public static double TicksToSeconds(double ticks, int tempo)
    {
        return ticks / TicksPerSecond(tempo);
    }

    // The beats a minute a tempo plays.
    public static double TempoToBpm(int tempo)
    {
        return DriverTempo(tempo) / TempoUnitsPerBpm;
    }

    // The tempo whose beats a minute come nearest a number, held to the tempos a song can carry.
    public static int TempoForBpm(double bpm)
    {
        return TempoRange.Clamp((int)Math.Round(bpm * TempoUnitsPerBpm) - DriverTempoIncrement);
    }

    // How much of a note's length a gate keys it on for, as a fraction of the ratio denominator.
    public static int KeyOnRatioFor(int gate)
    {
        return KeyOnRatios[gate];
    }

    // The ticks into a duration byte that the q gate's counter cuts it at, counted from the byte's own fetch.
    public static int GateCounterTicks(int byteTicks, int gate)
    {
        return Math.Max(MinGateCounterTicks, byteTicks * KeyOnRatios[gate] >> KeyOnRatioBits);
    }

    // The driver's wait-time byte a light staccato lane value stands for.
    public static int WaitTimeFor(int staccato)
    {
        return staccato == MinStaccato ? DefaultWaitTime : StaccatoWaitTime;
    }

    // How many of a note's ticks the voice is keyed on for under a gate: the earlier of the driver's two key-off cuts, both counted on the last piece the note is written as.
    public static int KeyOnTicks(int lengthTicks, int gate, int? finalPieceTicks, Func<int, int> waitTimeAt)
    {
        var triggers = KeyOffTriggers.For(lengthTicks, gate, finalPieceTicks, waitTimeAt);
        return triggers.LeadTicks + Math.Min(triggers.GateTicks, triggers.WaitTicks);
    }

    // How many of a note's ticks the voice is keyed on for under a gate, with one wait time for the whole of the note.
    public static int KeyOnTicks(int lengthTicks, int gate, int? finalPieceTicks = null, int waitTime = DefaultWaitTime)
    {
        return KeyOnTicks(lengthTicks, gate, finalPieceTicks, _ => waitTime);
    }

    // The written length of an ordinary note that is keyed on for exactly a number of ticks, no longer than a limit, or null where no length says it.
    public static int? PlainLength(int soundingTicks, int limitTicks, Func<int, int> waitTimeAt)
    {
        int longestTicks = Math.Min(limitTicks, soundingTicks + DefaultWaitTime);
        for (int lengthTicks = soundingTicks; lengthTicks <= longestTicks; lengthTicks++)
        {
            if (KeyOnTicks(lengthTicks, MaxGate, null, waitTimeAt) == soundingTicks)
            {
                return lengthTicks;
            }
        }

        return null;
    }

    // The written length and q gate of a note heard for a number of ticks, written in whole steps and no longer than a limit, or null where no pair comes within a tolerance of what was heard.
    public static (int LengthTicks, int Gate)? GatedLength(double soundingTicks, int stepTicks, int limitTicks, double toleranceTicks)
    {
        if (stepTicks <= 0 || limitTicks < stepTicks)
        {
            return null;
        }

        (int LengthTicks, int Gate)? nearestPair = null;
        double nearestMissTicks = double.MaxValue;

        // The longest length the shortest gate could have cut to what was heard, a step past it.
        int longestTicks = Math.Min(limitTicks, (int)Math.Ceiling(soundingTicks * KeyOnRatioDenominator / KeyOnRatios[MinGate]) + stepTicks);
        for (int lengthTicks = stepTicks; lengthTicks <= longestTicks; lengthTicks += stepTicks)
        {
            for (int gate = MinGate; gate <= MaxGate; gate++)
            {
                double missTicks = Math.Abs(KeyOnTicks(lengthTicks, gate) - soundingTicks);
                if (missTicks >= nearestMissTicks)
                {
                    continue;
                }

                nearestMissTicks = missTicks;
                nearestPair = (lengthTicks, gate);
            }
        }

        return nearestMissTicks <= toleranceTicks ? nearestPair : null;
    }

    // The length of the last piece a note is written as, given the ticks it is broken at.
    public static int FinalPieceTicks(int startTick, int lengthTicks, IEnumerable<int> breakTicks)
    {
        int lastBreakTick = startTick;
        foreach (int breakTick in breakTicks)
        {
            lastBreakTick = Math.Max(lastBreakTick, breakTick);
        }

        return startTick + lengthTicks - lastBreakTick;
    }

    // The driver's two key-off cuts for a note, kept apart: the ticks to the last piece, its length, and how far into it the q gate's counter and the wait time's comparison each cut.
    public readonly record struct KeyOffTriggers(int LeadTicks, int LastPieceTicks, int GateTicks, int WaitTicks)
    {
        // Whether the gate counter reaches zero inside the last piece at all.
        public bool DoesGateFire
        {
            get
            {
                return GateTicks < LastPieceTicks;
            }
        }

        // Whether the wait-time comparison is met inside the last piece at all.
        public bool DoesWaitFire
        {
            get
            {
                return WaitTicks < LastPieceTicks;
            }
        }

        // Both cuts for a note of a length under a gate, the last piece being the one AddmusicK splits off whatever the exporter writes last.
        public static KeyOffTriggers For(int lengthTicks, int gate, int? finalPieceTicks, Func<int, int> waitTimeAt)
        {
            int lastPieceTicks = MmlDurationEncoding.LastTiedPieceTicks(finalPieceTicks ?? lengthTicks);
            int leadTicks = lengthTicks - lastPieceTicks;
            int gateTicks = GateCounterTicks(lastPieceTicks, gate);
            int waitTicks = lastPieceTicks;
            for (int ticksIn = Math.Max(1, lastPieceTicks - DefaultWaitTime); ticksIn < lastPieceTicks; ticksIn++)
            {
                if (lastPieceTicks - ticksIn == waitTimeAt(leadTicks + ticksIn))
                {
                    waitTicks = ticksIn;
                    break;
                }
            }

            return new KeyOffTriggers(leadTicks, lastPieceTicks, gateTicks, waitTicks);
        }
    }
}
