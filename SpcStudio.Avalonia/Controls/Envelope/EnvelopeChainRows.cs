using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SpcStudio.Audio;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The envelope's rows - the note panel's or the instrument panel's: the graph over the whole of them, and under it one row's controls alone, or a
// folding banner per row holding them with its Length at the banner's left and its bin beside its chevron, one open at a time.
public sealed class EnvelopeChainRows : SpanRows<EnvelopeRow, IEnvelopeHost>
{
    private readonly List<EnvelopeEditor> _editors = [];
    private readonly List<PanelSection> _rowSections = [];
    private readonly List<Control> _bannerControls = [];
    private bool _isGraphHooked;
    private object? _shownOwner;
    private int? _openRowIndex;

    public required AdsrGraph Graph { get; init; }

    // The small graph on the section's banner, which draws the rows as the graph does.
    public required EnvelopeMiniGraph MiniGraph { get; init; }

    // What the rows stand for - the selected notes, or the instrument - so every row folds when it changes.
    public required Func<object?> Owner { get; init; }

    // The tempo map the graph turns a row's ticks into seconds through, and the tick the rows' key-on falls on in it.
    public required Func<(TempoMap Tempos, int KeyOnTick)> GraphTiming { get; init; }

    // The one field a row has, its Length.
    protected override IReadOnlyList<string> FieldLabels
    {
        get
        {
            return [FieldRows.LengthLabel];
        }
    }

    // The rows over an Envelope section - its body's graph, list and '+', and a small graph put on its banner - and what they edit.
    public static EnvelopeChainRows Over(EnvelopeRowsView view, PanelSection section, Func<RowSubjects<IEnvelopeHost>> subjects, Func<object?> owner,
        Func<(TempoMap Tempos, int KeyOnTick)> graphTiming, InertClaim? inertClaim = null)
    {
        var miniGraph = new EnvelopeMiniGraph();
        section.BannerMiddleContent = miniGraph;
        return new EnvelopeChainRows
        {
            SectionParts = new ListSectionParts(section, view.RowList, view.RowAddButton),
            SectionTooltips = new RowSectionTooltips(Tooltips.AddEnvelopeRow, Tooltips.ClearEnvelopeChanges),
            Graph = view.RowsGraph,
            MiniGraph = miniGraph,
            Subjects = subjects,
            Owner = owner,
            GraphTiming = graphTiming,
            InertClaim = inertClaim,
        };
    }

    // Splits the last row in two, or adds a row behind an endless last one, on every subject with room for it, and opens the row it adds with the panel scrolled to show the whole of it.
    public override void AddRow()
    {
        _openRowIndex = SharedChain(Subjects().Members)?.Length;
        base.AddRow();

        if (_openRowIndex is int addedRowIndex && addedRowIndex < _rowSections.Count)
        {
            _rowSections[addedRowIndex].ScrollWholeIntoViewOnceLaidOut();
        }
    }

    // Shows the section, folding every row where the rows stand for something else now, and then each row's envelope, which row is open and the graph.
    public override void Show()
    {
        HookGraph();
        object? owner = Owner();
        if (!Equals(owner, _shownOwner))
        {
            _shownOwner = owner;
            _openRowIndex = null;
        }

        base.Show();
        ShowRowStates();
    }

    // Draws the graph again from the rows as they stand.
    public void RedrawGraph()
    {
        BindGraph(SharedChain(Subjects().Members));
    }

    // No count on the banner, whose middle holds the small graph.
    protected override int? BannerRowCount(int rowCount)
    {
        return null;
    }

    // Whether the rows standing were built for a different number of rows, clearing them where they were.
    protected override bool AreRowsStale(ImmutableArray<RowSpan<EnvelopeRow>> chain)
    {
        return FieldRows.ClearIfStale(SectionParts.Rows, ImmutableArray.Create(chain.Length));
    }

    // Builds one row's controls alone where there is one row, or a folding banner per row with a row gap under the last.
    protected override void BuildRows(ImmutableArray<RowSpan<EnvelopeRow>> chain)
    {
        _editors.Clear();
        _rowSections.Clear();
        _bannerControls.Clear();
        for (int rowIndex = 0; rowIndex < chain.Length; rowIndex++)
        {
            _editors.Add(BuildEditor(rowIndex));
        }

        bool isRowAlone = IsRowAlone(chain.Length);
        SectionParts.Rows.Margin = isRowAlone ? default : new Thickness(0, 0, 0, Space.Px4);
        if (isRowAlone)
        {
            SectionParts.Rows.Children.Add(_editors[OffsetRows.FirstRowIndex]);
            return;
        }

        for (int rowIndex = 0; rowIndex < chain.Length; rowIndex++)
        {
            SectionParts.Rows.Children.Add(BuildRowSection(chain, rowIndex));
        }
    }

    // Every control a row is edited with: the banners' Length fields and bins, and each row's own controls.
    protected override IEnumerable<Control> RowControls()
    {
        return [.. _bannerControls, .. _editors.SelectMany(editor => editor.EditingControls)];
    }

    // Whether a press landed on something a row is edited with, which a row's banner is not: pressing it folds or opens the row.
    protected override bool IsOnEditingControl(object? hit)
    {
        return base.IsOnEditingControl(hit) && !(hit is Visual visual && visual.FindAncestorOfType<Button>(includeSelf: true)?.TemplatedParent is PanelSection);
    }

    // Hangs the graph's drag on the rows, once.
    private void HookGraph()
    {
        if (_isGraphHooked)
        {
            return;
        }

        _isGraphHooked = true;
        Graph.Edited += OnGraphEdited;
    }

