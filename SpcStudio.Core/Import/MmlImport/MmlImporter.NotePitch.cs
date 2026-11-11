using System.Collections.Immutable;
using SpcStudio.Model;

namespace SpcStudio.Import;

// The importer's note pitch: a note's pitch bends, the pitch envelope's among them, as the driver plays them.
public static partial class MmlImporter
{
    // A note's pitch bends as the driver plays them, the pitch envelope's first, and the tick the note has to end at where it would sound a slide into silence.
    private static (ImmutableArray<PitchBend> Bends, int? SilentFrom) ResolvedBends(
        MmlNote note, int noteSemitone, int globalTranspose, (int DelayTicks, int LengthTicks, int TargetSemitone)? pitchEnvelopeRamp, PitchCeiling? ceiling)
    {
        var (writtenRamps, targetSemitones) = WrittenRamps(note, globalTranspose, pitchEnvelopeRamp);
        if (writtenRamps.Count == 0)
        {
            return ([], null);
        }

        var layout = FetchLayout(note);
        var (playedRamps, unreadRampIndex) = BendWalk.WalkChain(layout, writtenRamps);
        if (unreadRampIndex is not null)
        {
            throw MmlImportException.UnreadBend(note.Line);
        }

        var chain = ImmutableArray.CreateBuilder<PitchBend>(writtenRamps.Count);
        int previousEndTick = 0;
        int previousSemitone = noteSemitone;
        for (int rampIndex = 0; rampIndex < playedRamps.Length; rampIndex++)
        {
            int startTick = playedRamps[rampIndex].FirstStepTick - 1;
            if (startTick >= note.LengthTicks)
            {
                break;
            }

            int endTick = playedRamps[rampIndex].LastStepTick;
            int targetSemitone = targetSemitones[rampIndex];
            bool isTargetPastB7 = targetSemitone + SnesPitch.C4NoteValue > SnesPitch.MaxPlayableNoteValue;
            if (isTargetPastB7)
            {
                if (ceiling is null)
                {
                    break;
                }

                int startValue16 = SnesFade.Value16Of(previousSemitone + SnesPitch.C4NoteValue, note.FineTune);
                var (cutLengthByte, cutTargetNoteValue, silentFromTick) = ceiling.CutRamp(
                    layout, playedRamps[rampIndex], writtenRamps[rampIndex].LengthByte, targetSemitone + SnesPitch.C4NoteValue, startValue16);
                if (silentFromTick is not null)
                {
                    return (chain.ToImmutable(), silentFromTick);
                }

                if (cutLengthByte is null || cutTargetNoteValue is null)
                {
                    break;
                }

                endTick = layout.LastStepTick(playedRamps[rampIndex].FirstStepTick, cutLengthByte.Value);
                targetSemitone = cutTargetNoteValue.Value - SnesPitch.C4NoteValue;
            }

            endTick = layout.FirstSteppableTickFrom(endTick + 1) - 1;
            int lengthTicks = Math.Min(endTick - startTick, PitchBend.MaxLengthTicks);
            chain.Add(new PitchBend(startTick - previousEndTick, lengthTicks, targetSemitone - previousSemitone));
            previousEndTick = startTick + lengthTicks;
            previousSemitone = targetSemitone;
            if (isTargetPastB7)
            {
                break;
            }
        }

        return (chain.ToImmutable(), null);
    }

    // The ramps the stream arms on a note, the pitch envelope's first, each with the semitone above C4 it ramps to.
    private static (List<BendWalk.WrittenRamp> Ramps, List<int> TargetSemitones) WrittenRamps(
        MmlNote note, int globalTranspose, (int DelayTicks, int LengthTicks, int TargetSemitone)? pitchEnvelopeRamp)
    {
        var ramps = new List<BendWalk.WrittenRamp>();
        var targetSemitones = new List<int>();
        if (pitchEnvelopeRamp is { } armedAtKeyOn)
        {
            ramps.Add(new(BendWalk.KeyOnPiece, armedAtKeyOn.DelayTicks, armedAtKeyOn.LengthTicks));
            targetSemitones.Add(armedAtKeyOn.TargetSemitone);
        }

        foreach (var bend in note.Bends)
        {
            ramps.Add(new(bend.ArmTicks, bend.DelayTicks, bend.LengthTicks));
            targetSemitones.Add(BendTarget(bend, globalTranspose) - SnesPitch.C4NoteValue);
        }

        return (ramps, targetSemitones);
    }

    // The note value a $DD ramps to: the target the file wrote with the global transpose added and the top bit masked off.
    private static int BendTarget(MmlPitchBend bend, int globalTranspose)
    {
        return SnesPitch.SoundedNoteValue(bend.TargetNoteValue + globalTranspose);
    }

    // How far above a note the vibrato reaches at most while it sounds, in 256ths of a semitone: the sweep in force at its key-on and every one set inside it.
    private static int VibratoPeak(List<MmlEvent> vibratoEventsBefore, MmlChannel channel, MmlNote note)
    {
        // The reach of the vibrato in force at the note's key-on, or none where there is none.
        int peak = vibratoEventsBefore.OfType<MmlVibrato>().LastOrDefault() is { } vibratoInForce ? SnesVibrato.PeakOffset(vibratoInForce.Extent) : 0;
        foreach (var channelEvent in channel.Events)
        {
            if (channelEvent is MmlVibrato vibratoInside && vibratoInside.Tick >= note.Tick && vibratoInside.Tick < note.EndTick)
            {
                peak = Math.Max(peak, SnesVibrato.PeakOffset(vibratoInside.Extent));
            }
        }

        return peak;
    }

