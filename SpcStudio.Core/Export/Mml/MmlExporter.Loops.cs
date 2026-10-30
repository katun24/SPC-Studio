using System.Text;
using SpcStudio.Model;

namespace SpcStudio.Export;

// Turns a song into an AddmusicK MML file. This part holds the loop pass: repeated phrases written once as label loops and subloops.
public static partial class MmlExporter
{
    // Repeated phrases written once and played again: a phrase used in more than one place becomes a label loop, and repeats directly following each other become a subloop.
    private static class LoopCompression
    {
        private const int SubloopEdgeCount = 2;
        private const int EdgeRestatementBytes = 2;
        private const int MaxSubloopUnitEvents = 64;
        private const int MaxLoopCount = byte.MaxValue;
        private const int StartQ = 0x7F;
        private const int NoDurationTicks = -1;
        private const int NoId = -1;
        private const int LowestStatedOctave = 1;
        private const int HighestStatedOctave = 6;
        private const int SortKeyRankShift = 32;
        private const string HoldArmedKeyMark = "H";
        private const string LegatoBitSetKeyMark = "L";
        private const string LegatoHoldArmedKeyMark = "K";

        private static readonly int SubloopBytes = SubloopEdgeCount * SongDataSize.SubloopEdgeBytes;

        // Every channel's body with its loops written, and how many labels they number from 1.
        public static (List<string> Bodies, int LabelCount) Apply(IReadOnlyList<(string Header, string Body)> channels)
        {
            var segmentsByChannel = ParseChannels(channels);
            var segments = segmentsByChannel.SelectMany(channelSegments => channelSegments).ToList();

            FindLabels(segments);

            foreach (var label in segments.SelectMany(LabelsCalledIn).Distinct().ToList())
            {
                label.BodyItems = WithSubloops(label.BodyItems);
            }

            foreach (var segment in segments)
            {
                segment.Items = WithSubloops(segment.Items);
            }

            return WriteChannels(channels, segmentsByChannel);
        }

        // A body with no space inside a loop's brackets, two brackets side by side keeping theirs.
        public static string WithoutSpacesInsideBrackets(string body)
        {
            var tokens = body.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var text = new StringBuilder();

            for (int tokenIndex = 0; tokenIndex < tokens.Length; tokenIndex++)
            {
                if (tokenIndex > 0)
                {
                    string tokenBefore = tokens[tokenIndex - 1];
                    string token = tokens[tokenIndex];
                    bool isAfterOpening = tokenBefore.EndsWith('[') && token[0] != '[';
                    bool isClosing = token[0] == ']' && tokenBefore[0] != ']';

                    if (!isAfterOpening && !isClosing)
                    {
                        text.Append(' ');
                    }
                }

                text.Append(tokens[tokenIndex]);
            }

            return text.ToString();
        }

        // Every channel's body with each label's body taken out into a phrase, in label order.
        public static (List<string> Bodies, List<Phrase> Phrases) WithPhrasesNamed(IReadOnlyList<string> bodies)
        {
            var textsByLabel = new SortedDictionary<int, string>();
            var channelsByLabel = new Dictionary<int, HashSet<int>>();
            var namedBodies = new List<string>();

            for (int channelIndex = 0; channelIndex < bodies.Count; channelIndex++)
            {
                var tokens = bodies[channelIndex].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var output = new List<string>();

                for (int tokenIndex = 0; tokenIndex < tokens.Length; tokenIndex++)
                {
                    output.Add(tokens[tokenIndex]);

                    if (WrittenTokens.LabelOf(tokens[tokenIndex]) is not var (labelNumber, isDefinition))
                    {
                        continue;
                    }

                    if (!channelsByLabel.TryGetValue(labelNumber, out var channels))
                    {
                        channelsByLabel[labelNumber] = channels = [];
                    }

                    channels.Add(channelIndex);

                    if (!isDefinition)
                    {
                        continue;
                    }

                    int endIndex = LabelDefinitionEnd(tokens, tokenIndex);
                    textsByLabel[labelNumber] = WithoutSpacesInsideBrackets(string.Join(' ', tokens[(tokenIndex + 1)..endIndex]));
                    output.Add(Phrase.NameFor(labelNumber));
                    output.Add(tokens[endIndex]);
                    tokenIndex = endIndex;
                }

                namedBodies.Add(string.Join(' ', output));
            }

            var phrases = textsByLabel.Select(pair => new Phrase(pair.Key, pair.Value, channelsByLabel[pair.Key])).ToList();
            return (namedBodies, phrases);
        }

        // Where the label definition opened at a token closes: the first ']' that is not a subloop's, a label holding at most a subloop and no label.
        private static int LabelDefinitionEnd(string[] tokens, int openIndex)
        {
            int subloopDepth = 0;

            for (int tokenIndex = openIndex + 1; tokenIndex < tokens.Length; tokenIndex++)
            {
                if (tokens[tokenIndex] == WrittenTokens.SubloopOpen)
                {
                    subloopDepth++;
                }
                else if (WrittenTokens.IsSubloopClose(tokens[tokenIndex]))
                {
                    subloopDepth--;
                }
                else if (WrittenTokens.IsLabelDefinitionClose(tokens[tokenIndex]) && subloopDepth == 0)
                {
                    return tokenIndex;
                }
            }

            throw new InvalidOperationException("A label loop's definition is never closed.");
        }

        // The labels a segment calls, directly or from inside a subloop.
        private static IEnumerable<Label> LabelsCalledIn(Segment segment)
        {
            return segment.Items.SelectMany(LabelsCalledBy);
        }

        // The labels an item calls, directly or from inside a subloop.
        private static IEnumerable<Label> LabelsCalledBy(LoopItem item)
        {
            return item switch
            {
                LabelCallItem call => [call.Label],
                SubloopItem subloop => subloop.UnitItems.SelectMany(LabelsCalledBy),
                _ => [],
            };
        }

