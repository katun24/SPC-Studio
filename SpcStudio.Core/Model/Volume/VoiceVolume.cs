namespace SpcStudio.Model;

// The DSP volume registers for one voice, as the driver leaves them.
public readonly record struct VoiceVolume(int Left, int Right);
