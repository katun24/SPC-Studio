using System.Collections.Immutable;
using System.Text.Json.Nodes;

namespace SpcStudio.Model;

// The song file's one conversion, from the version before the current one: the Amplify lane every channel held until then, moved onto the notes as
// the rows each one played.
public static partial class SongFile
{
    // Gives each note of a file written before the amplify left its lane the rows it played, on its instrument's rung where it followed the
    // instrument, or where it followed the lane and those rows are exactly its instrument's - and takes the lane out of the file.
    private static void MoveAmplifyLaneOntoNotes(JsonObject file)
    {
        var instruments = SavedFileJson.FromNode<List<LaneAmplifyInstrumentDto>>(file[SavedFileJson.PropertyName(nameof(SongDto.Instruments))]) ?? [];
        var amplifyById = instruments.ToDictionary(instrument => instrument.Id, instrument => instrument.Amplify);
        if (file[SavedFileJson.PropertyName(nameof(SongDto.Channels))] is not JsonArray channels)
        {
            return;
        }

        int channelIndex = 0;
        foreach (var channel in channels.OfType<JsonObject>())
        {
            MoveChannelAmplifyLaneOntoNotes(channel, $"channel {Song.ChannelNumberOf(channelIndex++)}", amplifyById);
        }
    }

    // Gives each note of one channel the rows it played, refusing a lane the file's version would have refused, and takes the channel's lane out of the file.
    private static void MoveChannelAmplifyLaneOntoNotes(JsonObject channel, string channelName, Dictionary<int, int> amplifyById)
    {
        var lane = SavedFileJson.FromNode<LaneAmplifyChannelDto>(channel) ?? new LaneAmplifyChannelDto();
        SavedFileChecks.RequireAmplify(channelName, lane.Amplify);
        var laneChanges = new List<LaneValueChange>();
        ReadChanges(
            lane.AmplifyChanges, channelName, SavedFileChecks.AmplifyName, SnesVolume.AmplifyRange, canFade: false, change => laneChanges.Add(new LaneValueChange(change.Tick, change.Value)));
        var laneMap = ChangeMap.FromSets(lane.Amplify, laneChanges);
        channel.Remove(SavedFileJson.PropertyName(nameof(LaneAmplifyChannelDto.Amplify)));
        channel.Remove(SavedFileJson.PropertyName(nameof(LaneAmplifyChannelDto.AmplifyChanges)));
        if (channel[SavedFileJson.PropertyName(nameof(ChannelDto.Notes))] is not JsonArray noteEntries)
        {
            return;
        }

        // Each note's entry with its old amplify taken out of it, read as the note it otherwise is.
        var notes = noteEntries.OfType<JsonObject>().Select(entry =>
        {
            var laneAmplify = SavedFileJson.FromNode<LaneAmplifyNoteDto>(entry)!;
            entry.Remove(SavedFileJson.PropertyName(nameof(LaneAmplifyNoteDto.Amplify)));
            entry.Remove(SavedFileJson.PropertyName(nameof(LaneAmplifyNoteDto.AmplifySource)));
            return (Entry: entry, LaneAmplify: laneAmplify, Note: SavedFileJson.FromNode<NoteDto>(entry)!);
        }).ToList();

        var laneRowsOfEach = OffsetRows.PlayedOverTimeline(
            [.. notes.Select(note => new Note(note.Note.Start, note.Note.Length, note.Note.Semitone, note.Note.Instrument))], [.. laneChanges.Select(change => change.Tick)],
            tick => laneMap.InForceAt(tick), (offsetTicks, value) => new AmplifyRow(offsetTicks, value), row => row.Value);
        for (int noteIndex = 0; noteIndex < notes.Count; noteIndex++)
        {
            var (entry, laneAmplify, note) = notes[noteIndex];
            ImmutableArray<AmplifyRow> instrumentRows = [new AmplifyRow(OffsetRows.FirstOffsetTicks, amplifyById.GetValueOrDefault(note.Instrument, SnesVolume.DefaultAmplify))];
            var rows = laneAmplify.AmplifySource switch
            {
                LaneSource.Note => [new AmplifyRow(OffsetRows.FirstOffsetTicks, laneAmplify.Amplify)],
                LaneSource.Instrument => instrumentRows,
                _ => laneRowsOfEach[noteIndex],
            };
            bool isOnInstrumentRung = laneAmplify.AmplifySource != LaneSource.Note && rows.SequenceEqual(instrumentRows);
            entry[SavedFileJson.PropertyName(nameof(NoteDto.AmplifySource))] = SavedFileJson.ToNode(isOnInstrumentRung ? NoteSource.Instrument : NoteSource.Note);
            entry[SavedFileJson.PropertyName(nameof(NoteDto.Amplifies))] = isOnInstrumentRung ? null : SavedFileJson.ToNode(AmplifyRowDto.ListOf(rows));
        }
    }

    // An instrument of a file written before the amplify left its lane, as far as its amplify goes.
    private sealed class LaneAmplifyInstrumentDto
    {
        public int Id { get; set; }
        public int Amplify { get; set; } = SnesVolume.DefaultAmplify;
    }

    // A channel of such a file, as far as its amplify lane goes.
    private sealed class LaneAmplifyChannelDto
    {
        public int Amplify { get; set; }
        public List<ChangeDto> AmplifyChanges { get; set; } = [];
    }

    // A note of such a file, as far as its amplify goes: its own amplify and which of three rungs it stood on.
    private sealed class LaneAmplifyNoteDto
    {
        public int Amplify { get; set; }
        public LaneSource AmplifySource { get; set; }
    }
}
