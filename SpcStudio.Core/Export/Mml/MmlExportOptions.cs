using SpcStudio.Model;

namespace SpcStudio.Export;

// Which of the MML export's optimizations run, each one making the song smaller without changing what the ROM plays.
public sealed record MmlExportOptions
{
    // Whether repeated phrases are written once and played again as label loops and subloops.
    public bool Loops { get; init; } = true;

    // Whether the commands every note of a run starts with are written once, in a key-on remote block.
    public bool KeyOnRemoteCode { get; init; } = true;

    // Whether held notes are written whole under the legato, a key-on remote block toggling it twice so that each one keys on.
    public bool HeldNotesAsLegato { get; init; } = true;

    // Every optimization on, which is what a new install starts with.
    public static MmlExportOptions All { get; } = new();

    // Every optimization off.
    public static MmlExportOptions None { get; } = new() { Loops = false, KeyOnRemoteCode = false, HeldNotesAsLegato = false };

    // How the export writes a held note under these options.
    public HeldNoteShape HeldNoteShapeWritten()
    {
        return HeldNotesAsLegato ? HeldNoteShape.Legato : HeldNoteShape.OneTickRest;
    }
}
