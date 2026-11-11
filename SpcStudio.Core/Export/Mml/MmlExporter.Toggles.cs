using SpcStudio.Model;

namespace SpcStudio.Export;

// Turns a song into an AddmusicK MML file. This part holds the toggles - the light staccato and the legato - and the remote code blocks that arm and clear around held notes.
public static partial class MmlExporter
{
    private const int HoldBlockNumber = 1;
    private const string HoldClearCommand = "(!!0)";

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

    // The command that arms the ordinary slot with a block's number as a key-off block.
    private static string KeyOffArmCommandFor(int blockNumber)
    {
        return $"(!{blockNumber},3)";
    }

    // The command that arms the ordinary slot with a block's number as a timer block, firing once the timer has counted that many ticks from a key-on.
    private static string TimerArmCommandFor(int blockNumber, int countedTicks)
    {
        return $"(!{blockNumber},1,${countedTicks:X2})";
    }

    // Whether a token arms the ordinary slot with a key-off block: a held note's or a release run's.
    private static bool IsKeyOffArmCommand(string token)
    {
        return HoldOnName.IsNameOf(token) || ReleaseRuns.IsArmPlaceholder(token);
    }

    // A channel's legato toggles and the arms of its held notes' key-off block, which together say whether a note keys on with nothing of either in force.
    private sealed record ChannelSlots(LegatoToggleList Legato, HoldCommandList Hold)
    {
        // The toggles and arms of one channel under the held note shape.
        public static ChannelSlots For(Song song, int channel, HeldNoteShape shape)
        {
            return new ChannelSlots(LegatoToggles.For(song, channel, shape), HoldToggles.For(song, channel, shape));
        }

        // Whether a note keys on plainly: neither held nor bound, with the legato's bit clear and neither remote slot armed for it.
        public bool KeysOnPlainly(Note note)
        {
            return !note.IsHeld
                   && !note.IsLegato
                   && !Legato.IsBitSetAt(note.StartTick)
                   && !Legato.KeyOnArms.IsArmedAt(note.StartTick)
                   && !Hold.IsArmedAt(note.StartTick);
        }
    }
}