        // Each channel's segments, walked as AddmusicK's parser walks the file: the octave carried from one channel into the next, and the q each channel's own.
        private static List<List<Segment>> ParseChannels(IReadOnlyList<(string Header, string Body)> channels)
        {
            var position = new ParserPosition();
            var segmentsByChannel = new List<List<Segment>>();

            foreach (var (header, body) in channels)
            {
                position.StartChannel(header);
                segmentsByChannel.Add(ParseChannelBody(WrittenTokens.Split(body), position));
            }

            return segmentsByChannel;
        }

        // One channel body's segments, each a run of events: every note, rest or tie and every command.
        private static List<Segment> ParseChannelBody(List<string> tokens, ParserPosition position)
        {
            var segments = new List<Segment> { new() };
            var commandGroup = new List<PhraseEvent>();
            bool isBehindHeldNote = false;
            int durationTicksInForce = NoDurationTicks;

            for (int tokenIndex = 0; tokenIndex < tokens.Count; tokenIndex++)
            {
                string token = tokens[tokenIndex];
                var segment = segments[^1];

                if (token == WrittenTokens.LoopMarker)
                {
                    EndCommandGroup(segment, commandGroup, !isBehindHeldNote);
                    segments.Add(new Segment());
                    durationTicksInForce = NoDurationTicks;
                    continue;
                }

                if (token == WrittenTokens.BendCommand && commandGroup.Count == 0 && segment.Events.Count > 0)
                {
                    tokenIndex += AttachBend(segment.Events[^1], tokens, tokenIndex) - 1;
                    continue;
                }

                if (WrittenTokens.TicksOf(token) is null)
                {
                    AddToCommandGroup(commandGroup, token, tokenIndex > 0 && IsRawHex(tokens[tokenIndex - 1]), position);
                    continue;
                }

                bool canGroupHaveEdge = !isBehindHeldNote && token[0] != '^';
                var (phraseEvent, lastTokenIndex) = ReadEvent(tokens, tokenIndex, position, ref durationTicksInForce);

                phraseEvent.CanHaveEdgeBefore = canGroupHaveEdge;
                EndCommandGroup(segment, commandGroup, canGroupHaveEdge);
                segment.Events.Add(phraseEvent);
                isBehindHeldNote = position.IsHoldArmed && token[0] != 'r';
                tokenIndex = lastTokenIndex;
            }

            EndCommandGroup(segments[^1], commandGroup, !isBehindHeldNote);
            foreach (var segment in segments)
            {
                segment.Items = [.. segment.Events.Select(phraseEvent => (LoopItem)new EventItem(phraseEvent))];
            }

            return segments;
        }

        // Puts a bend and its arguments behind the event in front of it, where the read-ahead finds it, and answers how many tokens it took.
        private static int AttachBend(PhraseEvent phraseEvent, List<string> tokens, int bendIndex)
        {
            var bendTokens = tokens.Skip(bendIndex).Take(WrittenTokens.BendTokenCount).ToList();

            foreach (string bendToken in bendTokens)
            {
                AppendCommandToken(phraseEvent, bendToken);
            }

            return bendTokens.Count;
        }

        // Adds a command to the group in front of the next note, rest or tie as an event of its own, or to the last one where it is a raw hex byte straight behind another.
        private static void AddToCommandGroup(List<PhraseEvent> commandGroup, string token, bool isAfterRawHex, ParserPosition position)
        {
            if (IsRawHex(token) && isAfterRawHex && commandGroup.Count > 0)
            {
                AppendCommandToken(commandGroup[^1], token);
                position.Follow(token);
                return;
            }

            var commandEvent = position.StartEvent();
            commandEvent.IsCommand = true;

            AppendCommandToken(commandEvent, token);
            position.Follow(token);
            commandGroup.Add(commandEvent);
        }

        // Adds a command's token to an event, its key and its bytes.
        private static void AppendCommandToken(PhraseEvent phraseEvent, string token)
        {
            phraseEvent.Tokens.Add(token);
            phraseEvent.CompiledKey += token + " ";
            phraseEvent.EstimatedBytes += WrittenTokens.CompiledBytesOf(token);
        }

        // Moves the commands gathered in front of a note, rest or tie into the segment, an edge standing among them only where one may at their start.
        private static void EndCommandGroup(Segment segment, List<PhraseEvent> commandGroup, bool canGroupHaveEdge)
        {
            foreach (var commandEvent in commandGroup)
            {
                commandEvent.CanHaveEdgeBefore = canGroupHaveEdge;
            }

            segment.Events.AddRange(commandGroup);
            commandGroup.Clear();
        }

        // Whether a token is one raw hex byte, which only belongs with the bytes around it.
        private static bool IsRawHex(string token)
        {
            return token[0] == '$';
        }

        // One note, rest or tie with every piece AddmusicK merges into it, and the index of its last piece.
        private static (PhraseEvent Event, int LastTokenIndex) ReadEvent(List<string> tokens, int firstPieceIndex, ParserPosition position, ref int durationTicksInForce)
        {
            string firstPiece = tokens[firstPieceIndex];
            bool isRest = firstPiece[0] == 'r';
            var phraseEvent = position.StartEvent();
            var compiledKey = new StringBuilder(phraseEvent.CompiledKey);

            int runTicks = 0;
            int lastPieceIndex = firstPieceIndex;
            for (int pieceIndex = firstPieceIndex; pieceIndex < tokens.Count; pieceIndex++)
            {
                string piece = tokens[pieceIndex];
                bool doesMerge = pieceIndex == firstPieceIndex || piece[0] == '^' || (isRest && piece[0] == 'r');
                if (!doesMerge || WrittenTokens.TicksOf(piece) is not int pieceTicks)
                {
                    break;
                }

                phraseEvent.Tokens.Add(piece);
                compiledKey.Append(pieceIndex == firstPieceIndex ? AsNoteValue(piece, position.Octave) : piece).Append(' ');
                runTicks += pieceTicks;
                lastPieceIndex = pieceIndex;
            }

            phraseEvent.EstimatedBytes = EstimatedRunBytes(runTicks, ref durationTicksInForce);
            phraseEvent.CompiledKey = compiledKey.ToString();

            return (phraseEvent, lastPieceIndex);
        }

