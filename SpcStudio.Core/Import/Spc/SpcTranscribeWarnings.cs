namespace SpcStudio.Import;

// The text of every warning an SPC transcription can give, one method each, with a parameter for each number the text mentions.
internal static class SpcTranscribeWarnings
{
    // Samples that could not be read out of the dump, their notes left out.
    public static string SamplesUnreadable(int skippedCount)
    {
        return $"{skippedCount} of the samples the song played could not be read out of the dump, and the notes that played them were left out.";
    }

    // No rhythm found that the grid can follow.
    public static string NoClearRhythm()
    {
        return "No clear rhythm could be found in this song, so the grid on the roll may not line up with the music. The notes are still where they played.";
    }

    // Notes at a pitch the roll cannot hold, put on its nearest row.
    public static string PitchesOutOfRange(int outOfRangeCount)
    {
        return $"{outOfRangeCount} notes played a pitch the roll cannot hold and sit at the nearest row it has. That is a sample the song played over a wider "
            + "span of pitches than the roll covers, or higher than the sample's own tuning can reach.";
    }

    // Notes whose heard length no written length and gate produce, left off the grid.
    public static string NotesOffGrid(int unfittedCount)
    {
        return $"{unfittedCount} notes were heard for a span no written length and gate produce, so they keep the length they played and end off the grid.";
    }

    // Pitch slides written as straight pitch bends.
    public static string SlidesAsStraightBends(int bendCount)
    {
        return $"{bendCount} pitch bends were written, one for each time a voice slid from one pitch to another without keying on again. Each is a straight "
            + "ramp between the two pitches, where the game's own curve between them may have been another shape.";
    }

    // Every voice turned down by one scale, the song having played louder than the driver's volume chain reaches.
    public static string VoicesTurnedDown(double scale)
    {
        return $"This song played louder than the driver's own volume chain reaches, so every voice sits at {scale * 100:F0}% of the level it played at. "
            + "What the voices are relative to each other is unchanged.";
    }
}
