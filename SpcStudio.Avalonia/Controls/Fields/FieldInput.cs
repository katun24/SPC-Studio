using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using SpcStudio.Model;

namespace SpcStudio.Controls;

// A text field's character set, as an attached property the field styles carry, with the filter it hangs on typing and pasting;
// and the one way a field's number is read, written, shared across a selection and focused.
public static class FieldInput
{
    private const string AcceptsPropertyName = "Accepts";
    private const string NoValueText = "";
    private const string HexByteFormat = "X2";
    private const char HexCommandPrefix = '$';

    // The character set a text field takes, which the field styles carry.
    public static readonly AttachedProperty<FieldChars> AcceptsProperty = AvaloniaProperty.RegisterAttached<TextBox, FieldChars>(AcceptsPropertyName, typeof(FieldInput), FieldChars.Text);

    // Hooks the filter onto a box whenever its character set changes.
    static FieldInput()
    {
        AcceptsProperty.Changed.AddClassHandler<TextBox, FieldChars>(OnAcceptsChanged);
    }

    // The decimal separator of the user's own culture.
    private static string LocalDecimalSeparator
    {
        get
        {
            return CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        }
    }

    // The character set a box takes.
    public static FieldChars GetAccepts(AvaloniaObject box)
    {
        return box.GetValue(AcceptsProperty);
    }

    // Sets the character set a box takes.
    public static void SetAccepts(AvaloniaObject box, FieldChars value)
    {
        box.SetValue(AcceptsProperty, value);
    }

    // Whether the focus is in a field where a space is a character rather than the transport's key.
    public static bool TakesSpace(IInputElement? focused)
    {
        return focused is TextBox box && GetAccepts(box) is FieldChars.Text or FieldChars.DiskName;
    }

    // Reads an unsigned whole number in a range from a field's text.
    public static bool TryReadValue(string? text, ValueRange range, out int value)
    {
        return TryReadInRange(text, NumberStyles.None, range, out value);
    }

    // Reads a whole number with an optional leading sign in a range from a field's text.
    public static bool TryReadSigned(string? text, ValueRange range, out int value)
    {
        return TryReadInRange(text, NumberStyles.AllowLeadingSign, range, out value);
    }

    // Reads hex digits in a range from a field's text, the dollar sign a command is written with being optional.
    public static bool TryReadHex(string? text, ValueRange range, out int value)
    {
        return TryReadInRange(text?.Trim().TrimStart(HexCommandPrefix), NumberStyles.AllowHexSpecifier, range, out value);
    }

    // A value as a field shows it, or nothing where there is no one value to show.
    public static string TextOf(int? value)
    {
        return value is int number ? number.ToString(CultureInfo.InvariantCulture) : NoValueText;
    }

    // A byte as a hex field shows it, two digits with no prefix.
    public static string HexTextOf(int value)
    {
        return value.ToString(HexByteFormat, CultureInfo.InvariantCulture);
    }

    // Writes a value into a field with the caret after it, as a step or a slider does.
    public static void WriteValue(TextBox box, int value)
    {
        WriteText(box, TextOf(value));
    }

    // Writes a value a step moved and held to a range into a field, as a step does.
    public static void WriteStepped(TextBox box, int value, int step, ValueRange range)
    {
        WriteValue(box, range.Clamp(value + step));
    }

    // Writes a field's text with the caret after it, as a step or a slider does.
    public static void WriteText(TextBox box, string text)
    {
        box.Text = text;
        box.CaretIndex = text.Length;
    }

    // Writes a field's text with the caret after it, unless the field already holds that text.
    public static void WriteChangedText(TextBox box, string text)
    {
        if (box.Text == text)
        {
            return;
        }

        WriteText(box, text);
    }

    // The value every item holds, or null where they disagree or there are none.
    public static int? SharedValue<T>(IReadOnlyList<T> items, Func<T, int> valueOf)
    {
        if (items.Count == 0)
        {
            return null;
        }

        int firstValue = valueOf(items[0]);
        for (int i = 1; i < items.Count; i++)
        {
            if (valueOf(items[i]) != firstValue)
            {
                return null;
            }
        }

        return firstValue;
    }

    // Whether every item is on or every one off, or null where they disagree or there are none.
    public static bool? SharedFlag<T>(IReadOnlyList<T> items, Func<T, bool> isOn)
    {
        return SharedValue(items, item => isOn(item) ? 1 : 0) switch
        {
            1 => true,
            0 => false,
            _ => null,
        };
    }

    // Whether every item is on or every one off, off with no items, and null where they differ.
    public static bool? SharedFlagOrOff<T>(IReadOnlyList<T> items, Func<T, bool> isOn)
    {
        return items.Count == 0 ? false : SharedFlag(items, isOn);
    }

