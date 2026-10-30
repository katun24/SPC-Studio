using System.Collections.Immutable;
using Avalonia.Controls;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// A fading value's rows - the channel volume's or the pan's, in the note panel or the instrument panel: one row per stretch, each its Length, the value
// it sets, the value it fades to - a hyphen where it holds flat - and, on the pan, the surround pair it holds.
public sealed class ChainRows<TState> : SpanRows<NoteChainRegion<TState>, IFadeHost<TState>> where TState : struct
{
    private const int ValueField = 1;
    private const int FadeField = 2;
    private const string LoneFadeWord = "fade";
    private const string SurroundLabel = "Surround";
    private const string SurroundWidthResource = "SurroundFieldWidth";

    // The lane the rows are a note's or an instrument's stretch of.
    public required NoteFadeLane<TState> Lane { get; init; }

    // What the value column is called, which is the lane's own name for its byte.
    public required string ValueLabel { get; init; }

    public required bool HasSurround { get; init; }
    public required Func<TState, SnesSurround> SurroundOf { get; init; }
    public required Func<TState, SnesSurround, TState> WithSurround { get; init; }

    // The labels of the three number fields.
    protected override IReadOnlyList<string> FieldLabels
    {
        get
        {
            return [FieldRows.LengthLabel, ValueLabel, FieldRows.FadeLabel];
        }
    }

    // The surround drop-down's column on the pan's rows, and none on the volume's.
    protected override (FieldColumn Column, Func<ImmutableArray<RowSpan<NoteChainRegion<TState>>>, int, Control> Build)? ChoiceColumn
    {
        get
        {
            if (!HasSurround)
            {
                return null;
            }

            return (new FieldColumn(SurroundLabel, ThemeResource.Of<double>(SurroundWidthResource)), BuildSurroundBox);
        }
    }

    // One subject's rows with one more: its instrument's rows where it has none of its own, and otherwise the last row split.
    protected override ImmutableArray<RowSpan<NoteChainRegion<TState>>>? WithRowAdded(IFadeHost<TState> subject, ImmutableArray<RowSpan<NoteChainRegion<TState>>> chain, int? shortestSplitTicks)
    {
        if (chain.IsEmpty)
        {
            return SpansOf(subject.InstrumentRows, subject);
        }

        return base.WithRowAdded(subject, chain, shortestSplitTicks);
    }

    // What the revert button leaves of the rows: the first row alone, held flat at the value it sets out from.
    protected override ImmutableArray<RowSpan<NoteChainRegion<TState>>> RevertedChain(ImmutableArray<RowSpan<NoteChainRegion<TState>>> chain)
    {
        return [.. base.RevertedChain(chain).Select(span => span with { Row = Lane.WithFadeTo(span.Row, Lane.ValueOf(span.Row.State)) })];
    }

    // The word a lone row that fades gives the banner, and none for a lone flat row or several rows.
    protected override string? BannerRowWord(ImmutableArray<RowSpan<NoteChainRegion<TState>>> chain)
    {
        return IsRowAlone(chain.Length) && chain[OffsetRows.FirstRowIndex].Row.Fades ? LoneFadeWord : null;
    }

    // A row split by '+' as it plays: the first half ramping to the byte on the row's straight line where it is split, and the second setting out from there.
    protected override (NoteChainRegion<TState> FirstHalf, NoteChainRegion<TState> SecondHalf) SplitRow(NoteChainRegion<TState> row, int lengthTicks, int firstHalfTicks)
    {
        int reachedValue = Lane.ValueOnLine(row, lengthTicks, firstHalfTicks);
        return (Lane.WithFadeTo(row, reachedValue), Lane.WithState(row, Lane.WithValue(row.State, reachedValue)));
    }

