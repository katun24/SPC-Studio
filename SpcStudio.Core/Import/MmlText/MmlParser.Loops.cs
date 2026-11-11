using System.Collections.Immutable;
using SpcStudio.Model;

namespace SpcStudio.Import;

public static partial class MmlParser
{
    private sealed partial class Parser
    {
        private const int SubLoopOpenArgument = 0;
        private const int OnePass = 1;
        private const int MaxLoopLabel = 0xFFFE;
        private const int NoLoopLabel = -1;

        private static readonly ValueRange LoopCountRange = new(1, byte.MaxValue);

        // Reads a /, the loop point.
        private void ReadLoopMark()
        {
            int line = StepOverCommandStart();
            if (_openLoopBodies.Count > 0)
            {
                throw MmlImportException.LoopPointInsideLoop(_line);
            }

            var channel = CurrentChannel(line);
            channel.Events.Add(new MmlLoopMark(channel.EndTick, line));
            channel.ForgetLastDurationByte();
        }

        // Reads a [ that opens a loop or a [[ that opens a subloop, each recording what follows until it is closed.
        private void ReadLoopOpen()
        {
            int line = StepOverCommandStart();
            var channel = CurrentChannel(line);
            if (TryConsume('['))
            {
                if (IsCharacterAt(_position, '['))
                {
                    throw MmlImportException.TripleOpeningBracket(_line);
                }

                OpenSubLoop(isWrittenAsHex: false, line);
                return;
            }

            if (!IsInChannelStream)
            {
                throw MmlImportException.LoopInsideLoop(_line);
            }

            int label = NoLoopLabel;
            if (_pendingLabel is { } pendingLabel)
            {
                if (_loopBodiesByLabel.ContainsKey(pendingLabel))
                {
                    throw MmlImportException.LoopLabelDefinedTwice(pendingLabel, _line);
                }

                label = pendingLabel;
                _pendingLabel = null;
            }

            _openLoopBodies.Push(new OpenLoopBody(false, channel.EndTick, label, line)
            {
                InstrumentNumberInForceAtStart = channel.InstrumentNumberInForce,
                IsTuningIgnoredAtStart = channel.IsTuningIgnored,
            });
        }

        // Reads a ]n or a ]]n, closing whichever is open: a remote code definition, a subloop or a loop.
        private void ReadLoopClose()
        {
            int line = StepOverCommandStart();
            bool isSubloop = TryConsume(']');
            if (isSubloop && IsCharacterAt(_position, ']'))
            {
                throw MmlImportException.TripleClosingBracket(_line);
            }

            int writtenCount = ReadInt();
            if (_remoteDefinitionChannel is { } remoteDefinition)
            {
                if (isSubloop)
                {
                    throw MmlImportException.SubloopCloseEndsRemoteDefinition(_line);
                }

                CloseRemoteDefinition(remoteDefinition, writtenCount);
                return;
            }

            if (isSubloop)
            {
                CloseSubLoopBracket(writtenCount, line);
                return;
            }

            CloseLoopBracket(writtenCount, line);
        }

        // Closes the remote code definition being read, which takes no repeat count, and keeps its events under its label.
        private void CloseRemoteDefinition(ChannelState remoteDefinition, int writtenCount)
        {
            if (writtenCount >= 0)
            {
                throw MmlImportException.RemoteDefinitionRepeatCount(_line);
            }

            _remoteCodeBodiesByLabel[_remoteDefinitionLabel] = remoteDefinition.Events;
            _remoteDefinitionChannel = null;
        }

        // Closes the open subloop at a ]], whose repeat count is required and cannot be one.
        private void CloseSubLoopBracket(int writtenCount, int line)
        {
            if (writtenCount < 0)
            {
                throw MmlImportException.SubloopRepeatCountMissing(_line);
            }

            if (writtenCount == OnePass)
            {
                throw MmlImportException.SubloopRepeatsOnce(_line);
            }

            CloseBody(SubLoopToClose(isWrittenAsHex: false), writtenCount, line);
        }

