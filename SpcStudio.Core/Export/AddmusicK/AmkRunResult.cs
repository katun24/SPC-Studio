namespace SpcStudio.Export;

// What a run of AddmusicK left once it ended: everything it printed, and whether it ended without an error.
public sealed record AmkRunResult(string Printed, bool HasSucceeded);
