using SpcStudio.Theme;

namespace SpcStudio.Controls;

// One row of a panel's values: the word at the left, the override slot beside it, and whatever the markup puts in the row, laid out in the columns every value row shares.
public sealed class FieldRow : LabeledRow
{
    private const string ColumnLayout = "Auto,Auto,*,Auto,Auto";

    // Builds the row's columns and its label.
    public FieldRow() : base(ColumnLayout, ThemeClasses.FieldRow)
    {
    }

    // Gives the row an empty override slot where its markup named none.
    protected override void OnInitialized()
    {
        base.OnInitialized();
        if (!Children.OfType<FieldOverride>().Any())
        {
            Children.Add(new FieldOverride());
        }
    }
}
