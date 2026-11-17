using Avalonia.Threading;

namespace SpcStudio.Controls;

// The timer every playhead in the app is moved on while something plays.
public static class PlayheadTimer
{
    private static readonly TimeSpan MoveInterval = TimeSpan.FromMilliseconds(25);

    // A stopped timer that ticks at the playhead's rate, at the priority the screen is drawn at.
    public static DispatcherTimer Build()
    {
        return new DispatcherTimer(DispatcherPriority.Render) { Interval = MoveInterval };
    }
}
