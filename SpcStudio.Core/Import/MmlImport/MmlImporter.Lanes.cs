using SpcStudio.Model;

namespace SpcStudio.Import;

// The importer's lanes: the region lanes and their fades, the lanes with no fade form, and the velocity table.
public static partial class MmlImporter
{
    private const int NoRampEndTick = int.MinValue;

    // The changes a region lane's file wrote, turned into that lane's regions: a fade ending inside its region gets a divider where it lands, and a fade the
    // next command cuts short holds where it had got to.
    private static void ApplyRegions(List<CollectedRegionCommand> changes, string laneName, Action<int, int, bool, int, SnesSurround> setRegion, Action<string>? warn)
    {
        var rearmedTicks = new List<int>();
        var overwrittenTicks = new List<int>();

        for (int changeIndex = 0; changeIndex < changes.Count; changeIndex++)
        {
            var change = changes[changeIndex];
            int regionEndTick = changeIndex + 1 < changes.Count ? changes[changeIndex + 1].Tick : int.MaxValue;
            int regionTicks = regionEndTick - change.Tick;

            if (!SnesFade.DoesFade(change.FadeTicks))
            {
                setRegion(change.Tick, change.Value, false, 0, change.Surround);
                continue;
            }

            if (change.FadeTicks > regionTicks)
            {
                var ramp = SnesFade.HeldAt(change.Value).FadingTo(change.FadeTo, change.FadeTicks);
                var cuttingChange = changes[changeIndex + 1];
                if (!cuttingChange.IsSetInsideFade && SnesFade.DoesFade(cuttingChange.FadeTicks))
                {
                    rearmedTicks.Add(change.Tick);
                }
                else if (!cuttingChange.IsSetInsideFade && !ramp.IsFlat)
                {
                    overwrittenTicks.Add(change.Tick);
                }

                setRegion(change.Tick, change.Value, true, ramp.Advanced(regionTicks).Value, change.Surround);
                continue;
            }

            setRegion(change.Tick, change.Value, true, change.FadeTo, change.Surround);
            if (change.FadeTicks < regionTicks)
            {
                setRegion(change.Tick + change.FadeTicks, change.FadeTo, false, 0, change.Surround);
            }
        }

        WarnAboutFades(rearmedTicks, laneName, ImportWarnings.RearmedFades, warn);
        WarnAboutFades(overwrittenTicks, laneName, ImportWarnings.OverwrittenFades, warn);
    }

    // Sets the song's velocity table from the file's switches: the last one at tick 0 in the driver's order, or the SMW row where there is none, and refuses a later switch that moves it.
    private static void ApplyVelocityTable(Song song, List<(int Tick, int Channel, int Order, bool Smw, int Line)> switches)
    {
        var orderedSwitches = switches.OrderBy(written => DriverReadOrder.Key(written.Tick, written.Channel, written.Order)).ToList();
        bool usesSmwRow = orderedSwitches.Where(written => written.Tick == 0).Select(written => written.Smw).DefaultIfEmpty(true).Last();
        foreach (var velocitySwitch in orderedSwitches)
        {
            if (velocitySwitch.Tick > 0 && velocitySwitch.Smw != usesSmwRow)
            {
                throw MmlImportException.VelocityTableSwitch(velocitySwitch.Tick, velocitySwitch.Line);
            }
        }

        song.UsesSmwVelocities = usesSmwRow;
    }

    // A song-wide lane's commands gathered from every channel, in the order the driver reads them: by channel from the highest down, each channel's own in the order they were written.
    private static IReadOnlyList<MmlCommand> SongWideInDriverOrder(List<(MmlCommand Written, int Channel)> commands)
    {
        return [.. commands.OrderByDescending(gathered => gathered.Channel).Select(gathered => gathered.Written)];
    }

