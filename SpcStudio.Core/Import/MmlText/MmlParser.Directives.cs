using SpcStudio.Model;

namespace SpcStudio.Import;

public static partial class MmlParser
{
    private sealed partial class Parser
    {
        private const int Amk109HotPatchPreset = 0x01;

        // Reads a '#' and what follows it: a channel header or one of the named directives.
        private void ReadDirective()
        {
            int line = _line;
            _position++;
            if (_position < _text.Length && char.IsAsciiDigit(_text[_position]))
            {
                ReadChannelHeader(line);
                return;
            }

            string directiveName = ReadWord().ToLowerInvariant();
            switch (directiveName)
            {
                case AmkDirectives.Amk: SkipAmkDirective(); break;
                case AmkDirectives.Path: _samplePath = ReadQuotedString(); break;
                case AmkDirectives.Samples: ReadSampleBlock(); break;
                case AmkDirectives.Instruments: ReadInstrumentBlock(); break;
                case AmkDirectives.Spc: ReadSpcBlock(); break;
                case AmkDirectives.Option: ReadOption(); break;
                case AmkDirectives.Louder: AddVelocityTableSwitch(false, line); break;
                case "": throw MmlImportException.HashWithoutDirective(_line);
                default: throw MmlImportException.DirectiveUnread(directiveName, _line);
            }
        }

        // Reads a '#n' channel header and makes that channel the one being written to.
        private void ReadChannelHeader(int line)
        {
            if (_openLoopBodies.Count > 0)
            {
                throw MmlImportException.ChannelDirectiveInLoop(_line);
            }

            RequirePitchBendResolved();
            int channelNumber = ReadInt();
            if (channelNumber < 0 || channelNumber >= Song.DriverChannelCount)
            {
                throw MmlImportException.ChannelNumberOutOfRange(channelNumber, line);
            }

            if (_channels[channelNumber].HasChannelDirective)
            {
                throw MmlImportException.ChannelWrittenTwice(channelNumber, line);
            }

            _channels[channelNumber].HasChannelDirective = true;
            _currentChannel = channelNumber;
            _isChannelDefined = true;
            _channelOrder.Add(channelNumber);
            _hTranspose = 0;
            _hasHTranspose = false;
        }

        // Steps over an #amk directive the preprocessor has already read.
        private void SkipAmkDirective()
        {
            SkipWhitespace();
            ReadInt();
        }

        // Reads a directive's block an entry at a time, refusing a block that is never closed before the entry it would be.
        private void ReadBlockEntries(string directive, Action<string> readEntry)
        {
            string writtenDirective = AmkDirectives.Written(directive);
            ReadBraceBlock(writtenDirective, () =>
            {
                if (_position >= _text.Length)
                {
                    throw MmlImportException.BlockUnclosed(writtenDirective, _line);
                }

                readEntry(writtenDirective);
            });
        }

        // Reads the #samples block: quoted sample files and '#' sample groups, to its closing brace.
        private void ReadSampleBlock()
        {
            ReadBlockEntries(AmkDirectives.Samples, ReadSampleEntry);
        }

        // Consumes the '#' a block's entry opens with, refusing any other character there.
        private void RequireHashMark(string writtenDirective)
        {
            if (!TryConsume('#'))
            {
                throw MmlImportException.CharacterInvalidInBlock(_text[_position], writtenDirective, _line);
            }
        }

        // Reads one #samples entry, a quoted sample file or a '#' sample group.
        private void ReadSampleEntry(string writtenDirective)
        {
            int line = _line;
            if (_text[_position] == '"')
            {
                _samples.Add(new MmlSampleEntry(ReadQuotedString(), false, line));
                return;
            }

            RequireHashMark(writtenDirective);

            _samples.Add(new MmlSampleEntry(ReadSampleGroupName(), true, line));
        }

        // Reads a sample group's name after its '#', as written, up to the next space, quote or closing brace.
        private string ReadSampleGroupName()
        {
            int nameStart = _position;
            while (_position < _text.Length && !AmkWhitespace.IsWhitespace(_text[_position]) && _text[_position] is not ('}' or '"'))
            {
                _position++;
            }

            if (_position == nameStart)
            {
                throw MmlImportException.SampleGroupNameMissing(_line);
            }

            return _text[nameStart.._position];
        }

        // Reads the #instruments block: each entry's sample, noise clock or copied built-in and its five bytes, to its closing brace.
        private void ReadInstrumentBlock()
        {
            ReadBlockEntries(AmkDirectives.Instruments, ReadInstrumentEntry);
        }

