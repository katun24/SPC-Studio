namespace SpcStudio;

// The title of a message box that wears the app's own icon.
public sealed record PlainTitle(string Text) : BoxTitle(Text, null);
