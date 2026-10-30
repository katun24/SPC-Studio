using SpcStudio.Export;
using SpcStudio.Model;

namespace SpcStudio.Import;

// One compiled song read out of its ARAM image: the header's phrases, the instrument table, each channel as one stream across every phrase, and the remote code blocks the streams arm.
internal sealed class RomSongReader
{
    private const int InstrumentEntryBytes = 6;
    private const int EntryFirstByteOffset = 1;
    private const int NoiseInstrumentFlag = 0x80;
    private const int NoiseClockMask = 0x1F;
    private const int LoopWordLimit = 0x100;
    private const int MostPhrases = 256;
    private const int ChannelPointerBytes = 2;

    private readonly RomSong _song;
    private readonly byte[] _data;
    private readonly int _aramBase;
    private readonly Dictionary<int, IReadOnlyList<MmlEvent>> _remoteBlocksByAddress = [];

    // A reader of one song, under one of the two read-aheads.
    public RomSongReader(RomSong song, IReadOnlyList<byte>? echoFilters, bool doesReadIntoLoops)
    {
        _song = song;
        _data = song.Data;
        _aramBase = song.AramBase;
        EchoFilters = echoFilters;
        DoesReadIntoLoops = doesReadIntoLoops;
    }

    // Whether the song is read under the hot patch that sends the read-ahead inside loops.
    public bool DoesReadIntoLoops { get; }

    // The driver's echo filter tables and the bytes behind them, or null where the ROM's driver could not be read.
    public IReadOnlyList<byte>? EchoFilters { get; }

    // The song's byte at an ARAM address, refused where the address is outside the song.
    public byte ByteAt(int address)
    {
        int offset = address - _aramBase;
        if (offset < 0 || offset >= _data.Length)
        {
            throw MmlImportException.PointerLeavesSong(address);
        }

        return _data[offset];
    }

    // The little-endian word at an ARAM address.
    public int WordAt(int address)
    {
        return ByteAt(address) | ByteAt(address + 1) << 8;
    }

    // Whether an ARAM address is inside the song's own data.
    public bool IsInsideSong(int address)
    {
        return address >= _aramBase && address < _aramBase + _data.Length;
    }

    // The real length of the command at an ARAM address.
    public int CommandLengthAt(int address)
    {
        return RomTrackReader.CommandLength(_data, address - _aramBase);
    }

    // The whole song: its phrases measured, each channel decoded across them, the loop point placed and the instruments and samples listed.
    public MmlFile ReadSong()
    {
        var (phrases, loopPhrase) = ReadHeader();
        var instruments = ReadInstruments(phrases[0]);
        var (channelStarts, phraseLengths, isChannelWritten) = MeasurePhrases(phrases);
        var phraseStartTicks = new int[phrases.Count];
        int songEndTick = 0;
        for (int phrase = 0; phrase < phrases.Count; phrase++)
        {
            phraseStartTicks[phrase] = songEndTick;
            songEndTick += phraseLengths[phrase];
        }

        var channels = new List<MmlChannel>();
        for (int channel = 0; channel < Song.DriverChannelCount; channel++)
        {
            if (isChannelWritten[channel])
            {
                channels.Add(DecodeChannel(channel, channelStarts, phraseStartTicks, phraseLengths, loopPhrase, songEndTick));
            }
        }

        var sampleEntries = _song.SampleNumbers.Select(number => new MmlSampleEntry(RomTrackReader.SampleName(number), false, 0)).ToList();
        return new MmlFile(
            SamplePath: "",
            Samples: sampleEntries,
            Instruments: instruments,
            DoesLoop: loopPhrase is not null,
            HasTempoImmunity: false,
            Channels: channels,
            RemoteBlocks: _remoteBlocksByAddress)
        {
            DoesReadIntoLoops = DoesReadIntoLoops,
        };
    }

    // How long each phrase is - its shortest track, the first $00 any channel reads ending it for all eight - with where each channel starts in it and which channels are written at all.
    private (int[][] ChannelStarts, int[] PhraseLengths, bool[] IsChannelWritten) MeasurePhrases(List<int> phrases)
    {
        var isChannelWritten = new bool[Song.DriverChannelCount];
        var channelStarts = new int[phrases.Count][];
        var phraseLengths = new int[phrases.Count];
        for (int phrase = 0; phrase < phrases.Count; phrase++)
        {
            channelStarts[phrase] = new int[Song.DriverChannelCount];
            int shortestTicks = int.MaxValue;
            for (int channel = 0; channel < Song.DriverChannelCount; channel++)
            {
                int startAddress = WordAt(phrases[phrase] + channel * ChannelPointerBytes);
                channelStarts[phrase][channel] = startAddress;
                if (startAddress == 0)
                {
                    continue;
                }

                isChannelWritten[channel] = true;
                var measuringTrack = new RomTrack(this, channel) { IsMeasuring = true };
                measuringTrack.DecodeUntil(startAddress, int.MaxValue);
                shortestTicks = Math.Min(shortestTicks, measuringTrack.Tick);
            }

            if (shortestTicks == int.MaxValue)
            {
                throw MmlImportException.PhraseWithoutChannels(phrase);
            }

            phraseLengths[phrase] = shortestTicks;
        }

        return (channelStarts, phraseLengths, isChannelWritten);
    }