    // An endless row given an end by '+': it keeps its own ramp over the ticks it now has, and the row after it holds flat where that ramp ends.
    protected override (NoteChainRegion<TState> Ending, NoteChainRegion<TState> Endless) SplitEndlessRow(NoteChainRegion<TState> row, int endingTicks)
    {
        return (row, new NoteChainRegion<TState>(OffsetRows.FirstOffsetTicks, Lane.WithValue(row.State, Lane.EndValueOf(row))));
    }

    // What the value field or the fade field shows, the fade showing the byte it holds where the row holds flat.
    protected override int RowFieldValue(NoteChainRegion<TState> row, int field)
    {
        return field == FadeField ? Lane.EndValueOf(row) : Lane.ValueOf(row.State);
    }

    // A hyphen in the fade field of a row that holds flat, and the number elsewhere.
    protected override string FieldText(RowSpan<NoteChainRegion<TState>> entry, int field)
    {
        return field == FadeField && !entry.Row.Fades ? ValueTexts.NoValue : base.FieldText(entry, field);
    }

    // The value field and the fade field both hold what the lane's command takes.
    protected override ValueRange RowFieldRange(int field)
    {
        return Lane.ValueRange;
    }

    // A row with its value or its fade set, a fade landing on the value it sets out from being no fade at all.
    protected override NoteChainRegion<TState> WithRowFieldEdited(NoteChainRegion<TState> row, int field, int value)
    {
        return field == FadeField ? Lane.WithFadeTo(row, value) : Lane.WithState(row, Lane.WithValue(row.State, value));
    }

    // The value a right-click puts a field back at: the fade to the row's own value, which is no fade; the first row's value to the subject's own
    // starting value; and any other row's value to where the row in front of it ends.
    protected override int ValueFieldBaseValue(IFadeHost<TState> subject, ImmutableArray<RowSpan<NoteChainRegion<TState>>> chain, int index, int field)
    {
        if (field == FadeField)
        {
            return Lane.ValueOf(chain[index].Row.State);
        }

        return index == OffsetRows.FirstRowIndex ? Lane.ValueOf(subject.BaseLeadState) : Lane.EndValueOf(chain[index - 1].Row);
    }

    // Writes the chain into every field and every row's surround drop-down, the drop-downs' own edits held off.
    protected override void RestateRows(ImmutableArray<RowSpan<NoteChainRegion<TState>>> chain)
    {
        base.RestateRows(chain);
        RunWithEditsHeldOff(() =>
        {
            foreach (var box in RowControls().OfType<ComboBox>())
            {
                if (box.Tag is int index && index < chain.Length)
                {
                    ShowSurround(box, chain, index);
                }
            }
        });
    }

    // One row's surround drop-down.
    private ComboBox BuildSurroundBox(ImmutableArray<RowSpan<NoteChainRegion<TState>>> chain, int index)
    {
        var box = SurroundField.NewDropDown();
        ShowSurround(box, chain, index);
        box.Tag = index;
        box.SelectionChanged += OnSurroundChanged;
        return box;
    }

    // Picks the surround pair one row holds in its drop-down.
    private void ShowSurround(ComboBox box, ImmutableArray<RowSpan<NoteChainRegion<TState>>> chain, int index)
    {
        box.SelectedIndex = SurroundField.IndexOf(SurroundOf(chain[index].Row.State));
    }

    // Puts one row under the surround pair picked, restating rather than rebuilding the rows.
    private void OnSurroundChanged(object? sender, SelectionChangedEventArgs e)
    {
        var box = (ComboBox)sender!;
        if (box.Tag is not int index || IsUpdating || box.SelectedIndex < 0)
        {
            return;
        }

        var pair = SurroundField.PairAt(box.SelectedIndex);
        RunWithEditsHeldOff(() => CommitToSharedChain(
            chain => index < chain.Length ? chain.SetItem(index, chain[index] with { Row = chain[index].Row with { State = WithSurround(chain[index].Row.State, pair) } }) : chain,
            PressUndoKey()));
    }
}
