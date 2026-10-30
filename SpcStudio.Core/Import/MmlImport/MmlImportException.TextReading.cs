using SpcStudio.Export;
using SpcStudio.Model;

namespace SpcStudio.Import;

// The refusals of reading a song's text: the preprocessor's directives, and the parser's commands, loops, notes and remote code.
public sealed partial class MmlImportException
{
    // A song written in another tool's dialect.
    public static MmlImportException OtherDialect(string directive, int line)
    {
        return new($"#{directive}; this reads AddmusicK's own dialect.", line);
    }

    // An #amk with no version number after it.
    public static MmlImportException AmkVersionMissing(int line)
    {
        return new("#amk has no version number.", line);
    }

    // A song with no #amk directive at all.
    public static MmlImportException NoAmkDirective()
    {
        return new("No #amk directive; this is not an AddmusicK song.", 0);
    }

    // An #amk 3, which AddmusicK refuses itself.
    public static MmlImportException AmkThreeRefused(int line)
    {
        return new("#amk 3 is refused by AddmusicK itself as an unimplemented beta.", line);
    }

    // An #amk version this does not read.
    public static MmlImportException AmkVersionUnread(int version, int line)
    {
        return new($"#amk {version}; this reads #amk 2 and #amk 4.", line);
    }

    // A quote the preprocessor finds no closing quote for.
    public static MmlImportException QuoteUnclosed(int line)
    {
        return new("A '\"' has no closing quote after it.", line);
    }

    // A conditional directive inside a block that is switched off.
    public static MmlImportException DirectiveInSwitchedOffBlock(string directive, int line)
    {
        return new($"A '#{directive}' inside a conditional block that is switched off.", line);
    }

    // An #endif with nothing open for it to close.
    public static MmlImportException EndifWithoutIf(int line)
    {
        return new("An '#endif' with no '#ifdef', '#ifndef' or '#if' in front of it.", line);
    }

    // An #error the song reached.
    public static MmlImportException ErrorDirectiveReached(int line)
    {
        return new("A '#error' the song reached.", line);
    }

    // An #ifdef, #ifndef or #if never closed.
    public static MmlImportException IfWithoutEndif(int line)
    {
        return new("An '#ifdef', '#ifndef' or '#if' with no '#endif' after it.", line);
    }

    // An #if on a name no #define has given a value.
    public static MmlImportException IfOnUndefinedName(string name, int line)
    {
        return new($"A '#if' on '{name}', which no '#define' has given a value.", line);
    }

    // An #if with no comparison after its name.
    public static MmlImportException IfComparisonMissing(int line)
    {
        return new("A '#if' has no comparison after it.", line);
    }

    // An #if with no number after its comparison.
    public static MmlImportException IfNumberMissing(int line)
    {
        return new("A '#if' has no number after its comparison.", line);
    }

    // An #if comparison that is not one of the six an #if reads.
    public static MmlImportException IfComparisonUnknown(string comparison, int line)
    {
        return new($"'{comparison}', which a '#if' does not compare with.", line);
    }

    // A directive with no name after it.
    public static MmlImportException DirectiveNameMissing(string directive, int line)
    {
        return new($"A '#{directive}' has no name after it.", line);
    }

    // A #define or #if value that is not a number.
    public static MmlImportException DirectiveValueNotNumber(string directive, string token, int line)
    {
        return new($"A '{directive}' whose value '{token}' is not a number.", line);
    }

    // A '#' with no directive after it.
    public static MmlImportException HashWithoutDirective(int line)
    {
        return new("A '#' has no directive after it.", line);
    }

    // A directive this does not read.
    public static MmlImportException DirectiveUnread(string directiveName, int line)
    {
        return new($"'#{directiveName}' is not a directive this reads.", line);
    }

    // A channel directive inside a loop.
    public static MmlImportException ChannelDirectiveInLoop(int line)
    {
        return new("A channel directive cannot be inside a loop.", line);
    }

    // A channel number the driver has no channel for.
    public static MmlImportException ChannelNumberOutOfRange(int channelNumber, int line)
    {
        return new($"Channel #{channelNumber}; the driver has #0 to #{Song.DriverChannelCount - 1}.", line);
    }

    // A channel written a second time.
    public static MmlImportException ChannelWrittenTwice(int channelNumber, int line)
    {
        return new($"Channel #{channelNumber} is written twice.", line);
    }

