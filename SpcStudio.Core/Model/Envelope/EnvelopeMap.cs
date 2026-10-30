using System.Collections.Immutable;

namespace SpcStudio.Model;

// One channel's envelope as a function of position: the three bytes the DSP reads, taken from the override unit covering the tick where there is one
// and from the instrument the channel's notes carry where there is not, the units being where the notes' own envelope rows differ from the instrument.
public sealed class EnvelopeMap
{
    private readonly EnvelopeState _baseEnvelope;
    private readonly List<(int Tick, EnvelopeState Envelope)> _instrumentSegments;
    private readonly IReadOnlyList<EnvelopeUnit> _units;

    private EnvelopeMap(EnvelopeState baseEnvelope, List<(int Tick, EnvelopeState Envelope)> instrumentSegments, IReadOnlyList<EnvelopeUnit> units)
    {
        _baseEnvelope = baseEnvelope;
        _instrumentSegments = instrumentSegments;
        _units = units;
    }

    // The override units over the instrument timeline, in start order and never overlapping.
    public IReadOnlyList<EnvelopeUnit> Units
    {
        get
        {
            return _units;
        }
    }

    // The envelope map of one channel of a song, the optional notes standing in for the channel's.
    public static EnvelopeMap For(Song song, int channel, IReadOnlyList<Note>? notes = null)
    {
        var channelNotes = notes ?? song.Notes(channel);
        var instrumentTimeline = InstrumentTimelineOf(song, channel, channelNotes);
        return instrumentTimeline.WithUnits(instrumentTimeline.UnitsPlayedBy(song, channelNotes));
    }

    // The instrument timeline of one channel with no override over it, the optional notes standing in for the channel's.
    public static EnvelopeMap InstrumentTimelineOf(Song song, int channel, IReadOnlyList<Note>? notes = null)
    {
        var channelNotes = notes ?? song.Notes(channel);
        return new(BaseEnvelopeOf(song, channelNotes), [.. InstrumentSegments(song, channel, channelNotes)], []);
    }

    // The same instrument timeline with the given override units over it, in start order and never overlapping.
    public EnvelopeMap WithUnits(IReadOnlyList<EnvelopeUnit> units)
    {
        return new(_baseEnvelope, _instrumentSegments, units);
    }

    // The override units that make the channel play each note's own envelope rows over this instrument timeline, the last row of a note running on through the silence after it to the next key-on.
    private List<EnvelopeUnit> UnitsPlayedBy(Song song, IReadOnlyList<Note> notes)
    {
        var units = new List<EnvelopeUnit>();

        // The notes the stream plays, each with its instrument, a note whose instrument has gone being written as a rest.
        var playedNotes = notes.Select(note => (Note: note, Instrument: song.InstrumentOf(note))).Where(played => played.Instrument is not null).ToList();
        for (int noteIndex = 0; noteIndex < playedNotes.Count; noteIndex++)
        {
            var (note, instrument) = playedNotes[noteIndex];
            var rows = EnvelopeRows.PlayedBy(note, instrument!);
            int lastRowEndTick = noteIndex + 1 < playedNotes.Count ? playedNotes[noteIndex + 1].Note.StartTick : Math.Max(song.EndTick, note.EndTick());
            for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
            {
                int rowStartTick = note.StartTick + rows[rowIndex].OffsetTicks;
                int rowEndTick = rowIndex + 1 < rows.Length ? Math.Min(note.StartTick + rows[rowIndex + 1].OffsetTicks, lastRowEndTick) : lastRowEndTick;
                if (rowEndTick > rowStartTick && rows[rowIndex].State != InstrumentEnvelopeAt(rowStartTick))
                {
                    EnvelopeUnit.AppendJoining(units, new EnvelopeUnit(rowStartTick, rowEndTick - rowStartTick, rows[rowIndex].State));
                }
            }
        }

        return units;
    }

    // The rows each note plays under this map: the state at its key-on, then one at every tick strictly inside it where the state moves.
    public List<ImmutableArray<EnvelopeRow>> RowsOfEach(IReadOnlyList<Note> notes)
    {
        var changeTicks = StateChangeTicks().ToList();
        var rowsOfEach = new List<ImmutableArray<EnvelopeRow>>(notes.Count);
        foreach (var note in notes)
        {
            var rows = ImmutableArray.CreateBuilder<EnvelopeRow>();
            rows.Add(new EnvelopeRow(OffsetRows.FirstOffsetTicks, StateAt(note.StartTick)));

            // The first state change after the note's key-on.
            int changeIndex = SortedSearch.CountLeading(changeTicks, tick => tick <= note.StartTick);
            for (; changeIndex < changeTicks.Count && changeTicks[changeIndex] < note.EndTick(); changeIndex++)
            {
                var state = StateAt(changeTicks[changeIndex]);
                if (state != rows[^1].State)
                {
                    rows.Add(new EnvelopeRow(changeTicks[changeIndex] - note.StartTick, state));
                }
            }

            rowsOfEach.Add(rows.ToImmutable());
        }

        return rowsOfEach;
    }

