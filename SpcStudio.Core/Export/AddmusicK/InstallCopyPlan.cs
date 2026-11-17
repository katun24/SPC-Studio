using System.IO;

namespace SpcStudio.Export;

// Whether a copy into the install is needed at a file's place, and whether it writes over a file already there.
internal static class InstallCopyPlan
{
    // The copy of a file's bytes to its place, made by the caller with whether it writes over a file already there, or null where the place already
    // holds the same bytes.
    public static TCopy? UnlessAlreadyThere<TCopy>(string destination, byte[] fileBytes, Func<bool, TCopy> copyWithIsOverwriting) where TCopy : struct
    {
        if (!File.Exists(destination))
        {
            return copyWithIsOverwriting(false);
        }

        if (!SampleExport.HoldsOtherBytes(destination, fileBytes))
        {
            return null;
        }

        return copyWithIsOverwriting(true);
    }
}