    // A directive's block with no closing brace.
    public static MmlImportException BlockUnclosed(string blockName, int line)
    {
        return new($"{blockName} has no closing '}}'.", line);
    }

    // A character a directive's block cannot hold.
    public static MmlImportException CharacterInvalidInBlock(char character, string blockName, int line)
    {
        return new($"The character '{character}' is not valid in {blockName}.", line);
    }

    // A directive's block with no opening brace.
    public static MmlImportException BlockOpeningBraceMissing(string blockName, int line)
    {
        return new($"{blockName} has no opening '{{'.", line);
    }

    // A '#' in #samples with no group name after it.
    public static MmlImportException SampleGroupNameMissing(int line)
    {
        return new("A '#' in #samples has no group name after it.", line);
    }

    // A noise instrument whose clock has no digits.
    public static MmlImportException NoiseInstrumentClockMissingDigits(int line)
    {
        return new("A noise instrument's clock has no digits.", line);
    }

    // A noise instrument whose clock is past the driver's range.
    public static MmlImportException NoiseInstrumentClockOutOfRange(int clock, int line)
    {
        return new($"Noise instrument n{clock:X2}; the clock is {SnesNoise.MinClock:X2} to {SnesNoise.MaxClock:X2}.", line);
    }

    // An instrument entry's '@' with no value.
    public static MmlImportException InstrumentEntryInstrumentMissing(int line)
    {
        return new("An instrument's '@' has no value.", line);
    }

    // An instrument entry that takes its sample from another custom instrument.
    public static MmlImportException InstrumentEntryCopiesCustom(int copiedNumber, int line)
    {
        return new($"@{copiedNumber} in #instruments; a custom instrument's sample cannot be another custom instrument's.", line);
    }

    // An instrument entry with fewer bytes than an entry holds.
    public static MmlImportException InstrumentEntryTooShort(string entryName, int line)
    {
        return new($"Instrument {entryName} has fewer than five bytes after it.", line);
    }

    // An instrument entry with a byte that has no digits.
    public static MmlImportException InstrumentEntryByteMissingDigits(string entryName, int line)
    {
        return new($"Instrument {entryName} has a byte with no digits.", line);
    }

    // A #spc field this does not read.
    public static MmlImportException SpcFieldUnread(string fieldName, int line)
    {
        return new($"'#{fieldName}' is not a #spc field this reads.", line);
    }

    // An #option before any channel.
    public static MmlImportException OptionBeforeChannel(int line)
    {
        return new("#option comes before any channel.", line);
    }

    // An #option this does not read.
    public static MmlImportException OptionUnread(string optionName, int line)
    {
        return new($"'#option {optionName}' is not an option this reads.", line);
    }

    // A '$' with no hex digits after it.
    public static MmlImportException DollarWithoutHexDigits(int line)
    {
        return new("A '$' has no hex digits after it.", line);
    }

    // A raw hex command this does not read.
    public static MmlImportException HexCommandUnread(int command, int line)
    {
        return new($"The raw hex command ${command:X2} is not read.", line);
    }

    // A subcommand of a hex command that this does not read.
    public static MmlImportException SubcommandUnread(int command, int subcommand, int line)
    {
        return new($"The '${command:X2} ${subcommand:X2}' subcommand is not read.", line);
    }

    // An $EE fine tune past the driver's range.
    public static MmlImportException FineTuneOutOfRange(int value, int line)
    {
        return new($"$EE ${value:X2}; the driver's fine tuning is ${SnesPitch.MinFineTune:X2} to ${SnesPitch.MaxFineTune:X2}.", line);
    }

    // A hex command missing one of its arguments, counted from one.
    public static MmlImportException HexArgumentMissing(int command, int argumentNumber, int line)
    {
        return new($"${command:X2} has no '$xx' argument {argumentNumber} after it.", line);
    }

    // A hex command's argument, counted from one, that has no hex digits.
    public static MmlImportException HexArgumentMissingDigits(int command, int argumentNumber, int line)
    {
        return new($"${command:X2} has no hex digits in argument {argumentNumber}.", line);
    }

    // An $F6 write to a DSP register this does not read.
    public static MmlImportException DspWriteUnread(int register, int line)
    {
        return new($"A '$F6' write to DSP register ${register:X2} is not read.", line);
    }

    // A one-letter command with no value after it.
    public static MmlImportException CommandHasNoValue(char command, int line)
    {
        return new($"'{command}' has no value.", line);
    }

