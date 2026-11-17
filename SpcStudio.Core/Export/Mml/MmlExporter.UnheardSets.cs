using SpcStudio.Audio;
using SpcStudio.Model;

namespace SpcStudio.Export;

// Turns a song into an AddmusicK MML file. This part holds the sets nothing hears: a set the next command of its lane replaces before any note sounds
// under it, and a header's value the driver holds already at song load where no fade reads it.
public static partial class MmlExporter
{
    private const int NoPlace = -1;

    private static readonly HashSet<int> ChannelLaneOrders = ['v', 'y', AmplifyOrder, VibratoOrder, TremoloOrder];
    private static readonly HashSet<int> SongWideLaneOrders = ['t', 'w', EchoOffOrder, EchoSendOrder, EchoUnitOrder, EchoFilterOrder];

    // The value song load leaves each channel's volume and pan and the song volume at, by the lane's letter.
    private static readonly Dictionary<char, string> DriverStartValuesByLetter = new()
    {
        ['v'] = LetteredText('v', SnesVolume.DefaultChannelVolume),
        ['y'] = LetteredText('y', SnesVolume.DefaultPanIndex),
        ['w'] = LetteredText('w', SnesVolume.DefaultMasterVolume),
    };

    // A channel's header, its restatements just after the loop marker and the commands of its body, each with every set no note hears left out.
    private static (List<TimedCommand> Header, List<TimedCommand> Restatements, List<TimedCommand> Body) HeardCommands(
        Song song, int channel, MmlExportOptions options, List<TimedCommand> header, IReadOnlyList<TimedCommand> restatements, List<TimedCommand> body)
    {
        int markerTick = LoopMarkerTick(song);
        bool isHeaderReadOnce = IsHeaderReadOnce(song);
        int firstTickReadAgain = markerTick == NoLoopMarker ? 0 : markerTick;
        var silence = new ChannelSilence(song, channel, options, firstTickReadAgain);

        var stream = new List<StreamedCommand>();
        stream.AddRange(header.Select(command => new StreamedCommand(command, StreamPart.Header, !isHeaderReadOnce)));
        stream.AddRange(body.Where(command => command.Tick < firstTickReadAgain).Select(command => new StreamedCommand(command, StreamPart.Body, !isHeaderReadOnce)));
        stream.AddRange(restatements.Select(command => new StreamedCommand(command, StreamPart.Restatements, song.IsLooping)));
        stream.AddRange(body.Where(command => command.Tick >= firstTickReadAgain).Select(command => new StreamedCommand(command, StreamPart.Body, song.IsLooping)));

        var unheardPlaces = UnheardPlaces(stream, ChannelLaneOrders, (set, next) => silence.IsSilentBetween(set.Command.Tick, next.Command.Tick, set.IsReadOnLaterPasses));
        var heard = stream.Where((_, place) => !unheardPlaces.Contains(place)).ToList();
        return (CommandsOf(heard, StreamPart.Header), CommandsOf(heard, StreamPart.Restatements), CommandsOf(heard, StreamPart.Body));
    }

    // The song-wide state above the channels as it is written, every set the loop marker at the song's start writes again behind it left out, and the song
    // volume the driver starts at where no fade reads it and the header is read once.
    private static List<string> GlobalHeaderWritten(Song song)
    {
        var stream = GlobalHeader(song).Select(command => new StreamedCommand(command, StreamPart.Header, !IsHeaderReadOnce(song))).ToList();
        if (LoopMarkerTick(song) == 0)
        {
            stream.AddRange(SongWideRestatements(song).Select(command => new StreamedCommand(command, StreamPart.Restatements, true)));
        }

        stream.AddRange(LaneRegions.MasterVolumeCommands(song).Take(1).SelectMany(command => RegionCommands('w', command)).Select(command => new StreamedCommand(command, StreamPart.Body, false)));

        var unheardPlaces = UnheardPlaces(stream, SongWideLaneOrders, (set, next) => set.Command.Tick == next.Command.Tick && next.Part == StreamPart.Restatements);
        return [.. stream.Where((streamed, place) => streamed.Part == StreamPart.Header && !unheardPlaces.Contains(place)).Select(streamed => streamed.Command.Text)];
    }

    // Whether the header runs on the first pass alone: where the song writes a loop marker, or does not loop at all, rather than looping from its start with
    // no marker, which plays the header again.
    private static bool IsHeaderReadOnce(Song song)
    {
        return !song.IsLooping || LoopMarkerTick(song) != NoLoopMarker;
    }

