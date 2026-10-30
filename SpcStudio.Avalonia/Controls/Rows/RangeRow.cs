using Avalonia;
using Avalonia.Controls;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// One row of a range: the word at the left, then the two fields the markup puts in the row with a word between them, the lowest value's first; the word
// at the left is as wide as the widest of the rows stacked with it, so their fields line up.
public sealed class RangeRow : LabeledRow
{
    private const string ColumnLayout = "Auto,Auto,Auto,Auto";
    private const string JoiningWord = "to";
    private const int LowestFieldColumn = 1;
    private const int JoiningWordColumn = 2;
    private const int HighestFieldColumn = 3;

    private static readonly int[] FieldColumns = [LowestFieldColumn, HighestFieldColumn];

    // Builds the row's columns, its label and the word between its two fields.
    public RangeRow() : base(ColumnLayout, ThemeClasses.RangeRow)
    {
        var joiningWord = new TextBlock { Text = JoiningWord };
        joiningWord.Classes.Add(ThemeClasses.RangeJoiningWord);
        SetColumn(joiningWord, JoiningWordColumn);
        Children.Add(joiningWord);
    }

    // Puts the row's two fields either side of the joining word, in the order the markup gives them, and marks the lowest one for its gap from the label.
    protected override void OnInitialized()
    {
        base.OnInitialized();
        var fields = Children.OfType<TextBox>().ToList();
        for (int i = 0; i < Math.Min(fields.Count, FieldColumns.Length); i++)
        {
            SetColumn(fields[i], FieldColumns[i]);
        }

        if (fields.Count > 0)
        {
            fields[0].Classes.Add(ThemeClasses.RangeLowestField);
        }
    }

    // Widens the word at the left to the widest word of the range rows stacked with this one before the row is laid out.
    protected override Size MeasureOverride(Size availableSize)
    {
        LabelBlock.Width = AppFonts.WidestUiTextWidth(StackedRows().Select(row => row.Label ?? ""), LabelBlock.FontSize);
        return base.MeasureOverride(availableSize);
    }

    // The range rows standing in the same stack as this one, this one included.
    private IEnumerable<RangeRow> StackedRows()
    {
        return Parent is Panel stack ? stack.Children.OfType<RangeRow>() : [this];
    }
}
