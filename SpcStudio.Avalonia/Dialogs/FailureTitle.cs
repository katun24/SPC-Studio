using SpcStudio.Theme;

namespace SpcStudio;

// The title of a box that reports a failure, which the box shows with the failure icon.
public sealed record FailureTitle(string Text) : BoxTitle(Text, ThemeClasses.FailureBox);
