using SpcStudio.Audio;
using SpcStudio.Model;

namespace SpcStudio.Import;

// The importer's instruments: SMW's piano sample read out of the AddmusicK folder, and one instrument on it for every program played and every drum key
// struck on the drum channel, each under its General MIDI name with SMW's piano's envelope and tuned to the sample's own measured pitch.
public static partial class MidiImporter
{
    // The sample SMW's piano plays out of the default sample group in an AddmusicK folder, or null where there is no folder or the sample is not in it.
    private static Sample? PianoSample(string? amkRoot)
    {
        if (amkRoot is null || SampleGroups.ReadPathsByGroup(amkRoot)?.GetValueOrDefault(SampleGroups.DefaultGroup) is not { } defaultGroupPaths)
        {
            return null;
        }

        return SmwSamples.ReadAt(amkRoot, defaultGroupPaths, MmlBuiltins.SampleNumberOf(SmwInstruments.PianoNumber));
    }

    // The instruments made on the piano sample so far, each made the first time a note asks for it.
    private sealed class PianoInstruments(Song song, Sample pianoSample)
    {
        private readonly Dictionary<(bool IsDrum, int Number), int> _idsByPlayed = [];

        // The instrument a played note plays: its drum key's on the drum channel and its program's on any other.
        public int IdFor(MidiPlayedNote played)
        {
            bool isDrum = played.Channel == MidiPerformance.DrumChannel;
            var instrumentKey = (IsDrum: isDrum, Number: isDrum ? played.Key : played.Program);
            if (_idsByPlayed.TryGetValue(instrumentKey, out int madeId))
            {
                return madeId;
            }

            string name = isDrum ? GeneralMidiNames.DrumKey(instrumentKey.Number) : GeneralMidiNames.Program(instrumentKey.Number);
            var instrument = song.CreateInstrument(song.FreeInstrumentName(name), pianoSample);
            instrument.Envelope = MmlBuiltins.EnvelopeOf(SmwInstruments.PianoNumber);
            _idsByPlayed[instrumentKey] = instrument.Id;
            return instrument.Id;
        }
    }
}