    // The envelope a channel starts under, which is its first playable note's instrument's, or the unset state where it has no playable note.
    private static EnvelopeState BaseEnvelopeOf(Song song, IReadOnlyList<Note> notes)
    {
        foreach (var note in notes)
        {
            if (song.InstrumentOf(note) is { } instrument)
            {
                return instrument.Envelope;
            }
        }

        return EnvelopeState.Unset;
    }

    // The tick the exporter's forced @ restatement at the loop marker lands on, or null where there is none, the optional notes standing in for the channel's.
    public static int? LoopRestatementTick(Song song, int channel, IReadOnlyList<Note>? notes = null)
    {
        if (!song.IsLooping || song.LoopTick <= 0)
        {
            return null;
        }

        foreach (var note in notes ?? song.Notes(channel))
        {
            if (note.StartTick >= song.LoopTick && song.InstrumentOf(note) is not null)
            {
                return note.StartTick;
            }
        }

        return null;
    }

    // Where the channel's stream applies an instrument past tick 0: every note whose instrument differs from the one before it, plus the first note at
    // or after the loop marker, the optional notes standing in for the channel's.
    public static IEnumerable<(int Tick, EnvelopeState Envelope)> InstrumentSegments(Song song, int channel, IReadOnlyList<Note>? notes = null)
    {
        var channelNotes = notes ?? song.Notes(channel);
        int loopRestatementTick = LoopRestatementTick(song, channel, channelNotes) ?? -1;
        int previousInstrumentId = -1;
        bool hasRestatedAtLoop = false;
        foreach (var note in channelNotes)
        {
            if (song.InstrumentOf(note) is not { } instrument)
            {
                continue;
            }

            bool hasInstrumentChanged = note.InstrumentId != previousInstrumentId;
            bool isLoopRestatement = !hasRestatedAtLoop && loopRestatementTick >= 0 && note.StartTick >= loopRestatementTick;
            previousInstrumentId = note.InstrumentId;
            if (isLoopRestatement)
            {
                hasRestatedAtLoop = true;
            }

            if (note.StartTick > 0 && (hasInstrumentChanged || isLoopRestatement))
            {
                yield return (note.StartTick, instrument.Envelope);
            }
        }
    }

    // The instrument's own envelope at a tick, with no override over it.
    public EnvelopeState InstrumentEnvelopeAt(double tick)
    {
        int segmentCount = InstrumentSegmentCountBy(tick);
        return segmentCount == 0 ? _baseEnvelope : _instrumentSegments[segmentCount - 1].Envelope;
    }

    // The whole envelope in force at a tick.
    public EnvelopeState StateAt(double tick)
    {
        if (LaneUnits.UnitCovering(_units, (int)Math.Floor(tick)) is { } unit)
        {
            return unit.State;
        }

        return InstrumentEnvelopeAt(tick);
    }

    // The ticks of the @s strictly inside a unit, at which the exporter has to state the unit's bytes again.
    public IEnumerable<int> RestatementTicksInside(EnvelopeUnit unit)
    {
        return _instrumentSegments
            .Skip(InstrumentSegmentCountBy(unit.StartTick))
            .TakeWhile(segment => segment.Tick < unit.EndTick())
            .Select(segment => segment.Tick);
    }

    // Every tick at which an $ED has to be written, with the state already in force there and the state to write: a unit's own start, and every @ it covers.
    public IEnumerable<(int Tick, EnvelopeState From, EnvelopeState To)> OverrideWrites()
    {
        foreach (var unit in _units)
        {
            yield return (unit.StartTick, InstrumentEnvelopeAt(unit.StartTick), unit.State);
            foreach (int tick in RestatementTicksInside(unit))
            {
                yield return (tick, InstrumentEnvelopeAt(tick), unit.State);
            }
        }
    }

    // The ticks at which a $F4 $09 has to be written: a unit's end, unless the next unit starting there or an @ landing there already ends the override.
    public IEnumerable<int> RestoreTicks()
    {
        foreach (var unit in _units)
        {
            if (LaneUnits.IndexStartingAt(_units, unit.EndTick()) >= 0)
            {
                continue;
            }

            int segmentCount = InstrumentSegmentCountBy(unit.EndTick());
            if (segmentCount > 0 && _instrumentSegments[segmentCount - 1].Tick == unit.EndTick())
            {
                continue;
            }

            yield return unit.EndTick();
        }
    }

    // How many of the instrument's segments start at or before a tick.
    private int InstrumentSegmentCountBy(double tick)
    {
        return SortedSearch.CountLeading(_instrumentSegments, segment => segment.Tick <= tick);
    }

    // Every tick at which the override commands move the registers: where an override is written and where one ends.
    public IEnumerable<int> StateChangeTicks()
    {
        return _units
            .SelectMany(unit => RestatementTicksInside(unit).Concat([unit.StartTick, unit.EndTick()]))
            .Distinct()
            .Order();
    }
}
