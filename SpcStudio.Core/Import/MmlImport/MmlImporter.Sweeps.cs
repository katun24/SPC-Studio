using SpcStudio.Model;

namespace SpcStudio.Import;

// The importer's sweeps: the vibrato and the tremolo stretches on each note.
public static partial class MmlImporter
{

    // One channel's vibrato as entries on the notes heard playing it, each second fade on one note added to the ones the song is warned about.
    private static void ApplyVibrato(Song song, int channel, List<MmlEvent> commands, List<(int Channel, int Tick)> secondFades)
    {
        if (commands.Count == 0)
        {
            return;
        }

        new VibratoWalk(song, channel, commands, secondFades).Run();
    }

    // One channel's tremolo as entries on the notes heard playing it, walked a tick at a time through the driver's delay counter.
    private static void ApplyTremolo(Song song, int channel, List<MmlTremolo> commands)
    {
        var timeline = TremoloTimeline(commands);
        if (timeline.Count == 0)
        {
            return;
        }

        int nextChangeIndex = 0;
        var carriedState = TremoloState.Off;
        foreach (var note in song.Notes(channel).ToList())
        {
            while (nextChangeIndex < timeline.Count && timeline[nextChangeIndex].Tick <= note.StartTick)
            {
                carriedState = timeline[nextChangeIndex++].State;
            }

            var chain = new SweepChain<TremoloState, NoteTremolo>(CreateTremoloStretch);
            var playingState = carriedState;
            int delayCounter = 0;
            for (int offsetTicks = 0; offsetTicks < note.LengthTicks; offsetTicks++)
            {
                int tick = note.StartTick + offsetTicks;
                while (nextChangeIndex < timeline.Count && timeline[nextChangeIndex].Tick == tick)
                {
                    playingState = timeline[nextChangeIndex++].State;
                }

                // Whether the tremolo runs on this tick: it is on and its delay has run out.
                bool isWavering = playingState.IsActive && delayCounter == playingState.Delay;
                if (playingState.IsActive && !isWavering)
                {
                    delayCounter = (delayCounter + 1) & byte.MaxValue;
                }

                chain.AddTick(offsetTicks, isWavering, playingState);
            }

            chain.Close(note.LengthTicks);
            carriedState = playingState;
            if (chain.Stretches.Count > 0)
            {
                song.SetNoteTremolos(channel, note.StartTick, [.. chain.Stretches]);
            }
        }
    }

    // The tremolo states a channel's $E5s and $FDs put in force, each at the tick it changes what can be heard.
    private static List<(int Tick, TremoloState State)> TremoloTimeline(List<MmlTremolo> commands)
    {
        var timeline = new List<(int Tick, TremoloState State)>();
        var stateInForce = TremoloState.Off;
        foreach (var tickCommands in commands.GroupBy(written => written.Tick).OrderBy(tickGroup => tickGroup.Key))
        {
            var resolvedState = stateInForce;
            foreach (var command in tickCommands)
            {
                resolvedState = new TremoloState(command.Delay ?? resolvedState.Delay, command.Rate ?? resolvedState.Rate, command.Amount);
            }

            resolvedState = resolvedState.Audible();
            if (resolvedState == stateInForce)
            {
                continue;
            }

            timeline.Add((tickCommands.Key, resolvedState));
            stateInForce = resolvedState;
        }

        return timeline;
    }

    // A vibrato stretch of a delay and a length, under a state.
    private static NoteVibrato CreateVibratoStretch(int delayTicks, int lengthTicks, VibratoState state)
    {
        return new NoteVibrato(delayTicks, lengthTicks, state.Rate, state.Extent, state.FadeTicks);
    }

    // A tremolo stretch of a delay and a length, under a state.
    private static NoteTremolo CreateTremoloStretch(int delayTicks, int lengthTicks, TremoloState state)
    {
        return new NoteTremolo(delayTicks, lengthTicks, state.Rate, state.Amount);
    }

    // One channel's vibrato walked against its notes, holding what the driver's sweep bytes, its fade counter and its delay counter hold.
    private sealed class VibratoWalk
    {
        private readonly Song _song;
        private readonly int _channel;
        private readonly List<MmlEvent> _commands;
        private readonly List<(int Channel, int Tick)> _secondFades;
        private int _nextCommandIndex;
        private VibratoState _declaredState = VibratoState.Off;
        private int _liveExtent = SnesVibrato.MinExtent;
        private int _fadeSteps;
        private int _delayCounter;
        private bool _isInsideNote;

        // What the note sounding plays where that is not the declared state, which is only after a second fade on it was dropped.
        private VibratoState? _heardState;

        // A walk at the channel's start, with no vibrato in force.
        public VibratoWalk(Song song, int channel, List<MmlEvent> commands, List<(int Channel, int Tick)> secondFades)
        {
            _song = song;
            _channel = channel;
            _commands = commands.OrderBy(command => command.Tick).ToList();
            _secondFades = secondFades;
        }

        // Puts the stretches each note sweeps under on it.
        public void Run()
        {
            foreach (var note in _song.Notes(_channel).ToList())
            {
                SweepNote(note);
            }
        }