        // A note written as the note value it compiles to under the octave, its length left as it is, and any other token as it is.
        private static string AsNoteValue(string token, int octave)
        {
            if (!WrittenTokens.IsNote(token))
            {
                return token;
            }

            int noteValue = SnesPitch.NoteValueFor(octave, SnesPitch.SemitoneForLetter(token[0])) + WrittenTokens.AccidentalSemitonesOf(token);
            return $"#{noteValue}{token[WrittenTokens.NoteLengthStartOf(token)..]}";
        }

        // An estimate of a run's bytes inline: the note, rest or tie byte, the duration byte where the length is not the one in force, and for a run too long for one byte the split AddmusicK makes.
        private static int EstimatedRunBytes(int runTicks, ref int durationTicksInForce)
        {
            var pieceTicks = MmlDurationEncoding.SplitRunIntoTiedPieces(runTicks);

            int bytes = pieceTicks.Count > 1 ? pieceTicks.Count + 2 : runTicks == durationTicksInForce ? 1 : 2;

            durationTicksInForce = pieceTicks[^1];
            return bytes;
        }

        // Chooses the label loops greedily over every segment at once and writes each segment's copies as calls, or as a subloop where they all stand back to back.
        private static void FindLabels(List<Segment> segments)
        {
            var finder = new LabelFinder(segments);
            var takenPhrases = finder.TakePhrases();

            ReplaceCopiesWithLoops(segments, finder.Events, takenPhrases);
        }

        // Writes each taken phrase's copies into the segments as calls of one label, or as a subloop where the copies are all in one place.
        private static void ReplaceCopiesWithLoops(List<Segment> segments, List<PhraseEvent?> events, List<(List<int> Starts, int Length)> takenPhrases)
        {
            var loopsByStart = new Dictionary<int, (Label? Label, int Count, int EventCount, List<LoopItem> UnitItems)>();

            foreach (var (starts, length) in takenPhrases)
            {
                var runs = RunsOfAdjacentCopies(starts, length);

                if (runs.Count == 1)
                {
                    var unitItems = Enumerable.Range(starts[0], length).Select(eventIndex => (LoopItem)new EventItem(events[eventIndex]!)).ToList();

                    loopsByStart[runs[0].Start] = (null, runs[0].Count, length * runs[0].Count, unitItems);
                    continue;
                }

                var bodyItems = LabelBodyItems(events, starts, length);
                var label = new Label { BodyItems = bodyItems };
                foreach (var (start, count) in runs)
                {
                    loopsByStart[start] = (label, count, length * count, bodyItems);
                }
            }

            int segmentStart = 0;
            foreach (var segment in segments)
            {
                var items = new List<LoopItem>();

                for (int eventIndex = 0; eventIndex < segment.Events.Count;)
                {
                    if (loopsByStart.TryGetValue(segmentStart + eventIndex, out var loop))
                    {
                        items.Add(loop.Label is { } label ? new LabelCallItem(label, loop.Count) : new SubloopItem(loop.UnitItems, loop.Count));
                        eventIndex += loop.EventCount;
                    }
                    else
                    {
                        items.Add(new EventItem(segment.Events[eventIndex]));
                        eventIndex++;
                    }
                }

                segment.Items = items;
                segmentStart += segment.Events.Count + 1;
            }
        }

        // A phrase's copies grouped into runs of copies directly following each other, each run no longer than a loop can count.
        private static List<(int Start, int Count)> RunsOfAdjacentCopies(List<int> starts, int length)
        {
            var runs = new List<(int Start, int Count)>();

            foreach (int start in starts)
            {
                if (runs.Count > 0 && runs[^1].Start + runs[^1].Count * length == start && runs[^1].Count < MaxLoopCount)
                {
                    runs[^1] = (runs[^1].Start, runs[^1].Count + 1);
                }
                else
                {
                    runs.Add((start, 1));
                }
            }

            return runs;
        }

        // A label's body, each event allowing an edge in front of it only where it does at every place the label is called from.
        private static List<LoopItem> LabelBodyItems(List<PhraseEvent?> events, List<int> starts, int length)
        {
            var bodyItems = new List<LoopItem>();

            for (int offset = 0; offset < length; offset++)
            {
                var phraseEvent = events[starts[0] + offset]!;
                bool canHaveEdge = starts.All(start => events[start + offset]!.CanHaveEdgeBefore);
                bodyItems.Add(new EventItem(canHaveEdge == phraseEvent.CanHaveEdgeBefore ? phraseEvent : phraseEvent.WithEdgeAllowed(canHaveEdge)));
            }

            return bodyItems;
        }

