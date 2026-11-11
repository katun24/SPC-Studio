namespace SpcStudio.Import;

// A message about one line of a song's text, the line written in front of it.
public static class LineMessage
{
    private const string LineWord = "Line ";
    private const string LineEnd = ": ";

    // A message with the line it is about written in front of it.
    public static string WithLine(int line, string message)
    {
        return LineWord + line + LineEnd + message;
    }

    // A message with the line written in front of it taken off, or the message as it is where it has none.
    public static string WithoutLine(string message)
    {
        if (!message.StartsWith(LineWord, StringComparison.Ordinal))
        {
            return message;
        }

        int lineEndIndex = message.IndexOf(LineEnd, LineWord.Length, StringComparison.Ordinal);
        bool hasLineNumber = lineEndIndex > LineWord.Length && message[LineWord.Length..lineEndIndex].All(char.IsAsciiDigit);
        if (!hasLineNumber)
        {
            return message;
        }

        return message[(lineEndIndex + LineEnd.Length)..];
    }
}
