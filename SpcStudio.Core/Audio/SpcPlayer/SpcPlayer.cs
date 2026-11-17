using SpcStudio.Model;

namespace SpcStudio.Audio;

// An .spc playing: blargg's snes_spc emulating the sound chip from a memory dump, its stereo resampled to the device's rate, with the transport bar's volume, speed and transpose applied on the way.
public sealed class SpcPlayer : IAudioSource, IDisposable
{
    private const int NormalTempo = 0x100;
    private const int NormalTimerDivider = 16;
    private const int MinTimerDivider = NormalTimerDivider / 4;
    internal const long KeyOnLeadFrames = 32;

    private readonly object _lock = new();
    private readonly double _baseStep;
    private readonly StereoResampler _resampler;
    private float _playbackGain = 1;
    private double _playbackSpeed = 1;
    private double _pitchFactor = 1;
    private double _songFramesPerEmulatedFrame = 1;
    private IntPtr _handle;
    private short[] _emulatorPoints = [];
    private double _songFramesEmulated;
    private SpcCheckpoints? _checkpoints;
    private int _mutedVoicesMask;
    private SpcSampleMuteSchedule? _sampleMuteSchedule;

    // The frame every voice falls silent at, set by playing one voice alone and cleared once reached.
    private long? _silentFromFrame;

    // Loads a whole .spc, header included, paused at its first sample, and throws where the emulator will not take it.
    public SpcPlayer(byte[] file, int sampleRate)
    {
        SampleRate = sampleRate;
        _baseStep = DspPoint.SampleRate / sampleRate;
        _resampler = new StereoResampler(_baseStep);
        _handle = SpcNative.Open(file);
    }

    public bool IsPlaying { get; set; }
    public int SampleRate { get; }

    // Where playback has got to, in the chip's own frames: what has been emulated less what is still waiting in the resampler.
    public long PositionFrames
    {
        get
        {
            lock (_lock)
            {
                return Math.Max(0, (long)(_songFramesEmulated - _resampler.AvailableSamples * _songFramesPerEmulatedFrame));
            }
        }
    }

    // Goes back to the song's first sample, by reloading the file.
    public void Restart()
    {
        lock (_lock)
        {
            if (_handle != IntPtr.Zero)
            {
                SpcNative.spc_play_restart(_handle);
            }

            _resampler.Reset();
            _songFramesEmulated = 0;
            ApplyMutes();
        }
    }

    // Puts playback at a frame of the song, by loading the nearest usable checkpoint or the file and emulating the rest of the way at full speed.
    public void Seek(long frame)
    {
        lock (_lock)
        {
            frame = Math.Max(0, frame);
            SeekFrom(frame, _checkpoints?.ResumePointFor(frame));
        }
    }

    // Plays one voice alone from just before a key-on and silences every voice at a later frame, every voice held silent on the way to the key-on.
    public void PlayVoiceAlone(int voice, long keyOnFrame, long silentFromFrame)
    {
        lock (_lock)
        {
            long frame = Math.Max(0, keyOnFrame - KeyOnLeadFrames);
            MuteVoicesLocked(SnesDspRegister.AllVoicesMask);
            SeekFrom(frame, _checkpoints?.LatestAtOrBefore(frame));
            MuteVoicesLocked(SnesDspRegister.AllVoicesMask & ~SnesDspRegister.VoiceBit(voice));
            _silentFromFrame = silentFromFrame;
            IsPlaying = true;
        }
    }

    // Hands over the states a seek resumes from, or null to go back to reloading the file and running.
    public void SetCheckpoints(SpcCheckpoints? checkpoints)
    {
        lock (_lock)
        {
            _checkpoints = checkpoints;
        }
    }

    // Scales what comes out by a gain between silence and one.
    public void SetPlaybackVolume(double gain)
    {
        lock (_lock)
        {
            _playbackGain = (float)gain;
        }
    }

