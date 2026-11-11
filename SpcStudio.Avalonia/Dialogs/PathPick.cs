using Avalonia.Controls;

namespace SpcStudio;

// One path a row picks or takes typed: the title and the text of the box refusing one it cannot take, and the picker its Browse button opens over a window,
// starting from the path the row shows.
public sealed record PathPick(FailureTitle RefusalTitle, Func<string, string?> RefusalOf, Func<Window, string?, Task<string?>> PickFrom);
