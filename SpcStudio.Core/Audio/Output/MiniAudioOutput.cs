using System.Runtime.InteropServices;

namespace SpcStudio.Audio;

// The output device on every platform: miniaudio through P/Invoke, behind the one interface the app knows a device by, playing through the device
// the user chose and following each change of it.
public sealed class MiniAudioOutput : IAudioOutput
{
    internal const string NativeLibraryName = "spcaudio";
    private const uint DevicesOwnSampleRate = 0;
    private readonly NativeFramesCallback _framesCallback;
    private readonly NativeStoppedCallback _stoppedCallback;
    private IntPtr _deviceHandle;
    private IAudioSource? _source;
    private bool _isStarted;
    private float[] _samplesBuffer = [];

    // Opens the device chosen, or the system's default where none is chosen or the one chosen cannot be opened, and reads back the rate it runs at.
    public MiniAudioOutput()
    {
        _framesCallback = OnFramesRequested;
        _stoppedCallback = OnDeviceStopped;

        lock (OutputDevices.DeviceLock)
        {
            if (!TryOpenChosenDevice(DevicesOwnSampleRate, out uint sampleRate))
            {
                throw new InvalidOperationException("The audio device could not be opened.");
            }

            SampleRate = (int)sampleRate;
            OutputDevices.Track(this);
        }
    }

    public int SampleRate { get; }

    // Starts the device pulling from a source.
    public void Start(IAudioSource source)
    {
        lock (OutputDevices.DeviceLock)
        {
            _source = source;
            _isStarted = true;

            if (spc_audio_start(_deviceHandle) == 0)
            {
                throw new InvalidOperationException("The audio device could not be started.");
            }
        }
    }

    // Stops the device pulling, where it is still open.
    public void Stop()
    {
        lock (OutputDevices.DeviceLock)
        {
            _isStarted = false;

            if (_deviceHandle != IntPtr.Zero)
            {
                spc_audio_stop(_deviceHandle);
            }
        }
    }

    // Closes the device, after which no callback is in flight and no change of device reaches it.
    public void Dispose()
    {
        lock (OutputDevices.DeviceLock)
        {
            OutputDevices.Untrack(this);
            CloseDevice();
            _source = null;
            _isStarted = false;
        }
    }

    // Closes the device and opens the one chosen in its place at the same rate, pulling again where it was started, or stays silent where none opens.
    internal void SwitchToChosenDevice()
    {
        CloseDevice();

        if (TryOpenChosenDevice((uint)SampleRate, out _) && _isStarted)
        {
            spc_audio_start(_deviceHandle);
        }
    }

    // Opens the device chosen at the rate asked for, forgetting the choice and opening the system's default where the one chosen cannot be opened,
    // answering whether one opened.
    private bool TryOpenChosenDevice(uint sampleRate, out uint openedSampleRate)
    {
        if (TryOpenDevice(OutputDevices.ChosenName, sampleRate, out openedSampleRate))
        {
            return true;
        }

        if (OutputDevices.ChosenName is null)
        {
            return false;
        }

        OutputDevices.ForgetChoice();
        return TryOpenDevice(null, sampleRate, out openedSampleRate);
    }

    // Opens the playback device of the name given, or the system's default where it is null, at the rate asked for, answering whether it opened.
    private bool TryOpenDevice(string? deviceName, uint sampleRate, out uint openedSampleRate)
    {
        _deviceHandle = spc_audio_open(_framesCallback, _stoppedCallback, IntPtr.Zero, deviceName, sampleRate, out openedSampleRate);
        return _deviceHandle != IntPtr.Zero;
    }

    // Closes the device where one is open.
    private void CloseDevice()
    {
        if (_deviceHandle == IntPtr.Zero)
        {
            return;
        }

        spc_audio_close(_deviceHandle);
        _deviceHandle = IntPtr.Zero;
    }

    // Opens the playback device of the name given, or the system's default where it is null, without starting it, and reports its rate, or answers a null handle.
    [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr spc_audio_open(NativeFramesCallback callback, NativeStoppedCallback stoppedCallback, IntPtr user,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? deviceName, uint sampleRate, out uint openedSampleRate);

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

    // Hands a stop of the device to the thread pool, where a chosen device that has gone missing gives way to the system's default.
    private void OnDeviceStopped(IntPtr user)
    {
        try
        {
            ThreadPool.QueueUserWorkItem(_ => OutputDevices.FallBackToSystemDefaultWhereChosenIsMissing());
        }
        catch
        {
        }
    }

    // Fills a buffer of interleaved stereo float frames, silent on entry, on the device's own thread.
    private delegate void NativeFramesCallback(IntPtr frames, uint frameCount, IntPtr user);

    // Reports that the device has stopped, on whichever thread stopped it.
    private delegate void NativeStoppedCallback(IntPtr user);
}
