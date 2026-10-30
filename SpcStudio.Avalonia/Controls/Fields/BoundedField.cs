using Avalonia.Controls;
using Avalonia.Input;
using SpcStudio.Model;

namespace SpcStudio.Controls;

// A field holding one value between two bounds: read as it is typed, stepped by the arrow keys and the wheel, put back at its base value by a
// right-click, and written back tidily once it loses the focus.
public sealed class BoundedField
{
    private const int PitchStep = 1;

    private readonly TextBox _box;
    private readonly ValueRange _range;
    private readonly int _baseValue;
    private readonly Func<int> _stepSizeOf;
    private readonly Func<int, string> _textOf;
    private readonly TextReader _tryRead;

    // Wires a field to its bounds and its spelling, and writes its base value into it.
    private BoundedField(TextBox box, ValueRange range, int baseValue, Func<int> stepSizeOf, Func<int, string> textOf, TextReader tryRead)
    {
        _box = box;
        _range = range;
        _baseValue = range.Clamp(baseValue);
        _stepSizeOf = stepSizeOf;
        _textOf = textOf;
        _tryRead = tryRead;
        Value = _baseValue;

        WriteValue(_baseValue);
        box.TextChanged += (_, _) => ReadTypedValue();
        box.LostFocus += (_, _) => WriteValue(Value);
        FieldSteps.OnKeys(box, (_, e) => StepValue(e));
        FieldResets.OnReset(box, () => WriteValue(_baseValue));
    }

    // Reads a field's text into a value, answering false for a text that is not one.
    private delegate bool TextReader(string? text, out int value);

    // Raised as the value changes, by typing, a step or a reset.
    public event Action? ValueChanged;

    // The value the field holds: the last one typed into it that it could hold.
    public int Value { get; private set; }

    // A field holding a whole number, stepped by a size that may change while it stands.
    public static BoundedField ForNumber(TextBox box, ValueRange range, int baseValue, Func<int> stepSizeOf)
    {
        return new BoundedField(box, range, baseValue, stepSizeOf, value => FieldInput.TextOf(value),
            (string? text, out int value) => FieldInput.TryReadValue(text, range, out value));
    }

    // A field holding a pitch on the roll by its name, stepped a semitone at a time.
    public static BoundedField ForPitch(TextBox box, int baseSemitone)
    {
        return new BoundedField(box, Song.SemitoneRange, baseSemitone, () => PitchStep, PitchLabel.NameOf,
            (string? text, out int value) => PitchLabel.TryParse(text ?? "", out value));
    }

    // Takes a typed text as the value where the field can hold it, leaving the value as it was otherwise.
    private void ReadTypedValue()
    {
        if (!_tryRead(_box.Text, out int value) || value == Value)
        {
            return;
        }

        Value = value;
        ValueChanged?.Invoke();
    }

    // Steps the value by an arrow key or a wheel notch.
    private void StepValue(KeyEventArgs e)
    {
        if (!FieldSteps.TryTakeStep(e, out int step))
        {
            return;
        }

        WriteValue(Value + step * _stepSizeOf());
    }

    // Writes a value into the field as typing would, held to the bounds.
    private void WriteValue(int value)
    {
        FieldInput.WriteText(_box, _textOf(_range.Clamp(value)));
    }
}
