using System.Collections.Immutable;
using SpcStudio.Model;

namespace SpcStudio.Audio;

// Everything a voice keys on with: what it decodes, or null on a noise note, under which envelope, at which pitch and level, gated after how many samples, the noise clock it sets and its $DD chain.
public sealed record VoiceKeyOn(BrrSample? Sample, Instrument Instrument, EnvelopeState Envelope, SnesPitch.NotePitch Pitch, VoiceLevel Level, int GateSamples = 0,
                                int? NoiseClock = null, ImmutableArray<PitchBend> Bends = default)
{
    // What a note of a song keys on with, its level read off a channel's lanes at its own tick, under the envelope and the gate its caller resolved.
    public static VoiceKeyOn Of(Song song, Instrument instrument, Note note, VoiceLanes lanes, EnvelopeState envelope, int gateSamples)
    {
        var sample = instrument.IsNoise ? null : song.SampleFor(instrument)!.Decoded;
        int? noiseClock = instrument.IsNoise ? SnesNoise.ClockForSemitone(note.Semitone) : null;
        return new(sample, instrument, envelope, SnesPitch.NotePitch.Of(note, song.SoundingTuningOf(instrument)), lanes.LevelOf(note, song.UsesSmwVelocities), gateSamples, noiseClock, note.Bends);
    }
}
