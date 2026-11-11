using System.Collections.Immutable;
using SpcStudio.Import;

namespace SpcStudio.Export;

// Turns a song into an AddmusicK MML file. This part holds the export read back through the passes the import makes ahead of its walk, which the
// optimizations that can change what the driver reads check themselves against.
public static partial class MmlExporter
{
    // An exported text as the import reads it back ahead of its walk, and where two such read-backs part.
    private static class ReadBacks
    {
        // The channels a text comes back as through every pass the import makes ahead of its walk, and the warnings it gives, or null where it is refused.
        public static StreamsReadBack? ReadBack(string text)
        {
            var warnings = new List<string>();
            try
            {
                return new StreamsReadBack(MmlImporter.RewriteStreamsBeforeWalk(MmlParser.Parse(text), warnings.Add).Channels, warnings);
            }
            catch (MmlImportException)
            {
                return null;
            }
        }

        // The warnings a read-back gives that another does not.
        public static List<string> NewWarnings(StreamsReadBack reference, StreamsReadBack other)
        {
            var newWarnings = other.Warnings.ToList();
            foreach (string warning in reference.Warnings)
            {
                newWarnings.Remove(warning);
            }

            return newWarnings;
        }

        // Each channel of a reference read-back that another parts from, with the tick of the first note apart, or null where the other has no such
        // channel.
        public static List<(int Channel, int? TickApart)> FirstTicksApartByChannel(StreamsReadBack reference, StreamsReadBack other)
        {
            var ticksApart = new List<(int Channel, int? TickApart)>();

            foreach (var channel in reference.Channels)
            {
                if (other.Channels.FirstOrDefault(otherChannel => otherChannel.Number == channel.Number) is not { } otherChannel)
                {
                    ticksApart.Add((channel.Number, null));
                    continue;
                }

                if (FirstTickApart(channel, otherChannel) is int tickApart)
                {
                    ticksApart.Add((channel.Number, tickApart));
                }
            }

            return ticksApart;
        }

        // The tick of the first note at which two read-backs of a channel part, or null where every note is heard the same.
        private static int? FirstTickApart(MmlChannel reference, MmlChannel other)
        {
            var referenceNotes = NotesWithTransposes(reference);
            var otherNotes = NotesWithTransposes(other);

            for (int noteIndex = 0; noteIndex < Math.Max(referenceNotes.Count, otherNotes.Count); noteIndex++)
            {
                if (noteIndex >= referenceNotes.Count)
                {
                    return otherNotes[noteIndex].Note.Tick;
                }

                if (noteIndex >= otherNotes.Count)
                {
                    return referenceNotes[noteIndex].Note.Tick;
                }

                if (!IsHeardTheSame(referenceNotes[noteIndex], otherNotes[noteIndex]))
                {
                    return Math.Min(referenceNotes[noteIndex].Note.Tick, otherNotes[noteIndex].Note.Tick);
                }
            }

            return null;
        }

        // A channel's notes in stream order, each with the $FA $02 transpose it keys on under.
        private static List<NoteUnderTranspose> NotesWithTransposes(MmlChannel channel)
        {
            var notes = new List<NoteUnderTranspose>();
            int transpose = 0;

            foreach (var channelEvent in channel.Events)
            {
                if (channelEvent is MmlTranspose transposeSet)
                {
                    transpose = transposeSet.Semitones;
                }
                else if (channelEvent is MmlNote note)
                {
                    notes.Add(new NoteUnderTranspose(note, transpose));
                }
            }

            return notes;
        }

        // Whether two notes, each under its transpose, key on at the same tick at the same pitch and play out the same.
        private static bool IsHeardTheSame(NoteUnderTranspose first, NoteUnderTranspose second)
        {
            return first.Note.Tick == second.Note.Tick
                   && first.Note.NoteValue + first.Transpose == second.Note.NoteValue + second.Transpose
                   && first.Note.LengthTicks == second.Note.LengthTicks
                   && first.Note.Quantization == second.Note.Quantization
                   && first.Note.FineTune == second.Note.FineTune
                   && first.Note.IsHeld == second.Note.IsHeld
                   && HasSameItems(first.Note.Pieces, second.Note.Pieces)
                   && HasSameItems(first.Note.RestPieces, second.Note.RestPieces)
                   && HasSameItems(first.Note.TieGates, second.Note.TieGates)
                   && HasSameItems(first.Note.Bends, second.Note.Bends);
        }

        // Whether two arrays hold the same items in the same order, a default array holding none.
        private static bool HasSameItems<TItem>(ImmutableArray<TItem> first, ImmutableArray<TItem> second)
        {
            return first.IsDefaultOrEmpty ? second.IsDefaultOrEmpty : !second.IsDefault && first.SequenceEqual(second);
        }

        // A note as read back, and the $FA $02 transpose it keys on under.
        private sealed record NoteUnderTranspose(MmlNote Note, int Transpose);
    }

    // The channels a text comes back as through the passes the import makes ahead of its walk, and the warnings those gave.
    private sealed record StreamsReadBack(IReadOnlyList<MmlChannel> Channels, List<string> Warnings);
}
