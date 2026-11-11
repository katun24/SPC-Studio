namespace SpcStudio.Audio;

// Owns the SPC Player's output device and the emulator feeding it, opened together and closed together.
public sealed class SpcEngine : IDisposable
{
    private readonly IAudioOutput _output;
    private readonly SpcPlayer _player;

    // Starts a device on a whole .spc, left paused at its start, and disposes the device where the emulator will not take the file.
    public SpcEngine(IAudioOutput output, byte[] file)
    {
        _output = output;
        try
        {
            _player = new SpcPlayer(file, output.SampleRate);
        }
        catch
        {
            output.Dispose();
            throw;
        }

        output.Start(_player);
    }

    public bool IsPlaying
    {
        get
        {
            return _player.IsPlaying;
        }
    }

    // Where playback has got to, in the song's own 32 kHz frames.
    public long PositionFrames
    {
        get
        {
            return _player.PositionFrames;
        }
    }

    // The emulator's state right now as a whole .spc, or null where it cannot be taken.
    public byte[]? Snapshot()
    {
        return _player.Snapshot();
    }

    // Puts playback at a frame of the song, leaving it playing or paused as it was.
    public void Seek(long frame)
    {
        _player.Seek(frame);
    }

    // Hands over the states a seek resumes from, which must have come from the dump this was opened with.
    public void SetCheckpoints(SpcCheckpoints? checkpoints)
    {
        _player.SetCheckpoints(checkpoints);
    }

    // Sets the transport bar's three sliders: a gain, a multiple of the song's speed and a shift in semitones.
    public void SetPlayback(double gain, double speed, double semitones)
    {
        _player.SetPlaybackVolume(gain);
        _player.SetPlaybackSpeed(speed);
        _player.SetPlaybackTranspose(semitones);
    }

    // Plays one voice alone from just before a key-on until a later frame, every other voice silent.
    public void PlayVoiceAlone(int voice, long keyOnFrame, long silentFromFrame)
    {
        _player.PlayVoiceAlone(voice, keyOnFrame, silentFromFrame);
    }

    // Silences the voices a mask names, a bit per voice, and sounds the rest.
    public void MuteVoices(int mask)
    {
        _player.MuteVoices(mask);
    }

    // Silences each voice too for as long as it plays a sample the schedule silences, or no voice for its sample where none is given.
    public void SetSampleMuteSchedule(SpcSampleMuteSchedule? schedule)
    {
        _player.SetSampleMuteSchedule(schedule);
    }

    // Starts, or resumes from wherever a pause left it.
    public void Play()
    {
        _player.IsPlaying = true;
    }

    // Stops advancing and leaves the position where it is.
    public void Pause()
    {
        _player.IsPlaying = false;
    }

    // Pauses and goes back to the song's first sample.
    public void Stop()
    {
        _player.IsPlaying = false;
        _player.Restart();
    }

    // Closes the device and then the emulator.
    public void Dispose()
    {
        _output.Dispose();
        _player.Dispose();
    }
}
