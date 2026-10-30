using SpcStudio.Model;

namespace SpcStudio.Import;

// What the importer's channel walk gathers.
public static partial class MmlImporter
{
    // Everything the channel walk gathers for the steps after it, across every channel.
    private sealed class WalkedCommands
    {
        public List<(MmlCommand Written, int Channel)> Tempos { get; } = [];
        public List<(MmlCommand Written, int Channel)> MasterVolumes { get; } = [];
        public List<MmlCommand>[] ChannelVolumes { get; } = ListPerChannel<MmlCommand>();
        public List<MmlCommand>[] Pans { get; } = ListPerChannel<MmlCommand>();
        public List<MmlAmplify>[] Amplifies { get; } = ListPerChannel<MmlAmplify>();
        public List<MmlEvent>[] Vibratos { get; } = ListPerChannel<MmlEvent>();
        public List<MmlTremolo>[] Tremolos { get; } = ListPerChannel<MmlTremolo>();
        public List<EnvelopeRegisterWrite>[] EnvelopeWrites { get; } = ListPerChannel<EnvelopeRegisterWrite>();
        public List<(MmlEchoCommand Written, int Channel, int Order)> EchoCommands { get; } = [];
        public List<(int Tick, int Channel, int Order, int Line)> EchoToggles { get; } = [];
        public List<(MmlEchoFade Written, int Channel, int Order)> EchoFades { get; } = [];
        public List<(MmlFirFilter Written, int Channel, int Order)> FirFilters { get; } = [];
        public List<(MmlEchoDelay Written, int Channel, int Order)> EchoDelays { get; } = [];
        public List<(MmlDspWrite Written, int Channel, int Order)> ChipRegisterWrites { get; } = [];
        public List<(int Tick, int Channel, int Order, int Clock)> NoiseClockPushes { get; } = [];
        public List<MmlStaccato> StaccatoToggles { get; } = [];
        public List<(int Tick, int Channel, int Order, bool Smw, int Line)> VelocityTableSwitches { get; } = [];
        public List<(int Channel, MmlIgnored Command)> IgnoredCommands { get; } = [];
        public Dictionary<int, int> LoopTicksByChannel { get; } = [];
        public List<(int Channel, int AtLoop, int? NoteTick, List<(int Tick, int Value)> Changes)> TransposesAcrossLoop { get; } = [];
        public List<(int Tick, CutReason Reason, Func<NoteCut> Cut)>[] CutsInsideNotes { get; } = ListPerChannel<(int Tick, CutReason Reason, Func<NoteCut> Cut)>();
        public Dictionary<(int Channel, int Tick), MmlNote> NotesReadFrom { get; } = [];
        public HashSet<int>[] SilentNoteTicks { get; } = [.. Enumerable.Range(0, Song.DriverChannelCount).Select(_ => new HashSet<int>())];
        public Dictionary<InstrumentBytes, int> NoiseInstruments { get; } = [];
        public Dictionary<(int, InstrumentBytes), int> SampleLoadInstruments { get; } = [];
        public bool HasTempoImmunity { get; set; }
        public int CutShortNoteCount { get; set; }
        public int? LoopTick { get; set; }
        public int? EndTick { get; set; }
    }
}
