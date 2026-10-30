using SpcStudio.Export;
using SpcStudio.Model;

namespace SpcStudio.Audio;

// Turns a song into the events the mixer plays, timed in samples, and works out the key-on length an audition of one note is gated at.
public static class SequenceBuilder
{
    private const int WholeLaneStartTick = -1;

    // The events for a song with its silenced channels left out, in time order, and the samples up to an end tick or the song's end marker.
    public static (List<SequenceEvent> Events, long LengthSamples) Build(Song song, int? endTick = null)
    {
        return Build(song, endTick, song.Muting.IsSilenced);
    }

    // The events for a song with every channel in, whatever is muted or soloed, and the samples up to an end tick or the song's end marker.
    public static (List<SequenceEvent> Events, long LengthSamples) BuildIgnoringMuting(Song song, int? endTick = null)
    {
        return Build(song, endTick, _ => false);
    }

    // The driver's key-on length for one note of a channel in samples, with the legato run the note is in on its channel taken in.
    public static int AuditionGateSamples(Song song, int channel, Note note)
    {
        return GateSamples(song, channel, note, IsInLegatoRun(song, channel, note));
    }

    // The driver's key-on length for one note of a stretch of a channel played over and over, the legato run it is in being the stretch's own, the last note followed by the first again.
    public static int LoopedStretchGateSamples(Song song, int channel, IReadOnlyList<Note> stretch, int noteIndex)
    {
        bool isInLegatoRun = LegatoToggles.DoesBindNextInLoop(stretch, noteIndex) || LegatoToggles.IsBoundFromPreviousInLoop(stretch, noteIndex);
        return GateSamples(song, channel, stretch[noteIndex], isInLegatoRun);
    }

    // The events for a song with the channels a test names left out, and the samples up to an end tick or the song's end marker.
    private static (List<SequenceEvent> Events, long LengthSamples) Build(Song song, int? endTick, Func<int, bool> isSilenced)
    {
        var tempo = TempoMap.For(song);
        var masterVolume = ChangeMap.ForMasterVolume(song);
        var staccato = ChangeMap.ForStaccato(song);
        var exportedEchoOfAllChannels = EchoMap.For(song);
        int loopMarkerTick = MmlExporter.LoopMarkerTick(song);
        var events = new List<SequenceEvent>();
        AddEchoEvents(events, song, tempo, EchoMap.For(song, channel => !isSilenced(channel)));
        events.AddRange(RegionLaneEvents(tempo, song.BaseTempo, [.. LaneRegions.TempoCommands(song)],
            (sampleTime, value, fade, doesSetValue, _) => new TempoEvent(sampleTime, value, fade, doesSetValue)));
        events.AddRange(RegionLaneEvents(tempo, song.BaseMasterVolume, [.. LaneRegions.MasterVolumeCommands(song)],
            (sampleTime, value, fade, doesSetValue, _) => new MasterVolumeEvent(sampleTime, value, fade, doesSetValue)));
        for (int channel = 0; channel < song.ChannelCount; channel++)
        {
            if (isSilenced(channel))
            {
                continue;
            }

            var maps = ChannelMaps.For(song, channel, tempo, masterVolume, staccato, exportedEchoOfAllChannels, loopMarkerTick);
            AddChannelStateEvents(events, song, channel, maps);
            AddNoteEvents(events, song, channel, maps);
        }

        events.Sort((a, b) => a.SampleTime.CompareTo(b.SampleTime));
        return (events, SampleAt(tempo, endTick ?? song.EndTick));
    }

    // Adds the echo's events: its state at sample 0, and every tick it changes at where the song has an echo.
    private static void AddEchoEvents(List<SequenceEvent> events, Song song, TempoMap tempo, EchoMap echo)
    {
        var echoVolumeFadesByTick = LaneRegions.EchoVolumeCommands(song).Where(command => command.Fades).ToDictionary(command => command.Tick);
        var echoVolumeSetTicks = echo.SendTicks(song).ToHashSet();
        events.Add(EchoEventAt(tempo, echo, echoVolumeFadesByTick, 0, true));
        if (!echo.IsEnabled)
        {
            return;
        }

        foreach (int tick in echo.AllTicks(song).Where(tick => tick > 0))
        {
            events.Add(EchoEventAt(tempo, echo, echoVolumeFadesByTick, tick, echoVolumeSetTicks.Contains(tick)));
        }
    }

