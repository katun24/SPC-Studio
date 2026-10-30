using System.Collections.Immutable;
using SpcStudio.Import;
using SpcStudio.Model;

namespace SpcStudio.Audio;

// What an .spc plays, found by playing it on an emulator of its own: every key-on with the sample, envelope, pitch and volume behind it, the chip's
// echo and master volumes, the chip states a seek resumes from, and where the song loops.
public sealed class SpcTrace
{
    public const int MaxPassCount = 2;
    public const int MaxCaptureMinutes = 30;
    internal const int PollFrames = 256;
    private const long ReadStepFrames = 180 * (long)DspPoint.SampleRate;
    private const long MaxCaptureFrames = MaxCaptureMinutes * 60 * (long)DspPoint.SampleRate;
    private const long StillPlayingFrames = 10 * (long)DspPoint.SampleRate;
    private const long MergeFrames = 2;
    private const long KonLookBackFrames = 8;
    private const long MinLoopFrames = 2 * (long)DspPoint.SampleRate;
    private const int MinLoopEvents = 64;
    private const long TailFrames = (long)DspPoint.SampleRate;
    private const long MinHeardPastCutFrames = 2 * PollFrames;

    public required IReadOnlyList<SpcTraceBlock> Blocks { get; init; }
    public required IReadOnlyList<SpcTraceEcho> EchoPath { get; init; }
    public required IReadOnlyList<SpcTraceMasterVolume> MasterVolumePath { get; init; }
    public required SpcCheckpoints Checkpoints { get; init; }

    // How long the canvas is: the intro plus one loop where a loop was found, and wherever the capture stopped otherwise.
    public required long LengthFrames { get; init; }

    // The frame the song goes back to, or null where no loop was found.
    public required long? LoopFrame { get; init; }

    // The sample directory entries a voice was actually heard playing.
    public required IReadOnlyList<int> HeardSrcns { get; init; }

    // The sample directory entries a voice was heard playing as a sample rather than as the noise.
    public IReadOnlyList<int> SampleSrcns
    {
        get
        {
            return SrcnsOf(Blocks.Where(block => !block.IsNoise));
        }
    }

    // The AddmusicK .brr bytes of each heard sample number, read off the chip the first time a voice keyed on with it, a number whose entry did not read as a sample then left out.
    public required IReadOnlyDictionary<int, byte[]> BrrFileBytesBySrcn { get; init; }

    // Whether the capture's limit was reached with no loop found and the song still playing.
    public required bool HasNoLoopOrEnd { get; init; }

    // How long the canvas is, in seconds.
    public double LengthSeconds
    {
        get
        {
            return LengthFrames / DspPoint.SampleRate;
        }
    }

    // Whether playback past the end goes back into a loop rather than staying at the end.
    public bool HasLoopToFoldInto
    {
        get
        {
            return LoopFrame is long loopFrame && loopFrame < LengthFrames;
        }
    }

    // Plays a whole .spc until it has found the loop, gone quiet or reached the capture's limit, and writes down what it heard, cut at the loop where
    // there is one, reading it a second time as far as the first where the first could not say which voices a key-on started.
    public static SpcTrace Run(byte[] file, Action<string, int, int>? progress = null, CancellationToken cancel = default)
    {
        IntPtr handle = SpcNative.Open(file);
        try
        {
            var pass = new Pass(handle, []);
            var trace = pass.Record("seconds read", MaxCaptureFrames, progress, cancel);
            if (!pass.HasMissedKeyOn || SpcNative.spc_play_restart(handle) == 0)
            {
                return trace;
            }

            return new Pass(handle, pass.KeyOnFrames).Record("seconds read again", pass.FramesRead, progress, cancel);
        }
        finally
        {
            SpcNative.spc_play_close(handle);
        }
    }

    // The block a voice last keyed on at or before a frame, or null where it had not keyed on yet.
    public SpcTraceBlock? LastBlockKeyedOn(int voice, long frame)
    {
        SpcTraceBlock? lastBlock = null;
        foreach (var block in Blocks)
        {
            if (block.Start > frame)
            {
                break;
            }

            if (block.Voice == voice)
            {
                lastBlock = block;
            }
        }

        return lastBlock;
    }

