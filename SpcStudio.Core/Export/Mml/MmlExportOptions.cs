using SpcStudio.Model;

namespace SpcStudio.Export;

// Which of the MML export's optimizations run, each one making the song smaller, and how the file's lines are broken.
public sealed record MmlExportOptions
{
    public const int DefaultLineLengthLimit = 100;
    private const int MinLineLengthLimit = 20;
    private const int MaxLineLengthLimit = 999;
    private const int MinBarsPerLine = 1;
    private const int MaxBarsPerLine = 99;

    public static readonly ValueRange LineLengthLimitRange = new(MinLineLengthLimit, MaxLineLengthLimit);
    public static readonly ValueRange BarsPerLineRange = new(MinBarsPerLine, MaxBarsPerLine);

    private readonly int _lineLengthLimit = DefaultLineLengthLimit;
    private readonly int? _barsPerLine;

    // Whether repeated phrases are written once and played again as label loops and subloops.
    public bool Loops { get; init; } = true;

    // Whether a phrase played again shifted by whole semitones is written as a call of its label under an $FA $02, where that saves bytes.
    public bool TransposedLoops { get; init; } = true;

    // Whether a note or rest too long for one byte is written with a subloop of its one-byte pieces, where that saves bytes.
    public bool LongNotesAndRestsAsLoops { get; init; } = true;

    // Whether the commands every note of a run starts with are written once, in a key-on remote block.
    public bool KeyOnRemoteCode { get; init; } = true;

    // Whether the commands every note of a run writes at one point inside itself are written once, in a timer remote block, the notes written whole.
    public bool RepeatedMidNoteCommandsAsRemoteCode { get; init; } = true;

    // Whether held notes are written whole under the legato, a key-on remote block toggling it twice so that each one keys on.
    public bool HeldNotesAsLegato { get; init; } = true;

    // Whether runs of notes an arpeggio or a trill would play are written as one note under an $FB.
    public bool ArpeggiosAsFb { get; init; } = true;

    // Whether a run of notes whose first bend is the same is written under one pitch envelope rather than with a $DD behind each note, where that saves bytes.
    public bool PitchEnvelopes { get; init; } = true;

    // Whether a change of the channels' echo bits alone is written as an $F4 $03 in each channel whose bit moves rather than as an $EF.
    public bool SingleChannelEchoChangesAsF403 { get; init; } = true;

    // Whether the song's loop marker and end move earlier together where the intro ends as the loop does.
    public bool FuseSongLoopEndWithIntro { get; init; } = true;

    // Whether a song loop that plays one stretch several times over is cut to one repeat.
    public bool ShortenSongLoopToOneRepeat { get; init; } = true;

    // Whether each sample that plays once is written cut off past the furthest point the song can reach in it, beside the whole one.
    public bool TrimOneShotSamples { get; init; }

    // The longest a line of channel data is written, held inside its range.
    public int LineLengthLimit
    {
        get
        {
            return _lineLengthLimit;
        }
        init
        {
            _lineLengthLimit = LineLengthLimitRange.Clamp(value);
        }
    }

    // How many bars one line of a channel's notes holds at most, or null where lines are not broken by bars, a count outside its range reading as null.
    public int? BarsPerLine
    {
        get
        {
            return _barsPerLine;
        }
        init
        {
            _barsPerLine = value is int barCount && BarsPerLineRange.Contains(barCount) ? barCount : null;
        }
    }

    // The optimizations a new install starts with: every one but the sample trim.
    public static MmlExportOptions Default { get; } = new();

    // Every optimization on.
    public static MmlExportOptions All { get; } = new() { TrimOneShotSamples = true };

    // Every optimization off.
    public static MmlExportOptions None { get; } = new()
    {
        Loops = false,
        TransposedLoops = false,
        LongNotesAndRestsAsLoops = false,
        KeyOnRemoteCode = false,
        RepeatedMidNoteCommandsAsRemoteCode = false,
        HeldNotesAsLegato = false,
        ArpeggiosAsFb = false,
        PitchEnvelopes = false,
        SingleChannelEchoChangesAsF403 = false,
        FuseSongLoopEndWithIntro = false,
        ShortenSongLoopToOneRepeat = false,
    };

    // How the export writes a held note under these options.
    public HeldNoteShape HeldNoteShapeWritten()
    {
        return HeldNotesAsLegato ? HeldNoteShape.Legato : HeldNoteShape.OneTickRest;
    }

    // Whether long notes and rests are written as loops: ticked, and repeats written as loops at all.
    public bool AreLongNotesAndRestsWrittenAsLoops()
    {
        return Loops && LongNotesAndRestsAsLoops;
    }
}