        // Keys a note on under the commands in force at its start, walks it a tick at a time, and puts the stretches it sweeps under on it.
        private void SweepNote(Note note)
        {
            while (_nextCommandIndex < _commands.Count && _commands[_nextCommandIndex].Tick <= note.StartTick)
            {
                ApplyCommand(_commands[_nextCommandIndex++]);
            }

            _isInsideNote = true;

            _fadeSteps = 0;
            _delayCounter = 0;
            if (_heardState is not null)
            {
                _heardState = null;
                _liveExtent = _declaredState.Extent;
            }

            var chain = new SweepChain<VibratoState, NoteVibrato>(CreateVibratoStretch);
            for (int offsetTicks = 0; offsetTicks < note.LengthTicks; offsetTicks++)
            {
                int tick = note.StartTick + offsetTicks;
                while (_nextCommandIndex < _commands.Count && _commands[_nextCommandIndex].Tick == tick)
                {
                    ApplyCommand(_commands[_nextCommandIndex++]);
                }

                var playingState = _heardState ?? _declaredState;
                bool isSweeping = StepTick(playingState);
                chain.AddTick(offsetTicks, isSweeping, playingState);
            }

            _isInsideNote = false;
            chain.Close(note.LengthTicks);
            if (chain.Stretches.Count > 0)
            {
                _song.SetNoteVibratos(_channel, note.StartTick, [.. chain.Stretches]);
            }
        }

        // Steps the delay and fade counters through one tick of a note playing a state, and returns whether the sweep moves the pitch on it.
        private bool StepTick(VibratoState playingState)
        {
            if (_liveExtent == SnesVibrato.MinExtent)
            {
                return false;
            }

            if (_delayCounter != playingState.Delay)
            {
                _delayCounter = (_delayCounter + 1) & byte.MaxValue;
                return false;
            }

            if (!playingState.HasFade)
            {
                return true;
            }

            if (_fadeSteps < playingState.FadeTicks)
            {
                _fadeSteps++;
            }

            _liveExtent = SnesVibrato.FadedExtent(playingState.Extent, playingState.FadeTicks, _fadeSteps);
            return _liveExtent != SnesVibrato.MinExtent;
        }

        // Applies one command, written inside a sounding note or in the rest ahead of one.
        private void ApplyCommand(MmlEvent command)
        {
            switch (command)
            {
                case MmlVibrato vibrato:
                    ApplySet(vibrato);
                    break;

                case MmlVibratoFade fade:
                    ApplyFade(fade);
                    break;
            }
        }

        // A p: its three bytes written outright, cancelling the fade behind the last one.
        private void ApplySet(MmlVibrato vibrato)
        {
            _declaredState = new VibratoState(vibrato.Delay ?? _declaredState.Delay, vibrato.Rate ?? _declaredState.Rate, vibrato.Extent, SnesVibrato.MinFadeTicks);
            _liveExtent = vibrato.Extent;
            _heardState = null;
        }

        // An $EA: a length of zero switches the fade off, a second fade on one note is dropped, and any other arms the fade, refusing one where the sweep
        // in front was cut off part-way through its own.
        private void ApplyFade(MmlVibratoFade fade)
        {
            if (fade.FadeTicks == SnesVibrato.MinFadeTicks)
            {
                SwitchFadeOff();
                return;
            }

            if (_isInsideNote && _fadeSteps != 0)
            {
                DropSecondFade(fade);
            }
            else if (_liveExtent != _declaredState.Extent)
            {
                throw MmlImportException.VibratoFadeAfterCutSweep(fade.Line);
            }

            if (_declaredState.Extent == SnesVibrato.MinExtent)
            {
                return;
            }

            _declaredState = _declaredState with { FadeTicks = fade.FadeTicks };
        }

        // An $EA of length zero: where a fade is in force, the width stays wherever the ramp had got to until the next p.
        private void SwitchFadeOff()
        {
            if (!_declaredState.HasFade)
            {
                return;
            }

            if (_isInsideNote && _fadeSteps >= _declaredState.FadeTicks)
            {
                _heardState ??= _declaredState;
            }

            _declaredState = _declaredState with { Extent = _liveExtent, FadeTicks = SnesVibrato.MinFadeTicks };
        }

        // Drops a second fade from the note it lands in, which plays on as it was while the notes after it fade up to the width it found.
        private void DropSecondFade(MmlVibratoFade fade)
        {
            _secondFades.Add((_channel, fade.Tick));
            _heardState ??= _declaredState;
            _declaredState = _declaredState with { Extent = _liveExtent };
        }
    }

    // A note's sweep stretches built a tick at a time: a tick the sweep runs on under the open stretch's state extends it, any other tick closes it, and a
    // running tick under another state opens a new one.
    private sealed class SweepChain<TState, TStretch> where TState : struct, IEquatable<TState>
    {
        private readonly Func<int, int, TState, TStretch> _createStretch;
        private readonly List<TStretch> _stretches = [];
        private int _previousEndOffsetTicks;
        private int? _openStartOffsetTicks;
        private TState _openState;

        // An empty chain, each stretch made from its delay, its length and the state it runs under.
        public SweepChain(Func<int, int, TState, TStretch> createStretch)
        {
            _createStretch = createStretch;
        }

        // The stretches closed so far, each delay counted from where the one in front ended.
        public IReadOnlyList<TStretch> Stretches
        {
            get
            {
                return _stretches;
            }
        }

        // Records one tick of the note, at an offset from its start, and whether the sweep runs on it under a state.
        public void AddTick(int offsetTicks, bool isSweeping, TState state)
        {
            if (isSweeping && _openStartOffsetTicks is not null && state.Equals(_openState))
            {
                return;
            }

            Close(offsetTicks);
            if (isSweeping)
            {
                _openStartOffsetTicks = offsetTicks;
                _openState = state;
            }
        }

        // Closes the open stretch, where there is one, at an offset from the note's start.
        public void Close(int endOffsetTicks)
        {
            if (_openStartOffsetTicks is not { } openStartOffsetTicks)
            {
                return;
            }

            _stretches.Add(_createStretch(openStartOffsetTicks - _previousEndOffsetTicks, endOffsetTicks - openStartOffsetTicks, _openState));
            _previousEndOffsetTicks = endOffsetTicks;
            _openStartOffsetTicks = null;
        }
    }
}