    // The frame of the trace a playback position plays: past the end folded back into the loop, or held at the end where there is none.
    public long FoldedIntoLoop(long position)
    {
        if (position <= LengthFrames)
        {
            return position;
        }

        if (!HasLoopToFoldInto)
        {
            return LengthFrames;
        }

        long loopFrame = LoopFrame.GetValueOrDefault();
        return loopFrame + (position - loopFrame) % (LengthFrames - loopFrame);
    }

    // The echo as it stood at a frame, the path's first point standing for anything before it.
    public SpcTraceEcho EchoInForceAt(long frame)
    {
        return InForceAt(EchoPath, frame, echo => echo.Frame);
    }

    // The master volumes as they stood at a frame, the path's first point standing for anything before it.
    public SpcTraceMasterVolume MasterVolumeInForceAt(long frame)
    {
        return InForceAt(MasterVolumePath, frame, masterVolume => masterVolume.Frame);
    }

    // The same trace with every block moved onto the sample number its own is merged into, and only the numbers merged into still heard.
    public SpcTrace WithSrcnsMerged(IReadOnlyDictionary<int, int> mergedSrcnOf)
    {
        return new SpcTrace
        {
            Blocks = [.. Blocks.Select(block => block with { Srcn = mergedSrcnOf[block.Srcn] })],
            EchoPath = EchoPath,
            MasterVolumePath = MasterVolumePath,
            Checkpoints = Checkpoints,
            LengthFrames = LengthFrames,
            LoopFrame = LoopFrame,
            HeardSrcns = [.. HeardSrcns.Where(srcn => mergedSrcnOf[srcn] == srcn)],
            BrrFileBytesBySrcn = BrrFileBytesBySrcn,
            HasNoLoopOrEnd = HasNoLoopOrEnd,
        };
    }

    // The same trace ending at a frame with no loop where the frame falls before its end, and the trace itself where it does not.
    public SpcTrace EndingAt(long frame, CancellationToken cancel)
    {
        if (frame >= LengthFrames)
        {
            return this;
        }

        return CutTrace([.. Blocks], EchoPath, MasterVolumePath, Checkpoints, BrrFileBytesBySrcn, frame, loop: null, hasNoLoopOrEnd: false, cancel);
    }

    // A trace of blocks, paths, checkpoints and samples cut at a length, its swings inferred again, the samples heard being those of the blocks left.
    private static SpcTrace CutTrace(List<SpcTraceBlock> blocks, IReadOnlyList<SpcTraceEcho> echoPath, IReadOnlyList<SpcTraceMasterVolume> masterVolumePath,
        SpcCheckpoints checkpoints, IReadOnlyDictionary<int, byte[]> brrFileBytesBySrcn, long length, long? loop, bool hasNoLoopOrEnd, CancellationToken cancel)
    {
        var withVibratos = SpcSwingInference.WithInferredSwings(CutAt(blocks, length), SpcPitchSwings.VibratoKind, cancel);
        var kept = SpcSwingInference.WithInferredSwings(withVibratos, SpcLevelSwings.TremoloKind, cancel);
        var heardSrcns = SrcnsOf(kept);
        return new SpcTrace
        {
            Blocks = kept,
            LengthFrames = Math.Max(length, 1),
            LoopFrame = loop,
            HeardSrcns = heardSrcns,
            BrrFileBytesBySrcn = brrFileBytesBySrcn.Where(sample => heardSrcns.Contains(sample.Key)).ToDictionary(),
            EchoPath = Trimmed(echoPath, length, echo => echo.Frame),
            MasterVolumePath = Trimmed(masterVolumePath, length, masterVolume => masterVolume.Frame),
            Checkpoints = checkpoints.EndingAt(length, kept),
            HasNoLoopOrEnd = hasNoLoopOrEnd,
        };
    }

    // The sample directory entries some blocks play, each once and in order.
    private static List<int> SrcnsOf(IEnumerable<SpcTraceBlock> blocks)
    {
        return [.. blocks.Select(block => block.Srcn).Distinct().Order()];
    }