        // The suffixes of a sequence of ids in order, by prefix doubling: each round sorts on the rank of a suffix's first half and of its second.
        private static int[] SuffixArray(List<int> ids)
        {
            int count = ids.Count;
            var suffixArray = new int[count];
            var rank = new int[count];
            var nextRank = new int[count];
            var sortKeys = new long[count];

            for (int index = 0; index < count; index++)
            {
                suffixArray[index] = index;
                rank[index] = ids[index];
            }

            for (int halfLength = 1; ; halfLength <<= 1)
            {
                for (int index = 0; index < count; index++)
                {
                    int suffix = suffixArray[index];
                    sortKeys[index] = ((long)rank[suffix] << SortKeyRankShift) | (uint)(suffix + halfLength < count ? rank[suffix + halfLength] + 1 : 0);
                }

                Array.Sort(sortKeys, suffixArray);

                nextRank[suffixArray[0]] = 0;
                for (int index = 1; index < count; index++)
                {
                    nextRank[suffixArray[index]] = nextRank[suffixArray[index - 1]] + (sortKeys[index] != sortKeys[index - 1] ? 1 : 0);
                }

                Array.Copy(nextRank, rank, count);
                if (rank[suffixArray[count - 1]] == count - 1 || halfLength >= count)
                {
                    break;
                }
            }

            return suffixArray;
        }

        // Kasai's longest common prefix of each suffix with the one before it in the suffix array, none for the first.
        private static int[] LongestCommonPrefixes(List<int> ids, int[] suffixArray)
        {
            int count = ids.Count;
            var rank = new int[count];
            for (int index = 0; index < count; index++)
            {
                rank[suffixArray[index]] = index;
            }

            var longestCommonPrefixes = new int[count];
            int prefixLength = 0;

            for (int suffix = 0; suffix < count; suffix++)
            {
                if (rank[suffix] == 0)
                {
                    prefixLength = 0;
                    continue;
                }

                int suffixBefore = suffixArray[rank[suffix] - 1];
                while (suffix + prefixLength < count && suffixBefore + prefixLength < count && ids[suffix + prefixLength] == ids[suffixBefore + prefixLength])
                {
                    prefixLength++;
                }

                longestCommonPrefixes[rank[suffix]] = prefixLength;
                if (prefixLength > 0)
                {
                    prefixLength--;
                }
            }

            return longestCommonPrefixes;
        }

        // Every interval of the suffix array whose suffixes share a prefix longer than they share with anything outside it: the prefix's length and the interval's two ends.
        private static IEnumerable<(int Length, int FirstRank, int LastRank)> SharedPrefixIntervals(int[] longestCommonPrefixes)
        {
            var openIntervals = new Stack<(int Length, int FirstRank)>();
            openIntervals.Push((0, 0));

            for (int rank = 1; rank <= longestCommonPrefixes.Length; rank++)
            {
                int prefixLength = rank < longestCommonPrefixes.Length ? longestCommonPrefixes[rank] : 0;
                int firstRank = rank - 1;

                while (prefixLength < openIntervals.Peek().Length)
                {
                    var closing = openIntervals.Pop();
                    yield return (closing.Length, closing.FirstRank, rank - 1);
                    firstRank = closing.FirstRank;
                }

                if (prefixLength > openIntervals.Peek().Length)
                {
                    openIntervals.Push((prefixLength, firstRank));
                }
            }
        }

        // A list with its repeats directly following each other taken into subloops, the one saving the most bytes first and again until none saves any.
        private static List<LoopItem> WithSubloops(List<LoopItem> items)
        {
            var idsByKey = new Dictionary<object, int>();

            while (true)
            {
                if (BestSubloop(items, idsByKey) is not { } bestSubloop)
                {
                    return items;
                }

                var subloop = new SubloopItem(items.GetRange(bestSubloop.Start, bestSubloop.UnitLength), bestSubloop.Count);
                items = [.. items.Take(bestSubloop.Start), subloop, .. items.Skip(bestSubloop.Start + bestSubloop.UnitLength * bestSubloop.Count)];
            }
        }

        // The subloop saving the most bytes in a list, or null where none saves any.
        private static (int Start, int UnitLength, int Count)? BestSubloop(List<LoopItem> items, Dictionary<object, int> idsByKey)
        {
            int itemCount = items.Count;
            var ids = new int[itemCount];
            var bytesBefore = new int[itemCount + 1];
            var canHaveEdgeAt = new bool[itemCount + 1];

            for (int itemIndex = 0; itemIndex < itemCount; itemIndex++)
            {
                ids[itemIndex] = SubloopIdOf(items[itemIndex], itemIndex, idsByKey);
                bytesBefore[itemIndex + 1] = bytesBefore[itemIndex] + items[itemIndex] switch
                {
                    EventItem eventItem => eventItem.Event.EstimatedBytes,
                    LabelCallItem => SongDataSize.LabelCallBytes,
                    _ => 0,
                };
                canHaveEdgeAt[itemIndex] = items[itemIndex] is not EventItem { Event.CanHaveEdgeBefore: false };
            }

            canHaveEdgeAt[itemCount] = true;

            (int Start, int UnitLength, int Count, int BytesSaved) best = default;
            var matchingRun = new int[itemCount + 1];

            for (int unitLength = 1; unitLength <= Math.Min(MaxSubloopUnitEvents, itemCount / 2); unitLength++)
            {
                matchingRun[itemCount - unitLength] = 0;
                for (int itemIndex = itemCount - unitLength - 1; itemIndex >= 0; itemIndex--)
                {
                    matchingRun[itemIndex] = ids[itemIndex] >= 0 && ids[itemIndex] == ids[itemIndex + unitLength] ? matchingRun[itemIndex + 1] + 1 : 0;
                }

                for (int start = 0; start + 2 * unitLength <= itemCount; start++)
                {
                    if (matchingRun[start] < unitLength || !canHaveEdgeAt[start])
                    {
                        continue;
                    }

                    int copyCount = CopiesEndingOnEdges(canHaveEdgeAt, start, unitLength, 1 + matchingRun[start] / unitLength);
                    if (copyCount < 2)
                    {
                        continue;
                    }

                    int bytesSaved = bytesBefore[start + copyCount * unitLength] - bytesBefore[start + unitLength] - SubloopBytes - 2 * EdgeRestatementBytes;

                    if (bytesSaved > best.BytesSaved)
                    {
                        best = (start, unitLength, copyCount, bytesSaved);
                    }
                }
            }

            return best.BytesSaved > 0 ? (best.Start, best.UnitLength, best.Count) : null;
        }