        // Closes the open loop at a ], one pass where no count is written.
        private void CloseLoopBracket(int writtenCount, int line)
        {
            if (_openLoopBodies.Count == 0)
            {
                throw MmlImportException.LoopCloseWithoutOpen(_line);
            }

            var openBody = _openLoopBodies.Peek();
            if (openBody.IsSubloop)
            {
                throw MmlImportException.LoopCloseEndsSubloop(openBody.IsOpenedByHex, _line);
            }

            CloseBody(openBody, writtenCount < 0 ? OnePass : writtenCount, line);
        }

        // Opens a subloop, written as [[ or as $E6 $00.
        private void OpenSubLoop(bool isWrittenAsHex, int line)
        {
            if (_openLoopBodies.Any(openBody => openBody.IsSubloop))
            {
                throw MmlImportException.SubloopInsideSubloop(isWrittenAsHex, _line);
            }

            if (_pendingLabel is not null)
            {
                throw MmlImportException.LabelLoopDefinesSubloop(isWrittenAsHex, _line);
            }

            _openLoopBodies.Push(new OpenLoopBody(true, CurrentChannel(line).EndTick, NoLoopLabel, line, isWrittenAsHex));
        }

        // The open subloop a ]] or a closing $E6 closes, refusing where none is open.
        private OpenLoopBody SubLoopToClose(bool isWrittenAsHex)
        {
            if (_openLoopBodies.Count == 0)
            {
                throw MmlImportException.SubloopCloseWithoutOpen(isWrittenAsHex, _line);
            }

            var openBody = _openLoopBodies.Peek();
            if (!openBody.IsSubloop)
            {
                throw MmlImportException.SubloopCloseEndsLoop(isWrittenAsHex, _line);
            }

            return openBody;
        }

        // Closes a loop body that many passes over: the clock goes back to where the body began and every pass is written from there.
        private void CloseBody(OpenLoopBody openBody, int passCount, int line)
        {
            RefuseLoopCountOutOfRange(passCount);
            _openLoopBodies.Pop();
            var channel = CurrentChannel(line);
            int passTicks = channel.EndTick - openBody.StartTick;
            var restBytes = TakeReadInBody(channel.RestBytes, tick => tick, (_, tick) => tick, openBody.StartTick, channel.EndTick);
            var restTies = TakeReadInBody(channel.RestTies, tick => tick, (_, tick) => tick, openBody.StartTick, channel.EndTick);

            // The body's own loop edges counted from its start, or none for a subloop.
            List<MmlLoopEdge> bodyLoopEdges = openBody.IsSubloop ? [] : TakeBracketBodyLoopEdges(openBody, channel, passTicks);

            // The ties listed in a [ ] body counted from its start, or none for a subloop, whose own passes list theirs in place.
            List<MmlCutTie> cutTies = openBody.IsSubloop ? [] : TakeReadInBody(channel.CutTies, tie => tie.Tick, (tie, tick) => tie with { Tick = tick }, openBody.StartTick, channel.EndTick);

            // The body as read, its events counted from its start.
            var body = new LoopBody(
                [.. openBody.Events.Select(bodyEvent => bodyEvent with { Tick = bodyEvent.Tick - openBody.StartTick })], passTicks, openBody.HeldNoteIndex, openBody.OpeningTieLine,
                openBody.OpeningTiePieces, openBody.OpeningTieQuantization, restBytes, restTies, cutTies, bodyLoopEdges, openBody.IsSubloop);

            if (openBody.DefinedLabel != NoLoopLabel)
            {
                _loopBodiesByLabel[openBody.DefinedLabel] = body;
            }

            if (openBody.IsSubloop)
            {
                RepeatCutTiesOverPasses(openBody, channel, passCount, passTicks);
                RepeatLoopEdgesOverPasses(openBody, channel, passCount, passTicks, line);
            }
            else
            {
                _previousLoopBody = body;
                channel.InstrumentNumberInForce = openBody.InstrumentNumberInForceAtStart;
                channel.IsTuningIgnored = openBody.IsTuningIgnoredAtStart;
            }

            channel.EndTick = openBody.StartTick;
            Replay(body, passCount, line);
        }

