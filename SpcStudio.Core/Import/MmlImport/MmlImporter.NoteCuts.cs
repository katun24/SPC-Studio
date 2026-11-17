using SpcStudio.Model;

namespace SpcStudio.Import;

// The importer's note cuts: a sounding note split in two where a change lands inside it.
public static partial class MmlImporter
{
    // Cuts each of a channel's sounding notes where a change lands inside it, in tick order, where the change is an n, a tie under a q of its own or a new tuning.
    private static void CutNotes(
        Song song, int channel, List<(int Tick, CutReason Reason, Func<NoteCut> Cut)> walkCuts, List<(NoteCut Cut, int Line, EnvelopeWrite Wrote)> tuningCuts,
        Dictionary<(int Channel, int Tick), MmlNote> readFrom, Lazy<Func<int, int>> waitTimeAt, CutsHeard cutsHeard)
    {
        // Every cut as its tick, whether the note can be cut there, what to do where it cannot, what to note down where it can, and what makes it.
        var cuts = walkCuts
            .Select(walkCut => (
                walkCut.Tick,
                CanCut: (Func<Note, bool>)(note => walkCut.Reason == CutReason.Noise || song.IsNoiseNote(note)
                    ? NoteCuts.CanCutAcrossPitchSweepsAt(note, walkCut.Tick - note.StartTick)
                    : NoteCuts.CanCutAt(note, walkCut.Tick - note.StartTick)),
                RecordBlocked: (Action)(() => RecordBlockedWalkCut(walkCut.Reason, channel, walkCut.Tick, cutsHeard)),
                RecordCut: (Action<Note>)(_ => { }),
                MakeCut: walkCut.Cut))
            .Concat(tuningCuts.Select(tuningCut => (
                tuningCut.Cut.Tick,
                CanCut: (Func<Note, bool>)(note => NoteCuts.CanCutAcrossPitchSweepsAt(note, tuningCut.Cut.Tick - note.StartTick)),
                RecordBlocked: (Action)(() => throw MmlImportException.TuningUnderTremolo(tuningCut.Wrote, tuningCut.Cut.Tick, channel, tuningCut.Line)),
                RecordCut: (Action<Note>)(note => RecordTuningCutRestarts(song, channel, note, tuningCut.Cut, cutsHeard)),
                MakeCut: (Func<NoteCut>)(() => tuningCut.Cut))))
            .OrderBy(cut => cut.Tick)
            .ToList();

        foreach (var (tick, canCut, recordBlocked, recordCut, makeCut) in cuts)
        {
            if (NoteCuts.NoteToCut(song, channel, tick) is not { } note)
            {
                continue;
            }

            int soundedEndTick = SoundedEndTick(channel, note, readFrom, waitTimeAt);
            if (soundedEndTick <= tick)
            {
                continue;
            }

            if (!canCut(note))
            {
                recordBlocked();
                continue;
            }

            recordCut(note);
            NoteCuts.Cut(song, channel, note, makeCut(), soundedEndTick);
        }
    }

    // Records a cut the walk asked for that a sweep or a bend across its tick stopped, for that cut's own warning.
    private static void RecordBlockedWalkCut(CutReason reason, int channel, int tick, CutsHeard cutsHeard)
    {
        if (reason == CutReason.Noise)
        {
            cutsHeard.NoiseInsideNotes.Add((channel, tick));
            return;
        }

        cutsHeard.TieGatesInsideNotes.Add((channel, tick));
    }

    // Records each of the vibrato and the bend that a tuning cut about to be made restarts differently from the note it cuts, for their own warnings.
    private static void RecordTuningCutRestarts(Song song, int channel, Note note, NoteCut cut, CutsHeard cutsHeard)
    {
        if (NoteCuts.IsVibratoRestartHeard(song, note, cut))
        {
            cutsHeard.VibratosRestarted.Add((channel, cut.Tick));
        }

        if (NoteCuts.IsBendRestartHeard(song, note, cut))
        {
            cutsHeard.BendsRestarted.Add((channel, cut.Tick));
        }
    }

    // The tick a placed note stops sounding on, read off the note of the file it was placed from - or its own end, where it is bound to the next or held or was cut out of another.
    private static int SoundedEndTick(int channel, Note note, Dictionary<(int Channel, int Tick), MmlNote> readFrom, Lazy<Func<int, int>> waitTimeAt)
    {
        if (note.IsLegato || note.IsHeld || !readFrom.TryGetValue((channel, note.StartTick), out var fileNote))
        {
            return note.EndTick();
        }

        return fileNote.Tick + fileNote.SoundedTicks(Note.GateOfQuantization(fileNote.LastByteQuantization), fileNote.LastPieceTicks, waitTimeAt.Value);
    }

    // The cuts a note's ties under a q of their own make, one where each of them starts.
    private static List<(int Tick, CutReason Reason, Func<NoteCut> Cut)> TieGateCuts(MmlNote note)
    {
        if (!note.HasKnownLayout)
        {
            return [];
        }

        return note.TieGates
            .Where(tieGate => tieGate.OffsetTicks < note.LengthTicks)
            .Select(tieGate => (note.Tick + tieGate.OffsetTicks, CutReason.TieGate, (Func<NoteCut>)(() => new NoteCut(note.Tick + tieGate.OffsetTicks, SecondHalfQuantization: tieGate.Quantization))))
            .ToList();
    }

    // The cuts that put each instrument change whose tuning a vibrato or a pitch bend carries onto the sounding note on a note of its own, each where one of them first moves the pitch.
    private static List<(NoteCut Cut, int Line, EnvelopeWrite Wrote)> TuningCuts(
        Song song, int channel, List<EnvelopeRegisterWrite> commands)
    {
        var notes = song.Notes(channel);
        var cuts = new List<(NoteCut Cut, int Line, EnvelopeWrite Wrote)>();
        foreach (var (audibleTick, lastWrite, hasInstrumentChange) in HeardEnvelopeWrites(notes, commands))
        {
            if (!hasInstrumentChange || song.InstrumentById(lastWrite.Instrument) is not { } appliedInstrument || LastNoteStartedBy(notes, audibleTick) is not { } soundingNote
                || song.InstrumentOf(soundingNote) is not { } playingInstrument
                || appliedInstrument.HasSameTuningAs(playingInstrument) || !NoteCuts.IsPitchMovingFrom(soundingNote, audibleTick))
            {
                continue;
            }

            if (playingInstrument.IsNoise)
            {
                throw MmlImportException.TuningUnderSweepOnNoise(lastWrite.Wrote, lastWrite.Tick, channel, lastWrite.Line);
            }

            int cutTick = NoteCuts.PitchFirstMovesTick(soundingNote, audibleTick);
            cuts.Add((new NoteCut(cutTick, lastWrite.Instrument), lastWrite.Line, lastWrite.Wrote));
        }

        return cuts;
    }

    // Why a change landing inside a sounding note cuts it in two, which decides what is done where it cannot.
    private enum CutReason
    {
        Noise,
        TieGate,
    }

    // The places where a note cut sounds different from the ROM, each kind gathered across the channels for its own warning.
    private sealed class CutsHeard
    {
        public List<(int Channel, int Tick)> NoiseInsideNotes { get; } = [];
        public List<(int Channel, int Tick)> TieGatesInsideNotes { get; } = [];
        public List<(int Channel, int Tick)> VibratosRestarted { get; } = [];
        public List<(int Channel, int Tick)> BendsRestarted { get; } = [];
    }
}