    // An $F8 with no argument after it.
    public static MmlImportException NoiseHexArgumentMissing(int line)
    {
        return new("'$F8' has no argument after it.", line);
    }

    // An $F8 whose argument has no digits.
    public static MmlImportException NoiseHexArgumentMissingDigits(int line)
    {
        return new("'$F8' has an argument with no digits.", line);
    }

    // A noise clock past the driver's highest, named as it was written.
    public static MmlImportException NoiseClockOutOfRange(string writtenAs, int line)
    {
        return new($"{writtenAs}; the driver's noise clock is {SnesNoise.MinClock:X2} to {SnesNoise.MaxClock:X2}.", line);
    }

    // An '@@' direct instrument.
    public static MmlImportException DirectInstrumentUnread(int line)
    {
        return new("A '@@' direct instrument is not read.", line);
    }

    // An instrument number past the driver's range.
    public static MmlImportException InstrumentNumberOutOfRange(int instrumentNumber, int line)
    {
        return new($"@{instrumentNumber}; the driver's instruments are 0 to 255.", line);
    }

    // An instrument number that is neither built in, percussion nor the song's own.
    public static MmlImportException InstrumentNumberUndefined(int instrumentNumber, int line)
    {
        return new(
            $"@{instrumentNumber} is neither a built-in instrument (@0 to @{MmlBuiltins.MaxPitchedNumber}), a percussion instrument "
            + $"(@{MmlBuiltins.FirstPercussionNumber} to @{MmlBuiltins.LastPercussionNumber}), nor one of the song's own (from @{MmlExporter.FirstCustomInstrument}).", line);
    }

    // A sample load command whose '@' has no number.
    public static MmlImportException SampleLoadInstrumentMissing(int line)
    {
        return new("A sample load command's '@' has no number.", line);
    }

    // A sample load command naming a built-in instrument past AddmusicK's own table.
    public static MmlImportException SampleLoadPastBuiltinTable(int builtinNumber, int line)
    {
        return new($"A sample load command names @{builtinNumber}, where AddmusicK's own table stops at @{MmlBuiltins.MaxCopiedNumber}.", line);
    }

    // A sample load command with no ',' after its sample.
    public static MmlImportException SampleLoadCommaMissing(int line)
    {
        return new("A sample load command has no ',' after its sample.", line);
    }

    // A sample load command with no tuning after its ','.
    public static MmlImportException SampleLoadTuningMissing(int line)
    {
        return new("A sample load command has no '$xx' tuning after its ','.", line);
    }

    // A sample load command whose tuning has no hex digits.
    public static MmlImportException SampleLoadTuningMissingDigits(int line)
    {
        return new("A sample load command has a tuning with no hex digits.", line);
    }

    // A sample load command with no closing parenthesis.
    public static MmlImportException SampleLoadCloseMissing(int line)
    {
        return new("A sample load command has no ')'.", line);
    }

    // An octave past the driver's range.
    public static MmlImportException OctaveOutOfRange(int octave, int line)
    {
        return new($"o{octave}; the driver's octaves are 0 to 6.", line);
    }

    // A default note length past the driver's range.
    public static MmlImportException DefaultLengthOutOfRange(int lengthDivisor, int line)
    {
        return new($"l{lengthDivisor}; the driver's note lengths are 1 to 192.", line);
    }

    // An 'l=' in a dialect that does not read one.
    public static MmlImportException DefaultLengthTicksInAmkTwo(int line)
    {
        return new("An 'l=' tick count, which #amk 2 does not read.", line);
    }

    // An 'l=' with no tick count after it.
    public static MmlImportException DefaultLengthTicksMissing(int line)
    {
        return new("An 'l=' has no tick count after it.", line);
    }

    // An 'l' setting a default length of zero ticks.
    public static MmlImportException DefaultLengthZero(int line)
    {
        return new("'l' cannot set a default length of 0 ticks.", line);
    }

    // A '>' raising the octave past the highest.
    public static MmlImportException OctaveRaisedPastTop(int line)
    {
        return new("A '>' raises the octave above 7.", line);
    }

    // A '<' lowering the octave past the lowest.
    public static MmlImportException OctaveLoweredPastBottom(int line)
    {
        return new("A '<' lowers the octave below -1.", line);
    }