    // Plays the song at a multiple of its own speed with the pitch left where it is, as nearly as the timers' steps allow.
    public void SetPlaybackSpeed(double speed)
    {
        lock (_lock)
        {
            _playbackSpeed = speed;
            ApplyRates();
        }
    }

    // Shifts the pitch by a number of semitones, fractions included, with the speed left where it is.
    public void SetPlaybackTranspose(double semitones)
    {
        lock (_lock)
        {
            _pitchFactor = SnesPitch.FrequencyRatioForSemitones(semitones);
            ApplyRates();
        }
    }

    // Silences the voices a mask names, bit 0 being voice 0, by keying them off, and sounds the rest.
    public void MuteVoices(int mask)
    {
        lock (_lock)
        {
            MuteVoicesLocked(mask);
        }
    }

    // Silences each voice too for as long as it plays a sample the schedule silences, or no voice for its sample where none is given.
    public void SetSampleMuteSchedule(SpcSampleMuteSchedule? schedule)
    {
        lock (_lock)
        {
            _sampleMuteSchedule = schedule;
            ApplyMutes();
        }
    }

    // The emulator's state right now as a whole .spc with a minimal header, or null once disposed or where the emulator refuses.
    public byte[]? Snapshot()
    {
        lock (_lock)
        {
            if (_handle == IntPtr.Zero)
            {
                return null;
            }

            byte[] file = new byte[SpcNative.spc_play_snapshot_size()];
            return SpcNative.spc_play_snapshot(_handle, file, file.Length) != 0 ? file : null;
        }
    }

    // Fills a run of the buffer with the emulator's next frames at the device's rate, or with silence while paused or closed.
    public int Read(float[] buffer, int offset, int count)
    {
        lock (_lock)
        {
            if (!IsPlaying || _handle == IntPtr.Zero)
            {
                Array.Clear(buffer, offset, count);
                return count;
            }

            int frames = count / IAudioSource.ChannelsPerFrame;
            Fill(_resampler.SamplesNeededFor(frames));
            _resampler.ReadFrames(buffer, offset, frames, _playbackGain);
            return count;
        }
    }

    // Closes the emulator, leaving any later read silent.
    public void Dispose()
    {
        lock (_lock)
        {
            if (_handle == IntPtr.Zero)
            {
                return;
            }

            SpcNative.spc_play_close(_handle);
            _handle = IntPtr.Zero;
            IsPlaying = false;
        }
    }

    // How fast the library really runs the timers for a tempo, as a multiple of their normal speed.
    private static double TimerSpeedFor(int tempo)
    {
        int divider = Math.Max(MinTimerDivider, (NormalTimerDivider * NormalTempo + (tempo >> 1)) / tempo);
        return (double)NormalTimerDivider / divider;
    }