    // One of the four lanes whose command has a fade form as well, the sets and the fades at each tick merged into the one change the lane holds: the base at tick 0, a change wherever the value moves
    // or a fade is armed, the landing of a fade a set was written inside, and a warning for each set written inside a sloping fade.
    private static void ApplyFadedLane(
        IReadOnlyList<MmlCommand> commands, int? loopTick, string laneName, int defaultValue, Action<int, SnesSurround> setBase, List<CollectedRegionCommand> changes, Action<string>? warn)
    {
        var laneState = SnesFade.HeldAt(defaultValue);
        int laneStateTick = 0;
        var setInsideFadeTicks = new List<int>();
        var surroundInForce = SnesSurround.None;

        // Where the region the last change opened stops ramping, or no tick at all where that region is flat.
        int fadeEndTick = NoRampEndTick;

        // Where a fade with a set written inside it lands and the target it lands on, waiting to be written as a divider.
        (int Tick, int Value)? pendingLanding = null;

        foreach (var tickCommands in commands.GroupBy(written => written.Tick).OrderBy(tickGroup => tickGroup.Key))
        {
            int tick = tickCommands.Key;
            if (pendingLanding is { } dueLanding && dueLanding.Tick < tick)
            {
                changes.Add(new CollectedRegionCommand(dueLanding.Tick, dueLanding.Value, SnesFade.NoFade, 0, surroundInForce));
                fadeEndTick = NoRampEndTick;
                pendingLanding = null;
            }

            laneState = laneState.Advanced(tick - laneStateTick);
            laneStateTick = tick;
            int valueInForce = laneState.Value;
            var replayed = ReplayTick(laneState, tickCommands);
            laneState = replayed.LaneState;
            int? setValue = replayed.SetValue;
            bool didArmFade = replayed.Fade is not null;

            // The fade the tick arms, or none where a set was written behind it.
            var fade = replayed.IsSetBehindFade ? null : replayed.Fade;

            if (fade is not null)
            {
                pendingLanding = null;
            }

            if (setValue is not null && fade is null && laneState.HasNoAdditionLeft)
            {
                if (laneState.TicksLeft == 1)
                {
                    setValue = laneState.Target;
                }

                pendingLanding = laneState.TicksLeft == 1 || laneState.Target == setValue ? null : (tick + laneState.TicksLeft - 1, laneState.Target);
            }

            bool doesLandArmedFade = !didArmFade && fadeEndTick == tick + 1;
            bool isSetAlreadyInForce = setValue == (doesLandArmedFade ? laneState.Target : valueInForce) && replayed.SetSurround == surroundInForce;

            // Whether a set in force restates the lane: at the loop point, past the last fade's end, or where an armed fade lands.
            bool isRestatement = isSetAlreadyInForce && (tick == loopTick || tick > fadeEndTick || doesLandArmedFade);

            // Whether a set is written inside a sloping fade the driver goes on running.
            bool isSetInsideFade = setValue is not null && fade is null && laneState.IsFading && !laneState.HasNoAdditionLeft && (didArmFade || !isRestatement);
            if (isSetInsideFade)
            {
                (fade, pendingLanding) = HeldFadeAfterSet(laneState, tick);
                setInsideFadeTicks.Add(tick);
            }

            if (tick == 0)
            {
                if (setValue is { } baseValue)
                {
                    setBase(baseValue, replayed.SetSurround);
                    surroundInForce = replayed.SetSurround;
                    valueInForce = baseValue;
                }

                if (fade is { } openingFade)
                {
                    changes.Add(new CollectedRegionCommand(0, valueInForce, openingFade.Ticks, openingFade.To, surroundInForce));
                    fadeEndTick = openingFade.Ticks;
                }

                continue;
            }

            if (setValue is { } writtenValue)
            {
                surroundInForce = replayed.SetSurround;
                valueInForce = writtenValue;
            }

            if (fade is not null || (setValue is not null && !isRestatement) || isSetInsideFade)
            {
                changes.Add(new CollectedRegionCommand(tick, valueInForce, fade?.Ticks ?? SnesFade.NoFade, fade?.To ?? 0, surroundInForce, isSetInsideFade));
                fadeEndTick = fade is { } ramp ? tick + ramp.Ticks : NoRampEndTick;
            }
        }

        if (pendingLanding is { } finalLanding)
        {
            changes.Add(new CollectedRegionCommand(finalLanding.Tick, finalLanding.Value, SnesFade.NoFade, 0, surroundInForce));
        }

        WarnAboutFades(setInsideFadeTicks, laneName, ImportWarnings.SetsInsideFades, warn);
    }

