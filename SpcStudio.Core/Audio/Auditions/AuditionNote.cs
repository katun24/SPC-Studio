using SpcStudio.Model;

namespace SpcStudio.Audio;

// Everything one audition keys on with: what the mixer sounds once for a press and again at every repeat of the note panel's loop.
public sealed record AuditionNote(VoiceKeyOn KeyOn, bool IsEchoOn = false, VibratoState Vibrato = default, TremoloState Tremolo = default, AuditionLanes? Lanes = null);
