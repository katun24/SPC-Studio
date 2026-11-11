namespace SpcStudio.Import;

public static partial class MmlParser
{
    // What a rest or a tie read with nothing sounding is being merged into.
    private enum RestByteKind
    {
        None,
        Rest,
        Tie,
    }
}
