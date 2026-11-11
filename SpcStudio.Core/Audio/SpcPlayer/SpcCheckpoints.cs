namespace SpcStudio.Audio;

// The chip states a seek resumes from, taken during the trace's own play-through, and the trace's blocks that say how far back a moment has to be
// resumed from for every voice sounding there to be heard.
public sealed class SpcCheckpoints
{
    public const long SpacingFrames = 4 * (long)DspPoint.SampleRate;
    public const long MaxLookbackFrames = 30 * (long)DspPoint.SampleRate;

    private readonly IReadOnlyList<SpcCheckpoint> _states;
    private readonly IReadOnlyList<SpcTraceBlock> _blocks;

    // Takes the states in increasing frame order and the trace's blocks.
    public SpcCheckpoints(IReadOnlyList<SpcCheckpoint> states, IReadOnlyList<SpcTraceBlock> blocks)
    {
        _states = states;
        _blocks = blocks;
    }

    // The state to load to reach a frame with every voice audible there played from its own key-on, or null where there is none at or before it.
    public SpcCheckpoint? ResumePointFor(long frame)
    {
        long from = EarliestKeyOnSoundingAt(frame);
        from = Math.Max(from, frame - MaxLookbackFrames);
        return LatestAtOrBefore(from);
    }

    // The same checkpoints with only the states up to a frame, read against a given set of blocks.
    public SpcCheckpoints EndingAt(long frame, IReadOnlyList<SpcTraceBlock> blocks)
    {
        return new SpcCheckpoints([.. _states.Where(state => state.Frame <= frame)], blocks);
    }

    // The last state taken at or before a frame, or null where none was.
    public SpcCheckpoint? LatestAtOrBefore(long frame)
    {
        SpcCheckpoint? found = null;
        foreach (var state in _states)
        {
            if (state.Frame > frame)
            {
                break;
            }

            found = state;
        }

        return found;
    }

    // The earliest key-on among the blocks still sounding at a frame, or the frame itself where none started before it.
    private long EarliestKeyOnSoundingAt(long frame)
    {
        long earliest = frame;
        foreach (var block in _blocks)
        {
            if (block.Start < earliest && frame < block.End)
            {
                earliest = block.Start;
            }
        }

        return earliest;
    }
}