    // What a lane holds for a set written inside a sloping fade, which the driver goes on running from the set value at its own step to its own landing: a fade to the target at the nearest step where
    // the step reaches it in time, the step itself and then the landing where it does not, and the set value held until the landing where the step leads away from the target.
    private static ((int Ticks, int To)? Fade, (int Tick, int Value)? Landing) HeldFadeAfterSet(SnesFade laneState, int tick)
    {
        int landingTick = tick + laneState.TicksLeft - 1;
        int stepsBeforeLanding = laneState.TicksLeft - 1;
        int distanceToTarget16 = SnesFade.HeldAt(laneState.Target).Value16 - laneState.Value16;
        if (distanceToTarget16 == 0)
        {
            return (null, null);
        }

        if (Math.Sign(distanceToTarget16) != Math.Sign(laneState.Delta16))
        {
            return (null, (landingTick, laneState.Target));
        }

        if (Math.Abs(distanceToTarget16) > Math.Abs(laneState.Delta16) * stepsBeforeLanding)
        {
            return ((stepsBeforeLanding, laneState.Advanced(stepsBeforeLanding).Value), (landingTick, laneState.Target));
        }

        return ((NearestStepTicks(laneState, laneState.Target), laneState.Target), null);
    }

    // The fade length, up to the ticks a running fade has left, whose step from where it now sits to a target comes nearest its own.
    private static int NearestStepTicks(SnesFade runningFade, int to)
    {
        return Enumerable.Range(SnesFade.MinFadeTicks, runningFade.TicksLeft).MinBy(ticks => StepMismatch(runningFade, to, ticks));
    }

    // How far the step of a fade from where a running fade now sits to a target over a number of ticks, divided the way the driver divides it, is from the running fade's own.
    private static int StepMismatch(SnesFade runningFade, int to, int ticks)
    {
        return Math.Abs(SnesFade.HeldAt(runningFade.Value).FadingTo(to, ticks).Delta16 - runningFade.Delta16);
    }

    // One tick's commands replayed on a lane's state in the order the driver reads them: the state they leave, the last set and fade among them, and
    // whether a set was written behind the fade still standing.
    private static ReplayedTick ReplayTick(SnesFade laneState, IEnumerable<MmlCommand> tickCommands)
    {
        int? setValue = null;
        var setSurround = SnesSurround.None;
        (int Ticks, int To)? fade = null;
        bool isSetBehindFade = false;
        foreach (var command in tickCommands)
        {
            if (SnesFade.DoesFade(command.FadeTicks))
            {
                laneState = laneState.FadingTo(command.Value, command.FadeTicks);
                fade = (command.FadeTicks, command.Value);
                isSetBehindFade = false;
                continue;
            }

            isSetBehindFade = fade is not null;
            laneState = laneState.WithValueSet(command.Value);
            setValue = command.Value;
            setSurround = command.Surround;
        }

        return new ReplayedTick(laneState, setValue, setSurround, fade, isSetBehindFade);
    }

    // One lane with no fade form: the command at tick 0 is the base, the last command at each later tick a change, and one stating the value in force is dropped.
    private static void ApplyLane(IReadOnlyList<(int Tick, int Value, int Line)> commands, int defaultValue, Action<int> setBase, Action<int, int> setChange)
    {
        int valueInForce = defaultValue;
        foreach (var command in commands.GroupBy(written => written.Tick).OrderBy(tickGroup => tickGroup.Key).Select(tickGroup => tickGroup.Last()))
        {
            if (command.Tick == 0)
            {
                setBase(command.Value);
                valueInForce = command.Value;
                continue;
            }

            if (command.Value == valueInForce)
            {
                continue;
            }

            setChange(command.Tick, command.Value);
            valueInForce = command.Value;
        }
    }

    // One command a region lane's file wrote, as the file wrote it: a fade's duration rather than a region, and whether it holds a set written inside a sloping fade.
    private readonly record struct CollectedRegionCommand(int Tick, int Value, int FadeTicks, int FadeTo, SnesSurround Surround, bool IsSetInsideFade = false);

    // What one tick's commands leave on a lane once replayed.
    private readonly record struct ReplayedTick(SnesFade LaneState, int? SetValue, SnesSurround SetSurround, (int Ticks, int To)? Fade, bool IsSetBehindFade);
}
