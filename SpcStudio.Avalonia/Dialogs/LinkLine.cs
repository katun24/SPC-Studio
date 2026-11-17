namespace SpcStudio;

// A line of the About box that opens with a link: the link's words, the page it opens and the words after it.
public sealed record LinkLine(string LinkText, Uri Source, string TextAfter);
