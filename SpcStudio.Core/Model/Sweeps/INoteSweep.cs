namespace SpcStudio.Model;

// What a chain of sweeps on a note needs of the thing on it: where it starts, how long it lasts, whether it runs on to the note's end, and how to rebuild it with those moved.
public interface INoteSweep<TSelf> where TSelf : struct, INoteSweep<TSelf>
{
    public int DelayTicks { get; }
    public int LengthTicks { get; }
    public bool IsEndless { get; }

    // This sweep with its delay and length replaced and its own values untouched.
    public TSelf WithSpan(int delayTicks, int lengthTicks);

    // This sweep running on to the end of every note playing it, or lasting its own length, with its other values untouched.
    public TSelf WithEndless(bool isEndless);
}