    // The echo's event at a tick, with the $F2 a fading echo volume region arms there.
    private static EchoEvent EchoEventAt(TempoMap tempo, EchoMap echo, Dictionary<int, EchoVolumeCommand> volumeFades, int tick, bool doesSetVolume)
    {
        var fadingVolumeCommand = volumeFades.TryGetValue(tick, out var armed) ? armed : default;
        return new EchoEvent(SampleAt(tempo, tick), echo.StateAt(tick), doesSetVolume, FadeFor(tempo, tick, fadingVolumeCommand.FadeTicks, fadingVolumeCommand.FadeToLeft),
            FadeFor(tempo, tick, fadingVolumeCommand.FadeTicks, fadingVolumeCommand.FadeToRight));
    }

    // Adds one channel's state events: its vibrato, tremolo and envelope override, its volume, pan and amplify.
    private static void AddChannelStateEvents(List<SequenceEvent> events, Song song, int channel, ChannelMaps maps)
    {
        var tempo = maps.Tempo;
        var lanes = maps.VoiceLanes;
        events.AddRange(StateEvents(tempo, lanes.Vibrato.StateChangeTicks(), (sampleTime, tick) => new VibratoEvent(sampleTime, channel, lanes.Vibrato.StateAt(tick))));
        events.AddRange(StateEvents(tempo, lanes.Tremolo.StateChangeTicks(), (sampleTime, tick) => new TremoloEvent(sampleTime, channel, lanes.Tremolo.StateAt(tick))));
        events.AddRange(StateEvents(tempo, lanes.Envelope.StateChangeTicks(), (sampleTime, tick) => new EnvelopeEvent(sampleTime, channel, lanes.Envelope.StateAt(tick))));
        var (channelVolumeBase, channelVolumeCommands) = LaneRegions.ChannelVolumeWritten(song, channel);
        events.AddRange(RegionLaneEvents(tempo, channelVolumeBase, channelVolumeCommands,
            (sampleTime, value, fade, doesSetValue, _) => new ChannelVolumeEvent(sampleTime, channel, value, fade, doesSetValue)));
        var (panBase, panCommands) = LaneRegions.PanWritten(song, channel);
        events.AddRange(RegionLaneEvents(tempo, panBase.Value, [.. panCommands.Select(written => written.Command)],
            (sampleTime, value, fade, doesSetValue, commandIndex) => new PanEvent(sampleTime, channel, value,
                SurroundFor(maps, panBase, panCommands, commandIndex), fade, doesSetValue)));
        events.Add(new AmplifyEvent(0, channel, lanes.Amplify.InForceAt(0)));
        foreach (var change in NoteAmplify.Lane.Merged(song, channel).Changes)
        {
            events.Add(new AmplifyEvent(SampleAt(tempo, change.Tick), channel, change.Value));
        }
    }

    // The surround pair a pan event carries: the base's at sample 0, a region's own where its command sets the pan, and the pair in force otherwise.
    private static SnesSurround SurroundFor(ChannelMaps maps, PanState panBase, IReadOnlyList<(LaneCommand Command, SnesSurround Surround)> panCommands,
                                            int commandIndex)
    {
        if (commandIndex < 0)
        {
            return panBase.Surround;
        }

        var (command, surround) = panCommands[commandIndex];
        return command.SetValue is not null ? surround : SnesSurround.FromPacked(maps.VoiceLanes.Surround.InForceAt(command.Tick));
    }

