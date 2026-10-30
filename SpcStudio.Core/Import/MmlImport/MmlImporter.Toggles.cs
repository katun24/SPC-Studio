using SpcStudio.Model;

namespace SpcStudio.Import;

// The importer's toggles: the light staccato lane and each note's legato.
public static partial class MmlImporter
{
    // The song's light staccato as the lane's units, each pair of $F4 $02s opening and closing one and a unit still open at the song's end closing there.
    private static void ApplyStaccato(Song song, List<MmlStaccato> toggles, int songEnd)
    {
        int? openUnitStartTick = null;
        var units = new List<StaccatoUnit>();
        foreach (var tickToggles in toggles.GroupBy(toggle => toggle.Tick).OrderBy(tickGroup => tickGroup.Key))
        {
            if (tickToggles.Count() % 2 == 0)
            {
                continue;
            }

            if (openUnitStartTick is not int unitStartTick)
            {
                openUnitStartTick = tickToggles.Key;
                continue;
            }

            if (tickToggles.Key > unitStartTick)
            {
                units.Add(new StaccatoUnit(unitStartTick, tickToggles.Key - unitStartTick));
            }

            openUnitStartTick = null;
        }

        if (openUnitStartTick is int lastUnitStartTick && songEnd > lastUnitStartTick)
        {
            units.Add(new StaccatoUnit(lastUnitStartTick, songEnd - lastUnitStartTick));
        }

        song.PlaceAllStaccato(units);
    }

    // One channel's legato onto its notes: each note that hands over to the next one is flagged, unless it was dropped.
    private static void ApplyLegato(Song song, MmlChannel channel, HashSet<int> dropped)
    {
        var notes = channel.Events.OfType<MmlNote>().ToList();
        if (notes.Count == 0)
        {
            return;
        }

        var isBound = LegatoBoundNotes(channel);

        // The ticks of the notes that hand over to the next one, leaving out the ones dropped.
        var handingTicks = Enumerable.Range(1, notes.Count - 1).Where(noteIndex => isBound[noteIndex] && !dropped.Contains(notes[noteIndex - 1].Tick))
            .Select(noteIndex => notes[noteIndex - 1].Tick).ToList();
        foreach (int handingTick in handingTicks)
        {
            song.SetNoteLegato(channel.Number, handingTick, true);
        }
    }

    // Which of a channel's notes, in the order they are written, the legato binds into from the note in front.
    private static bool[] LegatoBoundNotes(MmlChannel channel)
    {
        return LegatoBits.WalkNotes([.. channel.Events.OfType<MmlNote>().Select(note => (note.Tick, note.EndTick))], LegatoBits.ToggleTicks(channel.Events)).IsBound;
    }
}
