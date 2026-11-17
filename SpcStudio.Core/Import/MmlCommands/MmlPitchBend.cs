using SpcStudio.Model;

namespace SpcStudio.Import;

// One $DD on a note: how far into the note the piece it is written behind starts, and its three bytes - the delay, the ramp length and the note value it ramps to.
public sealed record MmlPitchBend(int ArmTicks, int DelayTicks, int LengthTicks, int TargetNoteValue)
{
    public const int NoRampTicks = 0;

    public static readonly string HexSpelling = AmkCommands.HexSpelling(AmkCommands.PitchBend);

    // Whether a $DD stands at a position in a song's text, spelled in either of the two cases AddmusicK checks for.
    public static bool IsSpelledAt(string text, int position)
    {
        return AmkTextReader.IsSpelledAt(text, position, HexSpelling) || AmkTextReader.IsSpelledAt(text, position, HexSpelling.ToLowerInvariant());
    }

    // Hangs a bend on the note at an index of a stream, armed at the piece starting at a tick, or adds the dropped bend it is at another tick where its ramp is no ticks.
    public static void HangOnNote(List<MmlEvent> events, int noteIndex, int pieceStartTick, int droppedTick, int line, int delayTicks, int lengthTicks, int targetNoteValue)
    {
        if (lengthTicks == NoRampTicks)
        {
            events.Add(new MmlIgnored(droppedTick, IgnoredCommand.ZeroLengthPitchBend, line));
            return;
        }

        events[noteIndex] = ((MmlNote)events[noteIndex]).WithBendArmedAt(pieceStartTick, delayTicks, lengthTicks, targetNoteValue);
    }
}
