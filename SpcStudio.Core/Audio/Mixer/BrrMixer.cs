using SpcStudio.Model;

namespace SpcStudio.Audio;

// Runs a pool of DSP voices at the DSP's native rate - the song's channels and the auditions above them - with the echo, the noise generator and the sample editor's preview, and resamples what they
// make to the output device's rate. This part holds the voices, the generation loop and the transport bar's speed, transpose and volume.
public sealed partial class BrrMixer : IAudioSource
{
    private const int SequencerVoiceCount = Song.MaxChannelCount;
    private const int AuditionVoiceCount = SnesDspRegister.VoiceCount;
    private const int VoiceCount = SequencerVoiceCount + AuditionVoiceCount;
    private const int InitialSilentSamples = 1;
    private const float UnityGain = 1f;

    private readonly SnesVoice[] _voices = new SnesVoice[VoiceCount];
    private readonly object _lock = new();
    private readonly StereoResampler _resampler;
    private readonly EchoUnit _echo = new();
    private readonly NoiseGenerator _noise = new();
    private int[] _echoSendLeft = [];
    private int[] _echoSendRight = [];
    private short[] _noisePoints = [];
    private long _keyOnCount;
    private double _playbackSpeed = 1;
    private double _pitchScale = 1;
    private float _playbackGain = UnityGain;

    // Opens the mixer at the output device's rate, every voice silent and every faded byte at the driver's own initial value.
    public BrrMixer(int sampleRate)
    {
        SampleRate = sampleRate;
        _resampler = new StereoResampler(DspPoint.SampleRate / sampleRate) { AvailableSamples = InitialSilentSamples };

        for (int i = 0; i < _voices.Length; i++)
        {
            _voices[i] = new SnesVoice();
        }

        ResetFades();
    }

    public int SampleRate { get; }
    public SamplePreview Preview { get; } = new();

    // Sets how fast everything plays - the song and every audition alike - as a multiple of the song's own speed, the pitch left where it is.
    public void SetPlaybackSpeed(double speed)
    {
        lock (_lock)
        {
            _playbackSpeed = speed;
        }
    }

    // Shifts every sampled voice by a number of semitones, fractions included, the speed and the noise generator left alone.
    public void SetPlaybackTranspose(double semitones)
    {
        lock (_lock)
        {
            _pitchScale = SnesPitch.FrequencyRatioForSemitones(semitones);
        }
    }

    // Scales the song and every audition by a gain between silence and one, ahead of the sample editor's preview.
    public void SetPlaybackVolume(double gain)
    {
        lock (_lock)
        {
            _playbackGain = (float)gain;
        }
    }

    // Hands every voice playing one decoded sample the audio that replaced it, taken up at that voice's own next loop wrap with the tuning its instrument
    // sounds at the fraction of its original rate the new audio is at.
    public void ReplaceVoiceAudio(BrrSample from, BrrSample to, double toRateFractionOfOriginal)
    {
        if (ReferenceEquals(from, to))
        {
            return;
        }

        HandEveryVoiceUnderLock(voice => voice.ReplaceAudioAtLoop(from, to, toRateFractionOfOriginal));
    }

    // Puts the tuning an edited instrument sounds with into every voice playing it, the notes already sounding included.
    public void RetuneInstrument(Instrument instrument, double soundingTuning)
    {
        HandEveryVoiceUnderLock(voice => voice.RetuneInstrument(instrument, soundingTuning));
    }

    // Hands every voice to an edit from outside the audio thread, under the mixer's lock.
    private void HandEveryVoiceUnderLock(Action<SnesVoice> edit)
    {
        lock (_lock)
        {
            foreach (var voice in _voices)
            {
                edit(voice);
            }
        }
    }

    // Brings a sounding voice to what its note now says: keyed on afresh a number of samples into the note where the note now sounds another instrument or
    // other audio, and otherwise only what the edit changed about the note written into it.
    private void ReaimVoice(SnesVoice voice, VoiceKeyOn? replacedNote, VoiceKeyOn note, int elapsedSamples)
    {
        if (!voice.IsPlayingSourceOf(note))
        {
            voice.KeyOn(note, ++_keyOnCount, elapsedSamples);
            return;
        }

        ReaimChangedParts(voice, replacedNote, note);
    }

