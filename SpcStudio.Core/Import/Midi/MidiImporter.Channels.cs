using SpcStudio.Model;

namespace SpcStudio.Import;

// The importer's channels: a MIDI channel's notes kept on the channels it played before wherever they are free, so a melody stays on one channel and
// each line of a chord on its own, then on the lowest channel nothing has played yet, then on a free one whose MIDI channel needs it again latest - and
// a note dropped only where every channel the song can hold is sounding.
public static partial class MidiImporter
{
    private const long NeverAgainTick = long.MaxValue;

    // The played notes on the song's ticks and channels, in the order they start, with how many channels they take, which MIDI channel each channel
    // plays from where, and how many notes found no channel.
    private static PlacedNotes PlacedOnChannels(MidiTimeBase timeBase, IReadOnlyList<MidiPlayedNote> playedNotes)
    {
        var timedNotes = playedNotes
            .Select(played => TimedNoteOf(played, timeBase))
            .OrderBy(timed => timed.StartTick)
            .ThenBy(timed => timed.Played.Channel)
            .ThenBy(timed => timed.Played.Key)
            .ToList();
        var startTicksByMidiChannel = timedNotes.GroupBy(timed => timed.Played.Channel).ToDictionary(group => group.Key, group => group.Select(timed => timed.StartTick).ToList());
        var channels = new List<ChannelUse>();
        var placedNotes = new List<TimedNote>();
        int droppedCount = 0;
        foreach (var timed in timedNotes)
        {
            int midiChannel = timed.Played.Channel;
            var chosenChannel = OwnFreeChannel(channels, timed)
                ?? UnusedChannel(channels)
                ?? FreeChannelNeededLatest(channels, timed, owner => NextStartTick(startTicksByMidiChannel[owner], timed.StartTick));
            if (chosenChannel is not int channel)
            {
                droppedCount++;
                continue;
            }

            if (channel == channels.Count)
            {
                channels.Add(new ChannelUse());
            }

            var use = channels[channel];
            if (use.Owners.Count == 0 || use.Owners[^1].MidiChannel != midiChannel)
            {
                use.Owners.Add((timed.Played.StartTick, midiChannel));
            }

            use.EndTick = timed.StartTick + timed.LengthTicks;
            placedNotes.Add(timed with { Channel = channel });
        }

        return new PlacedNotes(placedNotes, channels.Count, [.. channels.Select(use => (IReadOnlyList<(long MidiTick, int MidiChannel)>)use.Owners)], droppedCount);
    }

    // A played note on the song's ticks, lasting at least one.
    private static TimedNote TimedNoteOf(MidiPlayedNote played, MidiTimeBase timeBase)
    {
        int startTick = timeBase.SongTickAt(played.StartTick);
        int lengthTicks = Math.Max(timeBase.SongTickAt(played.EndTick) - startTick, MinNoteTicks);
        return new TimedNote(played, startTick, lengthTicks, 0);
    }

    // The lowest channel free where a note starts whose last notes were its own MIDI channel's, or null where none is.
    private static int? OwnFreeChannel(List<ChannelUse> channels, TimedNote timed)
    {
        int channel = channels.FindIndex(use => use.EndTick <= timed.StartTick && use.Owners[^1].MidiChannel == timed.Played.Channel);
        return channel < 0 ? null : channel;
    }

    // The lowest channel nothing has played yet, or null where the song can hold no more.
    private static int? UnusedChannel(List<ChannelUse> channels)
    {
        return channels.Count < Song.MaxChannelCount ? channels.Count : null;
    }

    // The channel free where a note starts whose last MIDI channel plays its next note latest, the lowest of any tied, or null where none is free.
    private static int? FreeChannelNeededLatest(List<ChannelUse> channels, TimedNote timed, Func<int, long> nextStartTickOf)
    {
        int? chosen = null;
        long chosenNextStartTick = 0;
        for (int channel = 0; channel < channels.Count; channel++)
        {
            if (channels[channel].EndTick > timed.StartTick)
            {
                continue;
            }

            long nextStartTick = nextStartTickOf(channels[channel].Owners[^1].MidiChannel);
            if (chosen is null || nextStartTick > chosenNextStartTick)
            {
                chosen = channel;
                chosenNextStartTick = nextStartTick;
            }
        }

        return chosen;
    }

    // The first of a MIDI channel's note starts, in order, after a tick, or never where it plays none.
    private static long NextStartTick(List<int> startTicks, int afterTick)
    {
        int startsAtOrBeforeTick = SortedSearch.CountLeading(startTicks, startTick => startTick <= afterTick);
        return startsAtOrBeforeTick < startTicks.Count ? startTicks[startsAtOrBeforeTick] : NeverAgainTick;
    }

    // A played note on the song's ticks: where it starts, how long it lasts, and the song's channel it plays on.
    private readonly record struct TimedNote(MidiPlayedNote Played, int StartTick, int LengthTicks, int Channel);

    // The notes given a channel, in the order they start, how many channels they take, each channel's MIDI channels with the MIDI tick each took it
    // over at, and how many notes found none.
    private sealed record PlacedNotes(List<TimedNote> Notes, int ChannelCount, IReadOnlyList<IReadOnlyList<(long MidiTick, int MidiChannel)>> OwnersOf, int DroppedCount);

    // One channel as the notes are handed out: the tick its last note ends, and each MIDI channel that has played on it with the MIDI tick it started at.
    private sealed class ChannelUse
    {
        public int EndTick { get; set; }
        public List<(long MidiTick, int MidiChannel)> Owners { get; } = [];
    }
}
