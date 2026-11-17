using System.Collections.Immutable;

namespace SpcStudio.Model;

// The $FA $03 amplify as each note's own rows or its instrument's: the rows held to the byte's range, an instrument's first row, and what a channel
// plays from its notes' rows.
public static class NoteAmplify
{
    // The rows with every value held to the byte's range.
    public static ImmutableArray<AmplifyRow> Clamped(ImmutableArray<AmplifyRow> rows)
    {
        return [.. rows.Select(row => row with { Value = SnesVolume.AmplifyRange.Clamp(row.Value) })];
    }

    // The amplify a note standing on an instrument's rung keys on under, and the driver's own with no instrument.
    public static int LeadOf(Instrument? instrument)
    {
        return instrument is null ? SnesVolume.DefaultAmplify : LeadOf(instrument.AmplifyRows);
    }

    // The amplify rows key on under, which is their first row's, and the driver's own where there are none.
    public static int LeadOf(ImmutableArray<AmplifyRow> rows)
    {
        return rows.IsEmpty ? SnesVolume.DefaultAmplify : rows[OffsetRows.FirstRowIndex].Value;
    }

    // The rows with their first row at another amplify, the rows after it left as they are.
    public static ImmutableArray<AmplifyRow> WithLead(ImmutableArray<AmplifyRow> rows, int amplify)
    {
        return rows.IsEmpty ? [new AmplifyRow(OffsetRows.FirstOffsetTicks, amplify)] : rows.SetItem(OffsetRows.FirstRowIndex, rows[OffsetRows.FirstRowIndex] with { Value = amplify });
    }

    // One channel's amplify as its notes' rows say it: the driver's own at tick 0, and a change wherever a row moves it, each row's value held through
    // the silence after its note until a later row moves it, a note whose instrument has gone being a rest; the optional notes stand in for the channel's.
    public static (int Base, IReadOnlyList<LaneValueChange> Changes) Played(Song song, int channel, IReadOnlyList<Note>? notes = null)
    {
        int baseValue = SnesVolume.DefaultAmplify;
        int valueInForce = baseValue;
        var changes = new List<LaneValueChange>();
        foreach (var note in notes ?? song.Notes(channel))
        {
            if (song.InstrumentOf(note) is not { } instrument)
            {
                continue;
            }

            foreach (var row in OffsetRows.OwnOrFirstOf(note.Amplifies, instrument.AmplifyRows))
            {
                int tick = note.StartTick + row.OffsetTicks;
                if (row.Value == valueInForce)
                {
                    continue;
                }

                valueInForce = row.Value;
                if (tick <= 0)
                {
                    baseValue = row.Value;
                    continue;
                }

                changes.Add(new LaneValueChange(tick, row.Value));
            }
        }

        return (baseValue, changes);
    }
}
