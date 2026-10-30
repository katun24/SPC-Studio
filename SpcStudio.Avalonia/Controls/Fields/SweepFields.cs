using Avalonia.Controls;
using SpcStudio.Model;

namespace SpcStudio.Controls;

// The two sweeps' fields, fresh stretches and rows, written once for both panels.
public static class SweepFields
{
    private const string RateLabel = "Rate";
    private const string ExtentLabel = "Extent";
    private const string AmountLabel = "Amount";
    private const int FreshVibratoRate = 12;
    private const int FreshVibratoExtent = 64;
    private const int FreshTremoloRate = 12;
    private const int FreshTremoloAmount = 64;

    private static readonly RowSectionTooltips VibratoTooltips = new(Tooltips.AddVibratoRow, Tooltips.ClearVibrato);
    private static readonly RowSectionTooltips TremoloTooltips = new(Tooltips.AddTremoloRow, Tooltips.ClearTremolo);

    // The vibrato's five: Delay, Length, Rate, Extent and Fade, the Extent and the Fade each held to the other's limit.
    public static readonly IReadOnlyList<SweepField<NoteVibrato>> Vibrato =
    [
        .. SpanFields<NoteVibrato>(SnesVibrato.Range(VibratoSetting.Delay).Max),
        new(RateLabel, vibrato => vibrato.Rate, (vibrato, rate) => vibrato with { Rate = rate }, (_, _, _) => SnesVibrato.Range(VibratoSetting.Rate)),
        new(ExtentLabel, vibrato => vibrato.Extent, (vibrato, extent) => vibrato with { Extent = extent },
            (chain, index, _) => new ValueRange(SnesVibrato.MinExtentIn(chain, index), SnesVibrato.Range(VibratoSetting.Extent).Max)),
        new(FieldRows.FadeLabel, vibrato => vibrato.FadeTicks, (vibrato, fadeTicks) => vibrato with { FadeTicks = fadeTicks },
            (chain, index, _) => new ValueRange(SnesVibrato.Range(VibratoSetting.Fade).Min, SnesVibrato.MaxFadeIn(chain, index)))
        {
            IsLocked = SnesVibrato.IsFadeTaken,
        },
    ];

    // The tremolo's four: Delay, Length, Rate and Amount.
    public static readonly IReadOnlyList<SweepField<NoteTremolo>> Tremolo =
    [
        .. SpanFields<NoteTremolo>(SnesTremolo.Range(TremoloSetting.Delay).Max),
        new(RateLabel, tremolo => tremolo.Rate, (tremolo, rate) => tremolo with { Rate = rate }, (_, _, _) => SnesTremolo.Range(TremoloSetting.Rate)),
        new(AmountLabel, tremolo => tremolo.Amount, (tremolo, amount) => tremolo with { Amount = amount }, (_, _, _) => SnesTremolo.Range(TremoloSetting.Amount)),
    ];

    // A fresh vibrato stretch at a delay and a length: the rate and the extent of the one in front of it, or the vibrato a first stretch starts on.
    public static NoteVibrato FreshVibrato(NoteVibrato? previousStretch, int delayTicks, int lengthTicks)
    {
        NoteVibrato? carriedOn = previousStretch is { } previous ? previous with { FadeTicks = SnesVibrato.MinFadeTicks } : null;
        return CarriedOnOrFirst(carriedOn, delayTicks, lengthTicks,
            (firstDelayTicks, firstLengthTicks) => new NoteVibrato(firstDelayTicks, firstLengthTicks, FreshVibratoRate, FreshVibratoExtent));
    }

    // A fresh tremolo stretch at a delay and a length: the rate and the amount of the one in front of it, or the tremolo a first stretch starts on.
    public static NoteTremolo FreshTremolo(NoteTremolo? previousStretch, int delayTicks, int lengthTicks)
    {
        return CarriedOnOrFirst(previousStretch, delayTicks, lengthTicks,
            (firstDelayTicks, firstLengthTicks) => new NoteTremolo(firstDelayTicks, firstLengthTicks, FreshTremoloRate, FreshTremoloAmount));
    }

    // The vibrato's rows over the subjects they edit.
    public static SweepRows<NoteVibrato> VibratoRows(ListSectionParts parts, Func<RowSubjects<IListHost<NoteVibrato>>> subjects, InertClaim? inertClaim = null)
    {
        return SweepRowsOf(Vibrato, FreshVibrato, VibratoTooltips, parts, subjects, inertClaim);
    }

    // The tremolo's rows over the subjects they edit.
    public static SweepRows<NoteTremolo> TremoloRows(ListSectionParts parts, Func<RowSubjects<IListHost<NoteTremolo>>> subjects, InertClaim? inertClaim = null)
    {
        return SweepRowsOf(Tremolo, FreshTremolo, TremoloTooltips, parts, subjects, inertClaim);
    }

    // One sweep's rows, with its fields and its fresh stretch, over the subjects they edit.
    private static SweepRows<T> SweepRowsOf<T>(IReadOnlyList<SweepField<T>> fields, Func<T?, int, int, T> freshStretch, RowSectionTooltips sectionTooltips, ListSectionParts parts,
        Func<RowSubjects<IListHost<T>>> subjects, InertClaim? inertClaim) where T : struct, INoteSweep<T>, IEquatable<T>
    {
        return new SweepRows<T>
        {
            SectionParts = parts,
            SectionTooltips = sectionTooltips,
            Fields = fields,
            FreshStretch = freshStretch,
            Subjects = subjects,
            InertClaim = inertClaim,
        };
    }

    // The Delay and the Length every sweep's row opens with: a delay that still starts the stretch inside the capacity, and a length the chain in front leaves room for, which ends an endless stretch.
    private static SweepField<T>[] SpanFields<T>(int maxDelayTicks) where T : struct, INoteSweep<T>
    {
        return
        [
            new(FieldRows.DelayLabel, sweep => sweep.DelayTicks, (sweep, delayTicks) => sweep.WithSpan(delayTicks, sweep.LengthTicks),
                (chain, index, capacityTicks) => new ValueRange(NoteSweep.MinDelayTicks, Math.Min(maxDelayTicks, NoteSweep.MaxDelayAt(chain, index, capacityTicks)))),
            new(FieldRows.LengthLabel, sweep => sweep.LengthTicks, NoteSweep.WithFixedLength,
                (chain, index, capacityTicks) => new ValueRange(NoteSweep.MinLengthTicks, NoteSweep.MaxLengthAt(chain, index, capacityTicks)))
            {
                IsTickCount = true,
            },
        ];
    }

    // A stretch at a delay and a length carrying on the given one's values, or a first one where there is none to carry on.
    private static T CarriedOnOrFirst<T>(T? carriedOn, int delayTicks, int lengthTicks, Func<int, int, T> firstAt) where T : struct, INoteSweep<T>
    {
        if (carriedOn is { } stretch)
        {
            return stretch.WithSpan(delayTicks, lengthTicks);
        }

        return firstAt(delayTicks, lengthTicks);
    }
}
