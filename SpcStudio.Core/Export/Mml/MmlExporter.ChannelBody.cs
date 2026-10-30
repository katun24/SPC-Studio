using System.Text;
using SpcStudio.Model;

namespace SpcStudio.Export;

// Turns a song into an AddmusicK MML file. This part holds a channel's body: its notes, the rests between them, and the commands and bends written among them.
public static partial class MmlExporter
{
    private const int NoEntry = -1;

    // A channel's notes as the ROM gets them: those starting before the song's end, one running across it cut to it.
    private static IEnumerable<Note> ExportedNotes(Song song, int channel)
    {
        return song.Notes(channel).Where(note => note.StartTick < song.EndTick).Select(note => note.EndTick() <= song.EndTick ? note : note with { LengthTicks = song.EndTick - note.StartTick });
    }

    // A channel's exported notes that are written as notes rather than as rests.
    private static IEnumerable<Note> PlayedNotes(Song song, int channel)
    {
        return ExportedNotes(song, channel).Where(song.IsNotePlayable);
    }

    // Whether the export writes a block for a channel: the command channel always, and any other channel with a note inside the song.
    private static bool WritesChannel(Song song, int channel)
    {
        return channel == CommandChannel || ExportedNotes(song, channel).Any();
    }

    // The $FA $02 shift a note is written under: the one in force where the note fits in the note band under it, and otherwise the smallest that brings it in.
    private static int TransposeFor(int semitone, int transposeInForce)
    {
        int noteValue = SnesPitch.NoteValueFromC4(semitone);
        int writtenNoteValue = noteValue - transposeInForce;

        return writtenNoteValue >= SnesPitch.MinNoteValue && writtenNoteValue <= SnesPitch.MaxNoteValue ? transposeInForce : Math.Max(0, noteValue - SnesPitch.MaxNoteValue);
    }

    // One channel's notes and the commands between them as MML, every gap filled by a rest and the channel padded to the song's end.
    private static string ChannelBodyText(Song song, int channel, List<Instrument> usedInstruments, IReadOnlyList<string> labels, HeldNoteShape shape)
    {
        var (loopPrelude, loopRestatements) = LoopMarkerCommands(song, channel, shape);
        var body = new ChannelBody(
            channel,
            labels,
            ChannelCommands(song, channel, shape),
            LoopMarkerTick(song),
            loopPrelude,
            loopRestatements,
            FineTuneLeftAtEnd(song, channel),
            TransposeLeftAtEnd(song, channel));

        int tick = 0;

        foreach (var note in ExportedNotes(song, channel))
        {
            var instrument = song.InstrumentOf(note);
            int entry = instrument is null ? NoEntry : EntryIndexOf(usedInstruments, instrument);

            tick = body.WriteRests(tick, note.StartTick, entry);
            tick = entry == NoEntry ? body.WriteRests(tick, note.EndTick(), NoEntry) : body.WriteNote(note, entry, instrument!.IsNoise, HoldToggles.IsWrittenShort(song, channel, note, shape));
        }

        body.WriteRests(tick, song.EndTick, NoEntry);
        return body.ToString();
    }

    // One channel's stream as it is written: the text so far, the commands still to go into it, and the driver state the delta pass holds.
    private sealed class ChannelBody
    {
        private const int NoOctave = -1;
        private const int NoQ = -1;
        private const int UnknownTranspose = -1;
        private const int HeldNoteRestTicks = 1;

        private readonly StringBuilder _text = new();
        private readonly Queue<TimedCommand> _pendingCommands;
        private readonly IReadOnlyList<TimedCommand> _loopPrelude;
        private readonly IReadOnlyList<TimedCommand> _loopRestatements;
        private readonly IReadOnlyList<string> _labels;
        private readonly int _fineTuneAtLoop;
        private readonly int _transposeAtLoop;
        private readonly int _channel;
        private int _loopMarkerTick;
        private int _octaveInForce = NoOctave;
        private int _entryInForce = NoEntry;
        private int _entryDueAtTick = NoEntry;
        private int _qInForce = NoQ;
        private int _fineTuneInForce = SnesPitch.MinFineTune;
        private int _transposeInForce = 0;

        // An empty stream for a channel, holding the commands still to write into it and what its loop marker writes and leaves.
        public ChannelBody(
            int channel,
            IReadOnlyList<string> labels,
            IEnumerable<TimedCommand> commands,
            int loopMarkerTick,
            IReadOnlyList<TimedCommand> loopPrelude,
            IReadOnlyList<TimedCommand> loopRestatements,
            int fineTuneAtLoop,
            int transposeAtLoop)
        {
            _channel = channel;
            _labels = labels;
            _pendingCommands = new Queue<TimedCommand>(commands);
            _loopMarkerTick = loopMarkerTick;
            _loopPrelude = loopPrelude;
            _loopRestatements = loopRestatements;
            _fineTuneAtLoop = fineTuneAtLoop;
            _transposeAtLoop = transposeAtLoop;
        }

