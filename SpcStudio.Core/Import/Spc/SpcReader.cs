using System.Buffers.Binary;
using System.Text;
using SpcStudio.Model;

namespace SpcStudio.Import;

// Reads an .spc - a dump of the SPC700's memory and register state - and its tags, ID666's and the extended tag's after the dump, and says where in such a dump the ARAM and the DSP registers sit.
public static class SpcReader
{
    public const string DialogFilter = "SPC file (*.spc)|*.spc" + DialogFilters.Separator + DialogFilters.AllFiles;
    public const int AramSize = 0x10000;
    private const int HeaderSize = 0x100;
    private const int AramOffset = HeaderSize;
    private const int DspRegistersOffset = AramOffset + AramSize;
    private const int MinimumLength = DspRegistersOffset + SnesDspRegister.RegisterCount;
    private const int TagsFlagOffset = 0x23;
    private const int TitleOffset = 0x2E;
    private const int TitleLength = 32;
    private const int GameOffset = 0x4E;
    private const int GameLength = 32;
    private const int CommentOffset = 0x7E;
    private const int CommentLength = 32;
    private const int DateOffset = 0x9E;
    private const int DateLength = 11;
    private const int ArtistTextLayoutOffset = 0xB1;
    private const int ArtistBinaryLayoutOffset = 0xB0;
    private const int ArtistLength = 32;
    private const byte TagsAbsent = 0x1B;
    private const string Signature = "SNES-SPC700 Sound File Data";
    private const int ExtendedTagOffset = 0x10200;
    private const string ExtendedTagSignature = "xid6";
    private const int ExtendedTagSizeOffset = ExtendedTagOffset + 4;
    private const int ExtendedFieldsOffset = ExtendedTagOffset + 8;
    private const int ExtendedFieldTypeOffset = 1;
    private const int ExtendedFieldLengthOffset = 2;
    private const int ExtendedFieldHeaderSize = 4;
    private const int ExtendedFieldAlignment = 4;
    private const byte ExtendedFieldInHeader = 0;
    private const byte ExtendedFieldIsText = 1;
    private const byte ExtendedTitleId = 0x01;
    private const byte ExtendedGameId = 0x02;
    private const byte ExtendedArtistId = 0x03;
    private const byte ExtendedCommentId = 0x07;

    // Reads an .spc file, refusing one that is not an SPC dump and letting the exception out where it cannot be read at all.
    public static SpcFile Read(string path)
    {
        return Parse(File.ReadAllBytes(path));
    }

    // Reads an .spc already in memory, refusing one that is not an SPC dump.
    public static SpcFile Parse(byte[] fileBytes)
    {
        if (fileBytes.Length < MinimumLength || !HasTextAt(fileBytes, 0, Signature))
        {
            throw SpcReadException.NotValidSpc();
        }

        var basicId666Tags = fileBytes[TagsFlagOffset] == TagsAbsent ? SpcTags.Empty : BasicTags(fileBytes);
        var extendedTextsById = ExtendedTexts(fileBytes);
        return new SpcFile(
            extendedTextsById.GetValueOrDefault(ExtendedTitleId, basicId666Tags.Title),
            extendedTextsById.GetValueOrDefault(ExtendedGameId, basicId666Tags.Game),
            extendedTextsById.GetValueOrDefault(ExtendedArtistId, basicId666Tags.Author),
            extendedTextsById.GetValueOrDefault(ExtendedCommentId, basicId666Tags.Comment),
            fileBytes);
    }

    // The ARAM in an SPC dump's bytes.
    public static ReadOnlySpan<byte> AramOf(byte[] dump)
    {
        return dump.AsSpan(AramOffset, AramSize);
    }

    // The DSP registers in an SPC dump's bytes.
    public static ReadOnlySpan<byte> DspRegistersOf(byte[] dump)
    {
        return dump.AsSpan(DspRegistersOffset, SnesDspRegister.RegisterCount);
    }

