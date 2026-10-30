using SpcStudio.Model;

namespace SpcStudio.Export;

// Turns a song into an AddmusicK MML file. This part holds the toggles - the light staccato and the legato - and the remote code blocks that arm and clear around held notes.
public static partial class MmlExporter
{
    private const string HoldBlock = "(!1)[]";
    private const string HoldArmCommand = "(!1,3)";
    private const string HoldClearCommand = "(!!0)";
    private const string LegatoHoldArmPlaceholder = "(!0,-1)";
    private const string KeyOnClearCommand = "(!!-1)";

    // The ticks the command channel writes a light staccato toggle at after its header: where the lanes' runs flip the state, inside the song and away from the loop marker's tick.
    public static IEnumerable<int> StaccatoToggleTicks(Song song, int markerTick)
    {
        foreach (var (tick, _) in StaccatoRuns.StateChanges(song.StaccatoUnits))
        {
            if (tick >= song.EndTick)
            {
                yield break;
            }

            if (tick > 0 && tick != markerTick)
            {
                yield return tick;
            }
        }
    }

    // What the song says about the light staccato above its channels: the toggle where it starts under it, and nothing where it does not.
    private static string? StaccatoHeader(Song song)
    {
        return StaccatoRuns.StateAt(song.StaccatoUnits, 0) == SnesTiming.DefaultStaccato ? null : StaccatoName.Write();
    }

    // What a channel's header says about its legato: the toggle where its first note hands over to the next or is written under it, and nothing where it is neither.
    private static string LegatoHeader(Song song, int channel, HeldNoteShape shape)
    {
        return LegatoToggles.For(song, channel, shape).DoesHeaderToggle ? $"{LegatoName.Write()} " : "";
    }

    // What a channel's header says about its key-off remote block: the arm where its first note is held, and nothing where it is not.
    private static string HoldHeader(Song song, int channel, HeldNoteShape shape)
    {
        return HoldToggles.For(song, channel, shape).DoesHeaderArm ? $"{HoldCommandFor(true)} " : "";
    }

    // The command that arms the key-off remote block, or the one that clears it.
    private static string HoldCommandFor(bool doesArm)
    {
        return doesArm ? HoldOnName.Write() : HoldOffName.Write();
    }

    // The command that arms the key-on block toggling the legato twice, written as a stand-in until the block's number is known, or the one that clears the key-on slot.
    private static string LegatoHoldCommandFor(bool doesArm)
    {
        return doesArm ? LegatoHoldArmPlaceholder : KeyOnClearCommand;
    }

    // The commands of the key-on block that keys every note on under the legato: two toggles, which leave the bit where it was and clear $0162.
    private static string[] LegatoHoldBlockCommands()
    {
        return [LegatoName.Write(), LegatoName.Write()];
    }

    // A remote code block as the file defines it, outside every channel: its number and its commands.
    private static string RemoteBlockDefinition(int blockNumber, string commands)
    {
        return $"(!{blockNumber})[{commands}]";
    }

    // The command that arms the key-on slot with a block's number.
    private static string KeyOnArmCommandFor(int blockNumber)
    {
        return $"(!{blockNumber},-1)";
    }
}
