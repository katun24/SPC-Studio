namespace SpcStudio.Audio;

// What reading one register's swings needs to know of that register: the units a swing is measured in and back, whether one write between two values is
// a jump rather than a step of a swing, how wide a swing may be, how far its middle may wander and how close two widths have to be at the narrowest, and
// the driver's triangle for it - how far into its cycle it first turns, whether that turn is a peak, and how many widths above its middle it keys on at.
public sealed record SpcSwingScale(
    Func<int, double> UnitsOf,
    Func<double, int> ValueOf,
    Func<int, int, bool> AreApart,
    double MaxSwing,
    double MaxCenterDrift,
    double MinWidthTolerance,
    double FirstTurnCycles,
    bool IsFirstTurnPeak,
    int KeyOnSidePeaks);
