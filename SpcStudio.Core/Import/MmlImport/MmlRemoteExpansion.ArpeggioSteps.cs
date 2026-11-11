using SpcStudio.Model;

namespace SpcStudio.Import;

internal static partial class MmlRemoteExpansion
{
    // A channel's arpeggio key-ons as the walk reads them: the notes that key nothing on, the steps that key on in their place, read once each in tick order,
    // and the bytes the channel fetches, which a timer reloaded at a step counts through.
    private sealed class ArpeggioSteps
    {
        private readonly ArpeggioKeyOns _keyOns;
        private readonly SortedSet<int> _fetchTicks;
        private int _nextStepIndex;

        // The steps of one channel, over the bytes its notes and rests are fetched as.
        public ArpeggioSteps(MmlChannel channel, ArpeggioKeyOns keyOns, RestSplits restSplits)
        {
            _keyOns = keyOns;
            _fetchTicks = FetchTicks(channel, restSplits);
        }

        // Whether the note at a tick is silenced by the arpeggio, keying nothing on.
        public bool IsSilenced(int noteTick)
        {
            return _keyOns.SilencedNoteTicks.Contains(noteTick);
        }

        // The steps before a tick not yet read, each handed out once.
        public List<int> TakeStepsBefore(int tick)
        {
            List<int> stepTicks = [];
            for (; _nextStepIndex < _keyOns.StepTicks.Count && _keyOns.StepTicks[_nextStepIndex] < tick; _nextStepIndex++)
            {
                stepTicks.Add(_keyOns.StepTicks[_nextStepIndex]);
            }

            return stepTicks;
        }

        // The first step at or after a tick, or null where none is.
        public int? FirstStepFrom(int tick)
        {
            int stepCount = SortedSearch.CountLeading(_keyOns.StepTicks, stepTick => stepTick < tick);
            return stepCount == _keyOns.StepTicks.Count ? null : _keyOns.StepTicks[stepCount];
        }

        // The tick a timer counts up to before the first step at or after a tick reloads it - the step's own tick counted in where no byte is fetched on it - or null where no step is there.
        public int? StepReachFrom(int tick)
        {
            if (FirstStepFrom(tick) is not { } stepTick)
            {
                return null;
            }

            return _fetchTicks.Contains(stepTick) ? stepTick : stepTick + 1;
        }

        // The bytes counted from one tick to another, the first cut short where the first tick falls inside a byte and the last where the second does.
        public List<int> PiecesBetween(int fromTick, int toTick)
        {
            List<int> pieces = [];
            int pieceStartTick = fromTick;
            if (fromTick + 1 <= toTick - 1)
            {
                foreach (int fetchTick in _fetchTicks.GetViewBetween(fromTick + 1, toTick - 1))
                {
                    pieces.Add(fetchTick - pieceStartTick);
                    pieceStartTick = fetchTick;
                }
            }

            if (toTick > pieceStartTick)
            {
                pieces.Add(toTick - pieceStartTick);
            }

            return pieces;
        }

        // Every tick a channel fetches a byte on: each of its notes' bytes, and each of its rests' bytes.
        private static SortedSet<int> FetchTicks(MmlChannel channel, RestSplits restSplits)
        {
            SortedSet<int> fetchTicks = [];

            // Adds the fetch of every byte of a run laid from a tick.
            void AddFetches(int startTick, IEnumerable<int> pieces)
            {
                int fetchTick = startTick;
                foreach (int pieceTicks in pieces)
                {
                    fetchTicks.Add(fetchTick);
                    fetchTick += pieceTicks;
                }
            }

            int cursorTick = 0;
            for (int eventIndex = 0; eventIndex < channel.Events.Count; eventIndex++)
            {
                var channelEvent = channel.Events[eventIndex];
                if (channelEvent.Tick > cursorTick)
                {
                    AddFetches(cursorTick, RestPieces(channel, eventIndex - 1, restSplits, cursorTick, channelEvent.Tick));
                    cursorTick = channelEvent.Tick;
                }

                if (channelEvent is MmlNote note)
                {
                    AddFetches(note.Tick, note.Pieces);
                    cursorTick = note.EndTick;
                }
            }

            AddFetches(cursorTick, RestPieces(channel, channel.Events.Count - 1, restSplits, cursorTick, channel.EndTick));
            return fetchTicks;
        }
    }
}
