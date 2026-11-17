using System.Runtime.InteropServices;

namespace SpcStudio.Audio;

// The spcplay shim's functions, declared once for every caller.
internal static class SpcNative
{
    private const string Library = "spcplay";

    // Opens a dump held in memory at its first sample, or answers a null handle where the emulator refuses it.
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spc_play_open(byte[] file, int size);

    // Takes the song back to its first sample, answering non-zero on success.
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern int spc_play_restart(IntPtr handle);

    // Loads a state a snapshot took, answering non-zero on success.
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern int spc_play_load_state(IntPtr handle, byte[] state, int size);

    // Plays a count of samples into a buffer as interleaved stereo at the chip's own rate, answering non-zero on success.
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern int spc_play_read(IntPtr handle, short[] outSamples, int count);

    // Plays a count of samples at the song's own speed and throws the audio away, answering non-zero on success.
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern int spc_play_skip(IntPtr handle, int count);

    // Runs the chip's timers at a multiple of their own speed.
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern void spc_play_set_tempo(IntPtr handle, int tempo);

    // Keys off and holds silent the voices a mask names, a bit per voice, and sounds the rest.
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern void spc_play_mute(IntPtr handle, int mask);

    // Whether any voice has keyed on since the last time this was asked.
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern int spc_play_check_kon(IntPtr handle);

    // Writes the emulator's current state out as a whole .spc, answering non-zero on success.
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern int spc_play_snapshot(IntPtr handle, byte[] outFile, int size);

    // How many bytes a snapshot writes.
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern int spc_play_snapshot_size();

    // Frees the emulator and everything it holds.
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern void spc_play_close(IntPtr handle);

    // Opens a dump held in memory at its first sample, and throws where the emulator refuses it.
    public static IntPtr Open(byte[] file)
    {
        IntPtr handle = spc_play_open(file, file.Length);
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("This SPC file could not be played.");
        }

        return handle;
    }
}
