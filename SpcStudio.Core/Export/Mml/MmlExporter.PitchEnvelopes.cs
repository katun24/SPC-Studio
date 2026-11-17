using System.Collections.Immutable;
using SpcStudio.Model;

namespace SpcStudio.Export;

// Turns a song into an AddmusicK MML file. This part holds the pitch envelope armed in front of a run of notes whose first bend is the same, in place of a
// $DD behind each of them.
public static partial class MmlExporter
{
    // The runs of notes worth playing their first bend from a pitch envelope, and the command a note keys on under.
    private static class PitchEnvelopes
    {
        public static readonly string OffCommand = PitchEnvOffName.Write();

        private static readonly int BendBytes = WrittenTokens.BendTokenCount * SongDataSize.HexByteBytes;
        private static readonly int ArmAndOffBytes = AmkCommands.LengthOf(AmkCommands.PitchEnvelopeRelease) * SongDataSize.HexByteBytes + WrittenTokens.CompiledBytesOf(OffCommand);

        // The envelope each note keys on under, by the note's start tick, for every note of a channel in a run worth arming: consecutive notes keying on
        // whose first bends one envelope plays, the run saving more bytes than its arm and its switch-off cost.
        public static Dictionary<int, string> ForChannel(Song song, int channel, HeldNoteShape shape, List<int> commandTicks, int markerTick, ChangeMap staccato)
        {
            var commandsByNoteTick = new Dictionary<int, string>();
            var runNotes = new List<(int NoteTick, int BytesSaved)>();
            string? runCommand = null;

            foreach (var note in NotesKeyingOn(song, channel))
            {
                bool isWrittenShort = HoldToggles.IsWrittenShort(song, channel, note, shape);
                var (command, bytesSaved) = EnvelopeWorth(note, isWrittenShort, commandTicks, markerTick, CutTicksToKeep(song, channel, note, isWrittenShort, staccato));
                if (command != runCommand)
                {
                    KeepIfPaying(commandsByNoteTick, runNotes, runCommand);
                    runNotes.Clear();
                    runCommand = command;
                }

                if (command is not null)
                {
                    runNotes.Add((note.StartTick, bytesSaved));
                }
            }

            KeepIfPaying(commandsByNoteTick, runNotes, runCommand);
            return commandsByNoteTick;
        }

        // The envelope a channel ends on: the one its last note keys on under, or switched off.
        public static string AtEnd(Song song, int channel, Dictionary<int, string> commandsByNoteTick)
        {
            var notes = NotesKeyingOn(song, channel);
            return notes.Count > 0 ? commandsByNoteTick.GetValueOrDefault(notes[^1].StartTick, OffCommand) : OffCommand;
        }

        // The command that arms the envelope playing a note's first bend where the bend is placed at the key-on, or the switch-off where it is not.
        public static string CommandFor(Note note, ImmutableArray<BendPlacement> placements)
        {
            if (placements.IsDefaultOrEmpty || placements[0].PieceStartTick != BendWalk.KeyOnPiece)
            {
                return OffCommand;
            }

            return PitchEnvName.Write(placements[0].DelayByte, placements[0].LengthByte, unchecked((byte)note.Bends[0].OffsetSemitones));
        }

        // A channel's notes the export writes as notes rather than as rests, each of which arms the envelope in force.
        private static List<Note> NotesKeyingOn(Song song, int channel)
        {
            return [.. song.NotesInsideSong(channel).Where(note => song.InstrumentOf(note) is not null)];
        }

        // Adds a run's notes where the bytes it saves pay for its arm and its switch-off.
        private static void KeepIfPaying(Dictionary<int, string> commandsByNoteTick, List<(int NoteTick, int BytesSaved)> runNotes, string? command)
        {
            if (command is null || runNotes.Sum(note => note.BytesSaved) <= ArmAndOffBytes)
            {
                return;
            }

            foreach (var (noteTick, _) in runNotes)
            {
                commandsByNoteTick[noteTick] = command;
            }
        }

        // The envelope that would play a note's first bend and a guess at the bytes the note saves by it, or no envelope where it has no bend or the
        // envelope cannot play its chain, both placed where the note's cuts stay put where it has any to keep.
        private static (string? Command, int BytesSaved) EnvelopeWorth(
            Note note, bool isWrittenShort, List<int> commandTicks, int markerTick, Func<int, (int? GateCutTick, int? WaitCutTick)>? cutTicksToKeep)
        {
            if (note.Bends.IsDefaultOrEmpty)
            {
                return (null, 0);
            }

            int? writtenTicks = WrittenTicksOf(note, isWrittenShort);
            var breakOffsets = BreakOffsetsInside(note, writtenTicks ?? note.LengthTicks, commandTicks, markerTick);
            var plainPlacements = PitchBend.Placements(note.Bends, note.LengthTicks, writtenTicks, breakOffsets, cutTicksToKeep);
            var envelopePlacements = PitchBend.PlacementsUnderPitchEnvelope(note.Bends, note.LengthTicks, writtenTicks, breakOffsets, cutTicksToKeep);
            if (plainPlacements.Length < note.Bends.Length || envelopePlacements.Length < note.Bends.Length)
            {
                return (null, 0);
            }

            int writtenEndTick = writtenTicks ?? note.LengthTicks;
            int bytesSaved = BendBytes + PieceBytes(breakOffsets, plainPlacements, writtenEndTick, note.LengthTicks) - PieceBytes(breakOffsets, envelopePlacements, writtenEndTick, note.LengthTicks);
            return (CommandFor(note, envelopePlacements), bytesSaved);
        }

        // A guess at the bytes a note's pieces take with its bends written behind the pieces they are placed at.
        private static int PieceBytes(List<int> breakOffsets, ImmutableArray<BendPlacement> placements, int writtenEndTick, int noteLengthTicks)
        {
            var layout = BendWalk.Layout.CompiledFrom(breakOffsets, placements.Select(placement => placement.PieceStartTick), writtenEndTick, noteLengthTicks);
            return MmlDurationEncoding.EstimatedRunsBytes(layout.PieceStarts, layout.WrittenTicks);
        }
    }
}
