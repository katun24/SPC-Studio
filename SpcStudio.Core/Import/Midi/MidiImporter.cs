using System.IO;
using SpcStudio.Audio;
using SpcStudio.Model;

namespace SpcStudio.Import;

// Turns a MIDI file into a song as near as the model allows, every instrument on SMW's piano sample: its notes spread over as many channels as sound at
// once, its tempos, its loudness, pan, bends and vibrato, and its song name as the title.
public static partial class MidiImporter
{
    public const string DialogFilter = "MIDI file (*.mid;*.midi)|*.mid;*.midi" + DialogFilters.Separator + DialogFilters.AllFiles;
    private const int MiddleCKey = 60;
    private const double DefaultBpm = 120;
    private const double MicrosecondsPerMinute = 60_000_000;
    private const int NoLoopTick = 0;
    private const int MinNoteTicks = 1;

    // Reads a MIDI file into a song, its piano sample read from the AddmusicK folder given.
    public static Song Load(string path, string? amkRoot, Action<string>? warn = null)
    {
        var file = MidiReader.Read(File.ReadAllBytes(path)) ?? throw MidiImportException.NotValidMidi(Path.GetFileName(path));
        var pianoSample = PianoSample(amkRoot) ?? throw MidiImportException.NoDefaultSample();
        return Build(file, pianoSample, warn);
    }

    // A read MIDI file as a song, every instrument on the piano sample given.
    public static Song Build(MidiFile file, Sample pianoSample, Action<string>? warn = null)
    {
        var song = new Song();
        if (file.SongName is { } songName)
        {
            song.SpcTags = SpcTags.Empty with { Title = songName };
        }

        WriteTempos(song, file);
        var performance = MidiPerformance.Of(file);
        var placed = PlacedOnChannels(file.TimeBase, performance.Notes);
        song.SetChannelCount(Math.Max(Song.DriverChannelCount, placed.ChannelCount));
        int endTick = Math.Max(file.TimeBase.SongTickAt(file.EndTick), placed.Notes.Select(note => note.StartTick + note.LengthTicks).DefaultIfEmpty(0).Max());
        if (endTick > 0)
        {
            song.SetMarkers(endTick, NoLoopTick);
        }

        var instruments = new PianoInstruments(song, song.AddSampleUnderFreeName(pianoSample));
        var tempoMap = TempoMap.For(song);
        int movedCount = 0;
        var notes = new List<(int Channel, Note Note)>();
        foreach (var timed in placed.Notes)
        {
            var written = WrittenNoteFor(timed, performance, file.TimeBase, instruments.IdFor(timed.Played), tempoMap);
            movedCount += written.IsMovedByOctaves ? 1 : 0;
            notes.Add((timed.Channel, written.Note));
        }

        song.PlaceNotes(notes);
        WritePans(song, performance, file.TimeBase, placed.OwnersOf);
        SharedNoteValues.MoveMostPlayedValuesOntoInstruments(song);
        WarnAboutNotes(warn, placed.DroppedCount, movedCount);
        return song;
    }

    // The file's tempos on the tempo lane, each rounded to a tempo the song can carry: the one in force at the start as the song's own tempo and each
    // later one that moves it as a change, a file timed in seconds playing at its one fixed tempo throughout.
    private static void WriteTempos(Song song, MidiFile file)
    {
        if (file.TimeBase.IsCountedInSeconds)
        {
            song.BaseTempo = MidiTimeBase.SecondsTimedTempo;
            return;
        }

        song.BaseTempo = SnesTiming.TempoForBpm(DefaultBpm);
        int playingTempo = song.BaseTempo;
        foreach (var tempoChangesAtTick in file.Tempos.GroupBy(tempo => file.TimeBase.SongTickAt(tempo.Tick)))
        {
            int tempo = SnesTiming.TempoForBpm(MicrosecondsPerMinute / tempoChangesAtTick.Last().MicrosecondsPerQuarterNote);
            if (!Song.IsChangeTick(tempoChangesAtTick.Key))
            {
                song.BaseTempo = tempo;
                playingTempo = tempo;
                continue;
            }

            if (tempo == playingTempo)
            {
                continue;
            }

            song.SetTempoChange(tempoChangesAtTick.Key, tempo);
            playingTempo = tempo;
        }
    }

    // A played note as the note written on the roll: on its own pitch's row with the bend it was struck under, moved by whole octaves where that row is
    // off the roll, with its loudness, bends and vibrato as chains of its own.
    private static WrittenNote WrittenNoteFor(TimedNote timed, MidiPerformance performance, MidiTimeBase timeBase, int instrumentId, TempoMap tempoMap)
    {
        var played = timed.Played;
        int struckBendSemitones = (int)Math.Round(performance.BendOf(played.Channel).ValueAt(played.StartTick));
        int semitone = played.Key - MiddleCKey + struckBendSemitones;
        int rowSemitone = OnTheRoll(semitone);
        var span = new NoteSpan(played, timed.StartTick, timed.LengthTicks, timeBase);
        var note = new Note(timed.StartTick, timed.LengthTicks, rowSemitone, instrumentId)
        {
            VolumeSource = LaneSource.Note,
            Volumes = VolumeRowsFor(span, performance.LoudnessOf(played.Channel)),
            Bends = PitchBend.Carried(BendsFor(span, performance.BendOf(played.Channel), struckBendSemitones), rowSemitone, timed.LengthTicks),
            Vibratos = VibratosFor(span, performance.VibratoOf(played.Channel), tempoMap.TempoAt(timed.StartTick)),
        };

        return new WrittenNote(note, rowSemitone != semitone);
    }

    // A pitch moved by the fewest whole octaves that put it on the roll.
    private static int OnTheRoll(int semitone)
    {
        int rowSemitone = semitone;
        while (rowSemitone < Song.LowestSemitone)
        {
            rowSemitone += SnesPitch.SemitonesPerOctave;
        }

        while (rowSemitone > Song.HighestSemitone)
        {
            rowSemitone -= SnesPitch.SemitonesPerOctave;
        }

        return rowSemitone;
    }

    // Says how many notes were dropped for want of a channel and how many were moved by octaves onto the roll.
    private static void WarnAboutNotes(Action<string>? warn, int droppedCount, int movedCount)
    {
        if (droppedCount > 0)
        {
            warn?.Invoke(MidiImportWarnings.NotesDropped(droppedCount, Song.MaxChannelCount));
        }

        if (movedCount > 0)
        {
            warn?.Invoke(MidiImportWarnings.NotesMovedByOctaves(movedCount));
        }
    }

    // A note as written on the roll, with whether it was moved by octaves to get there.
    private readonly record struct WrittenNote(Note Note, bool IsMovedByOctaves);
}
