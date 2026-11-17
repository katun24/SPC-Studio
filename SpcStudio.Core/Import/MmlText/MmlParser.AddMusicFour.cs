using SpcStudio.Export;
using SpcStudio.Model;

namespace SpcStudio.Import;

public static partial class MmlParser
{
    private sealed partial class Parser
    {
        private const int AddMusicFourDspWrite = 0x80;
        private const int AddMusicFourTranspose = 0x81;
        private const int AddMusicFourMemoryWrite = 0x82;
        private const int AddMusicFourUnreadEnvelope = 0x83;
        private const int AddMusicFourInstrumentTableAddress = 0x6136;
        private const int AddMusicFourInstrumentEntryBytes = 5;
        private const int AddMusicFourSampleLoadFlag = 0x80;
        private const int AddMusicFourLastSampleWithoutList = 0x13;
        private const int AddMusicFourFirstCustomInstrumentByte = 0x13;
        private const int AddMusicFourMostDots = 2;
        private const int AddMusicFourHotPatchPreset = 0x04;

        // How many dots after a duration are read onto it: two in an AddMusic4 song, and every one otherwise.
        protected override int MostDots
        {
            get
            {
                return _dialect.IsAddMusicFour ? AddMusicFourMostDots : base.MostDots;
            }
        }

        // The hot patch preset AddmusicK writes at the start of an AddMusic4 song, standing on the song's dialect line.
        private MmlIgnored AddMusicFourPreset()
        {
            return MmlIgnored.ForHotPatchPreset(0, AddMusicFourHotPatchPreset, _dialect.Line) with { IsAddMusicFourPreset = true };
        }

        // Reads an $ED in an AddMusic4 song: one of AddMusic4's own commands where its first argument names one, and the envelope override otherwise.
        private void ReadAddMusicFourEnvelope(int line)
        {
            int firstArgument = ReadHexArgument(AmkCommands.Envelope, 0);
            switch (firstArgument)
            {
                case AddMusicFourDspWrite: ReadAddMusicFourDspWrite(line); return;
                case AddMusicFourTranspose: AddTranspose(ReadHexArgument(AmkCommands.Envelope, 1), line); return;
                case AddMusicFourMemoryWrite: ReadAddMusicFourMemoryWrite(line); return;
                case AddMusicFourUnreadEnvelope: throw MmlImportException.AddMusicFourEnvelopeUnread(firstArgument, _line);
            }

            ReadEnvelopeAfter(firstArgument, line);
        }

        // Reads AddMusic4's write of a value to a DSP register, which AddmusicK writes as the noise command for FLG, drops for the echo's start and delay,
        // and writes as a raw DSP write otherwise.
        private void ReadAddMusicFourDspWrite(int line)
        {
            int register = ReadHexArgument(AmkCommands.Envelope, 1);
            int value = ReadHexArgument(AmkCommands.Envelope, 2);
            switch (register)
            {
                case SnesDspRegister.Flags:
                    AddNoiseCommand(value, $"${AmkCommands.Envelope:X2} ${AddMusicFourDspWrite:X2} ${register:X2} ${value:X2}", line);
                    return;

                case SnesDspRegister.EchoStart or SnesDspRegister.EchoDelay:
                    return;
            }

            AddDspWrite(register, value, line);
        }

        // Reads AddMusic4's write to the driver's memory - an address, a count one short of the bytes after it, and the bytes - which AddmusicK makes custom
        // instruments of at AddMusic4's instrument table, refused where they end on part of one, and drops anywhere else.
        private void ReadAddMusicFourMemoryWrite(int line)
        {
            int argumentIndex = 1;

            // Reads the command's next argument.
            int ReadNextArgument()
            {
                return ReadHexArgument(AmkCommands.Envelope, argumentIndex++);
            }

            int address = (ReadNextArgument() << ByteUnits.BitsPerByte) | ReadNextArgument();
            int byteCount = ((ReadNextArgument() << ByteUnits.BitsPerByte) | ReadNextArgument()) + 1;
            var writtenBytes = Enumerable.Range(0, byteCount).Select(_ => ReadNextArgument()).ToList();
            if (address != AddMusicFourInstrumentTableAddress)
            {
                return;
            }

            if (writtenBytes.Count % AddMusicFourInstrumentEntryBytes != 0)
            {
                throw MmlImportException.AddMusicFourInstrumentTablePartial(AddMusicFourMemoryWrite, _line);
            }

            for (int entryStart = 0; entryStart < writtenBytes.Count; entryStart += AddMusicFourInstrumentEntryBytes)
            {
                AddAddMusicFourInstrument(writtenBytes.GetRange(entryStart, AddMusicFourInstrumentEntryBytes), line);
            }
        }

        // Adds one entry of AddMusic4's instrument table as a custom instrument: its sample or noise byte, its envelope and its whole-number tuning, the
        // fraction AddmusicK adds to it being zero.
        private void AddAddMusicFourInstrument(List<int> entry, int line)
        {
            int sampleByte = entry[0];
            var bytes = new InstrumentBytes((byte)entry[1], (byte)entry[2], (byte)entry[3], (byte)entry[4], 0);
            int? noiseClock = SnesNoise.ClockOfInstrumentSample(sampleByte);
            _instruments.Add(new MmlInstrument(null, noiseClock, null, bytes, line) { SamplePosition = noiseClock is null ? sampleByte : null });
        }

        // Reads an $E5 in an AddMusic4 song: a sample load where its first argument has the top bit set, and the tremolo otherwise.
        private void ReadAddMusicFourTremoloOrSampleLoad(int line)
        {
            int firstArgument = ReadHexArgument(AmkCommands.TremoloOn, 0);
            if (firstArgument < AddMusicFourSampleLoadFlag)
            {
                ReadTremoloAfter(firstArgument, line);
                return;
            }

            int sampleNumber = firstArgument - AddMusicFourSampleLoadFlag;
            if (_samples.Count == 0 && sampleNumber > AddMusicFourLastSampleWithoutList)
            {
                throw MmlImportException.AddMusicFourSampleLoadWithoutList(AddMusicFourLastSampleWithoutList, _line);
            }

            AddSampleLoad(sampleNumber, ReadHexArgument(AmkCommands.TremoloOn, 1), line);
        }

        // Reads a raw $DA in an AddMusic4 song, which AddmusicK writes with the numbers from $13 on moved up to the custom instruments', and which changes
        // neither the instrument whose tuning the notes after it take nor whether they take one.
        private void ReadAddMusicFourInstrumentHex(int line)
        {
            int written = ReadHexArgument(AmkCommands.Instrument, 0);
            int instrumentNumber = written < AddMusicFourFirstCustomInstrumentByte
                ? written
                : (written - AddMusicFourFirstCustomInstrumentByte + MmlExporter.FirstCustomInstrument) & byte.MaxValue;
            EventsBeingWritten(line).Add(new MmlInstrumentChange(CurrentChannel(line).EndTick, instrumentNumber, line));
        }
    }
}