        // The channel's text as written so far.
        public override string ToString()
        {
            return _text.ToString().TrimEnd();
        }

        // Rests from one tick to another, broken wherever a command falls, with the entry the note starting at the second tick plays written in
        // front of that tick's envelope override; returns the second tick.
        public int WriteRests(int fromTick, int toTick, int entryOfNextNote)
        {
            while (fromTick < toTick)
            {
                WriteCommandsDueAt(fromTick);

                int nextTick = NextBreakTick(fromTick, toTick);
                WriteToken("r" + MmlDurationEncoding.DurationSuffixForTicks(nextTick - fromTick));
                fromTick = nextTick;
            }

            _entryDueAtTick = entryOfNextNote;
            WriteCommandsDueAt(toTick);
            return toTick;
        }

        // A note, with whatever the delta pass owes in front of it; returns the note's end tick.
        public int WriteNote(Note note, int entry, bool isNoise, bool isWrittenShort)
        {
            WriteInstrumentChange(entry);

            if (isNoise)
            {
                WriteToken(WrittenTokens.HexLettered('n', SnesNoise.ClockForSemitone(note.Semitone)));
            }

            if (note.Quantization != _qInForce)
            {
                WriteToken(WrittenTokens.HexLettered('q', note.Quantization));
                _qInForce = note.Quantization;
            }

            if (note.FineTune != _fineTuneInForce)
            {
                WriteToken(DetuneName.Write(note.FineTune));
                _fineTuneInForce = note.FineTune;
            }

            int transpose = TransposeFor(note.Semitone, _transposeInForce == UnknownTranspose ? 0 : _transposeInForce);
            if (transpose != _transposeInForce)
            {
                WriteToken(TransposeName.Write(transpose));
                _transposeInForce = transpose;
            }

            var (octave, semitoneInOctave) = SnesPitch.OctaveAndSemitone(note.Semitone - _transposeInForce);
            if (octave != _octaveInForce)
            {
                WriteToken(LetteredText('o', octave));
                _octaveInForce = octave;
            }

            return WriteNotePieces(note, SnesPitch.NoteNames[semitoneInOctave], isWrittenShort);
        }

        // The note itself as tie-joined pieces, broken wherever a command or a bend falls inside it, and a held note written short given its rest; returns the note's end tick.
        private int WriteNotePieces(Note note, string noteName, bool isWrittenShort)
        {
            var bends = PlacedBends(note, isWrittenShort);
            int fromTick = note.StartTick;
            int writtenEndTick = isWrittenShort ? note.EndTick() - HeldNoteRestTicks : note.EndTick();

            for (bool isFirstPiece = true; fromTick < writtenEndTick; isFirstPiece = false)
            {
                int nextTick = NextBreakTick(fromTick, writtenEndTick);

                foreach (var bend in bends)
                {
                    if (bend.Tick > fromTick && bend.Tick < nextTick)
                    {
                        nextTick = bend.Tick;
                    }
                }

                if (IsAnyBendPlacedAt(bends, fromTick))
                {
                    nextTick = Math.Min(nextTick, fromTick + MmlDurationEncoding.MaxTicksWithoutAutoTies);
                }

                WriteToken((isFirstPiece ? noteName : "^") + MmlDurationEncoding.DurationSuffixForTicks(nextTick - fromTick));
                WriteBendsAt(bends, fromTick);

                fromTick = nextTick;
                if (fromTick < writtenEndTick)
                {
                    WriteCommandsDueAt(fromTick);
                }
            }

            if (isWrittenShort)
            {
                WriteCommandsDueAt(writtenEndTick);
                WriteToken("r" + MmlDurationEncoding.DurationSuffixForTicks(HeldNoteRestTicks));
            }

            return note.EndTick();
        }

