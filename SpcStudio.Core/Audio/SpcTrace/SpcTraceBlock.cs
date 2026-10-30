using System.Collections.Immutable;
using SpcStudio.Model;

namespace SpcStudio.Audio;

// One key-on in the trace: the voice and the sample it keyed on with, the notes it settled on, where its PITCH, VOL and envelope registers went, the
// stretches its pitch and its level swung in, the frames from its key-on to the last moment anything was heard of it, whether it was still heard well
// after the point where the trace was cut, and whether it keyed on playing the noise rather than its sample.
public sealed record SpcTraceBlock(
    int Voice,
    int Srcn,
    ImmutableArray<SpcTraceNote> Notes,
    ImmutableArray<SpcTracePoint> Path,
    ImmutableArray<SpcTraceSwing> Vibratos,
    ImmutableArray<SpcTraceLevel> Levels,
    ImmutableArray<SpcTraceSwing> Tremolos,
    ImmutableArray<SpcTraceEnvelope> Envelopes,
    long Start,
    long End,
    bool IsHeardPastTraceEnd = false,
    bool IsNoise = false)
{
    // The PITCH register the block keyed on with, as its first note holds it.
    public int KeyOnPitch
    {
        get
        {
            return Notes.IsDefaultOrEmpty ? 1 : Notes[0].Pitch;
        }
    }

    // The envelope registers the block keyed on with, as its first envelope point holds them.
    public EnvelopeState KeyOnEnvelope
    {
        get
        {
            return Envelopes.IsDefaultOrEmpty ? EnvelopeState.Unset : Envelopes[0].State;
        }
    }

    // Whether anything is heard of the block from one frame to another, both included, one frame given twice asking of that frame alone.
    public bool IsSoundingBetween(long fromFrame, long toFrame)
    {
        return Start <= toFrame && fromFrame < End;
    }
}