    // Puts the emulator at a frame by loading a checkpoint, or the file where there is none, and emulating the rest of the way at full speed.
    private void SeekFrom(long frame, SpcCheckpoint? resume)
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        _resampler.Reset();
        _songFramesEmulated = frame;
        ApplyMutes();
        long from = LoadStateToResumeFrom(resume);
        if (frame > from)
        {
            SpcNative.spc_play_skip(_handle, checked((int)((frame - from) * IAudioSource.ChannelsPerFrame)));
        }
    }

    // Keys off and holds silent the voices a mask names, beside those silenced for their samples, for a caller already holding the lock.
    private void MuteVoicesLocked(int mask)
    {
        _mutedVoicesMask = mask;
        ApplyMutes();
    }

    // Keys off and holds silent the voices muted and those silenced for the samples they play where the emulator has got to.
    private void ApplyMutes()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        int silencedForSamples = _sampleMuteSchedule?.SilencedVoicesAt((long)_songFramesEmulated) ?? 0;
        SpcNative.spc_play_mute(_handle, _mutedVoicesMask | silencedForSamples);
    }

    // Loads a checkpoint, or reloads the file where there is none or the emulator refuses it, and answers the frame the emulator now stands at.
    private long LoadStateToResumeFrom(SpcCheckpoint? resume)
    {
        if (resume is not null && SpcNative.spc_play_load_state(_handle, resume.State, resume.State.Length) != 0)
        {
            return resume.Frame;
        }

        SpcNative.spc_play_restart(_handle);
        return 0;
    }

    // Runs the chip at the transpose's pitch factor and its timers at what is left of the speed once that factor is taken out.
    private void ApplyRates()
    {
        _resampler.Step = _baseStep * _pitchFactor;
        int tempo = Math.Max(1, (int)Math.Round(_playbackSpeed / _pitchFactor * NormalTempo));
        _songFramesPerEmulatedFrame = TimerSpeedFor(tempo);
        if (_handle != IntPtr.Zero)
        {
            SpcNative.spc_play_set_tempo(_handle, tempo);
        }
    }

    // Tops the resampler's buffers up to a number of frames from the emulator, switching the voices' mutes on the way at every frame they change at.
    private void Fill(int required)
    {
        _resampler.EnsureCapacity(required);
        int filled = _resampler.AvailableSamples;
        while (filled < required)
        {
            int framesBeforeChange = EmulatedFramesBeforeMuteChange(required - filled);
            EmulateInto(filled, framesBeforeChange);
            filled += framesBeforeChange;
            if (filled < required)
            {
                SwitchMutesReached();
            }
        }

        _resampler.AvailableSamples = filled;
    }

    // How many of a run of emulated frames come before the next frame the voices' mutes change at, the whole run where none is ahead or it lies beyond.
    private int EmulatedFramesBeforeMuteChange(int frames)
    {
        if (NextMuteChangeFrame() is not long changeFrame)
        {
            return frames;
        }

        double emulatedFramesLeft = Math.Ceiling((changeFrame - _songFramesEmulated) / _songFramesPerEmulatedFrame);
        return (int)Math.Clamp(emulatedFramesLeft, 0, frames);
    }

    // The next song frame the voices' mutes change at - the frame every voice falls silent at or the sample mutes' next change, whichever is sooner -
    // or null where neither is ahead.
    private long? NextMuteChangeFrame()
    {
        long? sampleMuteChange = _sampleMuteSchedule?.NextChangeAfter((long)_songFramesEmulated);
        if (_silentFromFrame is not long silentFromFrame)
        {
            return sampleMuteChange;
        }

        return Math.Min(silentFromFrame, sampleMuteChange ?? silentFromFrame);
    }

    // Puts the voices' mutes as they stand where the emulator has got to, every voice muted once the frame they fall silent at is reached.
    private void SwitchMutesReached()
    {
        if (_silentFromFrame is long silentFromFrame && silentFromFrame <= _songFramesEmulated)
        {
            _silentFromFrame = null;
            MuteVoicesLocked(SnesDspRegister.AllVoicesMask);
            return;
        }

        ApplyMutes();
    }

    // Emulates a number of frames into the resampler's buffers from a sample on, silence where the emulator refuses the read.
    private void EmulateInto(int start, int frames)
    {
        if (frames <= 0)
        {
            return;
        }

        int count = frames * IAudioSource.ChannelsPerFrame;
        if (_emulatorPoints.Length < count)
        {
            Array.Resize(ref _emulatorPoints, count);
        }

        _songFramesEmulated += frames * _songFramesPerEmulatedFrame;
        bool isRead = SpcNative.spc_play_read(_handle, _emulatorPoints, count) != 0;
        for (int frame = 0; frame < frames; frame++)
        {
            int at = start + frame;
            _resampler.Left[at] = isRead ? DspPoint.ToLevel(_emulatorPoints[frame * IAudioSource.ChannelsPerFrame]) : 0;
            _resampler.Right[at] = isRead ? DspPoint.ToLevel(_emulatorPoints[frame * IAudioSource.ChannelsPerFrame + 1]) : 0;
        }
    }
}
