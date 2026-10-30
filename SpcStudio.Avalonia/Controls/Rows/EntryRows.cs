using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// A chain's rows in a panel section: one row per entry, each a run of number fields opening with a Delay and a Length, an optional drop-down and a bin, under a row of labels. The rows edit every
// subject at once - each selected note, or the instrument - and show only where every subject holds the same chain, the banner saying the section is mixed where they do not. What parts one chain's
// rows from another's is the chain's own rules, which a subclass supplies.
public abstract class EntryRows<T, THost> where T : struct, IEquatable<T> where THost : IRowHost
{
    protected const int DelayField = 0;
    protected const int LengthField = 1;
    protected const int EndedEndlessRowTicks = SnesTiming.TicksPerQuarterNote;
    protected const int HalvesInASplit = 2;
    protected const string EndlessLengthText = "end";
    private const int WideFieldMaxLength = 4;
    private const int NoDelayTicks = 0;

    private bool _areSectionHandlersHung;

    public required ListSectionParts SectionParts { get; init; }
    public RowSectionTooltips? SectionTooltips { get; init; }

    // What the rows edit, read again at every use.
    public required Func<RowSubjects<THost>> Subjects { get; init; }

    // Whether the rows are inert and what a press on an inert control does before that control gets it; null for a section that is always live.
    public InertClaim? InertClaim { get; init; }

    // True while an edit from one of these fields is being applied, so the refresh it causes builds no rows.
    public bool IsUpdating { get; set; }

    // The number fields' labels, in the order they stand.
    protected abstract IReadOnlyList<string> FieldLabels { get; }

    // A drop-down column standing after the number fields and how one is built for an entry, or null on rows that have none.
    protected virtual (FieldColumn Column, Func<ImmutableArray<T>, int, Control> Build)? ChoiceColumn
    {
        get
        {
            return null;
        }
    }

    // Whether the rows are inert now.
    private bool AreRowsInert
    {
        get
        {
            return InertClaim?.IsInert() == true;
        }
    }

    // The ticks of the first of the two halves '+' splits a row running a number of ticks into - half the shortest row split with it, where one is shorter - or null where it is too short to split.
    protected static int? FirstHalfTicks(int lengthTicks, int? shortestSplitTicks)
    {
        if (!IsLongEnoughToSplit(lengthTicks))
        {
            return null;
        }

        return Math.Min(lengthTicks, shortestSplitTicks ?? lengthTicks) / HalvesInASplit;
    }

    // Whether a row running a number of ticks is long enough for '+' to split it in two.
    private static bool IsLongEnoughToSplit(int lengthTicks)
    {
        return lengthTicks >= HalvesInASplit;
    }

    // The key a press in this section records its undo step under: the section itself.
    public object PressUndoKey()
    {
        return SectionParts.Rows;
    }

    // The chain one subject's rows show.
    protected abstract ImmutableArray<T> ChainOf(THost subject);

    // Writes a chain of rows onto one subject without recording it, answering whether it wrote anything.
    protected abstract bool WriteChain(THost subject, ImmutableArray<T> chain);

    // A chain cut at the end of one subject with a length of its own, as the subject holds it once it is written there.
    protected abstract ImmutableArray<T> FittedTo(THost subject, ImmutableArray<T> chain);

    // One subject's chain with one more entry on the end, a row it splits split at half the shortest row split with it, or null where there is no room for one.
    protected abstract ImmutableArray<T>? WithRowAdded(THost subject, ImmutableArray<T> chain, int? shortestSplitTicks);

    // The ticks the row '+' splits in half on one subject runs, or null where it splits none.
    protected abstract int? SplitRowTicks(THost subject, ImmutableArray<T> chain);

    // What one field of an entry shows.
    protected abstract int FieldValue(T entry, int field);

    // What one field of an entry can hold on one subject, given the chain it is in.
    protected abstract ValueRange FieldRange(THost subject, ImmutableArray<T> chain, int index, int field);

    // The chain with one field of one entry set to a value already in its range.
    protected abstract ImmutableArray<T> WithFieldEdited(ImmutableArray<T> chain, int index, int field, int value);

    // The value a right-click puts one of an entry's fields after its Delay and Length back at, on one subject.
    protected abstract int ValueFieldBaseValue(THost subject, ImmutableArray<T> chain, int index, int field);

    // Whether a field is a tick count, which takes more digits than a byte and whose range is the shown subject's alone.
    protected virtual bool IsTickCountField(int field)
    {
        return false;
    }