    // One channel decoded as one continuous stream across every phrase, with the loop point placed in front of the phrase the song goes back to.
    private MmlChannel DecodeChannel(int channel, int[][] channelStarts, int[] phraseStartTicks, int[] phraseLengths, int? loopPhrase, int songEndTick)
    {
        var track = new RomTrack(this, channel);
        int loopEventIndex = 0;
        for (int phrase = 0; phrase < channelStarts.Length; phrase++)
        {
            if (phrase == loopPhrase)
            {
                loopEventIndex = track.Events.Count;
            }

            int phraseEndTick = phraseStartTicks[phrase] + phraseLengths[phrase];
            if (channelStarts[phrase][channel] != 0)
            {
                track.DecodeUntil(channelStarts[phrase][channel], phraseEndTick);
            }
            else
            {
                track.SitOutPhrase();
            }

            track.CutAtPhraseEnd(phraseEndTick);
        }

        var events = track.Events;
        if (loopPhrase is { } loopPhraseIndex)
        {
            events.Insert(loopEventIndex, new MmlLoopMark(phraseStartTicks[loopPhraseIndex], 0));
        }

        return new MmlChannel(channel, [.. events.OrderBy(channelEvent => channelEvent.Tick)], songEndTick)
        {
            CutTies = track.CutTies,
            LoopEdges = [.. track.LoopEdges.Distinct().OrderBy(edge => edge.Tick)],
            RestBytes = track.RestBytes,
            RestTies = track.RestTies,
        };
    }

    // The song header: the phrase pointers in order, and the phrase a loop goes back to, or null where the song ends.
    private (List<int> Phrases, int? LoopPhrase) ReadHeader()
    {
        var phrases = new List<int>();
        int address = _aramBase;
        while (true)
        {
            int word = WordAt(address);
            if (word == 0)
            {
                if (phrases.Count == 0)
                {
                    throw MmlImportException.SongWithoutPhrases();
                }

                return (phrases, null);
            }

            if (word < LoopWordLimit)
            {
                int targetAddress = WordAt(address + 2);
                int targetPhrase = (targetAddress - _aramBase) / 2;
                if ((targetAddress - _aramBase) % 2 != 0 || targetPhrase < 0 || targetPhrase >= phrases.Count)
                {
                    throw MmlImportException.LoopTargetNotPhrase(targetAddress);
                }

                return (phrases, targetPhrase);
            }

            phrases.Add(word);
            address += 2;
            if (phrases.Count > MostPhrases)
            {
                throw MmlImportException.SongHeaderUnterminated();
            }
        }
    }

    // The song's own instrument table, which sits between the header and the first phrase.
    private List<MmlInstrument> ReadInstruments(int firstPhraseAddress)
    {
        int address = _aramBase;
        while (WordAt(address) >= LoopWordLimit)
        {
            address += 2;
        }

        address += WordAt(address) == 0 ? 2 : 4;
        int instrumentTableBytes = firstPhraseAddress - address;
        if (instrumentTableBytes < 0 || instrumentTableBytes % InstrumentEntryBytes != 0)
        {
            throw MmlImportException.InstrumentTableUneven(instrumentTableBytes);
        }

        var instruments = new List<MmlInstrument>();
        for (int entry = 0; entry < instrumentTableBytes / InstrumentEntryBytes; entry++)
        {
            int entryAddress = address + entry * InstrumentEntryBytes;
            int sample = ByteAt(entryAddress);
            if (sample >= NoiseInstrumentFlag)
            {
                instruments.Add(InstrumentAt(entryAddress, null, sample & NoiseClockMask));
                continue;
            }

            if (sample >= _song.SampleNumbers.Count)
            {
                throw MmlImportException.InstrumentSamplePastSongSamples(MmlExporter.FirstCustomInstrument + entry, sample, _song.SampleNumbers.Count);
            }

            instruments.Add(InstrumentAt(entryAddress, RomTrackReader.SampleName(_song.SampleNumbers[sample]), null));
        }

        return instruments;
    }

    // One instrument entry's bytes after its first, under the sample name or noise clock that first byte gave.
    private MmlInstrument InstrumentAt(int entryAddress, string? sampleName, int? noiseClock)
    {
        int byteAddress = entryAddress + EntryFirstByteOffset;

        // Reads the entry's next byte.
        byte ReadEntryByte()
        {
            return ByteAt(byteAddress++);
        }

        var bytes = new InstrumentBytes(ReadEntryByte(), ReadEntryByte(), ReadEntryByte(), ReadEntryByte(), ReadEntryByte());
        return new MmlInstrument(sampleName, noiseClock, null, bytes, RomTrackReader.NoTextLine);
    }

    // The key a remote code call's address is filed under: a block decoded at that address, or the remote gain a page 1 address names.
    public int RemoteBlockKey(int address)
    {
        if (address >> 8 == MmlRemoteGain.AddressPage)
        {
            int gain = address & byte.MaxValue;
            int gainKey = MmlRemoteGain.BlockLabelOf(gain);
            _remoteBlocksByAddress.TryAdd(gainKey, [new MmlRemoteGain(0, gain, 0)]);
            return gainKey;
        }

        if (_remoteBlocksByAddress.ContainsKey(address))
        {
            return address;
        }

        _remoteBlocksByAddress[address] = [];
        var remoteBlockTrack = new RomTrack(this, 0) { IsRemoteBlock = true };
        remoteBlockTrack.DecodeUntil(address, int.MaxValue);
        _remoteBlocksByAddress[address] = [.. remoteBlockTrack.Events.Select(blockEvent => blockEvent with { Tick = 0 })];
        return address;
    }
}