        // Where in a note each of its bends is written, with its delay byte, its length byte and the absolute pitch its target byte holds.
        private List<WrittenBend> PlacedBends(Note note, bool isWrittenShort)
        {
            int writtenLengthTicks = isWrittenShort ? note.LengthTicks - HeldNoteRestTicks : note.LengthTicks;

            var breakOffsets = _pendingCommands
                .Select(command => command.Tick)
                .Append(_loopMarkerTick)
                .Where(tick => tick > note.StartTick && tick < note.StartTick + writtenLengthTicks)
                .Select(tick => tick - note.StartTick);

            var placements = PitchBend.Placements(note.Bends, note.LengthTicks, isWrittenShort ? writtenLengthTicks : null, breakOffsets);

            if (placements.Length < note.Bends.Length)
            {
                throw new InvalidOperationException(
                    $"Channel #{_channel} has a note at tick {note.StartTick} whose pitch " +
                    "bends fall too close together, or too close to the commands inside the " +
                    "note, to be written where the driver reads them. The driver would stop at " +
                    "the one it cannot read.");
            }

            var writtenBends = new List<WrittenBend>();
            int semitone = note.Semitone;

            for (int bendIndex = 0; bendIndex < placements.Length; bendIndex++)
            {
                semitone += note.Bends[bendIndex].OffsetSemitones;
                writtenBends.Add(new WrittenBend(note.StartTick + placements[bendIndex].PieceStartTick, placements[bendIndex].DelayByte, placements[bendIndex].LengthByte, semitone));
            }

            return writtenBends;
        }

        // Whether any bend is written behind the piece starting at the tick.
        private static bool IsAnyBendPlacedAt(List<WrittenBend> bends, int tick)
        {
            return bends.Any(bend => bend.Tick == tick);
        }

        // Writes the bends placed behind the piece starting at the tick, in the order the driver reads them.
        private void WriteBendsAt(List<WrittenBend> bends, int tick)
        {
            foreach (var bend in bends.Where(bend => bend.Tick == tick))
            {
                WriteToken(AmkCommands.HexSpelling(AmkCommands.PitchBend, bend.DelayByte, bend.LengthByte, SnesPitch.NoteByte(bend.Semitone)));
            }
        }

        // The next tick between two at which the stream has to be broken, or the second where nothing falls between them.
        private int NextBreakTick(int fromTick, int toTick)
        {
            int nextTick = toTick;

            if (_pendingCommands.Count > 0 && _pendingCommands.Peek().Tick < nextTick)
            {
                nextTick = _pendingCommands.Peek().Tick;
            }

            if (_loopMarkerTick > fromTick && _loopMarkerTick < nextTick)
            {
                nextTick = _loopMarkerTick;
            }

            return nextTick;
        }

        // Writes everything due at or before a tick: the loop marker, the commands, the instrument change due there, and then the envelope override.
        private void WriteCommandsDueAt(int tick)
        {
            if (_loopMarkerTick != NoLoopMarker && _loopMarkerTick <= tick)
            {
                WriteLoopMarker();
            }

            while (_pendingCommands.Count > 0 && _pendingCommands.Peek().Tick <= tick && _pendingCommands.Peek().SortOrder < EnvelopeOrder)
            {
                WriteToken(_pendingCommands.Dequeue().Text);
            }

            if (_entryDueAtTick != NoEntry)
            {
                WriteInstrumentChange(_entryDueAtTick);
                _entryDueAtTick = NoEntry;
            }

            while (_pendingCommands.Count > 0 && _pendingCommands.Peek().Tick <= tick)
            {
                WriteToken(_pendingCommands.Dequeue().Text);
            }
        }

        // Writes an instrument change, as the entry's label, where the stream is not already under that entry.
        private void WriteInstrumentChange(int entry)
        {
            if (entry == _entryInForce)
            {
                return;
            }

            WriteToken(_labels[entry]);
            _entryInForce = entry;
        }

        // Writes a token of the stream with the space that parts it from the next.
        private void WriteToken(string token)
        {
            _text.Append(token).Append(' ');
        }

        // Writes the loop marker with the prelude in front of it and the restatements behind it, and sets the shadow state to what the second pass reaches the marker under.
        private void WriteLoopMarker()
        {
            foreach (var command in _loopPrelude)
            {
                WriteToken(command.Text);
            }

            WriteToken(WrittenTokens.LoopMarker);
            _loopMarkerTick = NoLoopMarker;

            _entryInForce = NoEntry;
            _qInForce = NoQ;
            _fineTuneInForce = _fineTuneAtLoop;
            _transposeInForce = _transposeInForce == _transposeAtLoop ? _transposeAtLoop : UnknownTranspose;

            foreach (var command in _loopRestatements)
            {
                WriteToken(command.Text);
            }
        }

        // One pitch bend as it is written: the tick of the piece it stands behind, its delay and length bytes, and the pitch of its target byte.
        private readonly record struct WrittenBend(int Tick, int DelayByte, int LengthByte, int Semitone);
    }
}
