using SpcStudio.Model;

namespace SpcStudio.Import;

// Reads a song's #path and #samples alone, whichever AddmusicK it is written for, stepping over everything else in its text.
internal static class MmlSampleListReader
{
    // The song's sample list, refusing a quoted string or a #samples block that cannot be read.
    public static MmlSampleList Read(string text)
    {
        return new Reader(MmlPreprocessor.PreparedForReading(text)).ReadSampleList();
    }

    // One pass over a prepared text, reading its #path and #samples directives.
    private sealed class Reader(string text) : AmkTextReader(text)
    {
        private readonly List<MmlSampleEntry> _samples = [];
        private string _samplePath = "";

        // Reads the whole text and returns its sample list.
        public MmlSampleList ReadSampleList()
        {
            while (_position < _text.Length)
            {
                ReadConstruct();
            }

            return new MmlSampleList(_samplePath, _samples);
        }

        // Reads what stands at the cursor: a quoted string, stepped over whole so a '#' inside it is not a directive, a directive, or one character.
        private void ReadConstruct()
        {
            switch (_text[_position])
            {
                case '"':
                    ReadQuotedString();
                    break;
                case '#':
                    _position++;
                    ReadDirective(ReadWord().ToLowerInvariant());
                    break;
                default:
                    AdvanceCountingLines();
                    break;
            }
        }

        // Reads a #path's folder or a #samples block, any other directive's name having been stepped over already.
        private void ReadDirective(string directiveName)
        {
            switch (directiveName)
            {
                case AmkDirectives.Path:
                    _samplePath = ReadQuotedString();
                    break;
                case AmkDirectives.Samples:
                    _samples.AddRange(ReadSampleBlock());
                    break;
            }
        }
    }
}
