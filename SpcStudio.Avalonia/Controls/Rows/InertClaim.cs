namespace SpcStudio.Controls;

// Whether a section's rows are shown but are not the thing to edit until pressed, and what a press on them does first, handed the key the press records under.
public sealed record InertClaim(Func<bool> IsInert, Action<object?> ClaimRows);