    // A quantization past the driver's range.
    public static MmlImportException QuantizationOutOfRange(int quantization, int line)
    {
        return new($"q{quantization:X2}; the driver's quantization is $01 to $7F.", line);
    }

    // A triplet block opened inside another.
    public static MmlImportException TripletInsideTriplet(int line)
    {
        return new("A '{' triplet block cannot be inside another.", line);
    }

    // A triplet block closed with none open.
    public static MmlImportException TripletCloseWithoutOpen(int line)
    {
        return new("A '}' has no '{' triplet block open.", line);
    }

    // A triplet block still open at the end of the file.
    public static MmlImportException TripletUnclosed(int line)
    {
        return new("A '{' triplet block has no '}' to close it.", line);
    }

    // A lettered command's value outside the driver's range.
    public static MmlImportException LetterValueOutOfRange(char letter, int value, ValueRange range, int line)
    {
        return new($"{letter}{value} is outside the driver's {range.Min} to {range.Max}.", line);
    }

    // A 'y' with no surround value after its ','.
    public static MmlImportException SurroundValueMissing(int line)
    {
        return new("'y' has no surround value.", line);
    }

    // A 'y' with one surround value where it takes two.
    public static MmlImportException SurroundValueSingle(int line)
    {
        return new("'y' has only one surround value.", line);
    }

    // A 'y' with no second surround value.
    public static MmlImportException SurroundSecondValueMissing(int line)
    {
        return new("'y' has no second surround value.", line);
    }

    // A lettered command's fade with no value.
    public static MmlImportException FadeHasNoValue(char letter, int line)
    {
        return new($"'{letter}' fade has no value.", line);
    }

    // A named value outside the driver's range.
    public static MmlImportException ValueOutOfRange(string valueName, int value, ValueRange range, int line)
    {
        return new($"A {valueName} of {value}, which is outside the driver's {range.Min} to {range.Max}.", line);
    }

    // A loop point inside a loop.
    public static MmlImportException LoopPointInsideLoop(int line)
    {
        return new("A '/' loop point cannot be inside a loop.", line);
    }

    // A '[[[' that could mean either order.
    public static MmlImportException TripleOpeningBracket(int line)
    {
        return new("'[[[': separate the '[[' and the '[' to say which is meant.", line);
    }

    // A ']]]' that could mean either order.
    public static MmlImportException TripleClosingBracket(int line)
    {
        return new("']]]': separate the ']]' and the ']' to say which is meant.", line);
    }

    // A loop opened inside another loop.
    public static MmlImportException LoopInsideLoop(int line)
    {
        return new("A '[' loop cannot be inside another '[' loop.", line);
    }

    // A loop label defined a second time.
    public static MmlImportException LoopLabelDefinedTwice(int label, int line)
    {
        return new($"Loop label ({label}) is defined twice.", line);
    }

    // A ']]' closing a remote code definition.
    public static MmlImportException SubloopCloseEndsRemoteDefinition(int line)
    {
        return new("A ']]' cannot close a remote code definition.", line);
    }

    // A remote code definition written with a repeat count.
    public static MmlImportException RemoteDefinitionRepeatCount(int line)
    {
        return new("A remote code definition cannot take a repeat count.", line);
    }

    // A ']]' with no repeat count.
    public static MmlImportException SubloopRepeatCountMissing(int line)
    {
        return new("A ']]' subloop has no repeat count.", line);
    }

    // A ']]1', which repeats once.
    public static MmlImportException SubloopRepeatsOnce(int line)
    {
        return new("A ']]1' subloop, which repeats once and does nothing.", line);
    }

    // A ']' with no loop open.
    public static MmlImportException LoopCloseWithoutOpen(int line)
    {
        return new("A ']' with no '[' loop open.", line);
    }

    // A ']' closing a subloop.
    public static MmlImportException LoopCloseEndsSubloop(bool isSubloopWrittenAsHex, int line)
    {
        return new($"A ']' closing a {SubloopOpenerSpelling(isSubloopWrittenAsHex)} subloop.", line);
    }

    // A subloop opened inside another subloop.
    public static MmlImportException SubloopInsideSubloop(bool isSubloopWrittenAsHex, int line)
    {
        return new($"A {SubloopOpenerSpelling(isSubloopWrittenAsHex)} subloop cannot be inside another subloop.", line);
    }

