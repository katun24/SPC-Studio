using System.Collections.Immutable;
using SpcStudio.Model;

namespace SpcStudio.Import;

// A change landing inside a sounding note: the tick it lands on, and what the part of the note from there on plays with instead.
public sealed record NoteCut(int Tick, int? SecondHalfInstrumentId = null, int? SecondHalfSemitone = null, int? SecondHalfQuantization = null);

// Cuts a sounding note in two where a change lands inside it, the second half bound to the first by legato and carrying the change.
public static class NoteCuts
{
    // The note a change landing at that tick lands inside, or null where it lands on a note's start or where no note is there.
    public static Note? NoteToCut(Song song, int channel, int tick)
    {
        if (song.NoteAt(channel, tick) is not { } note || note.StartTick == tick)
        {
            return null;
        }

        return note;
    }

    // Whether nothing the bound second half would restart is running across that many ticks into the note.
    public static bool CanCutAt(Note note, int offsetTicks)
    {
        if (RunsAcross(BendStretches(note), offsetTicks) || RunsAcross(VibratoStretches(note), offsetTicks))
        {
            return false;
        }

        return CanCutAcrossPitchSweepsAt(note, offsetTicks);
    }

    // Whether nothing but a pitch bend or a vibrato the bound second half would restart is running across that many ticks into the note.
    public static bool CanCutAcrossPitchSweepsAt(Note note, int offsetTicks)
    {
        return !RunsAcross(TremoloStretches(note), offsetTicks);
    }

    // Whether the vibrato a cut's second half starts again sweeps differently from the one the original note carried on with: one running across the cut
    // whose fade is still widening there, or whose wave is not back at its start there.
    public static bool IsVibratoRestartHeard(Song song, Note note, NoteCut cut)
    {
        int cutTicksIntoNote = cut.Tick - note.StartTick;
        if (!IsPitchCarried(song, note, cut) || RunningVibrato(note, cutTicksIntoNote) is not { } running)
        {
            return false;
        }

        int ticksSweptByCut = cutTicksIntoNote - running.StartTicks;
        return !IsFadeDoneAt(running.Vibrato, ticksSweptByCut) || SnesVibrato.PhaseAfter(SnesVibrato.StartPhase, running.Vibrato.Rate, ticksSweptByCut) != SnesVibrato.StartPhase;
    }

    // Whether the bend a cut carries onto its second half plays differently from the original's: one running across the cut that has not reached a whole
    // semitone there, or whose step from there differs.
    public static bool IsBendRestartHeard(Song song, Note note, NoteCut cut)
    {
        return IsPitchCarried(song, note, cut) && CarryRunningBend(note, cut.Tick - note.StartTick) is { IsExact: false };
    }

    // The first tick at or after a tick that a pitch bend or a vibrato of the note moves its pitch on, or that tick itself where one is already running there or none is still to come.
    public static int PitchFirstMovesTick(Note note, int tick)
    {
        var stretchStartsStillToEnd = PitchStretchesEndingAfter(note, tick).Select(stretch => stretch.StartTicks).ToList();
        int ticksIntoNote = tick - note.StartTick;

        if (stretchStartsStillToEnd.Count == 0 || stretchStartsStillToEnd.Min() < ticksIntoNote)
        {
            return tick;
        }

        return note.StartTick + stretchStartsStillToEnd.Min();
    }

    // Whether a bend or a running vibrato still moves the note's pitch at or after a tick.
    public static bool IsPitchMovingFrom(Note note, int tick)
    {
        return PitchStretchesEndingAfter(note, tick).Any();
    }

    // Replaces the note with its part in front of the cut, bound by legato to its part from the cut to where it stops sounding.
    public static void Cut(Song song, int channel, Note note, NoteCut cut, int soundedEndTick)
    {
        int cutTicksIntoNote = cut.Tick - note.StartTick;
        var firstHalf = note.CutToSpan(note.StartTick, cutTicksIntoNote) with { IsLegato = true, IsHeld = false };
        bool isPitchCarried = IsPitchCarried(song, note, cut);
        var carriedBend = isPitchCarried ? CarryRunningBend(note, cutTicksIntoNote) : null;
        var secondHalf = note.CutToSpan(cut.Tick, soundedEndTick - cut.Tick) with
        {
            Bends = carriedBend?.Bends ?? BendsFrom(note, cutTicksIntoNote),
            Semitone = cut.SecondHalfSemitone ?? carriedBend?.Semitone ?? PitchReached(song, note, cutTicksIntoNote),
            InstrumentId = cut.SecondHalfInstrumentId ?? note.InstrumentId,
            Gate = cut.SecondHalfQuantization is { } gateQuantization ? Note.GateOfQuantization(gateQuantization) : note.Gate,
            Velocity = cut.SecondHalfQuantization is { } velocityQuantization ? Note.VelocityOfQuantization(velocityQuantization) : note.Velocity,
        };

        if (isPitchCarried)
        {
            secondHalf = secondHalf with { Vibratos = WithDoneFadeDropped(secondHalf.Vibratos, note, cutTicksIntoNote) };
        }

        song.LiftNote(channel, note.StartTick);
        song.PlaceNote(channel, firstHalf);
        song.PlaceNote(channel, secondHalf);
    }

