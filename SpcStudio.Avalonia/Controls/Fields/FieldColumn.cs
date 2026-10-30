namespace SpcStudio.Controls;

// One column of a repeating list of field rows: the word over it and how wide the thing in the cell is.
public readonly record struct FieldColumn(string Label, double Width);
