namespace SpcStudio.Audio;

// A sequence event that reaches one channel's voice alone.
public interface IChannelEvent
{
    int VoiceIndex { get; }
}