        // Takes what a loop body's own bytes listed out of a channel's list, by the tick each was read at, and returns it moved to count from the body's start.
        private static List<T> TakeReadInBody<T>(List<T> channelItems, Func<T, int> tickOf, Func<T, int, T> atTick, int bodyStartTick, int bodyEndTick)
        {
            // What was read in the body, counted from its start.
            var itemsInBody = channelItems.Where(item => IsReadInBody(tickOf(item), bodyStartTick, bodyEndTick)).Select(item => atTick(item, tickOf(item) - bodyStartTick)).ToList();
            channelItems.RemoveAll(item => IsReadInBody(tickOf(item), bodyStartTick, bodyEndTick));
            return itemsInBody;
        }

        // Ends every loop edge still waiting in a [ ] body on the body's own end, and takes the body's edges out of the channel counted from its start.
        private static List<MmlLoopEdge> TakeBracketBodyLoopEdges(OpenLoopBody openBody, ChannelState channel, int passTicks)
        {
            foreach (int tick in openBody.PendingLoopEdges)
            {
                channel.LoopEdges.Add(new(tick, ReadAheadStop.KeyOff));
            }

            if (passTicks > 0)
            {
                channel.LoopEdges.Add(new(channel.EndTick, ReadAheadStop.KeyOff));
            }

            // The edges that stand in the body, counted from its start.
            List<MmlLoopEdge> bodyLoopEdges = [.. channel.LoopEdges
                .Where(edge => IsEdgeOfBody(edge.Tick, openBody.StartTick, channel.EndTick))
                .Select(edge => edge with { Tick = edge.Tick - openBody.StartTick })];

            channel.LoopEdges.RemoveAll(edge => IsEdgeOfBody(edge.Tick, openBody.StartTick, channel.EndTick));
            return bodyLoopEdges;
        }

        // Whether a tick is one a loop body's own bytes can be read at, from its start up to its end.
        private static bool IsReadInBody(int tick, int bodyStartTick, int bodyEndTick)
        {
            return tick >= bodyStartTick && tick < bodyEndTick;
        }

        // Whether a tick is one a loop body's edges can stand at, from just past its start up to and including its end.
        private static bool IsEdgeOfBody(int tick, int bodyStartTick, int bodyEndTick)
        {
            return tick > bodyStartTick && tick <= bodyEndTick;
        }

        // Copies the ties after a loop that a subloop's first pass listed onto every later pass, and closes the run of them.
        private static void RepeatCutTiesOverPasses(OpenLoopBody subLoop, ChannelState channel, int passCount, int passTicks)
        {
            var cutTiesInFirstPass = channel.CutTies.Where(tie => IsReadInBody(tie.Tick, subLoop.StartTick, channel.EndTick)).ToList();
            for (int repeat = 1; repeat < passCount; repeat++)
            {
                foreach (var tie in cutTiesInFirstPass)
                {
                    channel.CutTies.Add(tie with { Tick = tie.Tick + repeat * passTicks });
                }
            }

            channel.IsLastCutTieOpen = false;
        }

        // Copies the loop edges inside a subloop's first pass, and those still waiting there, onto every later pass, and leaves each pass's end waiting.
        private void RepeatLoopEdgesOverPasses(OpenLoopBody subLoop, ChannelState channel, int passCount, int passTicks, int line)
        {
            var waitingEdges = LoopEdgesWaitingHere(line);
            var loopEdgesInFirstPass = channel.LoopEdges.Where(edge => IsEdgeOfBody(edge.Tick, subLoop.StartTick, channel.EndTick)).ToList();
            var waitingEdgesInFirstPass = waitingEdges.Where(tick => IsEdgeOfBody(tick, subLoop.StartTick, channel.EndTick)).ToList();
            for (int repeat = 1; repeat < passCount; repeat++)
            {
                channel.LoopEdges.AddRange(loopEdgesInFirstPass.Select(edge => edge with { Tick = edge.Tick + repeat * passTicks }));
                waitingEdges.AddRange(waitingEdgesInFirstPass.Select(tick => tick + repeat * passTicks));
            }

            if (passTicks <= 0 || (!_doesReadIntoLoops && subLoop.IsLastByteFromCall))
            {
                return;
            }

            for (int pass = 1; pass <= passCount; pass++)
            {
                waitingEdges.Add(subLoop.StartTick + pass * passTicks);
            }
        }