    // The note's bends that start at or after that many ticks into it, the first one's delay counted from there.
    private static ImmutableArray<PitchBend> BendsFrom(Note note, int offsetTicks)
    {
        var moved = ImmutableArray.CreateBuilder<PitchBend>();
        foreach (var (stretch, bend) in BendStretches(note).Zip(note.Bends))
        {
            if (stretch.StartTicks >= offsetTicks)
            {
                moved.Add(moved.Count == 0 ? bend with { DelayTicks = stretch.StartTicks - offsetTicks } : bend);
            }
        }

        return moved.ToImmutable();
    }

    // The row the note is sounding on that many ticks into it, once the bends in front of there have landed - a noise note's row being its clock and moved by none of them.
    private static int PitchReached(Song song, Note note, int offsetTicks)
    {
        if (song.IsNoiseNote(note))
        {
            return note.Semitone;
        }

        return note.Semitone + BendStretches(note)
            .Zip(note.Bends)
            .Where(pair => pair.First.StartTicks < offsetTicks)
            .Sum(pair => pair.Second.OffsetSemitones);
    }

    // Whether the pitch is heard on both sides of a cut, which neither the note nor its second half being a noise note says.
    private static bool IsPitchCarried(Song song, Note note, NoteCut cut)
    {
        return !song.IsNoiseNote(note) && song.InstrumentById(cut.SecondHalfInstrumentId ?? note.InstrumentId) is not { IsNoise: true };
    }

    // The bend running across a cut that many ticks into the note, carried onto the second half: the semitone nearest the pitch it has reached there, the rest of its ramp from that semitone to its
    // own target with the bends behind it, and whether that plays exactly as the original does - or null where no bend runs across the cut.
    private static CarriedBend? CarryRunningBend(Note note, int offsetTicks)
    {
        var stretches = BendStretches(note);
        int runningBendIndex = stretches.FindIndex(stretch => stretch.StartTicks < offsetTicks && stretch.EndTicks > offsetTicks);
        if (runningBendIndex < 0)
        {
            return null;
        }

        var (startTicks, endTicks, _) = stretches[runningBendIndex];
        var runningBend = note.Bends[runningBendIndex];
        int fromSemitone = note.Semitone + PitchBend.OffsetBefore(note.Bends, runningBendIndex);
        int targetSemitone = fromSemitone + runningBend.OffsetSemitones;
        int targetNoteValue = SnesPitch.NoteValueFromC4(targetSemitone);
        int fromValue16 = SnesFade.Value16Of(SnesPitch.NoteValueFromC4(fromSemitone), note.FineTune);
        var rampAtCut = PitchBend.RampFrom(fromValue16, targetNoteValue, runningBend.LengthTicks).Advanced(offsetTicks - startTicks);
        int nearestSemitoneAtCut = fromSemitone + (int)Math.Round((rampAtCut.Value16 - fromValue16) / (double)SnesPitch.FineTuneStepsPerSemitone, MidpointRounding.AwayFromZero);
        int remainingTicks = endTicks - offsetTicks;
        var carriedRamp = PitchBend.RampFrom(SnesFade.Value16Of(SnesPitch.NoteValueFromC4(nearestSemitoneAtCut), note.FineTune), targetNoteValue, remainingTicks);
        bool isCarriedRampExact = carriedRamp.Value16 == rampAtCut.Value16 && carriedRamp.Delta16 == rampAtCut.Delta16;
        var runningBendRemainder = new PitchBend(PitchBend.MinDelayTicks, remainingTicks, targetSemitone - nearestSemitoneAtCut);
        return new CarriedBend(nearestSemitoneAtCut, [runningBendRemainder, .. note.Bends.Skip(runningBendIndex + 1)], isCarriedRampExact);
    }