    // A note's pieces as the driver fetches them, the pieces of a rest it carries included, and one piece where the note carries no layout of its own.
    private static BendWalk.Layout FetchLayout(MmlNote note)
    {
        if (!note.RestPieces.IsDefaultOrEmpty)
        {
            return BendWalk.Layout.OfPieces(note.Pieces.Concat(note.RestPieces), note.LengthTicks);
        }

        var notePiecesLayout = BendWalk.Layout.OfPieces(note.Pieces, note.LengthTicks);
        ImmutableArray<int> pieceStartsInsideNote = [.. notePiecesLayout.PieceStarts.Where(pieceStart => pieceStart < note.LengthTicks)];
        if (pieceStartsInsideNote.Length == 0)
        {
            return new BendWalk.Layout([0], note.LengthTicks, note.LengthTicks);
        }

        return new BendWalk.Layout(pieceStartsInsideNote, Math.Min(notePiecesLayout.WrittenTicks, note.LengthTicks), note.LengthTicks);
    }

    // What cuts a note's ramps past B7 at B7, and the note itself where it would sound the slide into silence: its vibrato's reach above it and how long it sounds.
    private sealed record PitchCeiling(int VibratoPeak, int SoundsToTick)
    {
        // A ramp cut at or below B7 as its length byte and target, or nulls where nothing below B7 is left to ramp over, or the tick the note has to end at instead.
        public (int? LengthByte, int? TargetNoteValue, int? SilentFromTick) CutRamp(BendWalk.Layout layout, BendWalk.PlayedRamp ramp, int lengthTicks, int targetNoteValue, int startValue16)
        {
            var rampStart = SnesFade.SetOutFrom(startValue16);
            var fileRamp = rampStart.FadingTo(targetNoteValue, lengthTicks);

            // How many steps of the ramp the note sounds, counting the step after its last tick.
            int heardSteps = layout.SteppableTicksBetween(ramp.FirstStepTick, SoundsToTick - 1) + 1;
            if (IsStepSilent(fileRamp, lengthTicks, heardSteps))
            {
                return (null, null, SilentFromTick(layout, ramp, fileRamp, lengthTicks));
            }

            if (fileRamp.Value == SnesPitch.MaxPlayableNoteValue)
            {
                return (null, null, null);
            }

            if (NearestRampBelowB7(rampStart, fileRamp, heardSteps) is not { } nearestRamp)
            {
                return (null, null, null);
            }

            return (nearestRamp.LengthByte, nearestRamp.TargetNoteValue, null);
        }

        // Whether the file's ramp, that many steps in, is past its own length or has the vibrato's peak riding on it at or above the first silent pitch.
        private bool IsStepSilent(SnesFade fileRamp, int lengthTicks, int step)
        {
            return step >= lengthTicks || fileRamp.Advanced(step).Value16 + VibratoPeak >= SnesFade.Value16Of(SnesPitch.MaxPlayableNoteValue + 1, 0);
        }

        // The tick from the key-on the note would have to end at to sound none of the file ramp's silent part: the ramp's own start, or the last step in front of the first silent one.
        private int SilentFromTick(BendWalk.Layout layout, BendWalk.PlayedRamp ramp, SnesFade fileRamp, int lengthTicks)
        {
            int firstSilentStep = 1;
            while (!IsStepSilent(fileRamp, lengthTicks, firstSilentStep))
            {
                firstSilentStep++;
            }

            if (firstSilentStep == 1)
            {
                return ramp.FirstStepTick - 1;
            }

            return layout.LastStepTick(ramp.FirstStepTick, firstSilentStep - 1);
        }

        // The ramp at or below B7, no shorter than the steps the note sounds, whose delta comes nearest the file's ramp's, or null where there is none.
        private static (int LengthByte, int TargetNoteValue)? NearestRampBelowB7(SnesFade rampStart, SnesFade fileRamp, int heardSteps)
        {
            (int LengthByte, int TargetNoteValue, int DeltaDistance)? nearest = null;
            for (int targetNoteValue = SnesPitch.MaxPlayableNoteValue; targetNoteValue > fileRamp.Value; targetNoteValue--)
            {
                for (int lengthByte = Math.Max(PitchBend.MinLengthTicks, heardSteps); lengthByte <= PitchBend.MaxLengthTicks; lengthByte++)
                {
                    int deltaDistance = Math.Abs(rampStart.FadingTo(targetNoteValue, lengthByte).Delta16 - fileRamp.Delta16);
                    if (nearest is not { } nearestSoFar || deltaDistance < nearestSoFar.DeltaDistance)
                    {
                        nearest = (lengthByte, targetNoteValue, deltaDistance);
                    }
                }
            }

            if (nearest is not { } found)
            {
                return null;
            }

            return (found.LengthByte, found.TargetNoteValue);
        }
    }
}
