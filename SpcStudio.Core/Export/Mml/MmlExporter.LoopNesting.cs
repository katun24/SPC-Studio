namespace SpcStudio.Export;

// Turns a song into an AddmusicK MML file. This part holds the loops nested inside other loops: the repeats inside a subloop written as labels played
// with their count, and a label holding a subloop written another way where that lets a subloop stand around its calls.
public static partial class MmlExporter
{
    // Repeated phrases written once and played again. This part nests one loop inside another.
    private static partial class LoopCompression
    {
        private const int PlayedOnce = 1;

        // Repeats written as a label of their own played with its count where they stand, which holds events alone.
        private static readonly RepeatLoopKind AsLabelCall = new(LabelBodyKeyOf, LabelBytesOver, (unitItems, count) => new LabelCallItem(new Label { BodyItems = unitItems }, count, null));

        // Takes each segment's repeats into subloops and the repeats inside those into labels, then writes each label holding a subloop in whichever form
        // leaves the segments smallest: as it is, with its subloops written out, or cut at its subloops into labels each call plays in turn.
        private static void TakeSegmentLoops(List<Segment> segments)
        {
            var itemsBeforeLoops = segments.Select(segment => segment.Items).ToList();
            var itemsWithLoops = itemsBeforeLoops.Select(WithSegmentLoops).ToList();
            var labelsHoldingSubloops = itemsBeforeLoops.SelectMany(items => items).SelectMany(LabelsCalledBy).Distinct().Where(label => label.HasSubloop).ToList();

            foreach (var label in labelsHoldingSubloops)
            {
                int leastBytes = EstimatedBytesOf(itemsWithLoops);
                var chosenBeforeLoops = itemsBeforeLoops;
                var chosenWithLoops = itemsWithLoops;
                bool isCalledShifted = itemsBeforeLoops.SelectMany(items => items).Any(item => IsCallOf(item, label) && item is LabelCallItem { Transpose: not null });

                foreach (var callReform in CallReformsOf(label, isCalledShifted))
                {
                    var reformedBeforeLoops = itemsBeforeLoops.Select(items => WithCallsReformed(items, label, callReform)).ToList();
                    var reformedWithLoops = reformedBeforeLoops
                        .Select((items, segmentIndex) => items == itemsBeforeLoops[segmentIndex] ? itemsWithLoops[segmentIndex] : WithSegmentLoops(items))
                        .ToList();

                    int bytes = EstimatedBytesOf(reformedWithLoops);
                    if (bytes < leastBytes)
                    {
                        leastBytes = bytes;
                        chosenBeforeLoops = reformedBeforeLoops;
                        chosenWithLoops = reformedWithLoops;
                    }
                }

                itemsBeforeLoops = chosenBeforeLoops;
                itemsWithLoops = chosenWithLoops;
            }

            for (int segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
            {
                segments[segmentIndex].Items = itemsWithLoops[segmentIndex];
            }
        }

        // A segment's items with its repeats taken into subloops, and the repeats inside each subloop into labels played with their count.
        private static List<LoopItem> WithSegmentLoops(List<LoopItem> items)
        {
            return [.. WithSubloops(items).Select(item => item is SubloopItem subloop ? subloop with { UnitItems = WithRepeatsTaken(subloop.UnitItems, AsLabelCall) } : item)];
        }

        // What an item is matched by in the repeats a label of their own is played for, null where a label's body cannot hold it.
        private static object? LabelBodyKeyOf(LoopItem item)
        {
            return item is EventItem eventItem ? eventItem.Event.CompiledKey : null;
        }

        // The bytes copies of a unit cost written as a label of their own played with its count: the unit once, the label's end, its call and the
        // restatements behind them.
        private static int LabelBytesOver(int unitBytes)
        {
            return unitBytes + LabelDefinitionBytes + CallSiteBytes(NoShift);
        }

        // The ways a call of a label holding a subloop may be written instead: a call of the label with its subloops written out, and the label's body
        // cut at its subloops into parts played in turn.
        private static IEnumerable<Func<LabelCallItem, List<LoopItem>>> CallReformsOf(Label label, bool isCalledShifted)
        {
            var writtenOutLabel = new Label { BodyItems = WrittenOut(label.BodyItems) };
            yield return call => [call with { Label = writtenOutLabel }];

            var parts = PartsCutAtSubloops(label.BodyItems, isCalledShifted);
            yield return call => PartsPlayedBy(call, parts);
        }

        // A segment's items with every call of a label written as a reform writes it, the same list where nothing calls the label.
        private static List<LoopItem> WithCallsReformed(List<LoopItem> items, Label label, Func<LabelCallItem, List<LoopItem>> callReform)
        {
            if (!items.Any(item => IsCallOf(item, label)))
            {
                return items;
            }

            return [.. items.SelectMany(item => IsCallOf(item, label) ? callReform((LabelCallItem)item) : [item])];
        }

        // Whether an item is a call of the label.
        private static bool IsCallOf(LoopItem item, Label label)
        {
            return item is LabelCallItem call && call.Label == label;
        }

        // A label's body with its subloops written out pass after pass, each pass's events copies of their own.
        private static List<LoopItem> WrittenOut(List<LoopItem> bodyItems)
        {
            var items = new List<LoopItem>();

            foreach (var item in bodyItems)
            {
                if (item is not SubloopItem subloop)
                {
                    items.Add(item);
                    continue;
                }

                for (int pass = 0; pass < subloop.Count; pass++)
                {
                    items.AddRange(subloop.UnitItems.Select(CopyOf));
                }
            }

            return items;
        }

        // A label's body cut at its subloops into the parts a call plays in turn: each subloop a label of its own played with its count, and each stretch
        // between them a label of its own, or its events where they cost no more than a call and the label is never called shifted.
        private static List<LoopItem> PartsCutAtSubloops(List<LoopItem> bodyItems, bool isCalledShifted)
        {
            var parts = new List<LoopItem>();
            var stretch = new List<LoopItem>();

            foreach (var item in bodyItems)
            {
                if (item is not SubloopItem subloop)
                {
                    stretch.Add(item);
                    continue;
                }

                AddStretchPart(parts, stretch, isCalledShifted);
                parts.Add(new LabelCallItem(new Label { BodyItems = subloop.UnitItems }, subloop.Count, null));
                stretch = [];
            }

            AddStretchPart(parts, stretch, isCalledShifted);
            return parts;
        }

        // Adds a stretch of a label's body between its subloops to the parts a call plays: a label of its own, or its events where they cost no more
        // than a call and the label is never called shifted.
        private static void AddStretchPart(List<LoopItem> parts, List<LoopItem> stretch, bool isCalledShifted)
        {
            if (stretch.Count == 0)
            {
                return;
            }

            if (!isCalledShifted && ItemsBytes(stretch) <= CallSiteBytes(NoShift))
            {
                parts.AddRange(stretch);
                return;
            }

            parts.Add(new LabelCallItem(new Label { BodyItems = stretch }, PlayedOnce, null));
        }

        // What one call of a label cut into parts plays: the parts in turn as many times as the call played the label, each part's label called under the
        // call's transpose and each event a copy of its own - or the one part's label with the two counts multiplied, where it is the only part and the
        // count fits.
        private static List<LoopItem> PartsPlayedBy(LabelCallItem call, List<LoopItem> parts)
        {
            if (parts is [LabelCallItem onlyPart] && onlyPart.Count * call.Count <= MaxLoopCount)
            {
                return [onlyPart with { Count = onlyPart.Count * call.Count, Transpose = call.Transpose }];
            }

            var played = new List<LoopItem>();
            for (int pass = 0; pass < call.Count; pass++)
            {
                played.AddRange(parts.Select(part => part is LabelCallItem partCall ? partCall with { Transpose = call.Transpose } : CopyOf(part)));
            }

            return played;
        }

        // An item standing apart from the one given wherever it is written: an event copied, anything else as it is.
        private static LoopItem CopyOf(LoopItem item)
        {
            return item is EventItem eventItem ? new EventItem(eventItem.Event.Copy()) : item;
        }

        // The bytes the segments' items are estimated to compile to, each label's body counted once with its end.
        private static int EstimatedBytesOf(List<List<LoopItem>> itemsBySegment)
        {
            var labels = itemsBySegment.SelectMany(items => items).SelectMany(LabelsCalledBy).Distinct();
            return itemsBySegment.Sum(ItemsBytes) + labels.Sum(label => ItemsBytes(label.BodyItems) + LabelDefinitionBytes);
        }
    }
}
