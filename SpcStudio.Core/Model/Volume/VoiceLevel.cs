namespace SpcStudio.Model;

// What the driver's volume routine reads for one voice, as the MML commands write them - w, v, the velocity byte q selects, y and $FA $03 - with y's
// surround pair and the fraction a pan fade leaves between two pan positions.
public readonly record struct VoiceLevel(
    int MasterVolume,
    int ChannelVolume,
    int Velocity,
    int PanIndex,
    int Amplify = SnesVolume.DefaultAmplify,
    SnesSurround Surround = default,
    int PanFraction = 0)
{
    // The DSP VOL registers these produce.
    public VoiceVolume Registers
    {
        get
        {
            return SnesVolume.RegistersFor(MasterVolume, ChannelVolume, Velocity, PanIndex, Amplify, Surround, PanFraction);
        }
    }
}
