using SpcStudio.Theme;

namespace SpcStudio;

// The title of a box that gives a notice or asks a question, which the box shows with the notice icon.
public sealed record NoticeTitle(string Text) : BoxTitle(Text, ThemeClasses.NoticeBox);
