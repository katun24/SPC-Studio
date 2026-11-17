using System.Collections.Immutable;
using SpcStudio.Model;

namespace SpcStudio.Controls;

// The amplify's rows - the note panel's or the instrument panel's: one row per stretch, each its Length and the $FA $03 byte it holds, the command
// having no fade form.
public sealed class AmplifyChainRows : SpanRows<AmplifyRow, IAmplifyHost>
{
    private static readonly RowSectionTooltips AmplifyTooltips = new(Tooltips.AddAmplifyRow, Tooltips.ClearAmplifyChanges);

    // The labels of the two number fields.
    protected override IReadOnlyList<string> FieldLabels
    {
        get
        {
            return [FieldRows.LengthLabel, SettingLabels.Amplify];
        }
    }

    // The rows over an Amplify section and what they edit.
    public static AmplifyChainRows Over(ListSectionParts parts, Func<RowSubjects<IAmplifyHost>> subjects, InertClaim? inertClaim = null)
    {
        return new AmplifyChainRows
        {
            SectionParts = parts,
            SectionTooltips = AmplifyTooltips,
            Subjects = subjects,
            InertClaim = inertClaim,
        };
    }

    // What the Amplify field shows.
    protected override int RowFieldValue(AmplifyRow row, int field)
    {
        return row.Value;
    }

    // The Amplify field holds what $FA $03 takes.
    protected override ValueRange RowFieldRange(int field)
    {
        return SnesVolume.AmplifyRange;
    }

    // A row with its amplify set.
    protected override AmplifyRow WithRowFieldEdited(AmplifyRow row, int field, int value)
    {
        return row with { Value = value };
    }

    // The value a right-click puts the Amplify field back at: the first row's to the subject's own starting amplify, and any other row's to the amplify
    // of the row in front of it.
    protected override int ValueFieldBaseValue(IAmplifyHost subject, ImmutableArray<RowSpan<AmplifyRow>> chain, int index, int field)
    {
        return index == OffsetRows.FirstRowIndex ? subject.BaseAmplify : chain[index - 1].Row.Value;
    }
}
