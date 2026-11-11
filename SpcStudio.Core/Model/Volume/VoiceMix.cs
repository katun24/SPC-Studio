namespace SpcStudio.Model;

// A VOL pair read back as the chain that would have produced it: the channel volume carrying the whole of the loudness, the pan, and the surround pair.
public readonly record struct VoiceMix(int ChannelVolume, int PanIndex, SnesSurround Surround);