        // Reads one #instruments entry: its sample, noise clock or copied built-in, and its five bytes.
        private void ReadInstrumentEntry(string writtenDirective)
        {
            if (_text[_position] is not ('"' or 'n' or '@'))
            {
                throw MmlImportException.CharacterInvalidInBlock(_text[_position], writtenDirective, _line);
            }

            int line = _line;
            var (sampleFile, noiseClock, copiedNumber) = ReadInstrumentSource();
            string entryName = copiedNumber is not null ? $"@{copiedNumber}" : sampleFile is null ? $"n{noiseClock:X2}" : $"\"{sampleFile}\"";
            _instruments.Add(new MmlInstrument(sampleFile, noiseClock, copiedNumber, ReadInstrumentEntryBytes(entryName), line));
        }

        // Reads what an #instruments entry plays: an 'n' noise clock, an '@' built-in whose sample it takes, or a quoted sample file.
        private (string? SampleFile, int? NoiseClock, int? CopiedNumber) ReadInstrumentSource()
        {
            if (TryConsume('n'))
            {
                int noiseClock = ReadRequiredHex(MmlImportException.NoiseInstrumentClockMissingDigits);

                if (noiseClock > SnesNoise.MaxClock)
                {
                    throw MmlImportException.NoiseInstrumentClockOutOfRange(noiseClock, _line);
                }

                return (null, noiseClock, null);
            }

            if (TryConsume('@'))
            {
                int copiedNumber = ReadRequiredInt(MmlImportException.InstrumentEntryInstrumentMissing);

                if (copiedNumber > MmlBuiltins.MaxCopiedNumber)
                {
                    throw MmlImportException.InstrumentEntryCopiesCustom(copiedNumber, _line);
                }

                return (null, null, copiedNumber);
            }

            return (ReadQuotedString(), null, null);
        }

        // Reads the five '$xx' bytes an #instruments entry carries.
        private InstrumentBytes ReadInstrumentEntryBytes(string entryName)
        {
            // Reads the entry's next byte.
            byte ReadEntryByte()
            {
                SkipWhitespace();
                return (byte)ReadDollarHex(
                    cursorLine => MmlImportException.InstrumentEntryTooShort(entryName, cursorLine), cursorLine => MmlImportException.InstrumentEntryByteMissingDigits(entryName, cursorLine));
            }

            return new InstrumentBytes(ReadEntryByte(), ReadEntryByte(), ReadEntryByte(), ReadEntryByte(), ReadEntryByte());
        }

        // Reads the #spc block's four text fields and its #length, keeping the last value of a field written twice.
        private void ReadSpcBlock()
        {
            ReadBlockEntries(AmkDirectives.Spc, ReadSpcField);
        }

        // Reads one #spc field and its quoted value.
        private void ReadSpcField(string writtenDirective)
        {
            RequireHashMark(writtenDirective);

            string fieldName = ReadWord().ToLowerInvariant();
            if (fieldName is not (AmkDirectives.TitleField or AmkDirectives.AuthorField or AmkDirectives.GameField or AmkDirectives.CommentField or AmkDirectives.LengthField))
            {
                throw MmlImportException.SpcFieldUnread(fieldName, _line);
            }

            SkipWhitespace();
            string value = ReadQuotedString();
            _spcTags = fieldName switch
            {
                AmkDirectives.TitleField => _spcTags with { Title = value },
                AmkDirectives.AuthorField => _spcTags with { Author = value },
                AmkDirectives.GameField => _spcTags with { Game = value },
                AmkDirectives.CommentField => _spcTags with { Comment = value },
                _ => _spcTags,
            };
        }

        // Reads an #option, which has to come before any channel header.
        private void ReadOption()
        {
            if (_isChannelDefined)
            {
                throw MmlImportException.OptionBeforeChannel(_line);
            }

            int line = _line;
            SkipWhitespace();
            string optionName = ReadWord();
            switch (optionName.ToLowerInvariant())
            {
                case AmkDirectives.NoLoopOption:
                    _doesLoop = false;
                    break;
                case AmkDirectives.SmwVelocityTableOption:
                    if (TrySwitchSmwVelocitiesOn())
                    {
                        AddVelocityTableSwitch(true, line);
                    }

                    break;
                case AmkDirectives.NspcVelocityTableOption:
                    AddVelocityTableSwitch(false, line);
                    _usesSmwVelocities = false;
                    break;
                case AmkDirectives.TempoImmunityOption:
                    _hasTempoImmunity = true;
                    break;
                case AmkDirectives.Amk109HotPatchOption:
                    AddIgnoredHotPatch(IgnoredCommand.HotPatchPreset, Amk109HotPatchPreset, line);
                    break;
                default:
                    throw MmlImportException.OptionUnread(optionName, _line);
            }
        }
    }
}
