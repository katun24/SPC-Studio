using SpcStudio.Model;

namespace SpcStudio.Audio;

// The lanes one audition voice is following, the tick it has reached, the bytes of the volume chain it last pushed, and whether it has been pointed at new
// lanes since it last pushed onto its voice.
internal sealed class AuditionWalk(SnesVoice voice)
{
    public SnesVoice Voice { get; } = voice;
    public AuditionLanes Lanes { get; private set; } = null!;
    public int Tick { get; set; }
    public int MasterVolume { get; set; }
    public int ChannelVolume { get; set; }
    public int Pan16 { get; set; }
    public int Amplify { get; set; }
    public SnesSurround Surround { get; set; }
    public SnesFade PanState { get; set; }
    public bool IsRepointed { get; set; }

    // Starts walking a set of lanes from a tick, from the level the voice was last given.
    public void Follow(AuditionLanes lanes, VoiceLevel level, int tick)
    {
        Lanes = lanes;
        Tick = tick;
        MasterVolume = level.MasterVolume;
        ChannelVolume = level.ChannelVolume;
        Pan16 = SnesFade.Value16Of(level.PanIndex, level.PanFraction);
        Surround = level.Surround;
        Amplify = level.Amplify;
        PanState = lanes.VoiceLanes.Pan.StateAt(tick);
    }
}
