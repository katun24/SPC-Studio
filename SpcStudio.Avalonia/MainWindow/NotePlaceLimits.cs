using SpcStudio.Model;

namespace SpcStudio;

// The bounds of the fields that place a note in the song - its position and its length - wherever such a field stands.
public static class NotePlaceLimits
{
    public const int MinPosition = 0;
    public const int MaxPosition = 99999;
    public const int MinLength = 1;
    public const int MaxLength = 9999;

    public static readonly ValueRange PositionRange = new(MinPosition, MaxPosition);
    public static readonly ValueRange LengthRange = new(MinLength, MaxLength);
}