        // Reads an $E6, which opens a subloop or closes the open one after the passes its argument adds to the first.
        private void ReadSubLoopHex(int line)
        {
            int argument = ReadHexArgument(AmkCommands.Subloop, 0);
            if (argument == SubLoopOpenArgument)
            {
                OpenSubLoop(isWrittenAsHex: true, line);
                return;
            }

            CloseBody(SubLoopToClose(isWrittenAsHex: true), argument + OnePass, line);
        }

        // Reads a (, which is a sample load or a remote code command where one of theirs follows it, and a (n) loop label otherwise.
        private void ReadParenthesized()
        {
            int line = _line;
            if (IsCharacterAt(_position + 1, '"') || IsCharacterAt(_position + 1, '@'))
            {
                ReadSampleLoadLetter(line);
                return;
            }

            if (IsCharacterAt(_position + 1, '!'))
            {
                ReadRemoteCode(line);
                return;
            }

            _position++;
            ReadLoopLabel(line);
        }

        // Reads a (n) label, which defines the loop straight after it or otherwise calls the body it names.
        private void ReadLoopLabel(int line)
        {
            int label = ReadRequiredInt(MmlImportException.LoopLabelNumberMissing);

            if (label > MaxLoopLabel)
            {
                throw MmlImportException.LoopLabelPastHighest(label, MaxLoopLabel, _line);
            }

            if (!TryConsume(')'))
            {
                throw MmlImportException.LoopLabelCloseMissing(label, _line);
            }

            if (!IsInChannelStream)
            {
                throw MmlImportException.LabelLoopInsideLoop(_line);
            }

            if (IsCharacterAt(_position, '['))
            {
                _pendingLabel = label;
                return;
            }

            if (!_loopBodiesByLabel.TryGetValue(label, out var labelBody))
            {
                throw MmlImportException.LoopLabelCalledBeforeDefined(label, _line);
            }

            Replay(labelBody, LoopCallCount(), line);
        }

        // Reads a *n, which repeats the most recent [ ] body, or an empty one where none has been read yet.
        private void ReadStarLoop()
        {
            int line = StepOverCommandStart();
            if (!IsInChannelStream)
            {
                throw MmlImportException.LoopRepeatInsideLoop(_line);
            }

            Replay(_previousLoopBody ?? LoopBody.Empty, LoopCallCount(), line);
        }

        // Reads a loop call's repeat count, one pass where none is written.
        private int LoopCallCount()
        {
            int count = ReadInt();
            if (count < 0)
            {
                return OnePass;
            }

            RefuseLoopCountOutOfRange(count);
            return count;
        }

        // Refuses a loop count outside the driver's range.
        private void RefuseLoopCountOutOfRange(int count)
        {
            if (!LoopCountRange.Contains(count))
            {
                throw MmlImportException.LoopCountOutOfRange(count, LoopCountRange, _line);
            }
        }

