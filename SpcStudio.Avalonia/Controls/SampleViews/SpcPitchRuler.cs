using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The pitch scale beside the SPC Player's canvas while one voice is shown alone: a tick a semitone, and a longer tick and a label an octave, counted in octaves from the sample's own rate.
public sealed class SpcPitchRuler : Control
{
    public const double RulerWidth = 38;
    private const double SemitoneTickLength = 4;
    private const double OctaveTickLength = 10;
    private const int SemitonesPerOctave = 12;

    private readonly IPen _semitonePen = new Pen(new SolidColorBrush(Palette.SpcPitchSemitoneTick), 1);
    private readonly IPen _octavePen = new Pen(new SolidColorBrush(Palette.SpcPitchOctaveTick), 1);
    private SpcTraceView? _trace;

    // Builds the column at its fixed width, clipping what it draws.
    public SpcPitchRuler()
    {
        Width = RulerWidth;
        ClipToBounds = true;
    }

    // The canvas whose shown voice this is the scale of; the column redraws whenever the canvas's rows move.
    public SpcTraceView? Trace
    {
        get
        {
            return _trace;
        }
        set
        {
            if (_trace is not null)
            {
                _trace.RowsChanged -= InvalidateVisual;
            }

            _trace = value;
            if (_trace is not null)
            {
                _trace.RowsChanged += InvalidateVisual;
            }

            InvalidateVisual();
        }
    }

    // Draws the column, the band level with the canvas's ruler, and the scale of the voice shown alone.
    public override void Render(DrawingContext dc)
    {
        var bounds = new Rect(Bounds.Size);
        dc.DrawRectangle(SpcTraceView.RowBrush, null, bounds);
        RulerBand.DrawBackground(dc, bounds.Width);
        if (_trace?.VoiceShownAlone is not int voice)
        {
            return;
        }

        var (low, high) = _trace.PitchRangeOf(voice);
        for (int semitone = (int)Math.Ceiling(low * SemitonesPerOctave); semitone <= high * SemitonesPerOctave; semitone++)
        {
            double y = DrawnSurface.PixelCenter(_trace.YOfPitch(voice, (double)semitone / SemitonesPerOctave));
            if (semitone % SemitonesPerOctave != 0)
            {
                dc.DrawLine(_semitonePen, new Point(bounds.Width - SemitoneTickLength, y), new Point(bounds.Width, y));
                continue;
            }

            dc.DrawLine(_octavePen, new Point(bounds.Width - OctaveTickLength, y), new Point(bounds.Width, y));
            DrawOctaveLabel(dc, semitone / SemitonesPerOctave - (int)SpcTraceView.UnityPitchOctave, bounds.Width, y);
        }
    }

    // Draws an octave's count from the sample's own rate, signed above it, left of the octave's tick and centered on it.
    private static void DrawOctaveLabel(DrawingContext dc, int octavesFromUnity, double right, double y)
    {
        string label = octavesFromUnity > 0 ? "+" + octavesFromUnity : octavesFromUnity.ToString(CultureInfo.InvariantCulture);
        var text = RulerBand.LabelText(label);
        dc.DrawText(text, new Point(right - OctaveTickLength - Space.Px3 - text.Width, y - text.Height / 2));
    }
}