    // Adds a key-on event for every note of the channel that plays, each gated where the driver would key it off.
    private static void AddNoteEvents(List<SequenceEvent> events, Song song, int channel, ChannelMaps maps)
    {
        int[] breaks = BreakTicks(song, channel, maps);
        var notes = song.Notes(channel);
        int? previous = null;
        for (int index = 0; index < notes.Count; index++)
        {
            var note = notes[index];
            if (!song.IsNotePlayable(note))
            {
                previous = null;
                continue;
            }

            bool isBoundFromPrevious = previous == index - 1 && LegatoToggles.DoesBindNext(notes, index - 1);
            bool isInLegatoRun = isBoundFromPrevious || LegatoToggles.DoesBindNext(notes, index);
            previous = index;
            events.Add(NoteEventFor(song, channel, note, maps, KeyOnTicks(note, breaks, maps.Staccato, isInLegatoRun), isBoundFromPrevious));
        }
    }

    // The key-on event for a note that plays, keyed on under the state its channel's lanes hold at its start and gated after its key-on length.
    private static NoteEvent NoteEventFor(Song song, int channel, Note note, ChannelMaps maps, int keyOnTicks, bool isBound)
    {
        var instrument = song.InstrumentOf(note)!;
        long start = SampleAt(maps.Tempo, note.StartTick);
        long keyOffSample = SampleAt(maps.Tempo, note.StartTick + keyOnTicks);
        var lanes = maps.VoiceLanes;
        var keyOn = VoiceKeyOn.Of(song, instrument, note, lanes, lanes.Envelope.StateAt(note.StartTick), (int)(keyOffSample - start));
        return new NoteEvent(start, channel, keyOn, SampleAt(maps.Tempo, note.EndTick()), isBound);
    }

    // The key-on length of a note whose legato run has been answered already, in samples.
    private static int GateSamples(Song song, int channel, Note note, bool isInLegatoRun)
    {
        var tempo = TempoMap.For(song);
        var maps = ChannelMaps.For(song, channel, tempo, ChangeMap.ForMasterVolume(song), ChangeMap.ForStaccato(song), EchoMap.For(song),
            MmlExporter.LoopMarkerTick(song));
        int keyOnTicks = KeyOnTicks(note, BreakTicks(song, channel, maps), maps.Staccato, isInLegatoRun);
        return (int)(SampleAt(tempo, note.StartTick + keyOnTicks) - SampleAt(tempo, note.StartTick));
    }

    // How many ticks a note is keyed on for: its whole length where it is held or in a legato run, and the driver's key-on length otherwise.
    private static int KeyOnTicks(Note note, int[] breaks, ChangeMap staccato, bool isInLegatoRun)
    {
        if (note.IsHeld || isInLegatoRun)
        {
            return note.LengthTicks;
        }

        return SnesTiming.KeyOnTicks(note.LengthTicks, note.Gate, FinalPiece(breaks, note),
            offset => SnesTiming.WaitTimeFor(staccato.InForceAt(note.StartTick + offset)));
    }

    // Whether a note of the channel hands over to the next one or is handed over to by the one before it, a note not in the channel being in no run.
    private static bool IsInLegatoRun(Song song, int channel, Note note)
    {
        var notes = song.Notes(channel);
        int index = -1;
        for (int i = 0; i < notes.Count; i++)
        {
            if (notes[i].StartTick == note.StartTick)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return false;
        }

        return LegatoToggles.DoesBindNext(notes, index) || (index > 0 && LegatoToggles.DoesBindNext(notes, index - 1) && song.IsNotePlayable(notes[index - 1]));
    }

    // The length of the last piece the exporter writes a note as, or null where it is written whole.
    private static int? FinalPiece(int[] breaks, Note note)
    {
        int breaksBeforeEndCount = SortedSearch.CountLeading(breaks, breakTick => breakTick < note.EndTick());
        if (breaksBeforeEndCount == 0 || breaks[breaksBeforeEndCount - 1] <= note.StartTick)
        {
            return null;
        }

        return SnesTiming.FinalPieceTicks(note.StartTick, note.LengthTicks, [breaks[breaksBeforeEndCount - 1]]);
    }

