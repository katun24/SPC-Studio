namespace SpcStudio.Import;

// Every `$E4` in a file in the order the driver reads them, so the global transpose a note keys on under can be asked for by where the note sits.
internal sealed class GlobalTransposes
{
    private readonly List<(int Tick, int Channel, int Position, int Semitones)> _commandsInReadOrder;

    // The resolver over commands already in read order.
    private GlobalTransposes(List<(int Tick, int Channel, int Position, int Semitones)> commandsInReadOrder)
    {
        _commandsInReadOrder = commandsInReadOrder;
    }

    // Whether the file writes the command at all.
    public bool IsWritten
    {
        get
        {
            return _commandsInReadOrder.Count > 0;
        }
    }

    // Every `$E4` in the channels, put in the order the driver reads them.
    public static GlobalTransposes From(IReadOnlyList<MmlChannel> channels)
    {
        List<(int Tick, int Channel, int Position, int Semitones)> commands = [];
        foreach (var channel in channels)
        {
            for (int position = 0; position < channel.Events.Count; position++)
            {
                if (channel.Events[position] is MmlGlobalTranspose command)
                {
                    commands.Add((command.Tick, channel.Number, position, command.Semitones));
                }
            }
        }

        commands.Sort((first, second) => DriverReadOrder.Key(first.Tick, first.Channel, first.Position).CompareTo(DriverReadOrder.Key(second.Tick, second.Channel, second.Position)));
        return new GlobalTransposes(commands);
    }

    // The transpose an event at this position in this channel's stream is read under.
    public int InForceAt(int tick, int channel, int position)
    {
        int index = LastReadBefore(tick, channel, position);
        return index < 0 ? 0 : _commandsInReadOrder[index].Semitones;
    }

    // The transpose in force strictly before a tick, or none where nothing has set it.
    public int InForceBefore(int tick)
    {
        int semitones = 0;
        foreach (var command in _commandsInReadOrder)
        {
            if (command.Tick < tick)
            {
                semitones = command.Semitones;
            }
        }

        return semitones;
    }

    // The tick of the first note at or after the loop point that no `$E4` after the loop point reaches, or null where there is none.
    public int? FirstStaleNoteTick(IReadOnlyList<MmlChannel> channels, int loopTick)
    {
        int? firstTick = null;
        foreach (var channel in channels)
        {
            for (int position = 0; position < channel.Events.Count; position++)
            {
                if (channel.Events[position] is not MmlNote note || note.Tick < loopTick)
                {
                    continue;
                }

                int index = LastReadBefore(note.Tick, channel.Number, position);
                if (index >= 0 && _commandsInReadOrder[index].Tick >= loopTick)
                {
                    continue;
                }

                if (firstTick is null || note.Tick < firstTick)
                {
                    firstTick = note.Tick;
                }
            }
        }

        return firstTick;
    }

    // The index of the last command the driver reads before an event at this position, or -1 where it reads none.
    private int LastReadBefore(int tick, int channel, int position)
    {
        int found = -1;
        for (int index = 0; index < _commandsInReadOrder.Count; index++)
        {
            var command = _commandsInReadOrder[index];
            if (DriverReadOrder.Key(command.Tick, command.Channel, command.Position).CompareTo(DriverReadOrder.Key(tick, channel, position)) >= 0)
            {
                break;
            }

            found = index;
        }

        return found;
    }
}