        // An item's id for matching repeats, a fresh negative one for an item no subloop can hold.
        private static int SubloopIdOf(LoopItem item, int itemIndex, Dictionary<object, int> idsByKey)
        {
            object? key = item switch
            {
                EventItem eventItem => eventItem.Event.CompiledKey,
                LabelCallItem call when !call.Label.HasSubloop => (call.Label, call.Count),
                _ => null,
            };

            if (key is null)
            {
                return NoId - itemIndex;
            }

            return IdFor(idsByKey, key);
        }

        // The id a key is matched by, the next one unused where the key is new.
        private static int IdFor<TKey>(Dictionary<TKey, int> idsByKey, TKey key) where TKey : notnull
        {
            if (!idsByKey.TryGetValue(key, out int id))
            {
                idsByKey[key] = id = idsByKey.Count;
            }

            return id;
        }

        // How many of the matching copies a subloop can take: each starting where an edge may stand and the last ending at one.
        private static int CopiesEndingOnEdges(bool[] canHaveEdgeAt, int start, int unitLength, int matchingCopies)
        {
            int copyCount = 1;
            while (copyCount < Math.Min(matchingCopies, MaxLoopCount) && canHaveEdgeAt[start + copyCount * unitLength])
            {
                copyCount++;
            }

            while (copyCount > 1 && !canHaveEdgeAt[start + copyCount * unitLength])
            {
                copyCount--;
            }

            return copyCount;
        }

