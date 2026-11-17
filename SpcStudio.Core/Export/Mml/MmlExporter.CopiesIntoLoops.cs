namespace SpcStudio.Export;

// Turns a song into an AddmusicK MML file. This part holds a phrase written out once right in front of a loop of itself, taken into the loop as one pass
// more where it lacks only sets at the start of the loop's body that restate what is in force there.
public static partial class MmlExporter
{
    // Repeated phrases written once and played again. This part takes the copy in front of a loop into the loop.
    private static partial class LoopCompression
    {
        // Every channel's body with each copy written out right in front of a loop of itself taken into the loop's count, each channel's header read
        // for what is in force at its start.
        public static List<string> WithCopiesTakenIntoLoops(IReadOnlyList<(string Header, string Body)> channels)
        {
            var tokensByChannel = channels.Select(channel => WrittenTokens.Split(channel.Body)).ToList();
            var labelBodies = LabelBodiesOf(tokensByChannel);
            return [.. channels.Select((channel, channelIndex) => string.Join(' ', WithCopiesTaken(WrittenTokens.Split(channel.Header), tokensByChannel[channelIndex], labelBodies)))];
        }

        // The body of every label the channels define, by its number.
        private static Dictionary<int, List<string>> LabelBodiesOf(List<List<string>> tokensByChannel)
        {
            var labelBodies = new Dictionary<int, List<string>>();

            foreach (var tokens in tokensByChannel)
            {
                for (int tokenIndex = 0; tokenIndex < tokens.Count; tokenIndex++)
                {
                    if (WrittenTokens.LabelOf(tokens[tokenIndex]) is (int number, true))
                    {
                        labelBodies[number] = tokens[(tokenIndex + 1)..WrittenTokens.LoopEndIndex(tokens, tokenIndex)];
                    }
                }
            }

            return labelBodies;
        }

        // A channel's tokens with each copy in front of a loop of itself taken into the loop.
        private static List<string> WithCopiesTaken(List<string> headerTokens, List<string> bodyTokens, Dictionary<int, List<string>> labelBodies)
        {
            var tokens = new List<string>(bodyTokens);

            for (int loopIndex = 0; loopIndex < tokens.Count; loopIndex++)
            {
                if (LoopAt(tokens, loopIndex, labelBodies) is not var (loopBody, countIndex) || WrittenTokens.LoopCountOf(tokens[countIndex]) >= MaxLoopCount)
                {
                    continue;
                }

                int copyEnd = loopIndex;
                while (copyEnd > 0 && WrittenTokens.IsOctaveMark(tokens[copyEnd - 1]))
                {
                    copyEnd--;
                }

                if (CopyLengthInFront(headerTokens, tokens, copyEnd, loopBody) is not int copyLength)
                {
                    continue;
                }

                tokens[countIndex] = WithCountRaised(tokens[countIndex]);
                tokens.RemoveRange(copyEnd - copyLength, copyLength);
                loopIndex -= copyLength;
            }

            return tokens;
        }

        // The body a loop at a token plays and the index of the token holding its count, or null where no loop opens or is called there.
        private static (List<string> Body, int CountIndex)? LoopAt(List<string> tokens, int tokenIndex, Dictionary<int, List<string>> labelBodies)
        {
            string token = tokens[tokenIndex];
            if (WrittenTokens.IsLoopOpening(token))
            {
                int closeIndex = WrittenTokens.LoopEndIndex(tokens, tokenIndex);
                return (tokens[(tokenIndex + 1)..closeIndex], closeIndex);
            }

            if (WrittenTokens.LabelOf(token) is (int number, false) && labelBodies.TryGetValue(number, out var body))
            {
                return (body, tokenIndex);
            }

            return null;
        }

        // How many tokens straight in front of an index are a copy of a loop's body, the body played from past its leading sets that restate what is in
        // force where the copy starts, or null where none are.
        private static int? CopyLengthInFront(List<string> headerTokens, List<string> tokens, int copyEnd, List<string> loopBody)
        {
            int leadingSetCount = loopBody.TakeWhile(IsSetOfFollowedLane).Count();

            for (int skippedSetCount = 0; skippedSetCount <= leadingSetCount; skippedSetCount++)
            {
                int copyLength = loopBody.Count - skippedSetCount;
                int copyStart = copyEnd - copyLength;
                if (copyLength == 0 || copyStart < 0 || !tokens.Skip(copyStart).Take(copyLength).SequenceEqual(loopBody.Skip(skippedSetCount)))
                {
                    continue;
                }

                var tokensBeforeCopy = headerTokens.Concat(tokens.Take(copyStart)).ToList();
                if (loopBody.Take(skippedSetCount).All(set => WrittenTokens.IsQ(set) || SetInForceAtEnd(tokensBeforeCopy, set) == set))
                {
                    return copyLength;
                }
            }

            return null;
        }

        // Whether a token sets a lane whose value in force the copies are checked against: the channel's volume or pan, or the q.
        private static bool IsSetOfFollowedLane(string token)
        {
            return (WrittenTokens.IsLettered(token, 'v') && !token.Contains(',')) || WrittenTokens.IsLettered(token, 'y') || WrittenTokens.IsQ(token);
        }

        // The last set of a volume's or pan's lane read before the end of a channel's tokens, the value song load leaves where none is, or null where a loop
        // around the end or the loop marker stands between, the sets inside a loop closed before the end read as its last pass leaves them.
        private static string? SetInForceAtEnd(List<string> tokens, string set)
        {
            char lane = set[0];
            int closedLoopDepth = 0;

            for (int tokenIndex = tokens.Count - 1; tokenIndex >= 0; tokenIndex--)
            {
                string token = tokens[tokenIndex];
                if (token == WrittenTokens.LoopMarker || (WrittenTokens.LabelOf(token) is (_, false)))
                {
                    return null;
                }

                if (WrittenTokens.IsSubloopClose(token) || WrittenTokens.IsLabelDefinitionClose(token))
                {
                    closedLoopDepth++;
                }
                else if (WrittenTokens.IsLoopOpening(token))
                {
                    if (closedLoopDepth == 0)
                    {
                        return null;
                    }

                    closedLoopDepth--;
                }
                else if (token[0] == lane && IsSetOfFollowedLane(token))
                {
                    return token;
                }
            }

            return DriverStartValuesByLetter.GetValueOrDefault(lane);
        }

        // A loop's close or call written to play it once more.
        private static string WithCountRaised(string countToken)
        {
            return WrittenTokens.WithoutLoopCount(countToken) + WrittenCount(WrittenTokens.LoopCountOf(countToken) + 1);
        }
    }
}