    // A label loop defining a subloop.
    public static MmlImportException LabelLoopDefinesSubloop(bool isSubloopWrittenAsHex, int line)
    {
        return new($"A label loop cannot define a {SubloopOpenerSpelling(isSubloopWrittenAsHex)} subloop.", line);
    }

    // A subloop's end with no subloop open.
    public static MmlImportException SubloopCloseWithoutOpen(bool isSubloopWrittenAsHex, int line)
    {
        return new($"A {SubloopCloserSpelling(isSubloopWrittenAsHex)} with no subloop open.", line);
    }

    // A subloop's end closing a loop.
    public static MmlImportException SubloopCloseEndsLoop(bool isSubloopWrittenAsHex, int line)
    {
        return new($"A {SubloopCloserSpelling(isSubloopWrittenAsHex)} closing a '[' loop.", line);
    }

    // A loop still open at the end of the file.
    public static MmlImportException LoopUnclosed(int line)
    {
        return new("A '[' loop with no ']' to close it.", line);
    }

    // A subloop still open at the end of the file.
    public static MmlImportException SubloopUnclosed(bool isSubloopWrittenAsHex, int line)
    {
        return new($"A {SubloopOpenerSpelling(isSubloopWrittenAsHex)} subloop with nothing to close it.", line);
    }

    // A tie opening a subloop that stands inside a loop.
    public static MmlImportException TieOpeningSubloopInsideLoop(int line)
    {
        return new("A '[[ ]]' subloop inside a '[ ]' loop cannot be read where it opens on a '^' tie.", line);
    }

    // A loop label with no number.
    public static MmlImportException LoopLabelNumberMissing(int line)
    {
        return new("A '(' loop label has no number.", line);
    }

    // A loop label past the highest AddmusicK takes.
    public static MmlImportException LoopLabelPastHighest(int label, int highestLabel, int line)
    {
        return new($"Loop label ({label}) is past the {highestLabel} AddmusicK's stop at.", line);
    }

    // A loop label with no closing parenthesis.
    public static MmlImportException LoopLabelCloseMissing(int label, int line)
    {
        return new($"Loop label ({label}) has no ')'.", line);
    }

    // A label loop inside a loop.
    public static MmlImportException LabelLoopInsideLoop(int line)
    {
        return new("A label loop cannot be inside a '[' loop.", line);
    }

    // A loop label called before it is defined.
    public static MmlImportException LoopLabelCalledBeforeDefined(int label, int line)
    {
        return new($"Loop label ({label}) is called before it is defined.", line);
    }

    // A '*' inside a loop.
    public static MmlImportException LoopRepeatInsideLoop(int line)
    {
        return new("A '*' loop call cannot be inside a '[' loop.", line);
    }

    // A '*' with no loop before it.
    public static MmlImportException LoopRepeatWithoutLoop(int line)
    {
        return new("A '*' has no '[' loop before it to repeat.", line);
    }

    // A loop count outside the driver's range.
    public static MmlImportException LoopCountOutOfRange(int count, ValueRange range, int line)
    {
        return new($"A loop count of {count}; the driver's are {range.Min} to {range.Max}.", line);
    }

    // A channel ending on a loop with nothing in it, which hangs the driver under the read-ahead hot patch.
    public static MmlImportException EmptyLoopEndsChannel(int line)
    {
        return new(
            "A loop with no note, rest or tie in it ends the channel, which hangs the driver under a '$FA $7F' or '$FA $FE' hot patch that makes it look inside loops.", line);
    }

    // A tie opening the loop that a note before it could sound into.
    public static MmlImportException OpeningTieAfterSoundingNote(int line)
    {
        return new("A '^' tie opening the loop cannot be read where legato, remote code or an arpeggio can keep the channel's last note sounding into it.", line);
    }

    // Note data inside a remote code definition.
    public static MmlImportException NoteDataInRemoteDefinition(int line)
    {
        return new("Note data cannot be inside a remote code definition.", line);
    }

    // Note data before any channel directive.
    public static MmlImportException NoteDataBeforeChannel(int line)
    {
        return new("Note data comes before any channel directive.", line);
    }

    // Music before any channel directive.
    public static MmlImportException MusicBeforeChannel(int line)
    {
        return new("Music comes before any channel directive.", line);
    }

    // A file with no channels in it.
    public static MmlImportException NoChannels()
    {
        return new("No channels; there is no music in this file.", 0);
    }

