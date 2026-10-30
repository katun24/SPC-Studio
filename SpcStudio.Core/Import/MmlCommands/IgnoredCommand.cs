namespace SpcStudio.Import;

// The commands an ignored event covers, one per driver command rather than per feature.
public enum IgnoredCommand
{
    YoshiDrumsChannel5,
    YoshiDrumsThisChannel,
    HotPatchPreset,
    HotPatchBits,
    ReadAheadHotPatch,
    ZeroLengthPitchBend,
    ZeroTickFade,
    DspStatusRegister,
    DspUnusedRegister,
    SendToGame,
    GameSyncCounter,
    PitchModulation,
    InertPitchModulation,
    MasterVolume,
    InertMasterVolume,
}