    // The places in a stream of the sets nothing hears: a set of one of the lanes whose next command is a set that replaces it unheard, and a header's set
    // of the value the driver starts at, read once, where the next command of its lane does not fade from it.
    private static HashSet<int> UnheardPlaces(List<StreamedCommand> stream, HashSet<int> laneOrders, Func<StreamedCommand, StreamedCommand, bool> isReplacedUnheard)
    {
        var unheardPlaces = new HashSet<int>();

        for (int place = 0; place < stream.Count; place++)
        {
            var set = stream[place];
            if (!laneOrders.Contains(set.Command.SortOrder) || set.Command.IsFade)
            {
                continue;
            }

            int nextPlace = NextPlaceOfLane(stream, place);
            if (nextPlace != NoPlace && stream[nextPlace].Command.IsFade)
            {
                continue;
            }

            bool isDriverStartValue = set.Part == StreamPart.Header && !set.IsReadOnLaterPasses && DriverStartValuesByLetter.GetValueOrDefault(set.Command.Text[0]) == set.Command.Text;
            if (isDriverStartValue || (nextPlace != NoPlace && isReplacedUnheard(set, stream[nextPlace])))
            {
                unheardPlaces.Add(place);
            }
        }

        return unheardPlaces;
    }

    // The place of the next command of a set's lane in a stream, an echo volume fade counting as the echo send's, or none where nothing follows.
    private static int NextPlaceOfLane(List<StreamedCommand> stream, int place)
    {
        int lane = LaneOf(stream[place].Command);
        for (int nextPlace = place + 1; nextPlace < stream.Count; nextPlace++)
        {
            if (LaneOf(stream[nextPlace].Command) == lane)
            {
                return nextPlace;
            }
        }

        return NoPlace;
    }

    // The lane a command writes, by its sort order, an echo volume fade being the echo send's.
    private static int LaneOf(TimedCommand command)
    {
        return command.SortOrder == EchoFadeOrder ? EchoSendOrder : command.SortOrder;
    }

    // The commands of one part of a stream, in order.
    private static List<TimedCommand> CommandsOf(List<StreamedCommand> stream, StreamPart part)
    {
        return [.. stream.Where(streamed => streamed.Part == part).Select(streamed => streamed.Command)];
    }

    // Which part of a channel's stream a command stands in.
    private enum StreamPart
    {
        Header,
        Restatements,
        Body,
    }

    // One command in the order the driver reads a channel's stream, with the part it stands in and whether a pass after the first reads it.
    private readonly record struct StreamedCommand(TimedCommand Command, StreamPart Part, bool IsReadOnLaterPasses);

    // When one channel is silent: from each note's key-off and the release after it to the next note's key-on, a pass after the first starting under the
    // release of the song's last note where that runs on past the song's end.
    private sealed class ChannelSilence
    {
        private readonly TempoMap _tempo;
        private readonly int[] _noteStartTicks;
        private readonly long[] _latestSoundEndSamples;
        private readonly long _firstSampleReadAgain;
        private readonly long _releasePastSongEndSamples;

        // The silence of a channel as the export under the options plays it, a pass after the first starting at a tick.
        public ChannelSilence(Song song, int channel, MmlExportOptions options, int firstTickReadAgain)
        {
            _tempo = TempoMap.For(song);
            var keyOffs = SequenceBuilder.KeyOffSamples(song, options, channel).Where(keyOff => keyOff.Note.StartTick < song.EndTick).ToList();
            _noteStartTicks = [.. keyOffs.Select(keyOff => keyOff.Note.StartTick)];
            _latestSoundEndSamples = new long[keyOffs.Count];

            long latestSoundEndSample = 0;
            for (int noteIndex = 0; noteIndex < keyOffs.Count; noteIndex++)
            {
                latestSoundEndSample = Math.Max(latestSoundEndSample, keyOffs[noteIndex].KeyOffSample + SnesEnvelope.LongestReleaseSamples);
                _latestSoundEndSamples[noteIndex] = latestSoundEndSample;
            }

            _firstSampleReadAgain = _tempo.SampleAt(firstTickReadAgain);
            _releasePastSongEndSamples = Math.Max(0, latestSoundEndSample - _tempo.SampleAt(song.EndTick));
        }

        // Whether no note of the channel sounds from one tick until a later one, on the first pass and, where it is read there, on every pass after it.
        public bool IsSilentBetween(int fromTick, int toTick, bool isReadOnLaterPasses)
        {
            int notesStartedCount = SortedSearch.CountLeading(_noteStartTicks, startTick => startTick < toTick);
            long fromSample = _tempo.SampleAt(fromTick);
            if (notesStartedCount > 0 && _latestSoundEndSamples[notesStartedCount - 1] > fromSample)
            {
                return false;
            }

            return !isReadOnLaterPasses || fromSample - _firstSampleReadAgain >= _releasePastSongEndSamples;
        }
    }
}
