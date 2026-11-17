using SpcStudio.Model;

namespace SpcStudio.Import;

// How a MIDI file counts its ticks: so many to a quarter note, or so many to a second in a file timed in seconds rather than beats.
public readonly record struct MidiTimeBase(double TicksPerUnit, bool IsCountedInSeconds)
{
    public const int SecondsTimedTempo = 60;

    // The song tick a MIDI tick falls nearest, a file timed in seconds being read at a fixed tempo.
    public int SongTickAt(long midiTick)
    {
        double units = midiTick / TicksPerUnit;
        double songTicksPerUnit = IsCountedInSeconds ? SnesTiming.TicksPerSecond(SecondsTimedTempo) : SnesTiming.TicksPerQuarterNote;
        return (int)Math.Round(units * songTicksPerUnit);
    }
}
