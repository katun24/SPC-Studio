using System.Runtime.InteropServices;

namespace SpcStudio.Audio;

// The playback devices the system offers, the one the app plays through, and every open output, each of which follows a change of it.
public static class OutputDevices
{
    private static readonly List<MiniAudioOutput> OpenOutputs = [];

    // Held across every opening, closing and switching of a device, and every change of the choice.
    internal static readonly object DeviceLock = new();

    // Raised, on no particular thread, as a chosen device that cannot be opened gives way to the system's default.
    public static event Action? ChoiceForgotten;

    // The device the app plays through, by name, or null where it plays through the system's default.
    public static string? ChosenName { get; private set; }

    // Every playback device the system offers, in the system's order, or none where they cannot be read.
    public static IReadOnlyList<OutputDevice> List()
    {
        List<OutputDevice> devices = [];
        NativeDeviceListedCallback addDevice = (deviceName, isSystemDefault, _) => devices.Add(new OutputDevice(deviceName, isSystemDefault != 0));
        spc_audio_list_devices(addDevice, IntPtr.Zero);
        GC.KeepAlive(addDevice);
        return devices;
    }

    // The name of the device among those given that the app plays through: the one chosen, or the system's default where none is.
    public static string? InUseName(IReadOnlyList<OutputDevice> devices)
    {
        string? chosenName = ChosenName;
        return devices.FirstOrDefault(device => chosenName is null ? device.IsSystemDefault : device.Name == chosenName)?.Name;
    }

    // Plays through the device of the name given, or the system's default where it is null, switching every open output to it.
    public static void Choose(string? deviceName)
    {
        lock (DeviceLock)
        {
            if (deviceName == ChosenName)
            {
                return;
            }

            ChosenName = deviceName;
            SwitchEveryOutput();
        }
    }

    // Moves every open output to the system's default where the device chosen is no longer offered.
    internal static void FallBackToSystemDefaultWhereChosenIsMissing()
    {
        lock (DeviceLock)
        {
            if (ChosenName is not { } chosenName || List().Any(device => device.Name == chosenName))
            {
                return;
            }

            ForgetChoice();
            SwitchEveryOutput();
        }
    }

    // Goes back to the system's default without switching any output, saying so.
    internal static void ForgetChoice()
    {
        ChosenName = null;
        ChoiceForgotten?.Invoke();
    }

    // Lets an output follow every change of device from now on.
    internal static void Track(MiniAudioOutput output)
    {
        OpenOutputs.Add(output);
    }

    // Stops an output following changes of device.
    internal static void Untrack(MiniAudioOutput output)
    {
        OpenOutputs.Remove(output);
    }

    // Closes every open output's device and opens the one chosen in its place.
    private static void SwitchEveryOutput()
    {
        foreach (var output in OpenOutputs)
        {
            output.SwitchToChosenDevice();
        }
    }

    // Hands every playback device the system offers to the callback, answering non-zero where they could be read.
    [DllImport(MiniAudioOutput.NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int spc_audio_list_devices(NativeDeviceListedCallback listedCallback, IntPtr user);

    // Takes one playback device: its name and whether the system plays through it by default.
    private delegate void NativeDeviceListedCallback([MarshalAs(UnmanagedType.LPUTF8Str)] string deviceName, int isSystemDefault, IntPtr user);
}
