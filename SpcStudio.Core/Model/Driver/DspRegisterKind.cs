namespace SpcStudio.Model;

// What a $F6's register is to the model.
public enum DspRegisterKind
{
    Envelope,
    MasterVolume,
    PitchModulation,
    EchoVolumeLeft,
    EchoVolumeRight,
    Flags,
    EchoFeedback,
    EchoCoefficient,
    Status,
    Unused,
    Other,
}
