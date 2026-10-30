using SpcStudio.Model;

namespace SpcStudio.Import;

// The importer's warnings given once every channel has been read.
public static partial class MmlImporter
{
    private const int MostNotesWithoutWarning = 100000;

    // Warns where the song holds more notes than the app works with comfortably.
    private static void WarnManyNotes(Song song, Action<string>? warn)
    {
        int noteCount = Enumerable.Range(0, Song.DriverChannelCount).Sum(channel => song.Notes(channel).Count);
        if (noteCount <= MostNotesWithoutWarning)
        {
            return;
        }

        warn?.Invoke(ImportWarnings.ManyNotes(noteCount));
    }

    // Warns about each channel whose $FA $02 transpose differs between its loop point and the song's end where a note after the loop point hears the difference.
    private static void WarnTransposesAcrossLoop(
        bool doesLoop, List<(int Channel, int AtLoop, int? NoteTick, List<(int Tick, int Value)> Changes)> transposes, int songEndTick, Action<string>? warn)
    {
        if (!doesLoop)
        {
            return;
        }

        foreach (var (channel, atLoop, firstNoteTick, changes) in transposes)
        {
            int transposeAtEnd = changes.Where(change => change.Tick < songEndTick).Select(change => change.Value).LastOrDefault();
            if (transposeAtEnd == atLoop || firstNoteTick is null)
            {
                continue;
            }

            warn?.Invoke(ImportWarnings.TransposeAcrossLoop(channel));
        }
    }

    // Warns once where the $E4 global transpose differs between the loop point and the song's end and a note after the loop point hears the difference.
    private static void WarnGlobalTransposeAcrossLoop(MmlFile file, int? loopTick, GlobalTransposes globalTransposes, int songEndTick, Action<string>? warn)
    {
        if (!file.DoesLoop || loopTick is not { } loopAt || !globalTransposes.IsWritten)
        {
            return;
        }

        int atEnd = globalTransposes.InForceBefore(songEndTick);
        int atLoop = globalTransposes.InForceBefore(loopAt);
        if (atEnd == atLoop || globalTransposes.FirstStaleNoteTick(file.Channels, loopAt) is null)
        {
            return;
        }

        warn?.Invoke(ImportWarnings.GlobalTransposeAcrossLoop());
    }

    // Warns where the channels' own loop points disagree, the song holding one loop marker for all of them.
    private static void WarnDisagreeingLoopPoints(Dictionary<int, int> loopTicksByChannel, int writtenChannelCount, Action<string>? warn)
    {
        if (loopTicksByChannel.Count == 0)
        {
            return;
        }

        var distinctTicks = loopTicksByChannel.Values.Distinct().ToList();
        if (distinctTicks.Count > 1)
        {
            warn?.Invoke(ImportWarnings.LoopPointsDisagree());
        }
        else if (loopTicksByChannel.Count != writtenChannelCount && distinctTicks[0] != 0)
        {
            warn?.Invoke(ImportWarnings.LoopPointsMissing());
        }
    }

    // Warns once, naming none of them, where notes or parts of notes the ROM plays at no pitch were left out.
    private static void WarnNotesAtNoPitch(WalkedCommands walked, Action<string>? warn)
    {
        int droppedCount = walked.SilentNoteTicks.Sum(ticks => ticks.Count);
        if (walked.CutShortNoteCount > 0)
        {
            warn?.Invoke(ImportWarnings.NotesOrPartsAtNoPitch());
        }
        else if (droppedCount > 0)
        {
            warn?.Invoke(droppedCount == 1 ? ImportWarnings.NoteAtNoPitch() : ImportWarnings.NotesAtNoPitch());
        }
    }

    // Warns once for each kind of dropped command whose dropping is warned about.
    private static void WarnIgnoredCommands(List<(int Channel, MmlIgnored Command)> ignored, Action<string>? warn)
    {
        ignored = WithInertMadeHeard(ignored, IgnoredCommand.InertPitchModulation, IgnoredCommand.PitchModulation);
        ignored = WithInertMadeHeard(ignored, IgnoredCommand.InertMasterVolume, IgnoredCommand.MasterVolume);
        foreach (var kind in ignored.Select(dropped => dropped.Command.Command).Where(IsDroppingWarned).Distinct())
        {
            warn?.Invoke(ImportWarnings.DroppedCommand(kind));
        }
    }

    // The dropped commands with every inert one of a kind counted as heard wherever one of that kind in the song is heard.
    private static List<(int Channel, MmlIgnored Command)> WithInertMadeHeard(List<(int Channel, MmlIgnored Command)> ignored, IgnoredCommand inert, IgnoredCommand heard)
    {
        if (!ignored.Any(dropped => dropped.Command.Command == heard))
        {
            return ignored;
        }

        return [.. ignored.Select(dropped => dropped.Command.Command == inert ? (dropped.Channel, dropped.Command with { Command = heard }) : dropped)];
    }

    // Warns about the vibrato fades dropped for being a note's second.
    private static void WarnSecondVibratoFades(List<(int Channel, int Tick)> secondFades, Action<string>? warn)
    {
        WarnWritten(secondFades, ImportWarnings.SecondVibratoFades, warn);
    }

    // Warns about each kind of note cut that sounds different from the ROM, and about the changes left uncut that do.
    private static void WarnCutsHeard(CutsHeard cutsHeard, Action<string>? warn)
    {
        WarnWritten(cutsHeard.NoiseInsideNotes, ImportWarnings.NoiseInsideTremoloNotes, warn);
        WarnWritten(cutsHeard.TieGatesInsideNotes, ImportWarnings.TieGatesInsideNotes, warn);
        WarnWritten(cutsHeard.VibratosRestarted, ImportWarnings.VibratosRestarted, warn);
        WarnWritten(cutsHeard.BendsRestarted, ImportWarnings.BendsRestarted, warn);
    }

    // Warns once about a kind of command written in some places, and says nothing where it was written nowhere.
    private static void WarnWritten(List<(int Channel, int Tick)> written, Func<string> warning, Action<string>? warn)
    {
        if (written.Count == 0)
        {
            return;
        }

        warn?.Invoke(warning());
    }

    // Whether dropping a command is warned about: where dropping it can change what the song sounds like, the Yoshi drums apart.
    private static bool IsDroppingWarned(IgnoredCommand command)
    {
        return command is not (
            IgnoredCommand.DspStatusRegister or IgnoredCommand.DspUnusedRegister or IgnoredCommand.SendToGame or IgnoredCommand.GameSyncCounter
            or IgnoredCommand.ZeroTickFade or IgnoredCommand.InertPitchModulation or IgnoredCommand.InertMasterVolume or IgnoredCommand.ReadAheadHotPatch
            or IgnoredCommand.YoshiDrumsChannel5 or IgnoredCommand.YoshiDrumsThisChannel);
    }

    // Warns about the fades of one lane a warning is about, where there are any.
    private static void WarnAboutFades(List<int> fadeTicks, string laneName, Func<string, string> fadesWarning, Action<string>? warn)
    {
        if (fadeTicks.Count == 0)
        {
            return;
        }

        warn?.Invoke(fadesWarning(laneName));
    }
}
