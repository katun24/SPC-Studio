namespace SpcStudio;

// Turns the stages a job reports into the share of the whole job done, the whole being a fixed number of equal steps and each new stage starting the next one.
public sealed class EqualStepsProgress
{
    private readonly int _stepCount;
    private readonly Action<double> _reportDoneFraction;
    private int _stepIndex;
    private string? _runningStage;

    // Starts counting at a given step of a given number of them, handing the share done to an action.
    public EqualStepsProgress(int stepCount, int firstStepIndex, Action<double> reportDoneFraction)
    {
        _stepCount = stepCount;
        _stepIndex = firstStepIndex;
        _reportDoneFraction = reportDoneFraction;
    }

    // Takes a stage's report, a stage in other words than the last starting the next step, and hands on the share of the whole job done.
    public void Report(string stage, int doneCount, int totalCount)
    {
        if (_runningStage is not null && stage != _runningStage)
        {
            _stepIndex++;
        }

        _runningStage = stage;
        _reportDoneFraction((_stepIndex + ProgressDialog.DoneFraction(doneCount, totalCount)) / _stepCount);
    }
}
