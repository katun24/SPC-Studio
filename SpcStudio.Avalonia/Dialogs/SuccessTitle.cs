using SpcStudio.Theme;

namespace SpcStudio;

// The title of a box that reports a success, which the box shows with the success icon.
public sealed record SuccessTitle(string Text) : BoxTitle(Text, ThemeClasses.SuccessBox);
