namespace SpcStudio.Audio;

// A song's blocks with one of the two sweeps given to each single note that was too short for its own to be read but swung as its sample swung elsewhere,
// or that could not show either way and followed a block of its own voice and sample that had one.
public static class SpcSwingInference
{
    // The blocks of a song, in the order they keyed on, each that swung too briefly for its own swing of a kind to be read given the one its sample's other
    // blocks were heard to make, where its own path agrees or cannot tell and the block before it on its voice and sample had one.
    public static List<SpcTraceBlock> WithInferredSwings(List<SpcTraceBlock> blocks, SpcSwingKind kind, CancellationToken cancel = default)
    {
        var templates = blocks.Select(block => TemplateOf(block, kind)).ToArray();
        var inferred = new List<SpcTraceBlock>(blocks);
        foreach (var run in blocks.Select((block, index) => (block, index)).GroupBy(entry => (entry.block.Voice, entry.block.Srcn)))
        {
            var indices = run.Select(entry => entry.index).ToList();
            var judgements = indices.Select((_, at) => JudgementOf(blocks, templates, indices, at, kind, cancel)).ToArray();
            var hasSwing = Decided([.. judgements.Select(judgement => judgement.HasSwing)]);
            for (int at = 0; at < indices.Count; at++)
            {
                var block = blocks[indices[at]];
                if (hasSwing[at] && judgements[at].Swing is { } swing)
                {
                    inferred[indices[at]] = kind.WithInferred(block, swing);
                }
            }
        }

        return inferred;
    }

    // Whether a block of a run of one voice and sample has a swing of a kind as far as it shows itself, and the one it would be given: yes where it has its own, no where its sample never swung from a
    // key-on, unknown where it sounded more than one note, yes where its path swings as one of its templates does, and otherwise as its path reads against the nearest.
    private static (bool? HasSwing, SpcTraceSwing? Swing) JudgementOf(List<SpcTraceBlock> blocks, SpcSwingTemplate?[] templates, List<int> indices, int at, SpcSwingKind kind, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        var block = blocks[indices[at]];
        if (!kind.SwingsOf(block).IsEmpty)
        {
            return (true, null);
        }

        if (block.Notes.Length > 1)
        {
            return (null, null);
        }

        var path = kind.PathOf(block);
        var readingsAgainstTemplates = TemplatesFor(blocks, templates, indices, at).Distinct().Select(template => SpcSwings.ReadAgainst(path, block.Start, block.End, template, kind.Scale));
        (SpcSwingFit Fit, SpcTraceSwing? Swing)? nearest = null;
        foreach (var reading in readingsAgainstTemplates)
        {
            if (reading.Fit == SpcSwingFit.Swings)
            {
                return (true, reading.Swing);
            }

            nearest ??= reading;
        }

        return nearest?.Fit switch
        {
            null or SpcSwingFit.DoesNotSwing => (false, null),
            _ => (null, nearest.Value.Swing),
        };
    }

    // Each block's answer to whether it has a swing, a block with none taking the answer of the block before it, or of the first block after it with one where none is before it.
    private static bool[] Decided(bool?[] answers)
    {
        bool carried = answers.FirstOrDefault(answer => answer is not null) ?? false;
        var decided = new bool[answers.Length];
        for (int at = 0; at < answers.Length; at++)
        {
            carried = answers[at] ?? carried;
            decided[at] = carried;
        }

        return decided;
    }

    // The swings a block is read against, nearest first: those read from a key-on on its voice and sample, the ones before it first, then those on its
    // sample on the other voices, by how far they keyed on from it.
    private static IEnumerable<SpcSwingTemplate> TemplatesFor(List<SpcTraceBlock> blocks, SpcSwingTemplate?[] templates, List<int> indices, int at)
    {
        var block = blocks[indices[at]];

        // The blocks on the same sample on the other voices, by how far they keyed on from this one.
        var otherVoices = Enumerable.Range(0, blocks.Count)
            .Where(index => blocks[index].Srcn == block.Srcn && blocks[index].Voice != block.Voice)
            .OrderBy(index => Math.Abs(blocks[index].Start - block.Start));

        // Every block a template is looked for on, nearest first: this run's before it, then this run's after it, then the other voices'.
        var nearestFirst = Enumerable.Range(0, at).Reverse().Concat(Enumerable.Range(at + 1, indices.Count - at - 1)).Select(runAt => indices[runAt]).Concat(otherVoices);
        foreach (int index in nearestFirst)
        {
            if (templates[index] is { } template)
            {
                yield return template;
            }
        }
    }

    // The swing of a kind a block made from its key-on as a template, or null where it made none or its first set off past the note it keyed on with.
    private static SpcSwingTemplate? TemplateOf(SpcTraceBlock block, SpcSwingKind kind)
    {
        var swings = kind.SwingsOf(block);
        if (swings.IsEmpty || swings[0].Start >= block.Notes[0].End)
        {
            return null;
        }

        var first = swings[0];
        return new SpcSwingTemplate(first.Start - block.Start, first.CycleFrames, first.Peak, first.FullWidthFrame - first.Start);
    }
}
