using System.Runtime.InteropServices;

namespace SpcStudio.Audio;

// The output device on every platform: miniaudio through P/Invoke, behind the one interface the app knows a device by.
public sealed class MiniAudioOutput : IAudioOutput
{
    private const string NativeLibraryName = "spcaudio";
    private readonly NativeFramesCallback _framesCallback;
    private IntPtr _deviceHandle;
    private IAudioSource? _source;
    private float[] _samplesBuffer = [];

    // Opens the device and reads back the rate it runs at.
    public MiniAudioOutput()
    {
        _framesCallback = OnFramesRequested;

        _deviceHandle = spc_audio_open(_framesCallback, IntPtr.Zero, out uint sampleRate);
        if (_deviceHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("The audio device could not be opened.");
        }

        SampleRate = (int)sampleRate;
    }

    public int SampleRate { get; }

    // Starts the device pulling from a source.
    public void Start(IAudioSource source)
    {
        _source = source;

        if (spc_audio_start(_deviceHandle) == 0)
        {
            throw new InvalidOperationException("The audio device could not be started.");
        }
    }

    // Stops the device pulling, where it is still open.
    public void Stop()
    {
        if (_deviceHandle != IntPtr.Zero)
        {
            spc_audio_stop(_deviceHandle);
        }
    }

    // Closes the device, after which no callback is in flight.
    public void Dispose()
    {
        if (_deviceHandle == IntPtr.Zero)
        {
            return;
        }

        spc_audio_close(_deviceHandle);
        _deviceHandle = IntPtr.Zero;
        _source = null;
    }

    // Opens the default playback device without starting it and reports its rate, or answers a null handle.
    [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr spc_audio_open(NativeFramesCallback callback, IntPtr user, out uint sampleRate);

    // Starts the device asking for frames, answering non-zero on success.
    [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int spc_audio_start(IntPtr handle);

    // Stops the device asking for frames, answering non-zero on success.
    [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int spc_audio_stop(IntPtr handle);

    // Stops and frees the device, no callback being in flight once it returns.
    [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void spc_audio_close(IntPtr handle);

    // Copies the source's next frames into the device's buffer, leaving it silent on any failure.
    private void OnFramesRequested(IntPtr frames, uint frameCount, IntPtr user)
    {
        try
        {
            if (_source is not { } source)
            {
                return;
            }

            int sampleCount = (int)frameCount * IAudioSource.ChannelsPerFrame;
            if (_samplesBuffer.Length < sampleCount)
            {
                Array.Resize(ref _samplesBuffer, sampleCount);
            }

            source.Read(_samplesBuffer, 0, sampleCount);
            Marshal.Copy(_samplesBuffer, 0, frames, sampleCount);
        }
        catch
        {
        }
    }

    // Fills a buffer of interleaved stereo float frames, silent on entry, on the device's own thread.
    private delegate void NativeFramesCallback(IntPtr frames, uint frameCount, IntPtr user);
}
