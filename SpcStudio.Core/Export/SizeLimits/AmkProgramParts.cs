using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using SpcStudio.Model;

namespace SpcStudio.Export;

// What the program an AddmusicK install loads under every local song is made of, in bytes: AddmusicK's own driver, the sound effects and the
// global songs.
public sealed record AmkProgramParts(int AddmusicKBytes, int SoundEffectBytes, int GlobalSongBytes)
{
    private const string DriverAndSoundEffectsPath = "asm/main.bin";
    private const string UserDefinesPath = "asm/UserDefines.asm";

    private static readonly string[] SoundEffectPaths = ["asm/SFX1DF9Table.bin", "asm/SFX1DFCTable.bin", "asm/SFXData.bin"];
    private static readonly Regex NoSoundEffectsSwitchOn = new(@"^\s*!noSFX\s*=\s*!true\b", RegexOptions.Multiline);

    // Reads the parts out of an install's last build, given the whole program's size, or null where one cannot be read.
    public static AmkProgramParts? Read(string amkRoot, int programBytes)
    {
        if (FileBytes(amkRoot, DriverAndSoundEffectsPath) is not int driverAndSoundEffectBytes || SoundEffectBytesOf(amkRoot) is not int soundEffectBytes)
        {
            return null;
        }

        return new AmkProgramParts(driverAndSoundEffectBytes - soundEffectBytes, soundEffectBytes, programBytes - driverAndSoundEffectBytes);
    }

    // What the sound effects take, nothing where the driver is built without them, or null where one of their files cannot be read.
    private static int? SoundEffectBytesOf(string amkRoot)
    {
        if (IsBuiltWithoutSoundEffects(amkRoot))
        {
            return 0;
        }

        int bytes = 0;
        foreach (string path in SoundEffectPaths)
        {
            if (FileBytes(amkRoot, path) is not int fileBytes)
            {
                return null;
            }

            bytes += fileBytes;
        }

        return bytes;
    }

    // Whether the install's build switches leave the sound effects out of the driver.
    private static bool IsBuiltWithoutSoundEffects(string amkRoot)
    {
        return ReadableFiles.BytesOrNull(Path.Combine(amkRoot, UserDefinesPath)) is { } userDefines && NoSoundEffectsSwitchOn.IsMatch(Encoding.UTF8.GetString(userDefines));
    }

    // The size of a file of the install, or null where it cannot be read.
    private static int? FileBytes(string amkRoot, string path)
    {
        return ReadableFiles.BytesOrNull(Path.Combine(amkRoot, path))?.Length;
    }
}
