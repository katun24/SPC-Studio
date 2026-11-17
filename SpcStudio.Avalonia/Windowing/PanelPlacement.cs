namespace SpcStudio;

// Where one of the right-hand panels, or the column's own window, stood when the app closed: in the column, in the column's window, or in a window of its own and where that window stood.
public sealed class PanelPlacement
{
    public bool PoppedOut { get; set; }
    public bool Grouped { get; set; }
    public int Left { get; set; }
    public int Top { get; set; }
    public int Height { get; set; }
}