    // Every tick a channel's notes are broken at by the export, sorted.
    private static int[] BreakTicks(Song song, int channel, ChannelMaps maps)
    {
        var breaks = new List<int>();
        if (channel == MmlExporter.CommandChannel)
        {
            breaks.AddRange(maps.Tempo.ChangesWithin(WholeLaneStartTick, int.MaxValue));
            breaks.AddRange(maps.VoiceLanes.MasterVolume.ChangesWithin(WholeLaneStartTick, int.MaxValue));
            if (maps.WrittenEcho.IsEnabled)
            {
                breaks.AddRange(maps.WrittenEcho.AllTicks(song));
            }

            breaks.AddRange(MmlExporter.StaccatoToggleTicks(song, maps.LoopMarkerTick));
        }

        var lanes = maps.VoiceLanes;
        breaks.AddRange(lanes.ChannelVolume.ChangesWithin(WholeLaneStartTick, int.MaxValue));
        breaks.AddRange(lanes.Pan.ChangesWithin(WholeLaneStartTick, int.MaxValue));
        breaks.AddRange(lanes.Amplify.ChangesWithin(WholeLaneStartTick, int.MaxValue));
        breaks.AddRange(lanes.Vibrato.StateChangeTicks());
        breaks.AddRange(lanes.Tremolo.StateChangeTicks());
        breaks.AddRange(lanes.Envelope.StateChangeTicks());
        if (maps.LoopMarkerTick >= 0)
        {
            breaks.Add(maps.LoopMarkerTick);
        }

        breaks.Sort();
        return [.. breaks];
    }

    // The sample a tick falls on, rounded from the tempo map's absolute time.
    private static long SampleAt(TempoMap tempo, double tick)
    {
        return (long)Math.Round(tempo.SecondsAt(tick) * DspPoint.SampleRate);
    }

    // The events a state lane comes to: one at sample 0, and one at every tick its state changes.
    private static IEnumerable<SequenceEvent> StateEvents(TempoMap tempo, IEnumerable<int> changeTicks, Func<long, int, SequenceEvent> eventAt)
    {
        yield return eventAt(0, 0);
        foreach (int tick in changeTicks)
        {
            yield return eventAt(SampleAt(tempo, tick), tick);
        }
    }

    // The events a region lane's commands come to: its base with its first region's fade at sample 0, and one per command after it, each told the index of its command, or a negative one for the base.
    private static IEnumerable<SequenceEvent> RegionLaneEvents(TempoMap tempo, int baseValue, IReadOnlyList<LaneCommand> commands,
                                                               Func<long, int, SequenceFade?, bool, int, SequenceEvent> eventFor)
    {
        var tickZeroCommand = commands.FirstOrDefault(command => command.Tick == 0);
        yield return eventFor(0, baseValue, FadeFor(tempo, 0, tickZeroCommand.FadeTicks, tickZeroCommand.FadeTo), true, -1);
        for (int index = 0; index < commands.Count; index++)
        {
            var command = commands[index];
            if (command.Tick > 0)
            {
                yield return eventFor(SampleAt(tempo, command.Tick), command.SetValue ?? 0, FadeFor(tempo, command.Tick, command.FadeTicks, command.FadeTo),
                    command.SetValue is not null, index);
            }
        }
    }

    // The fade an event carries, or null where the change is a plain set.
    private static SequenceFade? FadeFor(TempoMap tempo, int tick, int fadeTicks, int fadeTo)
    {
        if (!SnesFade.DoesFade(fadeTicks))
        {
            return null;
        }

        return new SequenceFade(fadeTicks, fadeTo, SampleAt(tempo, tick + fadeTicks) - SampleAt(tempo, tick));
    }

    // The maps one channel's events and splits are read from, the song's own among them.
    private sealed record ChannelMaps(TempoMap Tempo, ChangeMap Staccato, EchoMap WrittenEcho, int LoopMarkerTick, VoiceLanes VoiceLanes)
    {
        // A channel's maps beside the song's own, built once for every channel.
        public static ChannelMaps For(Song song, int channel, TempoMap tempo, ChangeMap masterVolume, ChangeMap staccato, EchoMap writtenEcho, int loopMarkerTick)
        {
            return new(tempo, staccato, writtenEcho, loopMarkerTick, VoiceLanes.For(song, channel, masterVolume));
        }
    }
}
