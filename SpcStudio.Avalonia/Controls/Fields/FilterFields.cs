using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The eight boxes holding an echo filter's coefficients as the hex the command writes, in two rows of four: writing a filter into them, reading one
// back out of them, telling which box is typed into, and their arrow keys, wheel and right-click.
public sealed class FilterFields : Grid
{
    private const int BoxesPerRow = 4;

    private readonly TextBox[] _boxes = new TextBox[SnesEcho.TapCount];

    // Lays the boxes out a row of four at a time, the first coefficient's first, each one behind its keys, its right-click and its typing.
    public FilterFields()
    {
        HorizontalAlignment = HorizontalAlignment.Left;
        ColumnSpacing = Space.Px8;
        RowSpacing = Space.Px4;
        for (int column = 0; column < BoxesPerRow; column++)
        {
            ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        }

        for (int row = 0; row < SnesEcho.TapCount / BoxesPerRow; row++)
        {
            RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        for (int tap = 0; tap < SnesEcho.TapCount; tap++)
        {
            _boxes[tap] = NewBox(tap);
        }
    }

    // The filter the boxes describe, packed, or null where any box holds no byte.
    public long? Coefficients
    {
        get
        {
            var coefficients = new sbyte[SnesEcho.TapCount];
            for (int tap = 0; tap < SnesEcho.TapCount; tap++)
            {
                if (!FieldInput.TryReadHex(_boxes[tap].Text, FieldInput.ByteRange, out int coefficient))
                {
                    return null;
                }

                coefficients[tap] = (sbyte)coefficient;
            }

            return SnesEcho.PackCoefficients(coefficients);
        }
    }

    // Raised with the box typed into, whenever one of them changes.
    public event Action<TextBox>? Typed;

    // Writes a filter's coefficients into the boxes, leaving alone the box an edit came from.
    public void Show(long coefficients, object? source)
    {
        for (int tap = 0; tap < SnesEcho.TapCount; tap++)
        {
            if (!ReferenceEquals(_boxes[tap], source))
            {
                _boxes[tap].Text = CoefficientText(coefficients, tap);
            }
        }
    }

    // Builds one coefficient's box in its place in the grid, stepped by its keys, put back at the default filter's coefficient by its right-click, and
    // reporting its typing.
    private TextBox NewBox(int tap)
    {
        var box = new TextBox();
        box.Classes.Add(ThemeClasses.HexByteField);
        SetRow(box, tap / BoxesPerRow);
        SetColumn(box, tap % BoxesPerRow);
        Children.Add(box);

        string defaultCoefficientText = CoefficientText(SnesEcho.PackedPresets[SnesEcho.DefaultFilter], tap);
        FieldSteps.OnKeys(box, OnKeyDown);
        FieldResets.OnReset(box, () => FieldInput.WriteText(box, defaultCoefficientText));
        box.TextChanged += (_, _) => Typed?.Invoke(box);
        return box;
    }

    // One of a filter's coefficients as the hex its box shows.
    private static string CoefficientText(long coefficients, int tap)
    {
        return FieldInput.HexTextOf((byte)SnesEcho.CoefficientAt(coefficients, tap));
    }

    // Steps the coefficient a box holds, as the signed byte it is.
    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var box = (TextBox)sender!;
        int step = FieldSteps.StepOf(e);
        if (step == 0 || !FieldInput.TryReadHex(box.Text, FieldInput.ByteRange, out int current))
        {
            return;
        }

        FieldInput.WriteText(box, FieldInput.HexTextOf((byte)Math.Clamp((sbyte)current + step, sbyte.MinValue, sbyte.MaxValue)));
        e.Handled = true;
    }
}