    // Commands above the first channel directive in a file that never writes the channel they fall into.
    public static MmlImportException CommandsAboveUnwrittenChannel(int channelNumber)
    {
        return new($"Commands above the first channel directive, in a file that never writes #{channelNumber}.", 0);
    }

    // A note below the driver's lowest, with the semitones it was moved by.
    public static MmlImportException NoteBelowRange(char letter, int octave, int shiftSemitones, bool isShiftedByH, int line)
    {
        return new($"Note '{letter}' in octave {octave}{NoteShiftClause(shiftSemitones, isShiftedByH)} is below the driver's o1 c.", line);
    }

    // A note above the driver's highest, with the semitones it was moved by.
    public static MmlImportException NoteAboveRange(char letter, int octave, int shiftSemitones, bool isShiftedByH, int line)
    {
        return new($"Note '{letter}' in octave {octave}{NoteShiftClause(shiftSemitones, isShiftedByH)} is above the driver's o6 a.", line);
    }

    // A tie with no note in front of it.
    public static MmlImportException TieWithoutNote(int line)
    {
        return new("A '^' tie has no note in front of it to extend.", line);
    }

    // A tie that does not carry on from the note it extends.
    public static MmlImportException TieDoesNotCarryOn(int line)
    {
        return new("A '^' tie does not carry on from the note it extends.", line);
    }

    // A note length outside the driver's range.
    public static MmlImportException NoteLengthOutOfRange(int noteLengthNumber, ValueRange range, int line)
    {
        return new($"A note length of {noteLengthNumber}; the driver's are {range.Min} to {range.Max}.", line);
    }

    // An '=' with no tick count after it.
    public static MmlImportException TickLengthMissing(int line)
    {
        return new("An '=' has no tick count after it.", line);
    }

    // A length of zero ticks.
    public static MmlImportException LengthZero(int line)
    {
        return new("A length cannot be 0 ticks.", line);
    }

    // A '?' directive, which AddmusicK ignores.
    public static MmlImportException QuestionDirectiveIgnored(int directiveNumber, int line)
    {
        return new($"?{directiveNumber}, which AddmusicK reads and then ignores.", line);
    }

    // A second '&' with no note since the first.
    public static MmlImportException SlideWithoutNoteBetween(int line)
    {
        return new("A second '&' has no note between it and the first.", line);
    }

    // A $DD with no target note after it.
    public static MmlImportException BendTargetMissing(int line)
    {
        return new("A '$DD' pitch bend has no target note after it.", line);
    }

    // An '&' with no note in front of it.
    public static MmlImportException SlideWithoutNote(int line)
    {
        return new("An '&' pitch slide has no note in front of it to slide from.", line);
    }

    // A remote code call with no block number.
    public static MmlImportException RemoteCallNumberMissing(int line)
    {
        return new("A remote code call has no block number.", line);
    }

    // A remote code call with no event type.
    public static MmlImportException RemoteCallEventTypeMissing(int blockLabel, int line)
    {
        return new($"Remote code call (!{blockLabel}) has no event type.", line);
    }

    // A remote code call with no closing parenthesis.
    public static MmlImportException RemoteCallCloseMissing(int blockLabel, int line)
    {
        return new($"Remote code call (!{blockLabel}) has no ')'.", line);
    }

    // A remote code definition inside a channel.
    public static MmlImportException RemoteDefinitionInsideChannel(int line)
    {
        return new("A remote code definition cannot be inside a channel.", line);
    }

    // A remote code block called before it is defined.
    public static MmlImportException RemoteBlockCalledBeforeDefined(int blockLabel, int line)
    {
        return new($"Remote code block (!{blockLabel}) is called before it is defined.", line);
    }

    // A remote code call with no third argument where its type takes one.
    public static MmlImportException RemoteCallThirdArgumentMissing(int blockLabel, int line)
    {
        return new($"Remote code call (!{blockLabel}) has no third argument.", line);
    }

    // A remote code call whose third argument cannot be read.
    public static MmlImportException RemoteCallThirdArgumentUnreadable(int blockLabel, int line)
    {
        return new($"Remote code call (!{blockLabel}) has an unreadable third argument.", line);
    }

    // A remote code call whose third argument does not fit in a byte.
    public static MmlImportException RemoteCallThirdArgumentTooLong(int blockLabel, int lengthTicks, int line)
    {
        return new($"Remote code call (!{blockLabel}) has a third argument of {lengthTicks} ticks, which does not fit in a byte.", line);
    }