    // Where the song ends and where it goes back to: the longest tail of key-ons that only repeats, or the capture's own end and no loop.
    private static (long Length, long? Loop) FindLoop(List<long> events, List<ulong> signatures, long captured)
    {
        int count = events.Count;
        int bestAt = count;
        int bestPeriod = 0;
        for (int period = 1; period <= count / 2; period++)
        {
            int at = count - period;
            while (at > 0 && signatures[at - 1] == signatures[at - 1 + period])
            {
                at--;
            }

            int tail = count - at;
            if (tail < 2 * period || tail < MinLoopEvents || events[at + period] - events[at] < MinLoopFrames || at >= bestAt)
            {
                continue;
            }

            bestAt = at;
            bestPeriod = period;
        }

        if (bestPeriod > 0)
        {
            return (events[bestAt + bestPeriod], events[bestAt]);
        }

        return (captured, null);
    }

    // The blocks that start before a length, any still sounding there ending there with their notes, pitch path and levels cut with them, their
    // vibratos and tremolos read again off the paths that are left, and whether they were still heard well past it.
    private static List<SpcTraceBlock> CutAt(List<SpcTraceBlock> blocks, long length)
    {
        var kept = new List<SpcTraceBlock>(blocks.Count);
        foreach (var block in blocks)
        {
            if (block.Start >= length)
            {
                continue;
            }

            if (block.End <= length)
            {
                kept.Add(block);
                continue;
            }

            var notes = block.Notes.Where(note => note.Start < length).Select(note => note.End > length ? note with { End = length } : note);
            var cut = PathsCutAt(block, length);
            kept.Add(WithSwingsRead(cut with { Notes = [.. notes], IsHeardPastTraceEnd = block.End >= length + MinHeardPastCutFrames }));
        }

        return kept;
    }

    // A block with its vibratos and tremolos read off its pitch and level paths up to its end.
    private static SpcTraceBlock WithSwingsRead(SpcTraceBlock block)
    {
        return block with { Vibratos = SpcPitchSwings.VibratosOf(block.Path, block.End), Tremolos = SpcLevelSwings.TremolosOf(block.Levels, block.End) };
    }

    // A block ending at a frame, with its pitch, level and envelope paths cut there.
    private static SpcTraceBlock PathsCutAt(SpcTraceBlock block, long end)
    {
        return block with
        {
            End = end,
            Path = Trimmed(block.Path, end, point => point.Frame),
            Levels = Trimmed(block.Levels, end, level => level.Frame),
            Envelopes = Trimmed(block.Envelopes, end, envelope => envelope.Frame),
        };
    }

    // The points of a path in front of an end, its first point always kept.
    private static ImmutableArray<T> Trimmed<T>(IReadOnlyList<T> path, long end, Func<T, long> frameOf)
    {
        var kept = ImmutableArray.CreateBuilder<T>();
        foreach (var point in path)
        {
            if (kept.Count > 0 && frameOf(point) >= end)
            {
                break;
            }

            kept.Add(point);
        }

        return kept.ToImmutable();
    }

    // The last point of a path at or before a frame, or its first where none is.
    private static T InForceAt<T>(IReadOnlyList<T> path, long frame, Func<T, long> frameOf)
    {
        var pointInForce = path[0];
        foreach (var point in path)
        {
            if (frameOf(point) > frame)
            {
                break;
            }

            pointInForce = point;
        }

        return pointInForce;
    }

    // Adds a point to the end of a path where what it holds moved from the path's last point, or where the path is empty.
    internal static void AddWhereMoved<TPoint, TValue>(List<TPoint> path, TPoint point, Func<TPoint, TValue> valueOf)
    {
        if (path.Count > 0 && EqualityComparer<TValue>.Default.Equals(valueOf(path[^1]), valueOf(point)))
        {
            return;
        }

        path.Add(point);
    }

