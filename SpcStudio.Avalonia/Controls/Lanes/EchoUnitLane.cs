using Avalonia;
using Avalonia.Media;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The lane holding the echo unit itself, as runs: where the echo is on, and the delay, the feedback and the filter it runs with.
public sealed class EchoUnitLane : SongUnitLane<EchoRun>
{
    private readonly Brush _valueInsideRunBrush = new SolidColorBrush(Palette.EchoValueInsideRun);

    // Draws the lane in the echo lanes' two colors.
    public EchoUnitLane() : base(LaneCellBrushes.Echo)
    {
    }

    // The song's echo runs.
    protected override IReadOnlyList<EchoRun> Units
    {
        get
        {
            return Song?.EchoRuns ?? [];
        }
    }

    // The runs as a change stream, each at its start with its delay.
    protected override IEnumerable<LaneValueChange> Changes
    {
        get
        {
            return Units.Select(run => new LaneValueChange(run.StartTick, run.State.Delay));
        }
    }

    public override ValueRange ValueRange
    {
        get
        {
            return SnesEcho.DelayRange;
        }
    }

    // A fresh run holding the echo settings already in force where it starts.
    protected override EchoRun NewUnit(int startTick, int lengthTicks)
    {
        return new EchoRun(startTick, lengthTicks, Song?.EchoUnitStateAt(startTick) ?? EchoUnitState.Default);
    }

    // Selects the pressed run, which opens the editor over it.
    protected override void ChosenUnit(EchoRun unit)
    {
        Select(unit.StartTick);
    }

    // Writes a run's delay and feedback inside it.
    protected override void DrawInside(DrawingContext dc, EchoRun unit, Rect box)
    {
        DrawValue(dc, $"{unit.State.Delay} / {unit.State.Feedback}", box, _valueInsideRunBrush);
    }
}
