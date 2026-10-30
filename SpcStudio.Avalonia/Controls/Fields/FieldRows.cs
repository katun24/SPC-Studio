using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The grid a repeating list of field rows is built on and the header row over it, both from one list of columns, and the rule that rebuilds such a list only where its rows would differ.
public static class FieldRows
{
    public const string DelayLabel = "Delay";
    public const string LengthLabel = "Length";
    public const string FadeLabel = "Fade";
    private const string ByteFieldWidthResource = "ByteFieldWidth";

    public static readonly Thickness GapUnderRow = new(0, 0, 0, Space.Px4);
    private static readonly ConditionalWeakTable<Panel, object> SourcesOfStandingRows = [];

    // How wide a numeric field in one of these rows is.
    public static double FieldWidth
    {
        get
        {
            return ThemeResource.Of<double>(ByteFieldWidthResource);
        }
    }

    // How wide the square remove button on the end of every row is.
    public static double RemoveButtonWidth
    {
        get
        {
            return ThemeResource.FieldHeight;
        }
    }

    // The grid one row of the list is built on, the header included: each column its own width, with an equal share of what is left over between every two.
    public static Grid BuildRowGrid(IReadOnlyList<FieldColumn> columns)
    {
        var grid = new Grid { Margin = GapUnderRow };
        for (int i = 0; i < columns.Count; i++)
        {
            if (i > 0)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
            }

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(columns[i].Width) });
        }

        return grid;
    }

    // The grid column a list column stands in, past the gap columns in front of it.
    private static int GridColumnOf(int index)
    {
        return index * 2;
    }

    // The header row: one labeled cell per column as wide as the field under it, and an empty cell of that width for a column with no word.
    public static Grid BuildHeader(IReadOnlyList<FieldColumn> columns)
    {
        var row = BuildRowGrid(columns);
        for (int i = 0; i < columns.Count; i++)
        {
            Control cell = columns[i].Label.Length == 0
                ? new Border { Width = columns[i].Width }
                : new LabeledField { Label = columns[i].Label, Width = columns[i].Width };

            Grid.SetColumn(cell, GridColumnOf(i));
            row.Children.Add(cell);
        }

        return row;
    }

    // Puts a control into its column of a row, at the width that column states.
    public static void PlaceCell(Grid row, IReadOnlyList<FieldColumn> columns, int index, Control cell)
    {
        cell.Width = columns[index].Width;
        cell.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumn(cell, GridColumnOf(index));
        row.Children.Add(cell);
    }

    // A new square remove button showing a bin, with the tooltip given.
    public static Button BuildRemoveButton(string tooltip)
    {
        var removeButton = new Button { Content = new IconShape { Data = AppIcons.Bin, Fill = ThemeResource.TextPrimaryBrush }, Classes = { ThemeClasses.SquareIcon } };
        ToolTip.SetTip(removeButton, tooltip);
        return removeButton;
    }

    // Clears a code-built list and records what it is about to be built from, where the rows standing were built from something else; answers whether the caller is to build its rows.
    public static bool ClearIfStale<T>(Panel rows, ImmutableArray<T> source) where T : IEquatable<T>
    {
        if (SourcesOfStandingRows.TryGetValue(rows, out object? standingSource) && standingSource is ImmutableArray<T> standingEntries && standingEntries.SequenceEqual(source))
        {
            return false;
        }

        SourcesOfStandingRows.AddOrUpdate(rows, source);
        rows.Children.Clear();
        return true;
    }

    // Takes a code-built list's rows away, where it has any.
    public static void ClearRows(Panel rows)
    {
        if (!SourcesOfStandingRows.Remove(rows))
        {
            return;
        }

        rows.Children.Clear();
    }
}
