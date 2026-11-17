namespace SpcStudio.Controls;

// The subjects a section's rows edit together - the selected notes, or the instrument showing - and the one place a write to all of them is
// recorded as one edit under an undo key, the write answering whether it changed anything.
public sealed record RowSubjects<THost>(IReadOnlyList<THost> Members, Action<Func<bool>, object?> Commit) where THost : IRowHost;
