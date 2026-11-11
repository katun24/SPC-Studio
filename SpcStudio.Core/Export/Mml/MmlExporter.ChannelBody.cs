using System.Collections.Immutable;
using System.Text;
using SpcStudio.Model;

namespace SpcStudio.Export;

// Turns a song into an AddmusicK MML file. This part holds a channel's body: its notes, the rests between them, and the commands and bends written among them.
public static partial class MmlExporter
{
    private const int NoEntry = -1;
    private const int HeldNoteRestTicks = 1;

    // A channel's exported notes that are written as notes rather than as rests.
    private static IEnumerable<Note> PlayedNotes(Song song, int channel)
    {
        return song.NotesInsideSong(channel).Where(song.IsNotePlayable);
    }

    // Whether the export writes a block for a channel: the command channel always, and any other channel with a note inside the song.
    private static bool WritesChannel(Song song, int channel)
    {
        return channel == CommandChannel || song.NotesInsideSong(channel).Any();
    }

    // The $FA $02 shift a note on an instrument is written under: none for a percussion instrument, whose fixed pitch the shift would move, the one in force
    // where the note fits in the note band under it, and otherwise the smallest that brings it in.
    private static int TransposeFor(Instrument instrument, Note note, int transposeInForce)
    {
        if (instrument.IsPercussion)
        {
            return 0;
        }

        int noteValue = SnesPitch.NoteValueFromC4(note.Semitone);
        int writtenNoteValue = noteValue - transposeInForce;

        return writtenNoteValue >= SnesPitch.MinNoteValue && writtenNoteValue <= SnesPitch.MaxNoteValue ? transposeInForce : Math.Max(0, noteValue - SnesPitch.MaxNoteValue);
    }

    // The ticks a note is written up to: a tick short of its end where it is written short, and its whole length where it is not.
    private static int? WrittenTicksOf(Note note, bool isWrittenShort)
    {
        return isWrittenShort ? note.LengthTicks - HeldNoteRestTicks : null;
    }

    // What a last piece of a note cuts it at, for its bends to be placed where its cuts stay put, or null where the note sounds its whole length and has no
    // cuts to keep: held, written short or in a legato run.
    private static Func<int, (int? GateCutTick, int? WaitCutTick)>? CutTicksToKeep(Song song, int channel, Note note, bool isWrittenShort, ChangeMap staccato)
    {
        if (note.IsHeld || isWrittenShort || LegatoToggles.IsInRun(song, channel, note))
        {
            return null;
        }

        var waitTimes = SnesTiming.WaitTimesFrom(staccato, note.StartTick);
        return lastPieceTicks => SnesTiming.KeyOffTriggers.For(note.LengthTicks, note.Gate, lastPieceTicks, waitTimes).CutTicks;
    }

    // The ticks inside a note, counted from its start, at which a command or the loop marker breaks what is written of it.
    private static List<int> BreakOffsetsInside(Note note, int writtenLengthTicks, IEnumerable<int> commandTicks, int markerTick)
    {
        return [.. commandTicks
            .Append(markerTick)
            .Where(tick => tick > note.StartTick && tick < note.StartTick + writtenLengthTicks)
            .Select(tick => tick - note.StartTick)];
    }

