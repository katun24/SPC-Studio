using System.Globalization;
using SpcStudio.Model;

namespace SpcStudio.Controls;

// A pitch on the roll as the screen names it, a note letter with an optional sharp and an octave, and that name read back.
public static class PitchLabel
{
    private const int OctaveDigitCount = 1;

    private static readonly string[] NoteLetters = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    // The name of a pitch given as semitones above C4, in AddmusicK's octave numbering.
    public static string NameOf(int semitone)
    {
        var (octave, semitoneInOctave) = SnesPitch.OctaveAndSemitone(semitone);
        return $"{NoteLetters[semitoneInOctave]}{octave}";
    }

    // Reads a name back into semitones above C4, in either case, answering false for anything else or a pitch off the roll.
    public static bool TryParse(string text, out int semitone)
    {
        semitone = 0;

        string name = text.Trim();
        if (name.Length <= OctaveDigitCount)
        {
            return false;
        }

        int octaveStart = name.Length - OctaveDigitCount;
        int semitoneInOctave = Array.FindIndex(NoteLetters, letter => letter.Equals(name[..octaveStart], StringComparison.OrdinalIgnoreCase));
        if (semitoneInOctave < 0)
        {
            return false;
        }

        if (!int.TryParse(name[octaveStart..], NumberStyles.None, CultureInfo.InvariantCulture, out int octave))
        {
            return false;
        }

        semitone = SnesPitch.NoteValueFor(octave, semitoneInOctave) - SnesPitch.C4NoteValue;
        return semitone >= Song.LowestSemitone && semitone <= Song.HighestSemitone;
    }
}
