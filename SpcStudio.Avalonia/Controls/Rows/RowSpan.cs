using SpcStudio.Model;

namespace SpcStudio.Controls;

// One row of rows that follow one another with no gap, as its section shows it: how long it runs, or null where it runs on to the end of every note, and the row itself.
public readonly record struct RowSpan<TRow>(int? LengthTicks, TRow Row) where TRow : struct, IOffsetRow<TRow>;
