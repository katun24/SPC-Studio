using Avalonia;
using Avalonia.Threading;

namespace SpcStudio.Controls;

// Scrolls a view a step at a time while a drag is held outside its viewport, re-running the drag after each step so it follows the content.
public sealed class EdgeScroll
{
    public const int MinStepColumns = 1;
    public const int MaxStepColumns = 8;
    public const int DefaultStepColumns = 2;
    private const int StepIntervalMilliseconds = 25;

    private readonly IScrolledView _scrolledView;
    private readonly DispatcherTimer _stepTimer;
    private readonly Func<Size> _stepCellSize;

    // Re-runs the drag from the cursor, answering false once the drag is over.
    private Func<bool>? _resumeDrag;

    // How far a step scrolls on each axis, signed by the edge the drag is past and zero on an axis it is inside or has none of.
    private double _stepOffsetX;
    private double _stepOffsetY;

    // Builds the scroll over one view whose content is laid out in cells of a size, its timer stopped.
    public EdgeScroll(IScrolledView scrolledView, Func<Size> stepCellSize)
    {
        _scrolledView = scrolledView;
        _stepCellSize = stepCellSize;
        _stepTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(StepIntervalMilliseconds) };
        _stepTimer.Tick += OnStepTimerTick;
    }

    // How far a step scrolls for every drag that follows the user's speed, in columns horizontally and in rows vertically.
    public static int PreferredStepColumns { get; set; } = DefaultStepColumns;

    // How far this scroll's steps go in the same units where it does not follow the user's speed, or null where it does.
    public int? FixedStepColumns { get; init; }

    // The cell a view with no columns of its own steps in: one of the roll's columns at its unzoomed width, and no height.
    public static Size UnzoomedRollColumn()
    {
        return new Size(RollMetrics.BaseCellWidth, 0);
    }

    // Follows a drag that has reached a position in the content, scrolling toward it at once and then on each step while it stays outside the viewport,
    // and letting go where it is back inside; a null position is an axis the drag does not follow.
    public void FollowDrag(double? contentX, double? contentY, Func<bool> resumeDrag)
    {
        _resumeDrag = resumeDrag;
        double outsideX = contentX is double positionX ? DistanceOutsideViewport(positionX, _scrolledView.Offset.X, _scrolledView.Viewport.Width) : 0;
        double outsideY = contentY is double positionY ? DistanceOutsideViewport(positionY, _scrolledView.Offset.Y, _scrolledView.Viewport.Height) : 0;
        if (outsideX == 0 && outsideY == 0)
        {
            _stepTimer.Stop();
            return;
        }

        var cellSize = _stepCellSize();
        int stepColumns = FixedStepColumns ?? PreferredStepColumns;
        _stepOffsetX = Math.Sign(outsideX) * stepColumns * cellSize.Width;
        _stepOffsetY = Math.Sign(outsideY) * stepColumns * cellSize.Height;
        if (_stepTimer.IsEnabled)
        {
            return;
        }

        ScrollOneStep();
        _stepTimer.Start();
    }

    // Ends the scroll, running or not.
    public void Stop()
    {
        _stepTimer.Stop();
        _resumeDrag = null;
    }

    // Re-runs the drag, stopping where it is over, and then takes a step if the drag is still outside the viewport.
    private void OnStepTimerTick(object? sender, EventArgs tickArgs)
    {
        if (_resumeDrag is null || !_resumeDrag())
        {
            Stop();
            return;
        }

        if (_stepTimer.IsEnabled)
        {
            ScrollOneStep();
        }
    }

    // Moves the view's offset by one step on both axes.
    private void ScrollOneStep()
    {
        _scrolledView.Offset = new Vector(_scrolledView.Offset.X + _stepOffsetX, _scrolledView.Offset.Y + _stepOffsetY);
    }

    // How far outside the viewport a position in the content is on one axis: negative off the near edge, positive off the far one, zero between them.
    private static double DistanceOutsideViewport(double position, double viewportOffset, double viewportLength)
    {
        double nearEdge = viewportOffset;
        double farEdge = viewportOffset + viewportLength;
        if (position < nearEdge)
        {
            return position - nearEdge;
        }

        if (position > farEdge)
        {
            return position - farEdge;
        }

        return 0;
    }
}