    // What a press on a check box over the items asks for: on where they were mixed, and the box's own new state otherwise.
    public static bool IsAskedOn<T>(CheckBox box, IReadOnlyList<T> items, Func<T, bool> isOn)
    {
        return SharedFlag(items, isOn) is null || box.IsChecked == true;
    }

    // Puts the caret in a field and selects what is in it, laying out the nearest ancestor in the tree first so the field is there to take the focus.
    public static void FocusField(TextBox box)
    {
        for (StyledElement? element = box; element is not null; element = element.Parent)
        {
            if (element is Layoutable layoutable && layoutable.GetVisualRoot() is not null)
            {
                layoutable.UpdateLayout();
                break;
            }
        }

        box.Focus();
        box.SelectAll();
    }

    // Hangs the filter and the select-on-first-press on a box that takes a set, and takes them off one that goes back to free text.
    private static void OnAcceptsChanged(TextBox box, AvaloniaPropertyChangedEventArgs<FieldChars> e)
    {
        bool wasFiltered = e.OldValue.GetValueOrDefault() != FieldChars.Text;
        bool isFiltered = e.NewValue.GetValueOrDefault() != FieldChars.Text;
        if (wasFiltered == isFiltered)
        {
            return;
        }

        if (isFiltered)
        {
            box.AddHandler(InputElement.TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
            box.AddHandler(TextBox.PastingFromClipboardEvent, OnPasting);
            box.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
            return;
        }

        box.RemoveHandler(InputElement.TextInputEvent, OnTextInput);
        box.RemoveHandler(TextBox.PastingFromClipboardEvent, OnPasting);
        box.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
    }

    // Focuses a field on its first left press and selects all of it, so its value can be typed over.
    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not TextBox box || box.IsFocused)
        {
            return;
        }

        if (!e.GetCurrentPoint(box).Properties.IsLeftButtonPressed)
        {
            return;
        }

        box.Focus(NavigationMethod.Pointer);
        box.SelectAll();
        e.Handled = true;
    }

    // Refuses typed text holding a character the box does not take.
    private static void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (sender is TextBox box && e.Text is string text && !AllowsText(GetAccepts(box), text))
        {
            e.Handled = true;
        }
    }

    // Cancels a paste and does it again once the clipboard's text is known to be allowed.
    private static void OnPasting(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box)
        {
            return;
        }

        e.Handled = true;
        _ = PasteIfAllowed(box);
    }

    // Pastes the clipboard's text into a box where every character of it is allowed, and nothing where one is not.
    private static async Task PasteIfAllowed(TextBox box)
    {
        if (TopLevel.GetTopLevel(box)?.Clipboard is not { } clipboard)
        {
            return;
        }

        using var transfer = await clipboard.TryGetDataAsync();
        if (transfer is null)
        {
            return;
        }

        string? text = await transfer.TryGetTextAsync();
        if (text is null || !AllowsText(GetAccepts(box), text))
        {
            return;
        }

        box.SelectedText = text;
    }

    // Parses a whole number with the given styles and answers whether it is inside a range.
    private static bool TryReadInRange(string? text, NumberStyles styles, ValueRange range, out int value)
    {
        return int.TryParse(text?.Trim(), styles, CultureInfo.InvariantCulture, out value) && range.Contains(value);
    }

    // Whether a set takes every character of a non-empty text.
    private static bool AllowsText(FieldChars accepts, string text)
    {
        return text.Length > 0 && text.All(character => AllowsCharacter(accepts, character));
    }

    // Whether a set takes one character.
    private static bool AllowsCharacter(FieldChars accepts, char character)
    {
        return accepts switch
        {
            FieldChars.Digits => IsDigit(character),
            FieldChars.Signed => IsDigit(character) || character == '-',
            FieldChars.Decimal => IsDigit(character) || character == '.' || LocalDecimalSeparator.Contains(character),
            FieldChars.Hex => IsHexDigit(character) || character is '$' or 'x' or 'X',
            FieldChars.NoteName => IsDigit(character) || character is >= 'a' and <= 'g' or >= 'A' and <= 'G' or '#',
            FieldChars.NoiseClock => IsHexDigit(character),
            FieldChars.DiskName => DiskNameRules.IsAllowed(character),
            _ => true,
        };
    }

    // Whether a character is a decimal digit.
    private static bool IsDigit(char character)
    {
        return character is >= '0' and <= '9';
    }

    // Whether a character is a hex digit in either case.
    private static bool IsHexDigit(char character)
    {
        return IsDigit(character) || character is >= 'a' and <= 'f' or >= 'A' and <= 'F';
    }
}
