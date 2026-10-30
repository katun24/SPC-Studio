namespace SpcStudio.Model;

// Every command byte AddmusicK's driver reads from $DA up, the subcommands of the two commands that pick a routine by their first argument, how many
// bytes each command takes, and how a command is spelled as raw hex.
public static class AmkCommands
{
    public const int First = 0xDA;
    public const int Instrument = 0xDA;
    public const int Pan = 0xDB;
    public const int PanFade = 0xDC;
    public const int PitchBend = 0xDD;
    public const int VibratoOn = 0xDE;
    public const int VibratoOff = 0xDF;
    public const int MasterVolume = 0xE0;
    public const int MasterVolumeFade = 0xE1;
    public const int Tempo = 0xE2;
    public const int TempoFade = 0xE3;
    public const int GlobalTranspose = 0xE4;
    public const int TremoloOn = 0xE5;
    public const int Subloop = 0xE6;
    public const int ChannelVolume = 0xE7;
    public const int ChannelVolumeFade = 0xE8;
    public const int Call = 0xE9;
    public const int VibratoFade = 0xEA;
    public const int PitchEnvelopeRelease = 0xEB;
    public const int PitchEnvelopeAttack = 0xEC;
    public const int Envelope = 0xED;
    public const int FineTune = 0xEE;
    public const int EchoSend = 0xEF;
    public const int EchoOff = 0xF0;
    public const int EchoUnit = 0xF1;
    public const int EchoFade = 0xF2;
    public const int SampleLoad = 0xF3;
    public const int Misc = 0xF4;
    public const int FirFilter = 0xF5;
    public const int DspWrite = 0xF6;
    public const int Noise = 0xF8;
    public const int SendToGame = 0xF9;
    public const int Parameter = 0xFA;
    public const int Arpeggio = 0xFB;
    public const int RemoteCode = 0xFC;
    public const int TremoloOff = 0xFD;
    public const int PitchEnvelopeOff = 0xFE;
    public const int YoshiDrumsChannel5Subcommand = 0x00;
    public const int LegatoSubcommand = 0x01;
    public const int LightStaccatoSubcommand = 0x02;
    public const int EchoToggleSubcommand = 0x03;
    public const int GameSyncCounterSubcommand = 0x05;
    public const int YoshiDrumsThisChannelSubcommand = 0x06;
    public const int TempoImmunitySubcommand = 0x07;
    public const int NspcVelocityTableSubcommand = 0x08;
    public const int InstrumentRestoreSubcommand = 0x09;
    public const int PitchModulationSubcommand = 0x00;
    public const int GainSubcommand = 0x01;
    public const int TransposeSubcommand = 0x02;
    public const int AmplifySubcommand = 0x03;
    public const int EchoDelaySubcommand = 0x04;
    public const int VelocityTableSubcommand = 0x06;
    public const int HotPatchPresetSubcommand = 0x7F;
    public const int HotPatchBitsSubcommand = 0xFE;
    public const int HotPatchContinuationBit = 0x80;

    // The total length of each command from $DA up, the command byte included, zero for a byte the driver has no command for.
    private static readonly byte[] CommandLengths =
    [
        0x02,
        0x02,
        0x03,
        0x04,
        0x04,
        0x01,
        0x02,
        0x03,
        0x02,
        0x03,
        0x02,
        0x04,
        0x02,
        0x02,
        0x03,
        0x04,
        0x02,
        0x04,
        0x04,
        0x03,
        0x02,
        0x04,
        0x01,
        0x04,
        0x04,
        0x03,
        0x02,
        0x09,
        0x03,
        0x04,
        0x02,
        0x03,
        0x03,
        0x03,
        0x05,
        0x01,
        0x01,
        0x00,
    ];

    // The bytes a command takes, the command byte included, as the driver's table gives it for the two whose length their arguments decide.
    public static int LengthOf(int command)
    {
        return CommandLengths[command - First];
    }

    // Bytes written as raw hex the way AddmusicK's text writes them, each a '$' and two capital digits, a space between two.
    public static string HexSpelling(params int[] bytes)
    {
        return string.Join(' ', bytes.Select(writtenByte => $"${writtenByte:X2}"));
    }
}
