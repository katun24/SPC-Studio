using SpcStudio.Model;

namespace SpcStudio.Import;

public static partial class MmlParser
{
    private sealed partial class Parser
    {
        private const string RemoteCodeOpener = "(!";
        private const int AmkOneRemoteGainDelayArgument = 0;
        private const int AmkOneRemoteGainArgument = 1;

        // Reads remote code: a definition above the first channel, a (!!n) reset, or a (!n,type) call.
        private void ReadRemoteCode(int line)
        {
            if (_amkVersion < MmlPreprocessor.AmkTwoDialect)
            {
                throw MmlImportException.RemoteCodeBeforeAmkTwo(WrittenDialectLine, _line);
            }

            _position += RemoteCodeOpener.Length;
            SkipWhitespace();
            if (!_isChannelDefined)
            {
                ReadRemoteDefinition();
                return;
            }

            if (TryConsume('!'))
            {
                ReadRemoteReset(line);
                return;
            }

            ReadRemoteCall(line);
        }

        // Reads a (!n,type) call, with the third argument the timed and the before-key-off types take, into the events being written.
        private void ReadRemoteCall(int line)
        {
            int blockLabel = ReadRequiredInt(MmlImportException.RemoteCallNumberMissing);

            if (!TryConsumeComma())
            {
                throw MmlImportException.RemoteCallEventTypeMissing(blockLabel, _line);
            }

            int writtenTriggerType = ReadRequiredIntWithNegative(cursorLine => MmlImportException.RemoteCallEventTypeMissing(blockLabel, cursorLine));

            SkipWhitespace();
            int timeTicks = 0;
            if (writtenTriggerType is MmlRemoteCall.Timed or MmlRemoteCall.BeforeKeyOff)
            {
                timeTicks = ReadRemoteCallTimeTicks(blockLabel);
                SkipWhitespace();
            }

            if (!TryConsume(')'))
            {
                throw MmlImportException.RemoteCallCloseMissing(blockLabel, _line);
            }

            if (IsCharacterAtCursor('['))
            {
                throw MmlImportException.RemoteDefinitionInsideChannel(_line);
            }

            int triggerType = MmlRemoteCall.TriggerTypeAsByte(writtenTriggerType);
            RefuseUnreadRemoteType(triggerType);
            var call = new MmlRemoteCall(CurrentChannel(line).EndTick, blockLabel, triggerType, timeTicks, line);
            if (!call.DoesClear)
            {
                _remoteCodeBodiesByLabel.TryAdd(blockLabel, []);
            }

            EventsBeingWritten(line).Add(call);
        }

        // Reads a call's third argument as the byte it is written in: a raw hex byte, or a note length in ticks, a whole byte's wrap being written as zero.
        private int ReadRemoteCallTimeTicks(int blockLabel)
        {
            if (!TryConsume(','))
            {
                throw MmlImportException.RemoteCallThirdArgumentMissing(blockLabel, _line);
            }

            if (TryConsumeAfterWhitespace('$'))
            {
                int hexValue = ReadRequiredHex(cursorLine => MmlImportException.RemoteCallThirdArgumentUnreadable(blockLabel, cursorLine));

                return hexValue;
            }

            int lengthTicks = ReadDurationTicks();
            if (lengthTicks > MaxRemoteCallLengthTicks)
            {
                throw MmlImportException.RemoteCallThirdArgumentTooLong(blockLabel, lengthTicks, _line);
            }

            return lengthTicks == MaxRemoteCallLengthTicks ? 0 : lengthTicks;
        }

        // Refuses a trigger type the model does not read, whichever of the two spellings the call was written in.
        private void RefuseUnreadRemoteType(int triggerType)
        {
            if (!MmlRemoteCall.IsReadType(triggerType))
            {
                throw MmlImportException.RemoteEventTypeUnread(triggerType, _line);
            }
        }

        // Reads (!n)[ and opens a remote code definition, whose body the ordinary dispatch then reads into a channel of its own.
        private void ReadRemoteDefinition()
        {
            int blockLabel = ReadRequiredInt(MmlImportException.RemoteDefinitionNumberMissing);

            if (!TryConsumeAfterWhitespace(')'))
            {
                throw MmlImportException.RemoteDefinitionCloseMissing(blockLabel, _line);
            }

            if (!IsCharacterAtCursor('['))
            {
                throw MmlImportException.RemoteDefinitionBodyMissing(blockLabel, _line);
            }

            if (_remoteCodeBodiesByLabel.ContainsKey(blockLabel) || _loopBodiesByLabel.ContainsKey(blockLabel))
            {
                throw MmlImportException.RemoteBlockDefinedTwice(blockLabel, _line);
            }

            _position++;
            _remoteDefinitionChannel = new ChannelState();
            _remoteDefinitionLabel = blockLabel;
        }

