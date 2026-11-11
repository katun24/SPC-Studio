using System.Collections.Immutable;
using SpcStudio.Model;

namespace SpcStudio.Import;

// Gives the notes of each instrument one vibrato chain, one tremolo chain and one bend chain wherever theirs differ by less than a transcription can tell
// apart, the most common of the close chains standing for all of them.
public static class SpcCloseChains
{
    public const double MaxVibratoWidthCents = 3;
    public const int MaxVibratoFadeTicks = 2;
    public const int MaxBendTicks = 1;
    public const int MaxTremoloAmount = 8;

    // The notes with each instrument's close vibrato, tremolo and bend chains made the most common of them.
    public static List<(int Channel, Note Note)> Unified(IReadOnlyList<(int Channel, Note Note)> notes)
    {
        var unifiedNotes = notes.ToList();
        foreach (var instrumentIndexes in Enumerable.Range(0, unifiedNotes.Count).GroupBy(index => unifiedNotes[index].Note.InstrumentId))
        {
            var indexes = instrumentIndexes.ToList();
            var unifiedVibratos = MostCommonOfClose([.. indexes.Select(index => unifiedNotes[index].Note.Vibratos)], VibratoKey, IsVibratoClose, VibratoAdopting);
            var unifiedTremolos = MostCommonOfClose([.. indexes.Select(index => unifiedNotes[index].Note.Tremolos)], TremoloKey, IsTremoloClose, TremoloAdopting);
            var unifiedBends = MostCommonOfClose([.. indexes.Select(index => unifiedNotes[index].Note.Bends)], bend => bend, IsBendClose, (keyBend, _) => keyBend);
            for (int instrumentNoteIndex = 0; instrumentNoteIndex < indexes.Count; instrumentNoteIndex++)
            {
                var (channel, note) = unifiedNotes[indexes[instrumentNoteIndex]];
                unifiedNotes[indexes[instrumentNoteIndex]] = (channel, note with
                {
                    Vibratos = unifiedVibratos[instrumentNoteIndex],
                    Tremolos = unifiedTremolos[instrumentNoteIndex],
                    Bends = unifiedBends[instrumentNoteIndex],
                });
            }
        }

        return unifiedNotes;
    }

    // Each chain made the most common chain close to it, a chain being compared and counted by the key of each stretch: the keys most held first, each
    // either joins the first group whose own key it lies close to or starts a group of its own.
    private static List<ImmutableArray<T>> MostCommonOfClose<T>(List<ImmutableArray<T>> chains, Func<T, T> keyOf, Func<T, T, bool> isClose, Func<T, T, T> adopting)
        where T : struct
    {
        var chainKeys = chains.Select(chain => chain.Select(keyOf).ToImmutableArray()).ToList();
        var groupKeys = new List<ImmutableArray<T>>();
        var groupKeyOfHeldKey = new List<(ImmutableArray<T> Key, ImmutableArray<T> GroupKey)>();
        foreach (var (key, _) in MostHeld.DistinctByCount(chainKeys, (first, second) => first.SequenceEqual(second)))
        {
            var joinedGroupKey = groupKeys.FirstOrDefault(groupKey => IsChainClose(groupKey, key, isClose));
            if (joinedGroupKey.IsDefault)
            {
                joinedGroupKey = key;
                groupKeys.Add(key);
            }

            groupKeyOfHeldKey.Add((key, joinedGroupKey));
        }

        return
        [
            .. chains.Select((chain, chainIndex) =>
            {
                var joinedGroupKey = groupKeyOfHeldKey.First(heldKey => heldKey.Key.SequenceEqual(chainKeys[chainIndex])).GroupKey;
                return joinedGroupKey.Zip(chain, adopting).ToImmutableArray();
            }),
        ];
    }

    // Whether a chain has as many stretches as a group's key and each lies close to the key's own.
    private static bool IsChainClose<T>(ImmutableArray<T> groupKey, ImmutableArray<T> chain, Func<T, T, bool> isClose)
        where T : struct
    {
        return groupKey.Length == chain.Length && groupKey.Zip(chain).All(pair => isClose(pair.First, pair.Second));
    }

    // What a vibrato stretch is counted and compared by: everything but its length, which is its note's.
    private static NoteVibrato VibratoKey(NoteVibrato vibrato)
    {
        return vibrato with { LengthTicks = 0 };
    }

    // Whether a vibrato stretch starts where the key does at its speed, swinging within the width and widening within the fade of it.
    private static bool IsVibratoClose(NoteVibrato key, NoteVibrato vibrato)
    {
        return key.DelayTicks == vibrato.DelayTicks
            && key.Rate == vibrato.Rate
            && Math.Abs(SnesVibrato.PeakCents(key.Extent) - SnesVibrato.PeakCents(vibrato.Extent)) <= MaxVibratoWidthCents
            && Math.Abs(key.FadeTicks - vibrato.FadeTicks) <= MaxVibratoFadeTicks;
    }

    // A vibrato stretch given the key's width and fade, keeping its own length.
    private static NoteVibrato VibratoAdopting(NoteVibrato key, NoteVibrato vibrato)
    {
        return vibrato with { Extent = key.Extent, FadeTicks = key.FadeTicks };
    }

    // What a tremolo stretch is counted and compared by: everything but its length, which is its note's.
    private static NoteTremolo TremoloKey(NoteTremolo tremolo)
    {
        return tremolo with { LengthTicks = 0 };
    }

    // Whether a tremolo stretch starts where the key does at its speed, ducking within the amount of it.
    private static bool IsTremoloClose(NoteTremolo key, NoteTremolo tremolo)
    {
        return key.DelayTicks == tremolo.DelayTicks && key.Rate == tremolo.Rate && Math.Abs(key.Amount - tremolo.Amount) <= MaxTremoloAmount;
    }

    // A tremolo stretch given the key's amount, keeping its own length.
    private static NoteTremolo TremoloAdopting(NoteTremolo key, NoteTremolo tremolo)
    {
        return tremolo with { Amount = key.Amount };
    }

    // Whether a bend goes as far as the key, starting and lasting within the ticks of it.
    private static bool IsBendClose(PitchBend key, PitchBend bend)
    {
        return key.OffsetSemitones == bend.OffsetSemitones
            && Math.Abs(key.DelayTicks - bend.DelayTicks) <= MaxBendTicks
            && Math.Abs(key.LengthTicks - bend.LengthTicks) <= MaxBendTicks;
    }
}
