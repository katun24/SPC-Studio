using System.IO;

namespace SpcStudio.Model;

// The song file's notes.
public static partial class SongFile
{
    // Writes every channel's notes.
    private static void WriteNotes(Song song, SongDto contents)
    {
        for (int channel = 0; channel < song.ChannelCount; channel++)
        {
            contents.Channels[channel].Notes = [.. song.Notes(channel).Select(note => new NoteDto
            {
                Start = note.StartTick,
                Length = note.LengthTicks,
                Semitone = note.Semitone,
                Instrument = note.InstrumentId,
                Gate = note.Gate,
                Velocity = note.Velocity,
                FineTune = note.FineTune,
                Legato = note.IsLegato,
                Hold = note.IsHeld,
                LegatoSource = note.LegatoSource,
                HoldSource = note.HoldSource,
                Bends = BendDto.ListOf(note.Bends),
                FlatBendNodes = SavedFileJson.EntriesOrNull(note.FlatBendNodeTicks, tick => tick),
                Vibratos = VibratoDto.ListOf(note.Vibratos),
                Tremolos = TremoloDto.ListOf(note.Tremolos),
                PanSource = note.PanSource,
                VolumeSource = note.VolumeSource,
                GateSource = note.GateSource,
                VelocitySource = note.VelocitySource,
                FineTuneSource = note.FineTuneSource,
                VibratoSource = note.VibratoSource,
                TremoloSource = note.TremoloSource,
                BendSource = note.BendSource,
                EnvelopeSource = note.EnvelopeSource,
                Envelopes = EnvelopeRowDto.ListOf(note.Envelopes),
                Amplify = note.Amplify,
                AmplifySource = note.AmplifySource,
                Echo = note.IsEchoOn,
                EchoSource = note.EchoSource,
                Volumes = VolumeRegionDto.ListOf(note.Volumes),
                Pans = PanRegionDto.ListOf(note.Pans),
            })];
        }
    }

    // Reads one channel's notes back, refusing a note off the roll's rows, carried off them by its bends, holding a value out of range or naming an
    // instrument the file does not hold - and two notes sounding at once.
    private static void ReadNotes(Song song, int channel, ChannelDto channelContents, string channelName)
    {
        ReadUnits(
            channelContents.Notes,
            note => note.Start,
            note => note.Length,
            $"{channelName} note",
            (note, where) =>
            {
                SavedFileChecks.RequireInRange(where, "semitone", note.Semitone, Song.SemitoneRange);
                SavedFileChecks.RequirePlayedValues(where, note.Gate, note.Velocity, note.FineTune, note.Amplify);

                var checkedBendChain = BendDto.ChainOf(note.Bends, where);
                int bentSemitone = note.Semitone;
                foreach (var bend in checkedBendChain)
                {
                    bentSemitone += bend.OffsetSemitones;
                    SavedFileChecks.RequireInRange(where, "a pitch bend reaching semitone", bentSemitone, Song.SemitoneRange);
                }

                SavedFileChecks.RequireFlatBendNodeTicks(where, note.FlatBendNodes ?? []);
                if (song.InstrumentById(note.Instrument) is null)
                {
                    throw new InvalidDataException($"{where} uses instrument {note.Instrument}, which is not in the file.");
                }

                song.PlaceNote(channel, new Note(
                    note.Start, note.Length, note.Semitone, note.Instrument, note.Gate, note.Velocity, note.FineTune, note.Legato, note.Hold)
                {
                    Bends = checkedBendChain,
                    FlatBendNodeTicks = [.. note.FlatBendNodes ?? []],
                    Vibratos = VibratoDto.NoteChainOf(note.Vibratos, where, note.Length),
                    Tremolos = TremoloDto.NoteChainOf(note.Tremolos, where, note.Length),
                    PanSource = note.PanSource,
                    VolumeSource = note.VolumeSource,
                    GateSource = note.GateSource,
                    VelocitySource = note.VelocitySource,
                    FineTuneSource = note.FineTuneSource,
                    VibratoSource = note.VibratoSource,
                    TremoloSource = note.TremoloSource,
                    BendSource = note.BendSource,
                    EnvelopeSource = note.EnvelopeSource,
                    Envelopes = EnvelopeRowDto.NoteChainOf(note.Envelopes, where, note.Length),
                    LegatoSource = note.LegatoSource,
                    HoldSource = note.HoldSource,
                    Amplify = note.Amplify,
                    AmplifySource = note.AmplifySource,
                    IsEchoOn = note.Echo,
                    EchoSource = note.EchoSource,
                    Volumes = VolumeRegionDto.ChainOf(note.Volumes, where),
                    Pans = PanRegionDto.ChainOf(note.Pans, where),
                });
            });
    }

    // A note as the file writes it: where it is, what it plays and everything it carries of its own, each with the rung that says where it comes from.
    private sealed class NoteDto
    {
        public int Start { get; set; }
        public int Length { get; set; }
        public int Semitone { get; set; }
        public int Instrument { get; set; }
        public int Gate { get; set; }
        public int Velocity { get; set; }
        public int FineTune { get; set; }
        public bool Legato { get; set; }
        public bool Hold { get; set; }
        public NoteSource LegatoSource { get; set; }
        public NoteSource HoldSource { get; set; }
        public List<BendDto>? Bends { get; set; }
        public List<int>? FlatBendNodes { get; set; }
        public List<VibratoDto>? Vibratos { get; set; }
        public List<TremoloDto>? Tremolos { get; set; }
        public LaneSource PanSource { get; set; }
        public LaneSource VolumeSource { get; set; }
        public NoteSource GateSource { get; set; }
        public NoteSource VelocitySource { get; set; }
        public NoteSource FineTuneSource { get; set; }
        public NoteSource VibratoSource { get; set; }
        public NoteSource TremoloSource { get; set; }
        public NoteSource BendSource { get; set; }
        public NoteSource EnvelopeSource { get; set; }
        public List<EnvelopeRowDto>? Envelopes { get; set; }
        public int Amplify { get; set; }
        public LaneSource AmplifySource { get; set; }
        public bool Echo { get; set; }
        public LaneSource EchoSource { get; set; }
        public List<PanRegionDto>? Pans { get; set; }
        public List<VolumeRegionDto>? Volumes { get; set; }
    }
}