    // A key-on as one number: which voices started, and the sample each of them started playing.
    private static ulong SignatureOf(int kon, ReadOnlySpan<byte> registers)
    {
        const ulong Offset = 14695981039346656037;
        const ulong Prime = 1099511628211;
        ulong hash = (Offset ^ (byte)kon) * Prime;
        for (int voice = 0; voice < SnesDspRegister.VoiceCount; voice++)
        {
            if (SnesDspRegister.HasVoiceBit(kon, voice))
            {
                hash = (hash ^ (ulong)SnesDspRegister.SourceOf(registers, voice)) * Prime;
            }
        }

        return hash;
    }

    // One play-through of the song: what each voice is sounding and has sounded, the key-ons and the echo heard so far, and the checkpoints taken.
    private sealed class Pass(IntPtr handle, IReadOnlyList<long> knownKeyOns)
    {
        private const int NoOpenBlock = -1;

        private readonly List<SpcTraceBlock> _blocks = [];
        private readonly long[] _lastHeard = new long[SnesDspRegister.VoiceCount];
        private readonly List<SpcTracePoint>[] _pitchPaths = [.. Enumerable.Range(0, SnesDspRegister.VoiceCount).Select(_ => new List<SpcTracePoint>())];
        private readonly List<SpcTraceLevel>[] _levelPaths = [.. Enumerable.Range(0, SnesDspRegister.VoiceCount).Select(_ => new List<SpcTraceLevel>())];
        private readonly List<SpcTraceEnvelope>[] _envelopePaths = [.. Enumerable.Range(0, SnesDspRegister.VoiceCount).Select(_ => new List<SpcTraceEnvelope>())];
        private readonly List<long> _events = [];
        private readonly List<ulong> _signatures = [];
        private readonly List<SpcTraceEcho> _echoPath = [];
        private readonly List<SpcTraceMasterVolume> _masterVolumePath = [];
        private readonly List<SpcCheckpoint> _states = [];
        private readonly byte[] _snapshot = new byte[SpcNative.spc_play_snapshot_size()];
        private readonly Dictionary<int, byte[]> _brrFileBytesBySrcn = [];
        private readonly HashSet<int> _samplesReadSrcns = [];
        private long _nextStateFrame;
        private int _nextKnownKeyOn;
        private int _konLast = -1;
        private int _konWritten;

        // The index of the block each voice is sounding, or none where it sounds none.
        private readonly int[] _openBlock = [.. Enumerable.Repeat(NoOpenBlock, SnesDspRegister.VoiceCount)];

        public List<long> KeyOnFrames { get; } = [];
        public bool HasMissedKeyOn { get; private set; }
        public long FramesRead { get; private set; }

        // Plays the song a frame at a time up to a limit, stopping at the end of a step where what was read already ends, reporting each second read,
        // and answers the trace it heard.
        public SpcTrace Record(string stage, long maxFrames, Action<string, int, int>? progress, CancellationToken cancel)
        {
            int seconds = (int)(maxFrames / DspPoint.SampleRate);
            int saidSecond = -1;
            progress?.Invoke(stage, 0, seconds);
            long frame = 0;
            for (; frame < maxFrames; frame++)
            {
                if (frame > 0 && frame % ReadStepFrames == 0 && HasHeardEnough(frame))
                {
                    break;
                }

                if (SpcNative.spc_play_skip(handle, IAudioSource.ChannelsPerFrame) == 0)
                {
                    break;
                }

                if (frame % PollFrames == 0)
                {
                    cancel.ThrowIfCancellationRequested();
                    int second = (int)(frame / DspPoint.SampleRate);
                    if (second != saidSecond)
                    {
                        progress?.Invoke(stage, saidSecond = second, seconds);
                    }
                }

                if (!ObserveFrame(frame))
                {
                    break;
                }
            }

            for (int voice = 0; voice < SnesDspRegister.VoiceCount; voice++)
            {
                CloseBlock(voice, frame);
            }

            FramesRead = frame;
            return Finish(frame, cancel);
        }

        // Whether what was read up to a frame already ends: no voice has keyed on for a while, or a loop was found in it.
        private bool HasHeardEnough(long frame)
        {
            if (!IsStillPlayingAt(frame))
            {
                return true;
            }

            return FindLoop(_events, _signatures, frame).Loop is not null;
        }

