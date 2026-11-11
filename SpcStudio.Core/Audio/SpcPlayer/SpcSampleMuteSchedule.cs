using SpcStudio.Model;

namespace SpcStudio.Audio;

// When the voices of a traced song are silenced for the samples they play: a voice is silenced from each key-on with a silenced sample until its next
// key-on with one that is heard, each switch falling a lead before its key-on and repeating with the loop as playback goes round it.
public sealed class SpcSampleMuteSchedule
{
    private readonly SpcTrace _trace;
    private readonly List<long> _switchFrames = [];
    private readonly List<int> _silencedVoiceMasks = [];

    // Works out every switch of a trace, a sample number silenced where the test given says so, whether a voice plays it as a sample or as the noise.
    public SpcSampleMuteSchedule(SpcTrace trace, Func<int, bool> isSampleSilenced)
    {
        _trace = trace;
        int silencedVoiceMask = 0;
        foreach (var block in trace.Blocks)
        {
            int voiceBit = SnesDspRegister.VoiceBit(block.Voice);
            int nextMask = isSampleSilenced(block.Srcn) ? silencedVoiceMask | voiceBit : silencedVoiceMask & ~voiceBit;
            if (nextMask == silencedVoiceMask)
            {
                continue;
            }

            silencedVoiceMask = nextMask;
            AddSwitch(block.Start, silencedVoiceMask);
        }
    }

    // The voices silenced at a playback position, a bit per voice.
    public int SilencedVoicesAt(long position)
    {
        int switchIndex = LastSwitchAtOrBefore(_trace.FoldedIntoLoop(position + SpcPlayer.KeyOnLeadFrames));
        return switchIndex < 0 ? 0 : _silencedVoiceMasks[switchIndex];
    }

    // The first playback position after one where the voices silenced can change, or null where they never change again.
    public long? NextChangeAfter(long position)
    {
        long leadPosition = position + SpcPlayer.KeyOnLeadFrames;
        long frame = _trace.FoldedIntoLoop(leadPosition);
        int nextSwitchIndex = LastSwitchAtOrBefore(frame) + 1;
        long? nextLeadPosition = nextSwitchIndex < _switchFrames.Count ? leadPosition + (_switchFrames[nextSwitchIndex] - frame) : null;
        if (_trace.HasLoopToFoldInto)
        {
            // The first lead position that folds back to the loop's start.
            long wrapLeadPosition = leadPosition + (_trace.LengthFrames - frame) + 1;
            nextLeadPosition = Math.Min(nextLeadPosition ?? wrapLeadPosition, wrapLeadPosition);
        }

        return nextLeadPosition - SpcPlayer.KeyOnLeadFrames;
    }

    // Adds a switch at a frame of the trace, or puts the mask in place of the last one where that falls on the same frame.
    private void AddSwitch(long frame, int silencedVoiceMask)
    {
        if (_switchFrames.Count > 0 && _switchFrames[^1] == frame)
        {
            _silencedVoiceMasks[^1] = silencedVoiceMask;
            return;
        }

        _switchFrames.Add(frame);
        _silencedVoiceMasks.Add(silencedVoiceMask);
    }

    // The index of the last switch at or before a frame of the trace, or -1 where there is none.
    private int LastSwitchAtOrBefore(long frame)
    {
        int foundIndex = _switchFrames.BinarySearch(frame);
        return foundIndex >= 0 ? foundIndex : ~foundIndex - 1;
    }
}