    // One row's controls, their edits written onto that row of every subject.
    private EnvelopeEditor BuildEditor(int rowIndex)
    {
        var editor = new EnvelopeEditor();
        editor.Edited += (source, change) => EditRowEnvelope(rowIndex, change, source);
        editor.ResetRequested += (source, resetFromBase) => ResetRowEnvelope(rowIndex, resetFromBase, source);
        return editor;
    }

    // One row's folding banner: its Length field at the left, its bin beside the chevron where the row can be taken off, and the row's controls inside.
    private PanelSection BuildRowSection(ImmutableArray<RowSpan<EnvelopeRow>> chain, int rowIndex)
    {
        var lengthBox = BuildFieldBox(chain, rowIndex, SpanLengthField);
        lengthBox.Width = FieldRows.FieldWidth;
        _bannerControls.Add(lengthBox);

        Control? removeButton = null;
        if (CanRemoveRow(chain, rowIndex))
        {
            removeButton = BuildRemoveButton(rowIndex);
            _bannerControls.Add(removeButton);
        }

        var section = new PanelSection
        {
            Content = _editors[rowIndex],
            BannerLeftContent = new BannerField { Label = FieldRows.LengthLabel, Content = lengthBox },
            BannerRightContent = removeButton,
        };
        section.PropertyChanged += (_, change) =>
        {
            if (change.Property == PanelSection.IsOpenProperty)
            {
                OnRowSectionToggled(rowIndex, section.IsOpen);
            }
        };
        _rowSections.Add(section);
        return section;
    }

    // Writes each row's envelope into its controls, opens the open row and folds the rest, and draws the graph.
    private void ShowRowStates()
    {
        if (SharedChain(Subjects().Members) is not { } chain || _editors.Count != chain.Length)
        {
            BindGraph(null);
            return;
        }

        if (_openRowIndex >= chain.Length)
        {
            _openRowIndex = null;
        }

        for (int rowIndex = 0; rowIndex < chain.Length; rowIndex++)
        {
            _editors[rowIndex].Show(chain[rowIndex].Row.State);
        }

        for (int rowIndex = 0; rowIndex < _rowSections.Count; rowIndex++)
        {
            _rowSections[rowIndex].IsOpen = rowIndex == _openRowIndex;
        }

        BindGraph(chain);
    }

    // Opens one row and folds the others as its banner is pressed open, or leaves every row folded as the open one is pressed shut, and redraws the graph with the open row's handles.
    private void OnRowSectionToggled(int rowIndex, bool isOpen)
    {
        if (isOpen)
        {
            _openRowIndex = rowIndex;
            for (int otherIndex = 0; otherIndex < _rowSections.Count; otherIndex++)
            {
                _rowSections[otherIndex].IsOpen = otherIndex == rowIndex;
            }
        }
        else if (_openRowIndex == rowIndex)
        {
            _openRowIndex = null;
        }
        else
        {
            return;
        }

        RedrawGraph();
    }

    // Draws the spans on both graphs at the times their rows take over, the section's with the handles of the one row there is or of the open row,
    // or nothing.
    private void BindGraph(ImmutableArray<RowSpan<EnvelopeRow>>? chain)
    {
        if (chain is not { } spans)
        {
            Graph.Bind(null, [], null);
            MiniGraph.Bind([]);
            return;
        }

        var (tempos, keyOnTick) = GraphTiming();
        double keyOnSeconds = tempos.SecondsAt(keyOnTick);
        var changes = RowsOf(spans).Select(row => new EnvelopeChange(tempos.SecondsAt(keyOnTick + row.OffsetTicks) - keyOnSeconds, row.State)).ToArray();
        int? handlesRowIndex = HandlesRowIndex(spans.Length);
        Graph.Bind((_shownOwner, handlesRowIndex), changes, handlesRowIndex);
        MiniGraph.Bind(changes);
    }

    // The row whose handles the graph shows: the one row there is, or the open row, or none.
    private int? HandlesRowIndex(int rowCount)
    {
        return IsRowAlone(rowCount) ? OffsetRows.FirstRowIndex : _openRowIndex;
    }

    // Writes a drag on the graph onto the row whose handles it moved.
    private void OnGraphEdited(object handle, Func<EnvelopeState, EnvelopeState> change)
    {
        if (SharedChain(Subjects().Members) is not { } chain)
        {
            return;
        }

        if (HandlesRowIndex(chain.Length) is not int rowIndex)
        {
            return;
        }

        EditRowEnvelope(rowIndex, change, handle);
    }

    // Changes one row's envelope on every subject, coalescing on the row and the control the change came from.
    private void EditRowEnvelope(int rowIndex, Func<EnvelopeState, EnvelopeState> change, object source)
    {
        CommitToSharedChain(chain => rowIndex < chain.Length ? chain.SetItem(rowIndex, chain[rowIndex] with { Row = chain[rowIndex].Row with { State = change(chain[rowIndex].Row.State) } }) : chain,
            (SectionParts.Rows, rowIndex, source));
    }

    // Puts one field of one row's envelope back at each subject's own base envelope for that row.
    private void ResetRowEnvelope(int rowIndex, Func<EnvelopeState, EnvelopeState, EnvelopeState> resetFromBase, object source)
    {
        CommitEach(Subjects(), subject =>
        {
            var chain = ChainOf(subject);
            if (rowIndex >= chain.Length)
            {
                return chain;
            }

            var baseEnvelope = subject.BaseEnvelopeAt(RowsOf(chain)[rowIndex].OffsetTicks);
            return chain.SetItem(rowIndex, chain[rowIndex] with { Row = chain[rowIndex].Row with { State = resetFromBase(baseEnvelope, chain[rowIndex].Row.State) } });
        }, (SectionParts.Rows, rowIndex, source));
    }
}