        // Every channel's body written back out with its loops, and how many labels were numbered.
        private static (List<string> Bodies, int LabelCount) WriteChannels(IReadOnlyList<(string Header, string Body)> channels, List<List<Segment>> segmentsByChannel)
        {
            var writer = new LoopWriter();
            var bodies = new List<string>();

            for (int channelIndex = 0; channelIndex < segmentsByChannel.Count; channelIndex++)
            {
                var segments = segmentsByChannel[channelIndex];
                writer.StartChannel(channels[channelIndex].Header);

                var output = new List<string>();
                for (int segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
                {
                    if (segmentIndex > 0)
                    {
                        output.Add(WrittenTokens.LoopMarker);
                    }

                    writer.WriteItems(segments[segmentIndex].Items, output);
                }

                bodies.Add(string.Join(' ', output));
            }

            return (bodies, writer.LabelCount);
        }

        // One label's body written as a replacement, and the channels it is defined or called in.
        public sealed record Phrase(int LabelNumber, string Text, IReadOnlySet<int> ChannelIndexes)
        {
            // The replacement's name, which nothing else the export writes starts with.
            public string Name
            {
                get
                {
                    return NameFor(LabelNumber);
                }
            }

            // Whether more than one channel defines or calls it.
            public bool IsShared
            {
                get
                {
                    return ChannelIndexes.Count > 1;
                }
            }

            // The replacement's name for a label's number.
            public static string NameFor(int labelNumber)
            {
                return $"P{labelNumber}";
            }
        }

        // One note, rest or tie as AddmusicK's parser reads it, with the bends behind it, or one command.
        private sealed class PhraseEvent
        {
            public readonly List<string> Tokens = [];
            public string CompiledKey = "";
            public int EstimatedBytes;
            public bool IsCommand;
            public bool CanHaveEdgeBefore;
            public int OctaveBefore;
            public int QBefore;

            // Whether it is a note, rest or tie.
            public bool IsRun
            {
                get
                {
                    return !IsCommand;
                }
            }

            // Whether it is a note, which keys the voice on.
            public bool IsNote
            {
                get
                {
                    return IsRun && WrittenTokens.IsNote(Tokens[0]);
                }
            }

            // Whether it states or moves the octave.
            public bool IsOctaveMark
            {
                get
                {
                    return IsCommand && WrittenTokens.IsOctaveMark(Tokens[0]);
                }
            }

            // Whether it is a q.
            public bool IsQ
            {
                get
                {
                    return IsCommand && WrittenTokens.IsQ(Tokens[0]);
                }
            }

            // Whether what it does depends on the octave in front of it: a note, or a mark moving the octave from where it is.
            public bool DoesReadOctave
            {
                get
                {
                    return IsNote || (IsCommand && WrittenTokens.IsOctaveShift(Tokens[0]));
                }
            }

            // The same event where an edge may or may not stand in front of it.
            public PhraseEvent WithEdgeAllowed(bool canHaveEdge)
            {
                var copy = new PhraseEvent
                {
                    CompiledKey = CompiledKey,
                    EstimatedBytes = EstimatedBytes,
                    IsCommand = IsCommand,
                    CanHaveEdgeBefore = canHaveEdge,
                    OctaveBefore = OctaveBefore,
                    QBefore = QBefore,
                };
                copy.Tokens.AddRange(Tokens);
                return copy;
            }
        }

        // A stretch of a channel no loop may cross: the part before the loop marker or after it.
        private sealed class Segment
        {
            public readonly List<PhraseEvent> Events = [];
            public List<LoopItem> Items = [];
        }

        // One thing a segment or a loop body is written as: an event, a label's call or a subloop.
        private abstract record LoopItem;

        // An event written as it is.
        private sealed record EventItem(PhraseEvent Event) : LoopItem;

        // A label played that many times.
        private sealed record LabelCallItem(Label Label, int Count) : LoopItem;

        // A subloop playing its unit that many times.
        private sealed record SubloopItem(List<LoopItem> UnitItems, int Count) : LoopItem;

        // A label loop's body and the number it is written under, none until it is first written.
        private sealed class Label
        {
            public List<LoopItem> BodyItems = [];
            public int Number;

            // Whether its body holds a subloop.
            public bool HasSubloop
            {
                get
                {
                    return BodyItems.Any(item => item is SubloopItem);
                }
            }
        }

        // The octave, the q, whether a hold's remote block is armed, and the legato's bit and its key-on block, as AddmusicK's parser and the driver hold them at a point of the text.
        private sealed class ParserPosition
        {
            public int Octave = SnesPitch.MmlStartOctave;
            public int Q;
            public bool IsHoldArmed;
            public bool IsLegatoBitSet;
            public bool IsLegatoHoldArmed;

            // Starts a channel on its own q with no hold armed and the legato off, the octave carrying over from the channel before, and follows its header.
            public void StartChannel(string header)
            {
                Q = StartQ;
                IsHoldArmed = false;
                IsLegatoBitSet = false;
                IsLegatoHoldArmed = false;

                foreach (string token in WrittenTokens.Split(header))
                {
                    Follow(token);
                }
            }

            // What a token does to the octave, the q, a hold's arm, the legato's bit and its key-on block.
            public void Follow(string token)
            {
                if (WrittenTokens.IsOctave(token))
                {
                    Octave = token[1] - '0';
                }
                else if (token == WrittenTokens.OctaveDown)
                {
                    Octave--;
                }
                else if (token == WrittenTokens.OctaveUp)
                {
                    Octave++;
                }
                else if (WrittenTokens.IsQ(token))
                {
                    Q = Convert.ToInt32(token[1..], HexRadix);
                }
                else if (HoldOnName.IsNameOf(token))
                {
                    IsHoldArmed = true;
                }
                else if (HoldOffName.IsNameOf(token))
                {
                    IsHoldArmed = false;
                    IsLegatoHoldArmed = false;
                }
                else if (LegatoName.IsNameOf(token))
                {
                    IsLegatoBitSet = !IsLegatoBitSet;
                }
                else if (token == LegatoHoldArmPlaceholder)
                {
                    IsLegatoHoldArmed = true;
                }
                else if (token == KeyOnClearCommand)
                {
                    IsLegatoHoldArmed = false;
                }
            }

            // A new event under the state here, its key opening on the q in force, whether a hold is armed, and the legato's bit and its key-on block, which every copy of a phrase has to agree on.
            public PhraseEvent StartEvent()
            {
                string holdMark = IsHoldArmed ? HoldArmedKeyMark : "";
                string legatoBitMark = IsLegatoBitSet ? LegatoBitSetKeyMark : "";
                string legatoHoldMark = IsLegatoHoldArmed ? LegatoHoldArmedKeyMark : "";

                return new PhraseEvent
                {
                    OctaveBefore = Octave,
                    QBefore = Q,
                    CompiledKey = $"{Q:X2}{holdMark}{legatoBitMark}{legatoHoldMark}|",
                };
            }
        }

        // The text written out, holding the octave and the q the parser will be under at each point of it, and numbering the labels as it defines them.
        private sealed class LoopWriter
        {
            private readonly ParserPosition _position = new();
            private readonly HashSet<PhraseEvent> _eventsWrittenOutsideTheirLoop = [];

            public int LabelCount { get; private set; }

            // Starts a channel on its own q, following its header, which is written as it was.
            public void StartChannel(string header)
            {
                _position.StartChannel(header);
            }

            // Writes items with the octave and the q each event expects restated in front of it where the parser holds another.
            public void WriteItems(List<LoopItem> items, List<string> output)
            {
                foreach (var item in items)
                {
                    switch (item)
                    {
                        case EventItem { Event: var phraseEvent } when !_eventsWrittenOutsideTheirLoop.Contains(phraseEvent):
                            WriteEvent(phraseEvent, output);
                            break;

                        case LabelCallItem { Label: var label, Count: var count }:
                            WriteLabelCall(label, count, output);
                            break;

                        case SubloopItem { UnitItems: var unitItems, Count: var count }:
                            WriteLoop(unitItems, WrittenTokens.SubloopOpen, WrittenTokens.SubloopClose + WrittenCount(count), output);
                            break;
                    }
                }
            }

            // Writes a token and follows what it does to the parser.
            private void WriteToken(string token, List<string> output)
            {
                _position.Follow(token);
                output.Add(token);
            }

            // Writes an event with the octave and the q it reads restated in front of it where the parser holds others.
            private void WriteEvent(PhraseEvent phraseEvent, List<string> output)
            {
                RestateOctaveFor(phraseEvent, output);
                RestateQFor(phraseEvent, output);

                foreach (string token in phraseEvent.Tokens)
                {
                    WriteToken(token, output);
                }
            }

            // States the octave an event was read under in front of it where it reads the octave and the parser holds another.
            private void RestateOctaveFor(PhraseEvent phraseEvent, List<string> output)
            {
                if (!phraseEvent.DoesReadOctave || _position.Octave == phraseEvent.OctaveBefore)
                {
                    return;
                }

                output.AddRange(StatedOctaveTokens(phraseEvent.OctaveBefore));
                _position.Octave = phraseEvent.OctaveBefore;
            }

            // States the q an event was read under in front of it where it is a note, rest or tie and the parser holds another.
            private void RestateQFor(PhraseEvent phraseEvent, List<string> output)
            {
                if (!phraseEvent.IsRun || _position.Q == phraseEvent.QBefore)
                {
                    return;
                }

                output.Add(WrittenTokens.HexLettered('q', phraseEvent.QBefore));
                _position.Q = phraseEvent.QBefore;
            }

            // Writes a loop with the octave marks ahead of its first note and behind its last, and the q's ahead of its first note, rest or tie and behind
            // its last, just outside it, and the octave and the q its first ones are read under stated in front of it.
            private void WriteLoop(List<LoopItem> bodyItems, string opening, string closing, List<string> output)
            {
                var eventsInReadOrder = EventsInReadOrder(bodyItems, []).ToList();
                var leadingMarks = MarksAheadOfReaders(eventsInReadOrder);
                var trailingMarks = MarksAheadOfReaders(Enumerable.Reverse(eventsInReadOrder).Where(phraseEvent => !leadingMarks.Contains(phraseEvent)));

                trailingMarks.Reverse();
                _eventsWrittenOutsideTheirLoop.UnionWith(leadingMarks);
                _eventsWrittenOutsideTheirLoop.UnionWith(trailingMarks);

                foreach (var mark in leadingMarks)
                {
                    WriteEvent(mark, output);
                }

                if (eventsInReadOrder.Find(phraseEvent => phraseEvent.IsNote) is { } firstNote)
                {
                    RestateOctaveFor(firstNote, output);
                }

                if (eventsInReadOrder.Find(phraseEvent => phraseEvent.IsRun) is { } firstRun)
                {
                    RestateQFor(firstRun, output);
                }

                output.Add(opening);
                WriteItems(bodyItems, output);
                output.Add(closing);

                foreach (var mark in trailingMarks)
                {
                    WriteEvent(mark, output);
                }
            }

            // The events a loop's text holds in the order the parser reads them, reaching into the subloops it opens and the labels it defines, a call of
            // a label written already reading nothing, and an event written outside its loop already left out.
            private IEnumerable<PhraseEvent> EventsInReadOrder(List<LoopItem> items, HashSet<Label> labelsDefined)
            {
                foreach (var item in items)
                {
                    if (item is EventItem { Event: var phraseEvent })
                    {
                        if (!_eventsWrittenOutsideTheirLoop.Contains(phraseEvent))
                        {
                            yield return phraseEvent;
                        }

                        continue;
                    }

                    if (ItemsReadInside(item, labelsDefined) is not { } innerItems)
                    {
                        continue;
                    }

                    foreach (var innerEvent in EventsInReadOrder(innerItems, labelsDefined))
                    {
                        yield return innerEvent;
                    }
                }
            }

            // The items whose text a loop item has the parser read: a subloop's unit, or the body of a label defined there, none for any other call.
            private static List<LoopItem>? ItemsReadInside(LoopItem item, HashSet<Label> labelsDefined)
            {
                return item switch
                {
                    SubloopItem subloop => subloop.UnitItems,
                    LabelCallItem { Label: var label } when label.Number == 0 && labelsDefined.Add(label) => label.BodyItems,
                    _ => null,
                };
            }

            // The octave marks ahead of the first note and the q's ahead of the first note, rest or tie, in the order given.
            private static List<PhraseEvent> MarksAheadOfReaders(IEnumerable<PhraseEvent> events)
            {
                var marks = new List<PhraseEvent>();
                bool hasPassedNote = false;
                bool hasPassedRun = false;

                foreach (var phraseEvent in events)
                {
                    if ((phraseEvent.IsOctaveMark && !hasPassedNote) || (phraseEvent.IsQ && !hasPassedRun))
                    {
                        marks.Add(phraseEvent);
                        continue;
                    }

                    hasPassedNote |= phraseEvent.IsNote;
                    hasPassedRun |= phraseEvent.IsRun;
                }

                return marks;
            }

            // Writes a label's definition where it is first called, numbering it, and a call of it everywhere else.
            private void WriteLabelCall(Label label, int count, List<string> output)
            {
                if (label.Number != 0)
                {
                    output.Add(WrittenTokens.LabelCall(label.Number) + WrittenCount(count));
                    return;
                }

                label.Number = ++LabelCount;
                WriteLoop(label.BodyItems, WrittenTokens.LabelCall(label.Number) + WrittenTokens.LabelDefinitionOpening, "]" + WrittenCount(count), output);
            }

            // A loop's count as written behind its close, nothing where it plays once.
            private static string WrittenCount(int count)
            {
                return count > 1 ? $"{count}" : "";
            }

            // An octave stated outright, held to the ones AddmusicK takes, with the marks reaching past them.
            private static IEnumerable<string> StatedOctaveTokens(int octave)
            {
                int statedOctave = Math.Clamp(octave, LowestStatedOctave, HighestStatedOctave);
                yield return LetteredText('o', statedOctave);

                for (int step = statedOctave; step < octave; step++)
                {
                    yield return WrittenTokens.OctaveUp;
                }

                for (int step = statedOctave; step > octave; step--)
                {
                    yield return WrittenTokens.OctaveDown;
                }
            }
        }

        // Finds the label loops greedily over every segment's events end to end, each round taking the repeated phrase that saves the most bytes.
        private sealed class LabelFinder
        {
            private readonly List<int> _eventIds = [];
            private readonly int[] _bytesBefore;
            private readonly int[] _suffixArray;
            private readonly bool[] _isClaimed;
            private readonly int[] _claimedBefore;

            // Lays every segment's events end to end, each segment closed by a separator with an id nothing else has.
            public LabelFinder(List<Segment> segments)
            {
                var idsByKey = new Dictionary<string, int>();

                foreach (var segment in segments)
                {
                    foreach (var phraseEvent in segment.Events)
                    {
                        Events.Add(phraseEvent);
                        _eventIds.Add(IdFor(idsByKey, phraseEvent.CompiledKey));
                    }

                    Events.Add(null);
                    _eventIds.Add(NoId);
                }

                int nextSeparatorId = idsByKey.Count;
                for (int index = 0; index < _eventIds.Count; index++)
                {
                    if (_eventIds[index] < 0)
                    {
                        _eventIds[index] = nextSeparatorId++;
                    }
                }

                _bytesBefore = new int[_eventIds.Count + 1];
                for (int index = 0; index < _eventIds.Count; index++)
                {
                    _bytesBefore[index + 1] = _bytesBefore[index] + (Events[index]?.EstimatedBytes ?? 0);
                }

                _suffixArray = SuffixArray(_eventIds);
                _isClaimed = new bool[_eventIds.Count];
                _claimedBefore = new int[_eventIds.Count + 1];
            }

            // Every segment's events end to end, a null where one segment ends.
            public List<PhraseEvent?> Events { get; } = [];

            // The phrases taken, each the starts of its copies and its length in events, the most saving first.
            public List<(List<int> Starts, int Length)> TakePhrases()
            {
                var takenPhrases = new List<(List<int> Starts, int Length)>();
                var candidates = Candidates();

                while (candidates.TryDequeue(out var candidate, out _))
                {
                    var (starts, bytesSaved) = Price(candidate.FirstRank, candidate.LastRank, candidate.Length);
                    if (bytesSaved <= 0)
                    {
                        continue;
                    }

                    if (candidates.TryPeek(out _, out int bestPriority) && bytesSaved < -bestPriority)
                    {
                        candidates.Enqueue(candidate, -bytesSaved);
                        continue;
                    }

                    takenPhrases.Add((starts, candidate.Length));
                    Claim(starts, candidate.Length);
                }

                return takenPhrases;
            }

            // Every repeat the suffix array finds, whole and cut back to where an edge may stand in every copy, queued by the most it could save.
            private PriorityQueue<(int FirstRank, int LastRank, int Length), int> Candidates()
            {
                var candidates = new PriorityQueue<(int FirstRank, int LastRank, int Length), int>();

                foreach (var (length, firstRank, lastRank) in SharedPrefixIntervals(LongestCommonPrefixes(_eventIds, _suffixArray)))
                {
                    int firstStart = _suffixArray[firstRank];
                    if (Events[firstStart] is not { CanHaveEdgeBefore: true })
                    {
                        continue;
                    }

                    candidates.Enqueue((firstRank, lastRank, length), -MostBytesSaved(firstRank, lastRank, length));

                    int shorterLength = length - 1;
                    while (shorterLength > 0 && !Events[firstStart + shorterLength]!.CanHaveEdgeBefore)
                    {
                        shorterLength--;
                    }

                    if (shorterLength > 0)
                    {
                        candidates.Enqueue((firstRank, lastRank, shorterLength), -MostBytesSaved(firstRank, lastRank, shorterLength));
                    }
                }

                return candidates;
            }

            // The most a phrase could save, every copy but the smallest counted whether or not it overlaps another.
            private int MostBytesSaved(int firstRank, int lastRank, int length)
            {
                int totalBytes = 0;
                int smallestCopyBytes = int.MaxValue;

                for (int rank = firstRank; rank <= lastRank; rank++)
                {
                    int copyBytes = CopyBytes(_suffixArray[rank], length);
                    totalBytes += copyBytes;
                    smallestCopyBytes = Math.Min(smallestCopyBytes, copyBytes);
                }

                return totalBytes - smallestCopyBytes;
            }

            // The bytes one copy of a phrase takes written out where it stands.
            private int CopyBytes(int start, int length)
            {
                return _bytesBefore[start + length] - _bytesBefore[start];
            }

            // A phrase's copies taken left to right where they overlap neither each other nor a phrase already taken, and what writing them as label calls saves.
            private (List<int> Starts, int BytesSaved) Price(int firstRank, int lastRank, int length)
            {
                var starts = new List<int>();

                for (int rank = firstRank; rank <= lastRank; rank++)
                {
                    int start = _suffixArray[rank];
                    if (!CanHaveEdgeAt(start) || !CanHaveEdgeAt(start + length))
                    {
                        continue;
                    }

                    if (_claimedBefore[start + length] != _claimedBefore[start])
                    {
                        continue;
                    }

                    starts.Add(start);
                }

                starts.Sort();

                var keptStarts = new List<int>();
                foreach (int start in starts)
                {
                    if (keptStarts.Count == 0 || start >= keptStarts[^1] + length)
                    {
                        keptStarts.Add(start);
                    }
                }

                if (keptStarts.Count < 2)
                {
                    return (keptStarts, 0);
                }

                int callPlaceCount = 1;
                for (int index = 1; index < keptStarts.Count; index++)
                {
                    if (keptStarts[index] != keptStarts[index - 1] + length)
                    {
                        callPlaceCount++;
                    }
                }

                int bytesSaved = keptStarts.Skip(1).Sum(start => CopyBytes(start, length))
                                 - SongDataSize.LoopEndBytes
                                 - EdgeRestatementBytes
                                 - callPlaceCount * (SongDataSize.LabelCallBytes + EdgeRestatementBytes);

                return (keptStarts, bytesSaved);
            }

            // Whether a loop edge may stand in front of a position, a segment's end always allowing one.
            private bool CanHaveEdgeAt(int position)
            {
                return Events[position] is not { } phraseEvent || phraseEvent.CanHaveEdgeBefore;
            }

            // Marks a taken phrase's copies so no later phrase overlaps them.
            private void Claim(List<int> starts, int length)
            {
                foreach (int start in starts)
                {
                    for (int position = start; position < start + length; position++)
                    {
                        _isClaimed[position] = true;
                    }
                }

                for (int position = 0; position < _isClaimed.Length; position++)
                {
                    _claimedBefore[position + 1] = _claimedBefore[position] + (_isClaimed[position] ? 1 : 0);
                }
            }
        }
    }
}
