using System.IO;

namespace SpcStudio.Model;

// The song file's lane of units, the light staccato's, with the one reader every stretch on the roll is read back through, the echo runs and the notes included.
public static partial class SongFile
{
    private const int ShortestUnitTicks = 1;

    // Writes the light staccato units.
    private static void WriteUnitLanes(Song song, SongDto contents)
    {
        contents.StaccatoUnits = [.. song.StaccatoUnits.Select(unit => new StaccatoUnitDto { StartTick = unit.StartTick, LengthTicks = unit.LengthTicks })];
    }

    // Reads the light staccato units back.
    private static void ReadStaccatoUnits(Song song, SongDto contents, string fileName)
    {
        ReadUnits(
            contents.StaccatoUnits,
            unit => unit.StartTick,
            unit => unit.LengthTicks,
            $"{fileName} light staccato unit",
            (unit, _) => song.PlaceStaccato(new StaccatoUnit(unit.StartTick, unit.LengthTicks)));
    }

    // Reads a lane's stretches back in start order and hands each one on with where it is in the file, refusing one that starts before tick 0, has no length or overlaps the one before it.
    private static void ReadUnits<TUnit>(
        IEnumerable<TUnit> units,
        Func<TUnit, int> startTickOf,
        Func<TUnit, int> lengthTicksOf,
        string laneName,
        Action<TUnit, string> place)
    {
        int previousEndTick = 0;
        foreach (var unit in units.OrderBy(startTickOf))
        {
            int startTick = startTickOf(unit);
            int lengthTicks = lengthTicksOf(unit);
            string where = $"{laneName} at tick {startTick}";
            if (startTick < 0)
            {
                throw new InvalidDataException($"{where} starts before tick 0.");
            }

            if (lengthTicks < ShortestUnitTicks)
            {
                throw new InvalidDataException($"{where} has a length of {lengthTicks} ticks.");
            }

            if (startTick < previousEndTick)
            {
                throw new InvalidDataException($"{where} overlaps the one before it.");
            }

            place(unit, where);
            previousEndTick = startTick + lengthTicks;
        }
    }

    // One light staccato unit: the song is under $F4 $02 from its start for its length.
    private sealed class StaccatoUnitDto
    {
        public int StartTick { get; set; }
        public int LengthTicks { get; set; }
    }
}
