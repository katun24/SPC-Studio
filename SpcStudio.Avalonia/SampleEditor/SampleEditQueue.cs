namespace SpcStudio;

// The Sample Editor's edits, made one at a time in the order they were asked for, each one's slow part off the window's own thread; an edit still
// waiting is replaced by a newer one asked for under the same key on the same sample.
public sealed class SampleEditQueue
{
    private readonly List<(int SampleId, object? ReplacingKey, Func<Task> Make)> _waiting = [];
    private bool _isRunning;
    private TaskCompletionSource? _emptied;

    // Whether an edit of a sample is still waiting its turn, the one being made not counted.
    public bool IsWaitingToEdit(int sampleId)
    {
        return _waiting.Exists(edit => edit.SampleId == sampleId);
    }

    // Adds an edit of a sample to the end of the queue - in place of the last one waiting, where that one is of the same sample under the same key that
    // is not null - and starts the queue where it is not already running.
    public void Add(int sampleId, object? replacingKey, Func<Task> make)
    {
        if (_waiting.Count > 0 && _waiting[^1].SampleId == sampleId && replacingKey is not null && replacingKey.Equals(_waiting[^1].ReplacingKey))
        {
            _waiting[^1] = (sampleId, replacingKey, make);
        }
        else
        {
            _waiting.Add((sampleId, replacingKey, make));
        }

        if (!_isRunning)
        {
            RunUntilEmpty();
        }
    }

    // A task that finishes once every edit asked for so far, and every one asked for while it waits, has been made.
    public Task WhenEmpty()
    {
        if (!_isRunning)
        {
            return Task.CompletedTask;
        }

        _emptied ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return _emptied.Task;
    }

    // Makes the edits waiting one after another until none is left, and then finishes every wait for the queue to empty.
    private async void RunUntilEmpty()
    {
        _isRunning = true;
        while (_waiting.Count > 0)
        {
            var next = _waiting[0];
            _waiting.RemoveAt(0);
            await next.Make();
        }

        _isRunning = false;
        _emptied?.SetResult();
        _emptied = null;
    }
}