        // Reads (!!n), which clears both slots, the key-on slot or the ordinary one, into the events being written.
        private void ReadRemoteReset(int line)
        {
            if (_amkVersion < MmlPreprocessor.NewestDialect)
            {
                throw MmlImportException.RemoteResetInAmkTwo(_line);
            }

            int resetNumber = ReadRequiredIntWithNegative(MmlImportException.RemoteResetNumberMissing);

            if (!TryConsumeAfterWhitespace(')'))
            {
                throw MmlImportException.RemoteResetCloseMissing(_line);
            }

            // The slots the reset number clears.
            int clearType = resetNumber switch
            {
                0 => MmlRemoteCall.ClearBoth,
                -1 => MmlRemoteCall.ClearKeyOn,
                _ => MmlRemoteCall.ClearOrdinary,
            };

            EventsBeingWritten(line).Add(new MmlRemoteCall(CurrentChannel(line).EndTick, null, clearType, 0, line));
        }

        // Reads $FC written as raw hex, which is read as a remote gain or a clear, into the events being written.
        private void ReadRemoteCodeHex(int line)
        {
            int addressLow = ReadHexArgument(AmkCommands.RemoteCode, MmlRemoteCall.AddressLowArgument);
            int addressHigh = ReadHexArgument(AmkCommands.RemoteCode, MmlRemoteCall.AddressHighArgument);
            int triggerType = MmlRemoteCall.TriggerTypeAsByte(ReadHexArgument(AmkCommands.RemoteCode, MmlRemoteCall.TriggerTypeArgument));
            int timeTicks = ReadHexArgument(AmkCommands.RemoteCode, MmlRemoteCall.TimeTicksArgument);
            AddRemoteCodeHex(addressLow, addressHigh, triggerType, timeTicks, line);
        }

        // Reads #amk 1's $FC, a delay and a gain, as the remote gain AddmusicK turns it into: armed a delay before the key-off where neither is zero,
        // cleared where either is.
        private void ReadAmkOneRemoteGain(int line)
        {
            int delayTicks = ReadHexArgument(AmkCommands.RemoteCode, AmkOneRemoteGainDelayArgument);
            int gain = ReadHexArgument(AmkCommands.RemoteCode, AmkOneRemoteGainArgument);
            if (gain == 0 || delayTicks == 0)
            {
                AddRemoteCodeHex(0, 0, MmlRemoteCall.ClearOrdinary, 0, line);
                return;
            }

            AddRemoteCodeHex(gain, MmlRemoteGain.AddressPage, MmlRemoteCall.BeforeKeyOffRestoring, delayTicks, line);
        }

        // Reads #amk 1's $FA $05 gain as the remote gain AddmusicK turns it into: written at the key-off, or cleared where the gain is zero.
        private void ReadAmkOneKeyOffGain(int line)
        {
            int gain = ReadHexArgument(AmkCommands.Parameter, ParameterValueArgument);
            if (gain == 0)
            {
                AddRemoteCodeHex(0, 0, MmlRemoteCall.ClearKeyOn, 0, line);
                return;
            }

            AddRemoteCodeHex(gain, MmlRemoteGain.AddressPage, MmlRemoteCall.KeyOffRestoring, 0, line);
        }

        // Adds the remote code call an $FC's four bytes make, a remote gain or a clear, to the events being written.
        private void AddRemoteCodeHex(int addressLow, int addressHigh, int triggerType, int timeTicks, int line)
        {
            if (_remoteDefinitionChannel is not null)
            {
                throw MmlImportException.RemoteCallHexInsideDefinition(_line);
            }

            RefuseUnreadRemoteType(triggerType);
            var call = new MmlRemoteCall(CurrentChannel(line).EndTick, null, triggerType, timeTicks, line);
            if (!call.DoesClear)
            {
                call = call with { BlockLabel = RemoteGainBlockLabel(addressLow, addressHigh, line) };
            }

            EventsBeingWritten(line).Add(call);
        }

        // The label of the remote gain block a $FC's address names, adding the block where it is not yet held and refusing an address off the remote gain page.
        private int RemoteGainBlockLabel(int addressLow, int addressHigh, int line)
        {
            if (addressHigh != MmlRemoteGain.AddressPage)
            {
                throw MmlImportException.RemoteCallHexAramAddress(addressHigh << ByteUnits.BitsPerByte | addressLow, _line);
            }

            int blockLabel = MmlRemoteGain.BlockLabelOf(addressLow);
            _remoteCodeBodiesByLabel.TryAdd(blockLabel, [new MmlRemoteGain(0, addressLow, line)]);
            return blockLabel;
        }
    }
}