    // One channel's notes and the commands between them as MML, every gap filled by a rest, each arpeggio run written as its base note under an $FB,
    // each release run's notes under its key-off block, the runs of notes sharing a first bend under a pitch envelope where the options name it, and the
    // channel padded to the song's end.
    private static string ChannelBodyText(
        Song song, int channel, List<Instrument> usedInstruments, IReadOnlyList<string> labels, MmlExportOptions options, IReadOnlyList<ArpeggioRun> arpeggioRuns,
        IReadOnlyList<ReleaseRun> releaseRuns, IReadOnlyList<string> releaseBlocks)
    {
        var shape = options.HeldNoteShapeWritten();
        var envelopeResetArms = EnvelopeResetArms(song, channel, shape, releaseRuns, arpeggioRuns);
        var (loopPrelude, loopRestatements) = LoopMarkerCommands(song, channel, shape, envelopeResetArms, releaseRuns, releaseBlocks);
        int markerTick = LoopMarkerTick(song);
        var commands = CommandsUnderEnvelopeReset(ReleaseRuns.CommandsUnder(ChannelCommands(song, channel, options, envelopeResetArms), releaseRuns, releaseBlocks), song, channel, envelopeResetArms).ToList();
        var staccato = ChangeMap.ForStaccato(song);
        var pitchEnvelopes = options.PitchEnvelopes ? PitchEnvelopes.ForChannel(song, channel, shape, [.. commands.Select(command => command.Tick)], markerTick, staccato) : [];
        var body = new ChannelBody(
            channel,
            labels,
            commands,
            markerTick,
            loopPrelude,
            loopRestatements,
            FineTuneLeftAtEnd(song, channel),
            TransposeLeftAtEnd(song, channel),
            pitchEnvelopes,
            PitchEnvelopes.AtEnd(song, channel, pitchEnvelopes),
            ReleaseRuns.RestCutTicks(releaseRuns));

        var arpeggioRunsByStartTick = arpeggioRuns.ToDictionary(run => run.StartTick);
        ArpeggioRun? previousRun = null;
        int tick = 0;

        foreach (var note in song.NotesInsideSong(channel))
        {
            if (note.StartTick < tick)
            {
                continue;
            }

            var instrument = song.InstrumentOf(note);
            int entry = instrument is null ? NoEntry : EntryIndexOf(usedInstruments, instrument);

            tick = body.WriteRests(tick, note.StartTick, entry);
            if (arpeggioRunsByStartTick.TryGetValue(note.StartTick, out var run))
            {
                if (!ArpeggioRuns.DoesPlayUnderPrevious(run, previousRun, markerTick))
                {
                    body.WriteArpeggioCommand(run);
                }

                tick = body.WriteArpeggio(run, note, entry, instrument!);
                if (!arpeggioRunsByStartTick.ContainsKey(run.EndTick))
                {
                    body.WriteArpeggioOff();
                }

                previousRun = run;
                continue;
            }

            if (entry == NoEntry)
            {
                tick = body.WriteRests(tick, note.EndTick(), NoEntry);
                continue;
            }

            var writtenNote = ReleaseRuns.WrittenNote(note, releaseRuns);
            bool isWrittenShort = HoldToggles.IsWrittenShort(song, channel, note, shape);
            tick = body.WriteNote(writtenNote, entry, instrument!, isWrittenShort, CutTicksToKeep(song, channel, writtenNote, isWrittenShort, staccato));
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
        private const int UnknownFineTune = -1;
        private const string UnknownPitchEnvelope = "";

        private readonly StringBuilder _text = new();
        private readonly Queue<TimedCommand> _pendingCommands;
        private readonly IReadOnlyList<TimedCommand> _loopPrelude;
        private readonly IReadOnlyList<TimedCommand> _loopRestatements;
        private readonly IReadOnlyList<string> _labels;
        private readonly Dictionary<int, string> _pitchEnvelopesByNoteTick;
        private readonly IReadOnlySet<int> _restCutTicks;
        private readonly string _pitchEnvelopeAtLoop;
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
        private string _pitchEnvelopeInForce = PitchEnvelopes.OffCommand;

        // An empty stream for a channel, holding the commands still to write into it, what its loop marker writes and leaves, the pitch envelope
        // each note keys on under, and the ticks a rest is cut at.
        public ChannelBody(
            int channel,
            IReadOnlyList<string> labels,
            IEnumerable<TimedCommand> commands,
            int loopMarkerTick,
            IReadOnlyList<TimedCommand> loopPrelude,
            IReadOnlyList<TimedCommand> loopRestatements,
            int fineTuneAtLoop,
            int transposeAtLoop,
            Dictionary<int, string> pitchEnvelopesByNoteTick,
            string pitchEnvelopeAtLoop,
            IReadOnlySet<int> restCutTicks)
        {
            _channel = channel;
            _labels = labels;
            _pendingCommands = new Queue<TimedCommand>(commands);
            _loopMarkerTick = loopMarkerTick;
            _loopPrelude = loopPrelude;
            _loopRestatements = loopRestatements;
            _fineTuneAtLoop = fineTuneAtLoop;
            _transposeAtLoop = transposeAtLoop;
            _pitchEnvelopesByNoteTick = pitchEnvelopesByNoteTick;
            _pitchEnvelopeAtLoop = pitchEnvelopeAtLoop;
            _restCutTicks = restCutTicks;
        }

        // The channel's text as written so far.
        public override string ToString()
        {
            return _text.ToString().TrimEnd();
        }

        // Rests from one tick to another, broken wherever a command falls and parted wherever a rest is cut, with the entry the note starting at the
        // second tick plays written in front of that tick's envelope override; returns the second tick.
        public int WriteRests(int fromTick, int toTick, int entryOfNextNote)
        {
            while (fromTick < toTick)
            {
                WriteCommandsDueAt(fromTick);

                int nextTick = NextBreakTick(fromTick, toTick);

                // Where this piece of the rest ends: the first cut before the next break, or the break.
                int pieceEndTick = _restCutTicks.Where(tick => tick > fromTick && tick < nextTick).DefaultIfEmpty(nextTick).Min();
                WriteToken(WrittenTokens.Rest(pieceEndTick - fromTick));
                if (pieceEndTick < nextTick)
                {
                    foreach (string token in LoopCompression.RestPartingTokens(_octaveInForce))
                    {
                        WriteToken(token);
                    }
                }

                fromTick = pieceEndTick;
            }

            _entryDueAtTick = entryOfNextNote;
            WriteCommandsDueAt(toTick);
            return toTick;
        }

        // A note on an instrument, with whatever the delta pass owes in front of it, a percussion instrument's @ owed again in front of the next note; returns
        // the note's end tick.
        public int WriteNote(Note note, int entry, Instrument instrument, bool isWrittenShort, Func<int, (int? GateCutTick, int? WaitCutTick)>? cutTicksToKeep)
        {
            var (semitone, noteName) = WriteStateFor(note, entry, instrument);
            int endTick = WriteNotePieces(note, semitone, noteName, isWrittenShort, cutTicksToKeep);
            if (instrument.IsPercussion)
            {
                _entryInForce = NoEntry;
            }

            return endTick;
        }

        // An arpeggio run as its first step played for the whole run at the base note's pitch, with whatever the delta pass owes in front of it;
        // returns the run's end tick.
        public int WriteArpeggio(ArpeggioRun run, Note firstStep, int entry, Instrument instrument)
        {
            var baseNote = firstStep with { LengthTicks = run.EndTick - run.StartTick, Semitone = run.BaseSemitone };
            var (semitone, noteName) = WriteStateFor(baseNote, entry, instrument);
            return WriteNotePieces(baseNote, semitone, noteName, false, null);
        }

        // The $FB an arpeggio run plays under.
        public void WriteArpeggioCommand(ArpeggioRun run)
        {
            WriteToken(run.Command);
        }

        // The arpeggio switched off.
        public void WriteArpeggioOff()
        {
            WriteToken(ArpOffName.Write());
        }

        // Whatever the delta pass owes in front of a note - its instrument, noise clock, q, fine tune, transpose and octave - and the semitone it sounds
        // at with the name it is written under.
        private (int Semitone, string NoteName) WriteStateFor(Note note, int entry, Instrument instrument)
        {
            WriteInstrumentChange(entry);

            if (instrument.IsNoise)
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

            int transpose = TransposeFor(instrument, note, _transposeInForce == UnknownTranspose ? 0 : _transposeInForce);
            if (transpose != _transposeInForce)
            {
                WriteToken(TransposeToken(transpose));
                _transposeInForce = transpose;
            }

            int semitone = instrument.SoundingSemitoneOf(note);
            var (octave, semitoneInOctave) = SnesPitch.OctaveAndSemitone(semitone - _transposeInForce);
            if (octave != _octaveInForce)
            {
                WriteToken(LetteredText('o', octave));
                _octaveInForce = octave;
            }

            return (semitone, SnesPitch.NoteNames[semitoneInOctave]);
        }

        // The note itself as tie-joined pieces at the semitone it sounds at, under the pitch envelope it keys on under, broken wherever a command or a bend
        // falls inside it, its bends placed where its cuts stay put where it has any to keep, and a held note written short given its rest; returns the
        // note's end tick.
        private int WriteNotePieces(Note note, int semitone, string noteName, bool isWrittenShort, Func<int, (int? GateCutTick, int? WaitCutTick)>? cutTicksToKeep)
        {
            var placements = Placements(note, isWrittenShort, cutTicksToKeep);
            WritePitchEnvelope(PitchEnvelopes.CommandFor(note, placements));
            var bends = WrittenBends(note, semitone, placements);
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

                WriteToken(WrittenTokens.RunOfLength(isFirstPiece ? noteName : WrittenTokens.TieMark, nextTick - fromTick));
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
                WriteToken(WrittenTokens.Rest(HeldNoteRestTicks));
            }

            return note.EndTick();
        }

        // Where in a note each of its bends is written and the delay and length bytes it carries, the first armed at the key-on where the note keys on
        // under a pitch envelope, each where the note's cuts stay put where it has any to keep.
        private ImmutableArray<BendPlacement> Placements(Note note, bool isWrittenShort, Func<int, (int? GateCutTick, int? WaitCutTick)>? cutTicksToKeep)
        {
            int? writtenTicks = WrittenTicksOf(note, isWrittenShort);
            var breakOffsets = BreakOffsetsInside(note, writtenTicks ?? note.LengthTicks, _pendingCommands.Select(command => command.Tick), _loopMarkerTick);
            var placements = _pitchEnvelopesByNoteTick.ContainsKey(note.StartTick)
                ? PitchBend.PlacementsUnderPitchEnvelope(note.Bends, note.LengthTicks, writtenTicks, breakOffsets, cutTicksToKeep)
                : PitchBend.Placements(note.Bends, note.LengthTicks, writtenTicks, breakOffsets, cutTicksToKeep);

            if (placements.Length < note.Bends.Length)
            {
                throw new InvalidOperationException(
                    $"Channel #{_channel} has a note at tick {note.StartTick} whose pitch " +
                    "bends fall too close together, or too close to the commands inside the " +
                    "note, to be written where the driver reads them. The driver would stop at " +
                    "the one it cannot read.");
            }

            return placements;
        }

        // The bends of a note sounding at a semitone written as $DD where they are placed, with the absolute pitch each target byte holds, the one the
        // pitch envelope arms at the key-on left out.
        private static List<WrittenBend> WrittenBends(Note note, int semitone, ImmutableArray<BendPlacement> placements)
        {
            var writtenBends = new List<WrittenBend>();

            for (int bendIndex = 0; bendIndex < placements.Length; bendIndex++)
            {
                semitone += note.Bends[bendIndex].OffsetSemitones;
                if (placements[bendIndex].PieceStartTick == BendWalk.KeyOnPiece)
                {
                    continue;
                }

                writtenBends.Add(new WrittenBend(note.StartTick + placements[bendIndex].PieceStartTick, placements[bendIndex].DelayByte, placements[bendIndex].LengthByte, semitone));
            }

            return writtenBends;
        }

        // Writes the pitch envelope a note keys on under where the stream is not already under it.
        private void WritePitchEnvelope(string command)
        {
            if (command == _pitchEnvelopeInForce)
            {
                return;
            }

            WriteToken(command);
            _pitchEnvelopeInForce = command;
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
            _fineTuneInForce = ShadowPastMarker(_fineTuneInForce, _fineTuneAtLoop, UnknownFineTune);
            _transposeInForce = ShadowPastMarker(_transposeInForce, _transposeAtLoop, UnknownTranspose);
            _pitchEnvelopeInForce = ShadowPastMarker(_pitchEnvelopeInForce, _pitchEnvelopeAtLoop, UnknownPitchEnvelope);

            foreach (var command in _loopRestatements)
            {
                WriteToken(command.Text);
            }
        }

        // What a register's shadow holds past the loop marker: the value both passes arrive at it holding, or the unknown value where they differ.
        private static TValue ShadowPastMarker<TValue>(TValue firstPassValue, TValue laterPassValue, TValue unknownValue)
        {
            return EqualityComparer<TValue>.Default.Equals(firstPassValue, laterPassValue) ? laterPassValue : unknownValue;
        }

        // One pitch bend as it is written: the tick of the piece it stands behind, its delay and length bytes, and the pitch of its target byte.
        private readonly record struct WrittenBend(int Tick, int DelayByte, int LengthByte, int Semitone);
    }
}