    // Writes into a sounding voice what an edit changed about its note - the pitch with the $DD chain, the velocity and the key-on length - or all three
    // where there was no note before the edit to compare with.
    private static void ReaimChangedParts(SnesVoice voice, VoiceKeyOn? replacedNote, VoiceKeyOn note)
    {
        if (replacedNote is null || replacedNote.Pitch != note.Pitch || !replacedNote.Bends.AsSpan().SequenceEqual(note.Bends.AsSpan()))
        {
            voice.ReaimPitch(note.Pitch, note.Bends);
        }

        if (replacedNote is null || replacedNote.Level.Velocity != note.Level.Velocity)
        {
            voice.SetVelocity(note.Level.Velocity);
        }

        if (replacedNote is null || replacedNote.GateSamples != note.GateSamples)
        {
            voice.SetGate(note.GateSamples);
        }
    }

    // Writes the noise clock a note sets into the noise generator, where the note sets one.
    private void WriteNoiseClock(int? noiseClock)
    {
        if (noiseClock is { } clock)
        {
            _noise.SetClock(clock);
        }
    }

    // Fills interleaved output frames, generating as many of the DSP's samples as the resampler needs for them first.
    public int Read(float[] buffer, int offset, int count)
    {
        int frames = count / IAudioSource.ChannelsPerFrame;
        int required = _resampler.SamplesNeededFor(frames);

        EnsureSourceCapacity(required);
        GenerateSourceSamples(required);
        _resampler.ReadFrames(buffer, offset, frames, UnityGain);

        return count;
    }

    // Tops the resampler's buffers up to a number of samples, in chunks that end on every event, loop end and driver frame they reach.
    private void GenerateSourceSamples(int required)
    {
        int start = _resampler.AvailableSamples;
        int toGenerate = required - start;
        if (toGenerate <= 0)
        {
            return;
        }

        Array.Clear(_resampler.Left, start, toGenerate);
        Array.Clear(_resampler.Right, start, toGenerate);
        Array.Clear(_echoSendLeft, start, toGenerate);
        Array.Clear(_echoSendRight, start, toGenerate);

        lock (_lock)
        {
            int written = 0;
            while (written < toGenerate)
            {
                RunDriverFrameWhereDue();

                int chunk = toGenerate - written;
                if (_isPlaying)
                {
                    FireDueEvents();
                    chunk = (int)Math.Min(chunk, SamplesUntilSequenceBoundary());
                }

                chunk = (int)Math.Min(chunk, SamplesUntilNextDriverFrame());

                MixChunk(start + written, chunk);
                written += chunk;
                AdvanceDriverClock(chunk);

                if (_isPlaying)
                {
                    AdvancePlayPosition(chunk);
                }
            }
        }

        _resampler.AvailableSamples = required;
    }

    // Mixes one chunk: the noise ahead of the voices that read it, the voices, the echo after them, the playback volume over all of it, and the preview.
    private void MixChunk(int start, int count)
    {
        _noise.Fill(_noisePoints, start, count);

        foreach (var voice in _voices)
        {
            if (voice.IsActive)
            {
                voice.Mix(_resampler.Left, _resampler.Right, _echoSendLeft, _echoSendRight, _noisePoints, start, count, _playbackSpeed, _pitchScale);
            }
        }

        _echo.Mix(_resampler.Left, _resampler.Right, _echoSendLeft, _echoSendRight, start, count);
        ApplyPlaybackGain(start, count);
        Preview.MixInto(_resampler.Left, _resampler.Right, start, count);
    }

    // Scales a stretch of the main buses by the playback volume.
    private void ApplyPlaybackGain(int start, int count)
    {
        if (_playbackGain == UnityGain)
        {
            return;
        }

        for (int i = start; i < start + count; i++)
        {
            _resampler.Left[i] *= _playbackGain;
            _resampler.Right[i] *= _playbackGain;
        }
    }

    // How many samples have to be generated to carry a clock a number of the song's samples further on at the playback speed, rounded up.
    private double SamplesToGenerateFor(long songSamples, double carry)
    {
        return Math.Ceiling((songSamples - carry) / _playbackSpeed);
    }

    // How many whole song samples a run of generated ones covers at the playback speed, the part of one left over carried to the next run.
    private long SongSamplesCovered(int generated, ref double carry)
    {
        double covered = generated * _playbackSpeed + carry;
        long whole = (long)covered;
        carry = covered - whole;
        return whole;
    }

    // Grows the resampler's buffers, and the echo sends and the noise beside them, to hold a number of samples.
    private void EnsureSourceCapacity(int samples)
    {
        if (!_resampler.EnsureCapacity(samples))
        {
            return;
        }

        Array.Resize(ref _echoSendLeft, _resampler.Capacity);
        Array.Resize(ref _echoSendRight, _resampler.Capacity);
        Array.Resize(ref _noisePoints, _resampler.Capacity);
    }
}
