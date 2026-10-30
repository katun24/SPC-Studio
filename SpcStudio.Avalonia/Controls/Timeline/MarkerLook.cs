using Avalonia.Media;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// How a marker is drawn - its line's pen and the brush of its flag or head - for the loop and end markers, on and off, the playback marker and the audition marker, on every surface that draws one.
public sealed record MarkerLook(IPen LinePen, IBrush HeadBrush)
{
    private const double LineThickness = 2;

    public static readonly MarkerLook Loop = OfColor(Palette.LoopMarker);
    public static readonly MarkerLook LoopOff = OfColor(Palette.LoopMarkerOff);
    public static readonly MarkerLook End = OfColor(Palette.EndMarker);
    public static readonly MarkerLook EndOff = OfColor(Palette.EndMarkerOff);
    public static readonly MarkerLook Playhead = OfColor(Palette.Amber);
    public static readonly MarkerLook Audition = OfColor(Palette.PastelBlue);

    // The loop marker's look, on or dimmed.
    public static MarkerLook OfLoop(bool isOn)
    {
        return isOn ? Loop : LoopOff;
    }

    // The end marker's look, on or dimmed.
    public static MarkerLook OfEnd(bool isOn)
    {
        return isOn ? End : EndOff;
    }

    // A look whose line and flag or head are both one color.
    private static MarkerLook OfColor(Color color)
    {
        var brush = new SolidColorBrush(color);
        return new MarkerLook(new Pen(brush, LineThickness), brush);
    }
}
