using Avalonia.Controls;
using SpcStudio.Controls;

namespace SpcStudio;

// One lane of the main window's stack and the label in the strip beside it, shown and hidden together.
public sealed class LaneRow(ChangeLane lane, string name)
{
    public ChangeLane Lane { get; } = lane;

    // What the label and the add button's menu call the lane.
    public string Name { get; } = name;

    // The label's own row in the strip, which is what is shown and hidden.
    public Border? LabelRow { get; set; }

    // The height this lane has been asked for, which it is drawn at wherever the window has the room.
    public double WantedHeight { get; set; } = RollMetrics.BaseLaneHeight;

    // Whether the lane is showing, its label showing with it.
    public bool IsVisible
    {
        get
        {
            return Lane.IsVisible;
        }

        set
        {
            Lane.IsVisible = value;
            LabelRow!.IsVisible = value;
        }
    }

    // Whether the lane is the song's rather than the selected channel's.
    public bool IsGlobal
    {
        get
        {
            return !Lane.IsPerChannel;
        }
    }
}