    // Whether a field takes a minus sign.
    protected virtual bool IsSignedField(int field)
    {
        return false;
    }

    // The value an arrow or the wheel steps one field of an entry to before it is held to the field's range: one step on from what it holds.
    protected virtual int SteppedValue(T entry, int field, int step)
    {
        return FieldValue(entry, field) + step;
    }

    // The chain a right-click on one field of an entry leaves on every subject, or null where it puts the field back at its base value instead.
    protected virtual ImmutableArray<T>? WithFieldReset(IReadOnlyList<THost> members, ImmutableArray<T> chain, int index, int field)
    {
        return null;
    }

    // Whether a field refuses one value inside its range, which the arrows and the wheel step over and typing does not apply.
    protected virtual bool IsValueRefused(int field, int value)
    {
        return false;
    }

    // Whether the chain leaves one field of an entry nothing it could hold, which disables it.
    protected virtual bool IsFieldLocked(ImmutableArray<T> chain, int index, int field)
    {
        return false;
    }

    // Whether one entry of a chain can be taken off, which shows its bin.
    protected virtual bool CanRemoveRow(ImmutableArray<T> chain, int index)
    {
        return true;
    }

    // The chain with one entry taken off, the ones behind it moving up into its place.
    protected virtual ImmutableArray<T> WithRowRemoved(ImmutableArray<T> chain, int index)
    {
        return chain.RemoveAt(index);
    }

    // What the revert button leaves of a chain: no entries, unless a kind of chain keeps some.
    protected virtual ImmutableArray<T> RevertedChain(ImmutableArray<T> chain)
    {
        return [];
    }

    // Puts one more entry on the end of every subject's chain that has room for one, leaving the others as they are.
    public virtual void AddRow()
    {
        var subjects = Subjects();
        var withRowAdded = WithRowAddedOnEach(subjects.Members);
        CommitEach(subjects, subject => withRowAdded(subject) ?? ChainOf(subject), PressUndoKey());
    }

    // Takes one entry off the chain every subject holds.
    public void RemoveRow(int index)
    {
        CommitToSharedChain(chain => index < chain.Length ? WithRowRemoved(chain, index) : chain, PressUndoKey());
    }

    // Shows the section: the banner's count and its '+', or that the subjects' chains differ, its revert button, and the rows themselves where they would differ from the ones standing.
    public virtual void Show()
    {
        HangSectionHandlers();
        var members = Subjects().Members;
        var chain = SharedChain(members);
        SectionParts.Section.IsMixed = members.Count > 0 && chain is null;
        SectionParts.Section.CanRevert = members.Any(member => !IsReverted(member));

        if (chain is not { } shownChain)
        {
            ClearRows();
            return;
        }

        SectionParts.AddButton.IsEnabled = CanAdd(members);
        SectionParts.Section.RowCount = BannerRowCount(shownChain.Length);
        SectionParts.Section.RowWord = BannerRowWord(shownChain);
        SectionParts.Section.CanAdd = SectionParts.AddButton.IsEnabled;

        if (!IsUpdating)
        {
            ShowRows(shownChain);
        }

        MarkInert(AreRowsInert);
    }

    // Builds the rows where the ones standing were built from something else, and otherwise writes the chain into the ones standing.
    private void ShowRows(ImmutableArray<T> chain)
    {
        if (AreRowsStale(chain))
        {
            BuildRows(chain);
            return;
        }

        RestateRows(chain);
    }

    // Writes a chain into the rows standing: every field, and the drop-down where a kind of row has one.
    protected virtual void RestateRows(ImmutableArray<T> chain)
    {
        RestateFieldsExcept(null);
    }

    // The count the banner shows for a chain of so many rows.
    protected virtual int? BannerRowCount(int rowCount)
    {
        return rowCount;
    }

    // A word the banner shows in place of the count for a chain, or null to show the count.
    protected virtual string? BannerRowWord(ImmutableArray<T> chain)
    {
        return null;
    }

    // Whether the rows standing were built from something other than a chain, clearing them where they were.
    protected virtual bool AreRowsStale(ImmutableArray<T> chain)
    {
        return FieldRows.ClearIfStale(SectionParts.Rows, chain);
    }

    // Builds the rows for a chain into the emptied list: a row of labels, then one row of fields per entry.
    protected virtual void BuildRows(ImmutableArray<T> chain)
    {
        if (!chain.IsEmpty)
        {
            SectionParts.Rows.Children.Add(FieldRows.BuildHeader(RowColumns()));
        }

        for (int i = 0; i < chain.Length; i++)
        {
            SectionParts.Rows.Children.Add(BuildRow(chain, i));
        }
    }

