using SpcStudio.Audio;

namespace SpcStudio.Export;

// Renders a sample to a 16-bit mono .wav at the DSP's own rate, once from its first point to its last, as the Sample Editor's play button sounds it.
public static class SampleWavExporter
{
    private const int MonoChannelsPerFrame = 1;
    private const int RenderChunkFrames = 4096;

    // Renders a sample and writes it to a file.
    public static void Write(Sample sample, string path)
    {
        var points = Render(sample);
        WavWriter.Write16Bit(path, points, points.Length, MonoChannelsPerFrame);
    }

    // A sample's points stepped through once, at the pitch register the Sample Editor plays it at and through the preview's interpolation, as 16-bit
    // points.
    private static short[] Render(Sample sample)
    {
        var sourcePoints = sample.Decoded.Points;
        var preview = new SamplePreview();
        preview.Start(sourcePoints, start: 0, end: sourcePoints.Length, SamplePreview.NoLoop, sample.PreviewPitchRegister);

        var points = new List<short>();
        var levels = new float[RenderChunkFrames];
        var unusedRightLevels = new float[RenderChunkFrames];
        int chunkFrameCount;
        do
        {
            Array.Clear(levels);
            Array.Clear(unusedRightLevels);
            chunkFrameCount = preview.MixInto(levels, unusedRightLevels, 0, RenderChunkFrames);
            points.AddRange(levels.Take(chunkFrameCount).Select(WavWriter.ToPoint));
        }
        while (chunkFrameCount == RenderChunkFrames);

        return [.. points];
    }
}
