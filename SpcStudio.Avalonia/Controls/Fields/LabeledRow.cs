using Avalonia.Controls;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// A row of fields with its word at the left, laid out in the columns its kind of row gives it.
public abstract class LabeledRow : ThemedGrid
{
    // Builds the row's columns and its label, and gives the row its kind's style class.
    protected LabeledRow(string columnLayout, string rowClass)
    {
        ColumnDefinitions = new ColumnDefinitions(columnLayout);
        Classes.Add(rowClass);
        LabelBlock.Classes.Add(ThemeClasses.FieldLabel);
        Children.Add(LabelBlock);
    }

    // The word at the left of the row.
    public string? Label
    {
        get
        {
            return LabelBlock.Text;
        }
        set
        {
            LabelBlock.Text = value;
        }
    }

    // The text block the word at the left is drawn in.
    protected TextBlock LabelBlock { get; } = new();
}