    // Whether the file holds a piece of text at an offset.
    private static bool HasTextAt(byte[] fileBytes, int offset, string text)
    {
        if (offset + text.Length > fileBytes.Length)
        {
            return false;
        }

        for (int at = 0; at < text.Length; at++)
        {
            if (fileBytes[offset + at] != text[at])
            {
                return false;
            }
        }

        return true;
    }

    // The four ID666 text fields, the artist read where the file's layout puts it.
    private static SpcTags BasicTags(byte[] fileBytes)
    {
        int artistOffset = IsTextLayout(fileBytes) ? ArtistTextLayoutOffset : ArtistBinaryLayoutOffset;
        return new SpcTags(
            FieldText(fileBytes, TitleOffset, TitleLength),
            FieldText(fileBytes, artistOffset, ArtistLength),
            FieldText(fileBytes, GameOffset, GameLength),
            FieldText(fileBytes, CommentOffset, CommentLength));
    }

    // The extended tag's non-blank text fields by their ID, empty where the file has no such tag, and read only as far as the file goes.
    private static Dictionary<byte, string> ExtendedTexts(byte[] fileBytes)
    {
        var texts = new Dictionary<byte, string>();
        if (fileBytes.Length < ExtendedFieldsOffset || !HasTextAt(fileBytes, ExtendedTagOffset, ExtendedTagSignature))
        {
            return texts;
        }

        int fieldsEnd = (int)Math.Min(fileBytes.Length, ExtendedFieldsOffset + (long)BinaryPrimitives.ReadUInt32LittleEndian(fileBytes.AsSpan(ExtendedTagSizeOffset)));
        int fieldOffset = ExtendedFieldsOffset;
        while (fieldOffset + ExtendedFieldHeaderSize <= fieldsEnd)
        {
            byte fieldType = fileBytes[fieldOffset + ExtendedFieldTypeOffset];
            int dataOffset = fieldOffset + ExtendedFieldHeaderSize;
            int dataLengthAfterHeader = fieldType == ExtendedFieldInHeader ? 0 : BinaryPrimitives.ReadUInt16LittleEndian(fileBytes.AsSpan(fieldOffset + ExtendedFieldLengthOffset));
            if (dataOffset + dataLengthAfterHeader > fieldsEnd)
            {
                break;
            }

            string fieldText = fieldType == ExtendedFieldIsText ? FieldText(fileBytes, dataOffset, dataLengthAfterHeader) : "";
            if (fieldText.Length > 0)
            {
                texts[fileBytes[fieldOffset]] = fieldText;
            }

            fieldOffset = dataOffset + (dataLengthAfterHeader + ExtendedFieldAlignment - 1) / ExtendedFieldAlignment * ExtendedFieldAlignment;
        }

        return texts;
    }

    // Whether the ID666 tags are in the text layout rather than the binary one, sniffed off the date field.
    private static bool IsTextLayout(byte[] fileBytes)
    {
        for (int at = DateOffset; at < DateOffset + DateLength; at++)
        {
            if (!IsTextDateCharacter(fileBytes[at]))
            {
                return false;
            }
        }

        return true;
    }

    // Whether a byte of the date field is one the text layout writes there: a digit, a separator, a space or nothing.
    private static bool IsTextDateCharacter(byte dateByte)
    {
        return dateByte == 0 || dateByte == ' ' || (dateByte >= '0' && dateByte <= '9') || dateByte == '/' || dateByte == '-' || dateByte == '.';
    }

    // One of ID666's fixed-width text fields as a string, ending at its first NUL or at its own width.
    private static string FieldText(byte[] fileBytes, int offset, int length)
    {
        int end = offset;
        while (end < offset + length && fileBytes[end] != 0)
        {
            end++;
        }

        return Encoding.Latin1.GetString(fileBytes, offset, end - offset).Trim();
    }
}
