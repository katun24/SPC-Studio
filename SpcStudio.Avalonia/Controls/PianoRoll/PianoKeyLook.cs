using Avalonia;
using Avalonia.Media;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// How a piano key is drawn, on the keyboard beside the roll and on the Create Chord Sample window's alike: a white or black face edged by the grid line,
// and its note name - on each C, or on every key where every note is named - in the label color for its face.
public static class PianoKeyLook
{
    private const double LabelFontSize = 10;
    private const int LabeledNoteInOctave = 0;

    private static readonly IBrush WhiteKeyBrush = new SolidColorBrush(Palette.WhiteKey);
    private static readonly IBrush BlackKeyBrush = new SolidColorBrush(Palette.GraphiteGray);
    private static readonly IBrush LabelOnWhiteBrush = new SolidColorBrush(Palette.GraphiteGray);
    private static readonly IBrush LabelOnBlackBrush = new SolidColorBrush(Palette.BlackKeyLabel);

    // Whether every key is labeled with its note name rather than only each C, as the Options menu's Show All Tones On Piano Keys says.
    public static bool IsShowingEveryNoteName { get; set; }

    // Draws a key's face over a rectangle, white or black as its semitone is, edged by the grid line.
    public static void DrawFace(DrawingContext dc, int semitone, Rect face)
    {
        dc.DrawRectangle(IsBlackKey(semitone) ? BlackKeyBrush : WhiteKeyBrush, DrawnSurface.GridLinePen, face);
    }

    // The part of a key's face inside its edge, where a fill laid over the key leaves the edge showing.
    public static Rect BodyOf(Rect face)
    {
        return face.Deflate(DrawnSurface.GridLinePen.Thickness);
    }

    // A key's note name as the piano roll's keys draw it, or null where the key carries none: only a C does, unless every note is named.
    public static FormattedText? LabelOf(int semitone)
    {
        var (_, noteInOctave) = SnesPitch.OctaveAndSemitone(semitone);
        if (!IsShowingEveryNoteName && noteInOctave != LabeledNoteInOctave)
        {
            return null;
        }

        return NameLabelOf(semitone);
    }

    // A key's note name as it is drawn on it, whichever key it is.
    public static FormattedText NameLabelOf(int semitone)
    {
        return AppFonts.UiText(PitchLabel.NameOf(semitone), LabelFontSize, IsBlackKey(semitone) ? LabelOnBlackBrush : LabelOnWhiteBrush);
    }

    // Whether a semitone is a black key.
    private static bool IsBlackKey(int semitone)
    {
        var (_, noteInOctave) = SnesPitch.OctaveAndSemitone(semitone);
        return RollMetrics.IsBlackKey[noteInOctave];
    }
}