    // The chain of the longest subject where every other subject holds it cut at its own end, or null where there are no subjects or one does not.
    protected ImmutableArray<T>? SharedChain(IReadOnlyList<THost> members)
    {
        if (members.Count == 0)
        {
            return null;
        }

        var chain = ChainOf(ShownSubject(members));
        return members.All(member => PlaysSame(member, ChainOf(member), HeldBy(member, chain))) ? chain : null;
    }

    // The subject the rows are shown from: the longest, every other one playing its chain cut at its own end.
    protected static THost ShownSubject(IReadOnlyList<THost> members)
    {
        return members.MaxBy(member => member.CapacityTicks)!;
    }

    // The chain one subject holds once a chain is written onto it: cut at its own end, where it has a length of its own.
    private ImmutableArray<T> HeldBy(THost subject, ImmutableArray<T> chain)
    {
        return subject.HasOwnLength ? FittedTo(subject, chain) : chain;
    }

    // Whether two chains play the same on one subject: entry for entry, unless a kind of chain says otherwise.
    protected virtual bool PlaysSame(THost subject, ImmutableArray<T> first, ImmutableArray<T> second)
    {
        return first.SequenceEqual(second);
    }

    // Edits the chain every subject holds and writes the result onto all of them, where they hold one chain and the edit changes it.
    protected void CommitToSharedChain(Func<ImmutableArray<T>, ImmutableArray<T>> edit, object? undoKey)
    {
        var subjects = Subjects();
        if (SharedChain(subjects.Members) is not { } chain)
        {
            return;
        }

        var edited = edit(chain);
        if (edited.SequenceEqual(chain))
        {
            return;
        }

        CommitEach(subjects, _ => edited, undoKey);
    }

    // Whether one more entry would still have room at the end of any subject's chain.
    private bool CanAdd(IReadOnlyList<THost> members)
    {
        var withRowAdded = WithRowAddedOnEach(members);
        return members.Any(member => withRowAdded(member) is not null);
    }

    // What '+' leaves on each of the subjects: its chain with one more entry on the end, every row split at half the shortest of the rows split, or null where it has no room for one.
    private Func<THost, ImmutableArray<T>?> WithRowAddedOnEach(IReadOnlyList<THost> members)
    {
        int? shortestSplitTicks = members.Select(member => SplitRowTicks(member, ChainOf(member))).Where(lengthTicks => lengthTicks is int ticks && IsLongEnoughToSplit(ticks)).Min();
        return member => WithRowAdded(member, ChainOf(member), shortestSplitTicks);
    }

    // Writes each subject the chain worked out for it, cut at its own end, where the subject does not already hold it, recorded as one edit under the given key.
    protected void CommitEach(RowSubjects<THost> subjects, Func<THost, ImmutableArray<T>> chainFor, object? undoKey)
    {
        subjects.Commit(() =>
        {
            bool isChanged = false;
            foreach (var member in subjects.Members)
            {
                var chain = chainFor(member);
                var heldChain = HeldBy(member, chain);
                if (!IsHeldAlready(member, chain, heldChain))
                {
                    isChanged |= WriteChain(member, heldChain);
                }
            }

            return isChanged;
        }, undoKey);
    }

    // Whether a subject already holds a chain written onto it: entry for entry where the chain reaches it uncut, and by what it plays where the subject cuts it.
    private bool IsHeldAlready(THost subject, ImmutableArray<T> chain, ImmutableArray<T> heldChain)
    {
        return heldChain.SequenceEqual(chain) ? ChainOf(subject).SequenceEqual(heldChain) : PlaysSame(subject, ChainOf(subject), heldChain);
    }

    // What the revert button leaves of one subject's chain.
    private ImmutableArray<T> RevertedChainOf(THost subject)
    {
        return RevertedChain(ChainOf(subject));
    }

    // Whether a subject already holds what the revert button would leave of its chain.
    private bool IsReverted(THost subject)
    {
        var revertedChain = RevertedChainOf(subject);
        return IsHeldAlready(subject, revertedChain, HeldBy(subject, revertedChain));
    }

    // Takes every row down, leaving nothing to add one to.
    private void ClearRows()
    {
        SectionParts.AddButton.IsEnabled = false;
        FieldRows.ClearRows(SectionParts.Rows);
    }

