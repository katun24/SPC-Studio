using Avalonia.Media;

namespace SpcStudio.Theme;

// The transport's four glyphs - play, pause, stop and loop - at the size they are drawn on the transport's buttons.
public static class TransportIcons
{
    public static readonly Geometry Play = Geometry.Parse("M 0,0 L 22,14 L 0,28 Z");
    public static readonly Geometry Pause = Geometry.Parse("M 0,0 H 8 V 28 H 0 Z M 14,0 H 22 V 28 H 14 Z");
    public static readonly Geometry Stop = Geometry.Parse("M 0,0 H 24 V 24 H 0 Z");

    // MDI repeat, as tall as the play glyph: playing over and over.
    public static readonly Geometry Loop = AppIcons.FittedAtOrigin(AppIcons.MdiRepeatPath, Play.Bounds.Height);
}