        // Writes a loop body into whatever is being read now, that many passes over, a subloop's inline and any other reached by a call.
        private void Replay(LoopBody body, int passCount, int line)
        {
            var channel = CurrentChannel(line);
            var eventsBeingWritten = EventsBeingWritten(line);
            int startTick = channel.EndTick;
            bool doesOpenOnTie = body.OpeningTieLine >= 0;

            // Whether the ties the body opens with are ordinary ties on every pass.
            bool areOpeningTiesOrdinary = _doesReadIntoLoops && doesOpenOnTie;

            // The note in front of the body that the ties it opens with carry on, if any.
            int noteBeforeBodyIndex = NoNote;

            // The subloop this body is played first in, or the [ ] body this subloop is the first sound of, whose opening ties the body's own become, or none.
            var openedBody = doesOpenOnTie && _openLoopBodies.Count > 0 && _openLoopBodies.Peek() is { HasFetchedByte: false } bodyAround && bodyAround.IsSubloop != body.IsSubloop
                ? bodyAround
                : null;

            // The first pass whose opening ties the body settles itself, the opened body settling the first.
            int firstSettledPass = openedBody is null ? 0 : OnePass;

            if (!body.IsSubloop && IsInChannelStream)
            {
                MarkCallInStream(channel, body, startTick, line);
            }

            if (_remoteDefinitionChannel is null)
            {
                ListBodyBytesOverPasses(channel, body, passCount, startTick, areOpeningTiesOrdinary);
            }

            channel.OpenRestByte = RestByteKind.None;
            if (openedBody is { IsSubloop: true })
            {
                TakeOpeningTiesOnto(openedBody, body, body.OpeningTiePieces);
            }

            if (doesOpenOnTie)
            {
                noteBeforeBodyIndex = ListOpeningTies(channel, body, firstSettledPass, passCount, startTick, areOpeningTiesOrdinary, line);
            }

            if (!body.IsSubloop && body.LengthTicks > 0 && !_doesReadIntoLoops)
            {
                channel.MayVoiceBeKeyedOff = true;
            }

            // The index of the last note the passes written so far leave sounding, or what stands in for one.
            int lastNoteIndex = NoNote;

            for (int repeat = 0; repeat < passCount; repeat++)
            {
                int passStartTick = body.PassStartTick(startTick, repeat);
                int passFirstIndex = eventsBeingWritten.Count;

                // The note sounding as the pass begins: the one in front of the body on the first pass, the one the passes before leave on every other.
                int soundingNoteIndex = repeat == 0 ? noteBeforeBodyIndex : lastNoteIndex;
                foreach (var bodyEvent in body.Events)
                {
                    eventsBeingWritten.Add(bodyEvent with { Tick = bodyEvent.Tick + passStartTick });
                }

                // Whether the ties this pass opens with lengthen the note in front of it.
                bool isNoteCarriedOn = doesOpenOnTie && soundingNoteIndex >= 0 && eventsBeingWritten[soundingNoteIndex] is MmlNote noteInFront && noteInFront.EndTick == passStartTick;

                if (isNoteCarriedOn)
                {
                    eventsBeingWritten[soundingNoteIndex] = ((MmlNote)eventsBeingWritten[soundingNoteIndex]).LengthenedBy(body.OpeningTiePieces);
                }
                else if (areOpeningTiesOrdinary && repeat >= firstSettledPass && _remoteDefinitionChannel is null)
                {
                    channel.RestTies.Add(passStartTick);
                }

                if (body.HeldNoteIndex >= 0)
                {
                    lastNoteIndex = passFirstIndex + body.HeldNoteIndex;
                }
                else if (doesOpenOnTie && (_doesReadIntoLoops || body.IsSubloop) && body.OpeningTieTicks == body.LengthTicks)
                {
                    lastNoteIndex = isNoteCarriedOn ? soundingNoteIndex : HoldingRest;
                }
                else if ((_doesReadIntoLoops || body.IsSubloop) && body.HeldNoteIndex == HoldingRest)
                {
                    lastNoteIndex = HoldingRest;
                }
            }

            channel.EndTick += passCount * body.LengthTicks;
            if (body.LengthTicks > 0)
            {
                SetCurrentHeldNoteIndex(line, lastNoteIndex);
                MarkFetched(!body.IsSubloop);
                CloseBodyOpeningTies();
            }

            if (openedBody is { IsSubloop: false })
            {
                GiveOpeningTiesToBodyAround(openedBody, body, passCount);
            }

            channel.IsHeldNoteFromLoop = true;
            channel.IsMergedRunOpen = false;
        }

        // Makes a subloop's opening ties those of the [ ] body it is the first sound of: every pass's and the run kept open for the ties after it where
        // the subloop is ties alone, its first pass's otherwise.
        private static void GiveOpeningTiesToBodyAround(OpenLoopBody bracketBody, LoopBody subloop, int passCount)
        {
            bool isTiesAlone = subloop.OpeningTieTicks == subloop.LengthTicks;
            TakeOpeningTiesOnto(bracketBody, subloop, isTiesAlone ? [.. Enumerable.Repeat(subloop.OpeningTiePieces, passCount).SelectMany(pieces => pieces)] : subloop.OpeningTiePieces);
            bracketBody.IsOpeningTieRunOpen = isTiesAlone;
        }