    // Hangs the banner's revert handler, the press handler that turns an inert section live and the section's buttons' tooltips, once.
    private void HangSectionHandlers()
    {
        if (_areSectionHandlersHung)
        {
            return;
        }

        _areSectionHandlersHung = true;
        ToolTip.SetTip(SectionParts.AddButton, SectionTooltips?.AddRow);
        SectionParts.Section.RevertTooltip = SectionTooltips?.Revert;
        SectionParts.Section.RevertRequested += OnRevertRequested;

        if (InertClaim is null)
        {
            return;
        }

        SectionParts.Rows.AddHandler(InputElement.PointerPressedEvent, OnInertPressed, RoutingStrategies.Tunnel);
    }

    // Leaves every subject what the revert button keeps of its own chain, moving them onto chains of their own first where the rows are inert, in one undo step.
    private void OnRevertRequested(object? sender, RoutedEventArgs e)
    {
        if (AreRowsInert)
        {
            InertClaim?.ClaimRows(EditStep.ClaimSource);
        }

        CommitEach(Subjects(), RevertedChainOf, PressUndoKey());
    }

    // Claims what the rows show for the note when an inert row's control is pressed, the press then running its ordinary course.
    private void OnInertPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!AreRowsInert || !IsOnEditingControl(e.Source) || !FieldSteps.DoesPressClaimInert(e))
        {
            return;
        }

        InertClaim?.ClaimRows(EditStep.ClaimSource);
    }

    // Whether a press landed on something a row is edited with rather than on the air between two of them.
    protected virtual bool IsOnEditingControl(object? hit)
    {
        return hit is Visual visual
            && (visual.FindAncestorOfType<TextBox>(includeSelf: true) is not null
                || visual.FindAncestorOfType<ComboBox>(includeSelf: true) is not null
                || visual.FindAncestorOfType<Slider>(includeSelf: true) is not null
                || visual.FindAncestorOfType<Button>(includeSelf: true) is not null);
    }

    // Every control standing in the rows, headers included.
    protected virtual IEnumerable<Control> RowControls()
    {
        return SectionParts.Rows.Children.SelectMany(row => ((Grid)row).Children).OfType<Control>();
    }

    // Shows the rows as inert or live: grayed and refusing the steps and the typing until a press, or ordinary.
    private void MarkInert(bool isInert)
    {
        FieldSteps.SetInert(SectionParts.AddButton, isInert);

        foreach (var control in RowControls())
        {
            if (control is ComboBox)
            {
                FieldSteps.SetInertOutOfTabOrder(control, isInert);
                continue;
            }

            FieldSteps.SetInert(control, isInert);
            if (control is TextBox box)
            {
                box.IsReadOnly = isInert;
            }
        }
    }

    // The columns a row and its header stand in: one per number field, the drop-down's where there is one, and the bin's.
    private IReadOnlyList<FieldColumn> RowColumns()
    {
        var columns = FieldLabels.Select(label => new FieldColumn(label, FieldRows.FieldWidth)).ToList();
        if (ChoiceColumn is { } choice)
        {
            columns.Add(choice.Column);
        }

        columns.Add(new FieldColumn("", FieldRows.RemoveButtonWidth));
        return columns;
    }

    // One entry's row: its fields, its drop-down where it has one, and the button that takes it off where it can be.
    private Grid BuildRow(ImmutableArray<T> chain, int index)
    {
        var columns = RowColumns();
        var row = FieldRows.BuildRowGrid(columns);

        for (int field = 0; field < FieldLabels.Count; field++)
        {
            FieldRows.PlaceCell(row, columns, field, BuildFieldBox(chain, index, field));
        }

        if (ChoiceColumn is { } choice)
        {
            FieldRows.PlaceCell(row, columns, FieldLabels.Count, choice.Build(chain, index));
        }

        if (CanRemoveRow(chain, index))
        {
            FieldRows.PlaceCell(row, columns, columns.Count - 1, BuildRemoveButton(index));
        }

        return row;
    }

    // The button that takes one entry off.
    protected Button BuildRemoveButton(int index)
    {
        var removeButton = FieldRows.BuildRemoveButton(Tooltips.DeleteRow);
        removeButton.Tag = index;
        removeButton.Click += OnRemoveClicked;
        return removeButton;
    }

    // What one field of an entry shows, as text.
    protected virtual string FieldText(T entry, int field)
    {
        return FieldInput.TextOf(FieldValue(entry, field));
    }

    // One field of one entry, wired for typing, the arrows, the wheel, the right-click and its commit.
    protected TextBox BuildFieldBox(ImmutableArray<T> chain, int index, int field)
    {
        var box = new TextBox
        {
            Classes = { IsSignedField(field) ? ThemeClasses.SignedField : ThemeClasses.NumericField },
            HorizontalAlignment = HorizontalAlignment.Left,
            Text = FieldText(chain[index], field),
            Tag = new FieldSlot(index, field),
            IsEnabled = !IsFieldLocked(chain, index, field),
        };

        if (IsTickCountField(field))
        {
            box.MaxLength = WideFieldMaxLength;
        }

        box.TextChanged += OnFieldChanged;
        FieldSteps.OnKeys(box, OnFieldKeyDown);
        FieldResets.OnReset(box, () => ResetField(box));
        box.LostFocus += OnFieldCommitted;
        return box;
    }

    // Writes a field's base value into it, held to what the chain lets the field hold on every subject, or applies the chain the click leaves where the field says one.
    private void ResetField(TextBox box)
    {
        if (!TryFindField(box, out var members, out var chain, out var slot))
        {
            return;
        }

        if (WithFieldReset(members, chain, slot.Index, slot.Field) is { } resetChain)
        {
            CommitFieldEdit(_ => resetChain, slot, null);
            return;
        }

        var range = SharedFieldRange(members, chain, slot.Index, slot.Field);
        ApplySteppedValue(box, slot, Math.Max(range.Min, Math.Min(FieldBaseValue(members, chain, slot.Index, slot.Field), range.Max)));
    }

    // The value a right-click puts one field of an entry back at: no delay, the rest of the shown subject's room or the length an endless row is given on
    // a subject with no length of its own, or the chain's own.
    protected virtual int FieldBaseValue(IReadOnlyList<THost> members, ImmutableArray<T> chain, int index, int field)
    {
        var shownSubject = ShownSubject(members);
        return field switch
        {
            DelayField => NoDelayTicks,
            LengthField => shownSubject.HasOwnLength ? RemainingTicksFor(shownSubject, chain, index) : EndedEndlessRowTicks,
            _ => ValueFieldBaseValue(shownSubject, chain, index, field),
        };
    }

    // The ticks left of a subject's capacity for one entry's length once every other entry's delay and length and its own delay are taken off.
    private int RemainingTicksFor(THost subject, ImmutableArray<T> chain, int index)
    {
        int takenTicks = FieldValue(chain[index], DelayField);
        for (int otherIndex = 0; otherIndex < chain.Length; otherIndex++)
        {
            if (otherIndex != index)
            {
                takenTicks += FieldValue(chain[otherIndex], DelayField) + FieldValue(chain[otherIndex], LengthField);
            }
        }

        return subject.CapacityTicks - takenTicks;
    }

    // What one field of an entry can hold on every subject at once: a tick count what it can hold on the shown subject, an edit past another's end being
    // cut there, and any other field the part of the ranges of the subjects the entry reaches that they all share.
    private ValueRange SharedFieldRange(IReadOnlyList<THost> members, ImmutableArray<T> chain, int index, int field)
    {
        IEnumerable<THost> rangeMembers = IsTickCountField(field) ? [ShownSubject(members)] : members.Where(member => index < HeldBy(member, chain).Length);
        var ranges = rangeMembers.Select(member => FieldRange(member, chain, index, field)).ToList();
        return new(ranges.Max(range => range.Min), ranges.Min(range => range.Max));
    }

    // The subjects, the chain they all hold and the field a box stands for, where the box is still one of the rows', they hold one chain and the entry is still there.
    private bool TryFindField(object? sender, out IReadOnlyList<THost> members, out ImmutableArray<T> chain, out FieldSlot slot)
    {
        members = Subjects().Members;
        chain = default;
        slot = default;

        if (sender is not TextBox { Tag: FieldSlot boxSlot } box || !RowControls().Contains(box) || SharedChain(members) is not { } sharedChain)
        {
            return false;
        }

        chain = sharedChain;
        slot = boxSlot;
        return slot.Index < chain.Length;
    }

    // A field's text read as a value the field can hold.
    private bool TryReadField(string? text, int field, ValueRange range, out int value)
    {
        bool isRead = IsSignedField(field) ? FieldInput.TryReadSigned(text, range, out value) : FieldInput.TryReadValue(text, range, out value);
        return isRead && !IsValueRefused(field, value);
    }

    // The key a run of typing into one field records its undo step under: the section, the row and the field.
    private object FieldUndoKey(int index, int field)
    {
        return (SectionParts.Rows, index, field);
    }

    // Applies a field to every subject as it is typed and restates the other boxes, which the edit may have moved.
    private void OnFieldChanged(object? sender, TextChangedEventArgs e)
    {
        if (IsUpdating || FieldSteps.IsInert((TextBox)sender!))
        {
            return;
        }

        if (!TryFindField(sender, out var members, out var before, out var slot))
        {
            return;
        }

        var box = (TextBox)sender!;
        if (!TryReadField(box.Text, slot.Field, SharedFieldRange(members, before, slot.Index, slot.Field), out int value))
        {
            return;
        }

        CommitFieldEdit(chain => WithFieldEdited(chain, slot.Index, slot.Field, value), slot, box);
    }

    // Applies an edit made on one field to every subject and restates the other boxes, which the edit may have moved, the box being typed into excepted where there is one.
    private void CommitFieldEdit(Func<ImmutableArray<T>, ImmutableArray<T>> edit, FieldSlot slot, TextBox? typing)
    {
        RunWithEditsHeldOff(() =>
        {
            CommitToSharedChain(edit, FieldUndoKey(slot.Index, slot.Field));
            RestateFieldsExcept(typing);
        });
    }

    // Runs an action with the rows' own edits held off, so what it writes into the rows is not applied back as an edit.
    protected void RunWithEditsHeldOff(Action action)
    {
        IsUpdating = true;
        try
        {
            action();
        }
        finally
        {
            IsUpdating = false;
        }
    }

    // Writes every box's value and locked state from the chain the subjects hold, the text of the box being typed into excepted where there is one.
    protected void RestateFieldsExcept(TextBox? typing)
    {
        if (SharedChain(Subjects().Members) is not { } chain)
        {
            return;
        }

        foreach (var box in RowControls().OfType<TextBox>())
        {
            if (box.Tag is not FieldSlot slot || slot.Index >= chain.Length)
            {
                continue;
            }

            box.IsEnabled = !IsFieldLocked(chain, slot.Index, slot.Field);

            if (box != typing)
            {
                box.Text = FieldText(chain[slot.Index], slot.Field);
            }
        }
    }

    // Steps a field by an arrow key, clamped to what the chain lets it hold on every subject and over a value the field refuses.
    private void OnFieldKeyDown(object? sender, KeyEventArgs e)
    {
        if (!FieldSteps.TryTakeStep(e, out int step))
        {
            return;
        }


        if (!TryFindField(sender, out var members, out var chain, out var slot))
        {
            return;
        }

        var range = SharedFieldRange(members, chain, slot.Index, slot.Field);
        if (range.Min > range.Max)
        {
            return;
        }

        int steppedValue = range.Clamp(SteppedValue(chain[slot.Index], slot.Field, step));
        if (IsValueRefused(slot.Field, steppedValue))
        {
            steppedValue = range.Clamp(steppedValue + step);
        }

        if (IsValueRefused(slot.Field, steppedValue))
        {
            return;
        }

        ApplySteppedValue((TextBox)sender!, slot, steppedValue);
    }

    // Applies a value a step or a reset arrived at to every subject and shows what the chain then holds in every field, this one included.
    private void ApplySteppedValue(TextBox box, FieldSlot slot, int value)
    {
        CommitFieldEdit(chain => WithFieldEdited(chain, slot.Index, slot.Field, value), slot, box);
        RestateField(box);
    }

    // Puts a field back to what the chain holds when it loses the focus, touching no other field.
    private void OnFieldCommitted(object? sender, RoutedEventArgs e)
    {
        RestateField((TextBox)sender!);
    }

    // Writes what the chain holds into one field with the caret after it, touching no other field.
    private void RestateField(TextBox box)
    {
        if (!TryFindField(box, out _, out var chain, out var slot))
        {
            return;
        }

        RunWithEditsHeldOff(() => FieldInput.WriteText(box, FieldText(chain[slot.Index], slot.Field)));
    }

    // Takes off the row whose bin was pressed.
    private void OnRemoveClicked(object? sender, RoutedEventArgs e)
    {
        if (((Button)sender!).Tag is not int index)
        {
            return;
        }

        RemoveRow(index);
    }

    // Which entry a box belongs to and which of its fields it is.
    private readonly record struct FieldSlot(int Index, int Field);
}
