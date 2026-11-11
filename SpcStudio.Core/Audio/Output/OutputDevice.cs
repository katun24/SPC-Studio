namespace SpcStudio.Audio;

// One playback device the system offers: the name it gives it, and whether the system plays through it by default.
public sealed record OutputDevice(string Name, bool IsSystemDefault);
