using SpcStudio.Model;

namespace SpcStudio.Audio;

// The lanes a channel's voice plays under - the volume chain, the pan and its surround, the amplify, the two sweeps and the envelope - and a note's level read off them.
public sealed record VoiceLanes(ChangeMap MasterVolume, ChangeMap ChannelVolume, ChangeMap Pan, ChangeMap Surround, ChangeMap Amplify, VibratoMap Vibrato, TremoloMap Tremolo, EnvelopeMap Envelope)
{
    // A channel's lanes under a song volume, the optional notes standing in for the channel's.
    public static VoiceLanes For(Song song, int channel, ChangeMap masterVolume, IReadOnlyList<Note>? notes = null)
    {
        return new(masterVolume, ChangeMap.ForChannelVolume(song, channel, notes), ChangeMap.ForChannelPan(song, channel, notes), ChangeMap.ForChannelSurround(song, channel, notes),
            ChangeMap.ForChannelAmplify(song, channel, notes), VibratoMap.For(song, channel, notes), TremoloMap.For(song, channel, notes), EnvelopeMap.For(song, channel, notes));
    }

    // A note's level as these lanes hold it at its own tick.
    public VoiceLevel LevelOf(Note note, bool usesSmwVelocities)
    {
        int tick = note.StartTick;
        return SnesVolume.LevelForNote(MasterVolume.InForceAt(tick), ChannelVolume.InForceAt(tick), note.Velocity, Pan.InForceAt(tick), usesSmwVelocities, Amplify.InForceAt(tick),
            SnesSurround.FromPacked(Surround.InForceAt(tick)), Pan.StateAt(tick).Fraction);
    }
}