        // Whether a voice keyed on shortly before a frame.
        private bool IsStillPlayingAt(long frame)
        {
            return _events.Count > 0 && frame - _events[^1] <= StillPlayingFrames;
        }

        // Reads the chip at a frame where a voice keyed on, a poll falls, or a known key-on is close ahead, and answers false where it cannot be read.
        private bool ObserveFrame(long frame)
        {
            while (_nextKnownKeyOn < knownKeyOns.Count && knownKeyOns[_nextKnownKeyOn] < frame)
            {
                _nextKnownKeyOn++;
            }

            bool isWatchingKon = _nextKnownKeyOn < knownKeyOns.Count && knownKeyOns[_nextKnownKeyOn] - frame <= KonLookBackFrames;
            bool isKeyedOn = SpcNative.spc_play_check_kon(handle) != 0;
            bool isObserved = isKeyedOn || frame % PollFrames == 0;
            if (!isObserved && !isWatchingKon)
            {
                return true;
            }

            if (SpcNative.spc_play_snapshot(handle, _snapshot, _snapshot.Length) == 0)
            {
                return false;
            }

            var registers = SpcReader.DspRegistersOf(_snapshot);
            int kon = registers[SnesDspRegister.KeyOn];
            if (kon != _konLast && kon != 0)
            {
                _konWritten = kon;
            }

            _konLast = kon;
            if (!isObserved)
            {
                return true;
            }

            RecordEcho(registers, frame);
            RecordMasterVolume(registers, frame);
            KeepCheckpoint(frame);
            RecordOpenVoices(registers, frame);
            if (isKeyedOn)
            {
                RecordKeyOn(registers, kon, frame);
            }

            return true;
        }

        // Adds the echo at a frame to its path where anything about it moved.
        private void RecordEcho(ReadOnlySpan<byte> registers, long frame)
        {
            AddWhereMoved(_echoPath, SpcTraceEcho.Of(registers, frame), echo => echo.WithoutFrame);
        }

        // Adds the master volumes at a frame to their path where either side moved.
        private void RecordMasterVolume(ReadOnlySpan<byte> registers, long frame)
        {
            AddWhereMoved(_masterVolumePath, SpcTraceMasterVolume.Of(registers, frame), point => (point.Left, point.Right));
        }

        // Keeps the snapshot as a checkpoint where the last one kept is a spacing behind.
        private void KeepCheckpoint(long frame)
        {
            if (frame < _nextStateFrame)
            {
                return;
            }

            _states.Add(new SpcCheckpoint(frame, (byte[])_snapshot.Clone()));
            _nextStateFrame = frame + SpcCheckpoints.SpacingFrames;
        }

        // Notes which open voices are heard at a frame, and where each one's pitch, volume and envelope moved to.
        private void RecordOpenVoices(ReadOnlySpan<byte> registers, long frame)
        {
            for (int voice = 0; voice < SnesDspRegister.VoiceCount; voice++)
            {
                if (_openBlock[voice] == NoOpenBlock)
                {
                    continue;
                }

                if (SnesDspRegister.EnvelopeLevelOf(registers, voice) != 0)
                {
                    _lastHeard[voice] = frame;
                }

                AddWhereMoved(_pitchPaths[voice], new SpcTracePoint(frame, SnesDspRegister.PitchOf(registers, voice)), point => point.Value);
                AddWhereMoved(_levelPaths[voice], new SpcTraceLevel(frame, SnesDspRegister.VolumeOf(registers, voice)), level => level.Volume);
                AddWhereMoved(_envelopePaths[voice], new SpcTraceEnvelope(frame, SnesDspRegister.EnvelopeOf(registers, voice)), envelope => envelope.State);
            }
        }

