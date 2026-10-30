using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// A list of rows under a line of column headings; the headings and every row are laid out on one grid, and what a row says and what a press on it
// does are the window's.
public sealed class ListTable : DockPanel
{
    private const double ColumnGap = Space.Px10;

    private readonly ListFrame _frame = new() { Margin = new Thickness(0, Space.Px4, 0, 0) };
    private ListTableColumn[] _columns = [];

    private readonly Decorator _header = new();

    // Builds the headings and the framed, scrolling list under them.
    public ListTable()
    {
        SetDock(_header, Dock.Top);
        Children.Add(_header);
        Children.Add(_frame);
        _frame.RowsPanel.SizeChanged += (_, _) => StandHeadingsOverRows();
    }

    // What each column says and how wide its widest content is; setting it lays the headings out.
    public ListTableColumn[] Columns
    {
        get
        {
            return _columns;
        }
        set
        {
            _columns = value;
            ShowHeadings();
        }
    }

    // The rows under the headings, which the window fills.
    public Avalonia.Controls.Controls Rows
    {
        get
        {
            return _frame.RowsPanel.Children;
        }
    }

    // One list row laid out on the headings' grid, its cells drawn in tabular figures and handed back through the array to be written, with controls
    // standing at its right end in the order given.
    public Border BuildRow(TextBlock[] cells, params Control[] controlsAtRight)
    {
        var content = new DockPanel();
        for (int i = controlsAtRight.Length - 1; i >= 0; i--)
        {
            SetDock(controlsAtRight[i], Dock.Right);
            content.Children.Add(controlsAtRight[i]);
        }

        content.Children.Add(BuildCellGrid(cells));
        foreach (var cell in cells)
        {
            cell.FontFeatures = AppFonts.Tabular;
        }

        return ListRows.Build(content);
    }

    // Shows a check mark in a row's cell where a flag is set and nothing where it is not, the mark drawn in whatever color the cell's text is painted.
    public static void ShowCheck(TextBlock cell, bool isSet)
    {
        ClearCell(cell);
        if (!isSet)
        {
            return;
        }

        var shape = new IconShape
        {
            Data = AppIcons.Check,
            [!Shape.FillProperty] = cell[!TextBlock.ForegroundProperty],
        };
        cell.Inlines = [CenteredOnLine(shape)];
    }

    // Empties a row's cell of its text and of any check mark in it.
    public static void ClearCell(TextBlock cell)
    {
        cell.Inlines = null;
        cell.Text = "";
    }

    // Writes a row's texts into its cells in column order, from the first, a column of hex numbers standing each hex digit in a slot of one width.
    public void WriteRow(TextBlock[] cells, params string[] texts)
    {
        for (int column = 0; column < texts.Length; column++)
        {
            if (_columns[column].HasDigitSlots)
            {
                WriteInDigitSlots(cells[column], texts[column]);
                continue;
            }

            cells[column].Text = texts[column];
        }
    }

    // Writes a text into a cell a character at a time, each hex digit centered in a slot of one width so that the digits of every row stand in line.
    private static void WriteInDigitSlots(TextBlock cell, string text)
    {
        cell.Inlines = [.. text.Select(character => CenteredOnLine(new TextBlock
        {
            Text = character.ToString(),
            Width = ListTableColumn.IsSlotted(character) ? ListTableColumn.HexDigitSlotWidth : double.NaN,
            TextAlignment = TextAlignment.Center,
            FontFeatures = AppFonts.Tabular,
        }))];
    }

    // A control standing inline in a cell's text, centered on the line rather than on the baseline, which would sit it high in the row.
    private static InlineUIContainer CenteredOnLine(Control control)
    {
        return new InlineUIContainer(control) { BaselineAlignment = BaselineAlignment.Center };
    }

    // Lays the headings out on the columns' grid.
    private void ShowHeadings()
    {
        var cells = new TextBlock[_columns.Length];
        var row = BuildCellGrid(cells);
        for (int i = 0; i < _columns.Length; i++)
        {
            cells[i].Text = _columns[i].Header;
            cells[i].Foreground = ThemeResource.TextSecondaryBrush;
        }

        _header.Child = row;
    }

    // Stands the headings' line in from the table's two sides as far as the rows' stack is, so each heading is over its column and none is drawn past
    // where the rows are cut off.
    private void StandHeadingsOverRows()
    {
        var rows = _frame.RowsPanel;
        if (rows.TranslatePoint(default, this) is not { } rowsTopLeft)
        {
            return;
        }

        var padding = new Thickness(rowsTopLeft.X, 0, Bounds.Width - rowsTopLeft.X - rows.Bounds.Width, Space.Px4);
        if (_header.Padding != padding)
        {
            _header.Padding = padding;
        }
    }

    // The grid the headings and every row share, each column its widest content and one gap wide whatever room it is given, one left-aligned cell per
    // column, each cell written into the array; held in a frame that cuts off what the room given does not hold.
    private Panel BuildCellGrid(TextBlock[] cells)
    {
        var grid = new Grid
        {
            Width = _columns.Sum(column => column.ContentWidth + ColumnGap),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        for (int i = 0; i < _columns.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(_columns[i].ContentWidth + ColumnGap, GridUnitType.Pixel));
            var cell = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, ColumnGap, 0),
            };

            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
            cells[i] = cell;
        }

        return new Panel { ClipToBounds = true, Children = { grid } };
    }
}
