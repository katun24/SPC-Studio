using SpcStudio.Model;

namespace SpcStudio.Audio;

// One pair of VOL registers a voice held inside a block, each side signed, and the frame it took them up at.
public readonly record struct SpcTraceLevel(long Frame, VoiceVolume Volume);