        // Records the $E9 a call compiles to in the channel's own stream: what the byte in front reads ahead over, and whether the channel would hang here.
        private static void MarkCallInStream(ChannelState channel, LoopBody body, int startTick, int line)
        {
            if (!channel.HasCallSinceByte)
            {
                channel.PendingLoopEdges.Add(startTick);
            }

            channel.HasCallSinceByte = true;
            if (body.LengthTicks > 0)
            {
                channel.HangingCallLine = NoLine;
            }
            else if (startTick > 0 && channel.HangingCallLine < 0)
            {
                channel.HangingCallLine = line;
            }
        }

        // Lists the rest bytes, the ties behind rests, the ties whose key-off is still to be decided and the loop edges a body carries at every pass it is played.
        private static void ListBodyBytesOverPasses(ChannelState channel, LoopBody body, int passCount, int startTick, bool areOpeningTiesOrdinary)
        {
            for (int repeat = 0; repeat < passCount; repeat++)
            {
                int passStartTick = body.PassStartTick(startTick, repeat);
                channel.RestBytes.AddRange(body.RestBytes.Where(tick => !areOpeningTiesOrdinary || tick > 0).Select(tick => passStartTick + tick));
                channel.RestTies.AddRange(body.RestTies.Select(tick => passStartTick + tick));
                channel.CutTies.AddRange(body.CutTies.Select(tie => tie with { Tick = passStartTick + tie.Tick }));
                channel.LoopEdges.AddRange(body.LoopEdges.Select(edge => edge with { Tick = passStartTick + edge.Tick }));
            }
        }

        // Lists the tie each pass of a body that opens on one opens with, from the first pass it settles itself, where its key-off is still to be decided, and returns the note in front.
        private int ListOpeningTies(ChannelState channel, LoopBody body, int firstSettledPass, int passCount, int startTick, bool areOpeningTiesOrdinary, int line)
        {
            if (!areOpeningTiesOrdinary)
            {
                for (int repeat = firstSettledPass; repeat < passCount; repeat++)
                {
                    channel.CutTies.Add(new MmlCutTie(body.PassStartTick(startTick, repeat), body.OpeningTieTicks, false, body.OpeningTieLine, body.OpeningTieQuantization));
                }
            }

            return CurrentHeldNoteIndex(line);
        }

        // Makes the ties a body opens with, those bytes of them, the opening ties of the body it opens: the subloop it is played first in, or the [ ] body
        // it is the first sound of.
        private static void TakeOpeningTiesOnto(OpenLoopBody openedBody, LoopBody body, ImmutableArray<int> openingTiePieces)
        {
            openedBody.OpeningTieLine = body.OpeningTieLine;
            openedBody.OpeningTiePieces = openingTiePieces;
            openedBody.OpeningTieQuantization = body.OpeningTieQuantization;
        }

        // The loop edges waiting on the next note, rest or tie where the text is being read now: an open [ ] body's, or the channel's own.
        private List<int> LoopEdgesWaitingHere(int line)
        {
            return _openLoopBodies.FirstOrDefault(openBody => !openBody.IsSubloop) is { } bracketBody ? bracketBody.PendingLoopEdges : CurrentChannel(line).PendingLoopEdges;
        }

        // Settles every loop edge waiting here on the note, rest or tie just read, which in the channel's own stream also ends a run of calls.
        private void ResolveWaitingLoopEdges(int line, ReadAheadStop foundStop)
        {
            if (_remoteDefinitionChannel is not null)
            {
                return;
            }

            var channel = CurrentChannel(line);
            var waitingEdges = LoopEdgesWaitingHere(line);
            foreach (int tick in waitingEdges)
            {
                channel.LoopEdges.Add(new(tick, foundStop));
            }

            waitingEdges.Clear();
            if (IsInChannelStream)
            {
                channel.HasCallSinceByte = false;
                channel.HangingCallLine = NoLine;
            }
        }

        // A channel's loop edges once it is read, each edge still waiting at the stream's end finding the key-off there.
        private static List<MmlLoopEdge> LoopEdgesWithStreamEnd(ChannelState channel)
        {
            return [.. channel.LoopEdges.Concat(channel.PendingLoopEdges.Select(tick => new MmlLoopEdge(tick, ReadAheadStop.KeyOff))).Distinct().OrderBy(edge => edge.Tick)];
        }
    }
}
