using SpcStudio.Audio;
using SpcStudio.Model;

namespace SpcStudio.Export;

// Renders a song to a 16-bit stereo .wav at the DSP's own rate, through a mixer of its own fed the same events playback is.
public static class WavExporter
{
    private const double TailSeconds = 3.0;

    // Renders the song once from tick 0 to its end, its notes cut where the export under the options cuts them, reporting seconds rendered out of seconds
    // to render, and writes the file only once all of it is rendered, so a cancelled render writes nothing.
    public static void Write(Song song, MmlExportOptions exportOptions, string path, Action<int, int>? progress = null, CancellationToken cancel = default)
    {
        var (points, frameCount) = Render(song, exportOptions, progress, cancel);

        cancel.ThrowIfCancellationRequested();

        WavWriter.Write16Bit(path, points, frameCount * IAudioSource.ChannelsPerFrame, IAudioSource.ChannelsPerFrame);
    }

    // The song's interleaved points with the tail's trailing silence taken off, and how many frames are left.
    private static (short[] Points, int FrameCount) Render(Song song, MmlExportOptions exportOptions, Action<int, int>? progress, CancellationToken cancel)
    {
        var (events, songFrames) = SequenceBuilder.BuildIgnoringMuting(song, exportOptions, song.PlaybackEndTick(0));

        var mixer = new BrrMixer(WavWriter.SampleRate);
        mixer.StartSequence(events, songFrames);

        long tailFrames = (long)(TailSeconds * WavWriter.SampleRate);
        int renderFrames = (int)Math.Min(songFrames + tailFrames, int.MaxValue / IAudioSource.ChannelsPerFrame);
        var points = new short[renderFrames * IAudioSource.ChannelsPerFrame];

        int totalSeconds = Math.Max(1, renderFrames / WavWriter.SampleRate);
        progress?.Invoke(0, totalSeconds);

        mixer.ReadFrames(renderFrames, (chunkLevels, frame, chunkFrames) =>
        {
            for (int index = 0; index < chunkFrames * IAudioSource.ChannelsPerFrame; index++)
            {
                points[frame * IAudioSource.ChannelsPerFrame + index] = WavWriter.ToPoint(chunkLevels[index]);
            }

            progress?.Invoke((int)Math.Min((frame + chunkFrames) / WavWriter.SampleRate, totalSeconds), totalSeconds);
        }, cancel);

        int tailStartPoint = (int)Math.Min(songFrames, renderFrames) * IAudioSource.ChannelsPerFrame;
        int silentEndFrames = DspPoint.TrailingSilentCount(points.AsSpan(tailStartPoint)) / IAudioSource.ChannelsPerFrame;
        return (points, renderFrames - silentEndFrames);
    }
}
