namespace SpcStudio.Controls;

// Which lane last filled a clipboard, or null where the roll did.
public static class LaneClipboard
{
    // The lane holding the clipboard, or null where the roll's notes do.
    public static ChangeLane? Owner { get; set; }

    // Hands the clipboard back to the roll.
    public static void Clear()
    {
        Owner = null;
    }
}
