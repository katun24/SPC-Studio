using SpcStudio.Model;

namespace SpcStudio.Audio;

// Every lane an audition reads, built once: the note keys on at what they say at its own tick, and then goes on being played through them for as long as it sounds.
public sealed class AuditionLanes
{
    // Holds the note's span and the channel's lanes read through a set of notes under a song volume, and reads the ticks the sweeps and the envelope change at.
    private AuditionLanes(Song song, int channel, Note note, IReadOnlyList<Note> notes, ChangeMap masterVolume, EchoMap echo, HashSet<int> echoChangeTicks, TempoMap tempo)
    {
        Channel = channel;
        StartTick = note.StartTick;
        EndTick = note.EndTick();
        VoiceLanes = VoiceLanes.For(song, channel, masterVolume, notes);
        Echo = echo;
        VibratoChangeTicks = VoiceLanes.Vibrato.StateChangeTicks().ToHashSet();
        TremoloChangeTicks = VoiceLanes.Tremolo.StateChangeTicks().ToHashSet();
        EnvelopeChangeTicks = VoiceLanes.Envelope.StateChangeTicks().ToHashSet();
        EchoChangeTicks = echoChangeTicks;
        Tempo = tempo;
    }

    public int Channel { get; }
    public int StartTick { get; }
    public int EndTick { get; }
    public VoiceLanes VoiceLanes { get; }
    public EchoMap Echo { get; }
    public IReadOnlySet<int> VibratoChangeTicks { get; }
    public IReadOnlySet<int> TremoloChangeTicks { get; }
    public IReadOnlySet<int> EnvelopeChangeTicks { get; }
    public IReadOnlySet<int> EchoChangeTicks { get; }
    public TempoMap Tempo { get; }

    // The lanes a note auditioned on a channel reads, the note laid among the channel's own whether or not it is in it, followed as far as its own end.
    public static AuditionLanes For(Song song, int channel, Note note)
    {
        var notes = song.NotesWithPlaced(channel, note);
        var echo = EchoMap.WithChannelNotes(song, channel, notes);
        return new AuditionLanes(song, channel, note, notes, ChangeMap.ForMasterVolume(song), echo, [.. echo.AllTicks(song)], TempoMap.For(song));
    }

    // The lanes an instrument is auditioned through, its own note laid over the channel where it carries something and every other lane flat at the channel's base.
    public static AuditionLanes ForInstrument(Song song, int channel, Note note)
    {
        var flatMasterVolume = ChangeMap.Flat(ChangeMap.ForMasterVolume(song).InForceAt(Song.TickBeforeSong));
        var flatTempo = TempoMap.Flat(TempoMap.For(song).TempoAt(Song.TickBeforeSong));
        return new AuditionLanes(song, channel, note, [note], flatMasterVolume, EchoMap.For(song), [], flatTempo);
    }

    // The note an instrument is auditioned as at a semitone: at tick 0, on the instrument's rung for everything it can, and long enough for everything the instrument does over time.
    public static Note InstrumentNote(Instrument instrument, int semitone)
    {
        int fadeTicks = Math.Max(instrument.PanRows[^1].OffsetTicks, instrument.VolumeRows[^1].OffsetTicks);
        int vibratoTicks = instrument.Vibratos.Sum(vibrato => NoteSweep.SpanTicks(vibrato));
        int tremoloTicks = instrument.Tremolos.Sum(tremolo => NoteSweep.SpanTicks(tremolo));
        int bendTicks = instrument.Bends.Sum(bend => NoteSweep.SpanTicks(bend));
        int envelopeTicks = instrument.EnvelopeChanges is [.., var lastChange] ? lastChange.OffsetTicks : 0;
        int longestChainTicks = Math.Max(Math.Max(Math.Max(fadeTicks, bendTicks), Math.Max(vibratoTicks, tremoloTicks)), envelopeTicks);
        int lengthTicks = Math.Max(SnesTiming.TicksPerQuarterNote, longestChainTicks + 1);

        return NoteDefaults.SyncedWithInstrument(NoteDefaults.OnInstrumentRungsExceptEcho(new Note(0, lengthTicks, semitone, instrument.Id)), instrument);
    }
}
