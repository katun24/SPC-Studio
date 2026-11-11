using SpcStudio.Model;

namespace SpcStudio.Import;

// The echo runs a timeline of the echo unit's states makes, fed tick by tick in order: a run opens where the echo starts, closes where it stops, and gives
// way to the next wherever its delay, feedback or filter moves while it runs.
internal sealed class EchoRunsBuilder(Song song)
{
    private readonly List<EchoRun> _runs = [];
    private int _runStartTick;

    public int Delay { get; private set; } = SnesEcho.Default(EchoSetting.Delay);
    public int Feedback { get; private set; } = SnesEcho.Default(EchoSetting.Feedback);
    public long Coefficients { get; private set; } = SnesEcho.PackedPresets[SnesEcho.DefaultFilter];
    public bool IsRunning { get; private set; }

    // Takes the state the unit ends a tick in, closing the run in force where the echo stopped there or its settings moved.
    public void Reach(int tick, bool isRunningAtEnd, int endDelay, int endFeedback, long endCoefficients)
    {
        bool haveSettingsMoved = endDelay != Delay || endFeedback != Feedback || endCoefficients != Coefficients;
        bool doesRunClose = IsRunning && (!isRunningAtEnd || haveSettingsMoved);
        if (doesRunClose && tick > _runStartTick)
        {
            _runs.Add(RunBetween(_runStartTick, tick));
        }

        if (doesRunClose || (!IsRunning && isRunningAtEnd))
        {
            _runStartTick = tick;
        }

        (Delay, Feedback, Coefficients, IsRunning) = (endDelay, endFeedback, endCoefficients, isRunningAtEnd);
    }

    // Writes the runs the timeline made onto the song, the one still running carried to the roll's end, and drops every filter no run names.
    public void WriteToSong()
    {
        var runs = new List<EchoRun>(_runs);
        if (IsRunning && song.RollLengthTicks > _runStartTick)
        {
            runs.Add(RunBetween(_runStartTick, song.RollLengthTicks));
        }

        song.SetEchoRuns(runs);
        song.CompactFilters();
    }

    // A run of the echo from one tick to another under the settings in force, the coefficients resolved to the song's filter index.
    private EchoRun RunBetween(int startTick, int endTick)
    {
        return new EchoRun(startTick, endTick - startTick, new EchoUnitState(Delay, Feedback, song.FilterIndexFor(Coefficients)));
    }
}