        // Records a key-on the DSP flagged at a frame, starting blocks on the voices it names unless it repeats the last one or names none.
        private void RecordKeyOn(ReadOnlySpan<byte> registers, int kon, long frame)
        {
            KeyOnFrames.Add(frame);
            int started = _konWritten != 0 ? _konWritten : kon;
            _konWritten = 0;
            if (started == 0)
            {
                HasMissedKeyOn = true;
                return;
            }

            ulong signature = SignatureOf(started, registers);
            if (_events.Count > 0 && _signatures[^1] == signature && frame - _events[^1] <= MergeFrames)
            {
                return;
            }

            _events.Add(frame);
            _signatures.Add(signature);
            StartBlocks(registers, started, frame);
        }

        // Starts a block on each voice a KON value names, ending whatever that voice was already sounding.
        private void StartBlocks(ReadOnlySpan<byte> registers, int kon, long frame)
        {
            for (int voice = 0; voice < SnesDspRegister.VoiceCount; voice++)
            {
                if (!SnesDspRegister.HasVoiceBit(kon, voice))
                {
                    continue;
                }

                CloseBlock(voice, frame);
                int srcn = SnesDspRegister.SourceOf(registers, voice);
                ReadSampleFirstHeard(registers, srcn);
                int pitch = SnesDspRegister.PitchOf(registers, voice);
                _openBlock[voice] = _blocks.Count;
                _lastHeard[voice] = frame;
                _pitchPaths[voice] = [new SpcTracePoint(frame, pitch)];
                _levelPaths[voice] = [new SpcTraceLevel(frame, SnesDspRegister.VolumeOf(registers, voice))];
                _envelopePaths[voice] = [new SpcTraceEnvelope(frame, SnesDspRegister.EnvelopeOf(registers, voice))];
                _blocks.Add(new SpcTraceBlock(voice, srcn, [new SpcTraceNote(pitch, frame, frame + PollFrames)],
                    [.. _pitchPaths[voice]], [], [.. _levelPaths[voice]], [], [.. _envelopePaths[voice]], frame, frame + PollFrames,
                    IsNoise: SnesDspRegister.IsPlayingNoise(registers, voice)));
            }
        }

        // Reads a sample number's sample off the chip as it stands where a voice keys on with it for the first time.
        private void ReadSampleFirstHeard(ReadOnlySpan<byte> registers, int srcn)
        {
            if (!_samplesReadSrcns.Add(srcn))
            {
                return;
            }

            var aram = SpcReader.AramOf(_snapshot);
            if (SpcSampleDirectory.EntryOf(aram, registers, srcn) is { } entry)
            {
                _brrFileBytesBySrcn[srcn] = SpcExtract.BrrFileBytes(aram, entry);
            }
        }

        // Ends a voice's open block, where it has one, at the last frame anything was heard of it, or at a limit where that is sooner.
        private void CloseBlock(int voice, long limit)
        {
            if (_openBlock[voice] == NoOpenBlock)
            {
                return;
            }

            long end = Math.Min(_lastHeard[voice] + PollFrames, limit);
            var heard = _blocks[_openBlock[voice]] with { Path = [.. _pitchPaths[voice]], Levels = [.. _levelPaths[voice]], Envelopes = [.. _envelopePaths[voice]] };
            var closed = WithSwingsRead(PathsCutAt(heard, end));
            _blocks[_openBlock[voice]] = closed with { Notes = SpcPitchPath.NotesOf(closed.Path, end, closed.Vibratos) };
            _openBlock[voice] = NoOpenBlock;
        }

        // The trace this pass heard up to a frame, cut at the loop where one was found and shortly after the last thing heard where none was.
        private SpcTrace Finish(long frame, CancellationToken cancel)
        {
            (long length, long? loop) = FindLoop(_events, _signatures, frame);
            if (loop is null && _blocks.Count > 0)
            {
                length = Math.Min(length, _blocks.Max(block => block.End) + TailFrames);
            }

            bool hasNoLoopOrEnd = loop is null && frame >= MaxCaptureFrames && IsStillPlayingAt(frame);
            return CutTrace(_blocks, _echoPath, _masterVolumePath, new SpcCheckpoints(_states, _blocks), _brrFileBytesBySrcn, length, loop, hasNoLoopOrEnd, cancel);
        }
    }
}