    // A remote code definition with no block number.
    public static MmlImportException RemoteDefinitionNumberMissing(int line)
    {
        return new("A remote code definition has no block number.", line);
    }

    // A remote code definition with no closing parenthesis.
    public static MmlImportException RemoteDefinitionCloseMissing(int blockLabel, int line)
    {
        return new($"Remote code definition (!{blockLabel}) has no ')'.", line);
    }

    // A remote code definition with no body.
    public static MmlImportException RemoteDefinitionBodyMissing(int blockLabel, int line)
    {
        return new($"Remote code definition (!{blockLabel}) has no '[' body.", line);
    }

    // A remote code definition still open at the end of the file.
    public static MmlImportException RemoteDefinitionUnclosed(int blockLabel)
    {
        return new($"Remote code definition (!{blockLabel}) has no ']'.", 0);
    }

    // A remote code block defined a second time.
    public static MmlImportException RemoteBlockDefinedTwice(int blockLabel, int line)
    {
        return new($"Remote code block (!{blockLabel}) is defined twice.", line);
    }

    // A remote code reset in a dialect that does not read one.
    public static MmlImportException RemoteResetInAmkTwo(int line)
    {
        return new("A '(!!' remote code reset, which #amk 2 does not read.", line);
    }

    // A remote code reset with no number.
    public static MmlImportException RemoteResetNumberMissing(int line)
    {
        return new("A remote code reset has no number.", line);
    }

    // A remote code reset with no closing parenthesis.
    public static MmlImportException RemoteResetCloseMissing(int line)
    {
        return new("A remote code reset has no ')'.", line);
    }

    // An $FC inside a remote code definition.
    public static MmlImportException RemoteCallHexInsideDefinition(int line)
    {
        return new("A '$FC' inside a remote code definition is not read.", line);
    }

    // An $FC naming an ARAM address.
    public static MmlImportException RemoteCallHexAramAddress(int address, int line)
    {
        return new($"A '$FC' names the ARAM address ${address:X4}, which a text file cannot know.", line);
    }

    // A 'p' with one argument where it takes two.
    public static MmlImportException VibratoOneArgument(int line)
    {
        return new("'p' with one argument; it takes a rate and an extent.", line);
    }

    // A 'p' with no extent.
    public static MmlImportException VibratoExtentMissing(int line)
    {
        return new("'p' has no extent.", line);
    }

    // A replacement with no '=' in it.
    public static MmlImportException ReplacementEqualsMissing(int line)
    {
        return new("A \"...\" replacement has no '=' in it.", line);
    }

    // A replacement with nothing to find.
    public static MmlImportException ReplacementFindEmpty(int line)
    {
        return new("A \"...\" replacement whose string to find is empty.", line);
    }

    // A replacement that expands into itself.
    public static MmlImportException ReplacementExpandsIntoItself(int line)
    {
        return new("A \"...\" replacement that expands into itself.", line);
    }

    // A quoted string missing where one is needed.
    public static MmlImportException QuotedStringMissing(int line)
    {
        return new("Expected a quoted string.", line);
    }

    // A quoted string with no closing quote.
    public static MmlImportException QuotedStringUnclosed(int line)
    {
        return new("A quoted string has no closing '\"'.", line);
    }

    // A backslash in a quoted string with something other than a quote mark behind it.
    public static MmlImportException QuotedStringBackslashUnescaped(int line)
    {
        return new("A quoted string holds a backslash that is not followed by a quote mark.", line);
    }

    // How a subloop's start was written, for the refusals that name it.
    private static string SubloopOpenerSpelling(bool isWrittenAsHex)
    {
        return isWrittenAsHex ? "'$E6 $00'" : "'[['";
    }

    // How a subloop's end was written, for the refusals that name it.
    private static string SubloopCloserSpelling(bool isWrittenAsHex)
    {
        return isWrittenAsHex ? "'$E6'" : "']]'";
    }

    // The clause a range refusal names a note's shift in, or nothing where the note was not moved.
    private static string NoteShiftClause(int shiftSemitones, bool isShiftedByH)
    {
        string shiftedBy = isShiftedByH ? "'h'" : "its instrument";
        return shiftSemitones switch
        {
            0 => "",
            > 0 => $", transposed {shiftSemitones} up by {shiftedBy},",
            _ => $", transposed {-shiftSemitones} down by {shiftedBy},",
        };
    }
}
