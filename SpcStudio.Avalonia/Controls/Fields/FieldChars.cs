namespace SpcStudio.Controls;

// The set of characters a text field takes; anything outside it never reaches the box.
public enum FieldChars
{
    Text,
    Digits,
    Signed,
    Decimal,
    Hex,
    NoteName,
    NoiseClock,
    DiskName,
}