    // A second half's vibrato stretches with the fade taken off the one running across the cut, where that fade has already widened all the way.
    private static ImmutableArray<NoteVibrato> WithDoneFadeDropped(ImmutableArray<NoteVibrato> secondHalfVibratos, Note note, int offsetTicks)
    {
        if (secondHalfVibratos.IsDefaultOrEmpty || RunningVibrato(note, offsetTicks) is not { } running || !IsFadeDoneAt(running.Vibrato, offsetTicks - running.StartTicks))
        {
            return secondHalfVibratos;
        }

        return secondHalfVibratos.SetItem(0, secondHalfVibratos[0] with { FadeTicks = SnesVibrato.MinFadeTicks });
    }

    // The vibrato stretch that sweeps across that many ticks into the note, with where it starts, or null where none does.
    private static (NoteVibrato Vibrato, int StartTicks)? RunningVibrato(Note note, int offsetTicks)
    {
        var stretches = VibratoStretches(note);
        int runningVibratoIndex = stretches.FindIndex(stretch => stretch.IsActive && stretch.StartTicks < offsetTicks && stretch.EndTicks > offsetTicks);
        if (runningVibratoIndex < 0)
        {
            return null;
        }

        return (note.Vibratos[runningVibratoIndex], stretches[runningVibratoIndex].StartTicks);
    }

    // Whether a vibrato's fade has widened all the way by the tick some ticks after the vibrato started, the sweep stepping on its own first tick too.
    private static bool IsFadeDoneAt(NoteVibrato vibrato, int ticksRun)
    {
        int fadeStepsTaken = ticksRun + 1;
        return !vibrato.State.HasFade || fadeStepsTaken >= vibrato.FadeTicks;
    }

    // The note's bends and running vibrato stretches that end after a tick.
    private static IEnumerable<(int StartTicks, int EndTicks, bool IsActive)> PitchStretchesEndingAfter(Note note, int tick)
    {
        int ticksIntoNote = tick - note.StartTick;
        return BendStretches(note).Concat(VibratoStretches(note)).Where(stretch => stretch.IsActive && stretch.EndTicks > ticksIntoNote);
    }

    // Whether a stretch that is switched on has started before that many ticks into the note and runs on past them.
    private static bool RunsAcross(IEnumerable<(int StartTicks, int EndTicks, bool IsActive)> stretches, int offsetTicks)
    {
        return stretches.Any(stretch => stretch.IsActive && stretch.StartTicks < offsetTicks && stretch.EndTicks > offsetTicks);
    }

    // Where each of the note's bends starts and ends, counted from the note's start.
    private static List<(int StartTicks, int EndTicks, bool IsActive)> BendStretches(Note note)
    {
        return Stretches(note.Bends.Select(bend => (bend.DelayTicks, bend.LengthTicks, true)));
    }

    // Where each of the note's vibrato stretches starts and ends, counted from the note's start, with whether it sweeps at all.
    private static List<(int StartTicks, int EndTicks, bool IsActive)> VibratoStretches(Note note)
    {
        return Stretches(note.Vibratos.Select(vibrato => (vibrato.DelayTicks, vibrato.LengthTicks, vibrato.IsActive)));
    }

    // Where each of the note's tremolo stretches starts and ends, counted from the note's start, with whether it sweeps at all.
    private static List<(int StartTicks, int EndTicks, bool IsActive)> TremoloStretches(Note note)
    {
        return Stretches(note.Tremolos.Select(tremolo => (tremolo.DelayTicks, tremolo.LengthTicks, tremolo.IsActive)));
    }

    // A chain's delays and lengths as where each stretch starts and ends, counted from the note's start.
    private static List<(int StartTicks, int EndTicks, bool IsActive)> Stretches(IEnumerable<(int DelayTicks, int LengthTicks, bool IsActive)> chain)
    {
        var stretches = new List<(int StartTicks, int EndTicks, bool IsActive)>();
        int endTicks = 0;
        foreach (var (delayTicks, lengthTicks, isActive) in chain)
        {
            int stretchStartTicks = endTicks + delayTicks;
            endTicks = stretchStartTicks + lengthTicks;
            stretches.Add((stretchStartTicks, endTicks, isActive));
        }

        return stretches;
    }

    // A running bend carried onto a cut's second half: the semitone that half starts on, its whole chain, and whether it plays exactly as the original does.
    private sealed record CarriedBend(int Semitone, ImmutableArray<PitchBend> Bends, bool IsExact);
}
