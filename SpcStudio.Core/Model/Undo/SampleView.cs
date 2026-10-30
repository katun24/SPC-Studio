namespace SpcStudio.Model;

// Where the sample editor was looking when a state was recorded: the sample its drop-down was on, null where the song has none, and the run of points its waveform had selected.
public readonly record struct SampleView(int? SampleId, int SelectionStart, int SelectionEnd);
