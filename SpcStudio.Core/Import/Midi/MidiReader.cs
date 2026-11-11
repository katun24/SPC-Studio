using System.Collections.Immutable;
using System.Text;
using SpcStudio.Model;

namespace SpcStudio.Import;

// Reads a Standard MIDI File's bytes: its header, and every track's channel messages, tempo changes, end and song name.
public static class MidiReader
{
    private const string HeaderChunkId = "MThd";
    private const string TrackChunkId = "MTrk";
    private const int ChunkIdLength = 4;
    private const int ChunkHeaderLength = 8;
    private const int MinHeaderLength = 6;
    private const int SeveralSongsFormat = 2;
    private const int SecondsTimingBit = 0x8000;
    private const int FramesPerSecondShift = 8;
    private const int DropFrameRate = 29;
    private const double DropFrameFramesPerSecond = 29.97;
    private const int StatusBit = 0x80;
    private const int DataMask = 0x7F;
    private const int MessageKindMask = 0xF0;
    private const int ChannelMask = 0x0F;
    private const int SystemStatus = 0xF0;
    private const int MetaStatus = 0xFF;
    private const int SystemExclusiveStatus = 0xF0;
    private const int SystemExclusiveContinuationStatus = 0xF7;
    private const int NoRunningStatus = 0;
    private const int NoteOffStatus = 0x80;
    private const int NoteOnStatus = 0x90;
    private const int ControlChangeStatus = 0xB0;
    private const int ProgramChangeStatus = 0xC0;
    private const int ChannelPressureStatus = 0xD0;
    private const int PitchBendStatus = 0xE0;
    private const int PitchBendHighShift = 7;
    private const int EndOfTrackType = 0x2F;
    private const int TempoType = 0x51;
    private const int TrackNameType = 0x03;
    private const int TempoLength = 3;
    private const int TwoBytesShift = 16;
    private const int VariableLengthContinueBit = 0x80;
    private const int VariableLengthShift = 7;
    private const int MaxVariableLengthBytes = 4;

    // The file's contents, or null where the bytes are not a Standard MIDI File this reader can take - one holding several separate songs among them.
    public static MidiFile? Read(byte[] bytes)
    {
        try
        {
            return ReadOrThrow(new ByteCursor(bytes));
        }
        catch (NotReadableException)
        {
            return null;
        }
    }

    // The file's contents, thrown out of at the first thing that is not a Standard MIDI File this reader can take.
    private static MidiFile ReadOrThrow(ByteCursor cursor)
    {
        if (cursor.ReadChunkId() != HeaderChunkId)
        {
            throw new NotReadableException();
        }

        int headerLength = cursor.ReadChunkLength();
        if (headerLength < MinHeaderLength)
        {
            throw new NotReadableException();
        }

        int headerEnd = cursor.Position + headerLength;
        int format = cursor.ReadUInt16();
        int trackCount = cursor.ReadUInt16();
        int division = cursor.ReadUInt16();
        if (format == SeveralSongsFormat)
        {
            throw new NotReadableException();
        }

        var timeBase = TimeBaseOf(division);
        cursor.Position = headerEnd;
        var contents = new FileContents();
        int tracksRead = 0;
        while (tracksRead < trackCount && cursor.BytesLeft >= ChunkHeaderLength)
        {
            string chunkId = cursor.ReadChunkId();
            int chunkLength = cursor.ReadChunkLength();
            int chunkEnd = (int)Math.Min((long)cursor.Position + chunkLength, cursor.Length);
            if (chunkId == TrackChunkId)
            {
                string? trackName = ReadTrack(cursor.Within(chunkEnd), contents);
                if (tracksRead == 0)
                {
                    contents.SongName = trackName;
                }

                tracksRead++;
            }

            cursor.Position = chunkEnd;
        }

        if (tracksRead == 0)
        {
            throw new NotReadableException();
        }

        return new MidiFile(timeBase, [.. contents.Messages.OrderBy(message => message.Tick)], [.. contents.Tempos.OrderBy(tempo => tempo.Tick)], contents.EndTick, contents.SongName);
    }

    // How the header's division counts ticks: per quarter note, or per second as frames a second times ticks a frame.
    private static MidiTimeBase TimeBaseOf(int division)
    {
        if (division == 0)
        {
            throw new NotReadableException();
        }

        if ((division & SecondsTimingBit) == 0)
        {
            return new MidiTimeBase(division, IsCountedInSeconds: false);
        }

        int framesPerSecond = -(sbyte)(division >> FramesPerSecondShift);
        int ticksPerFrame = division & byte.MaxValue;
        if (framesPerSecond <= 0 || ticksPerFrame == 0)
        {
            throw new NotReadableException();
        }

        double exactFramesPerSecond = framesPerSecond == DropFrameRate ? DropFrameFramesPerSecond : framesPerSecond;
        return new MidiTimeBase(exactFramesPerSecond * ticksPerFrame, IsCountedInSeconds: true);
    }

    // Reads one track's events into the file's contents, answering the first name the track gives itself, or null where it gives none.
    private static string? ReadTrack(ByteCursor track, FileContents contents)
    {
        long tick = 0;
        int runningStatus = NoRunningStatus;
        string? trackName = null;
        while (track.BytesLeft > 0)
        {
            tick += track.ReadVariableLength();
            int firstByte = track.ReadByte();
            bool isStatus = (firstByte & StatusBit) != 0;
            if (!isStatus && runningStatus == NoRunningStatus)
            {
                throw new NotReadableException();
            }

            int status = isStatus ? firstByte : runningStatus;
            if (status == MetaStatus)
            {
                runningStatus = NoRunningStatus;
                var meta = ReadMetaEvent(track, contents, tick);
                trackName ??= meta.TrackName;
                if (meta.IsEndOfTrack)
                {
                    break;
                }

                continue;
            }

            if (status is SystemExclusiveStatus or SystemExclusiveContinuationStatus)
            {
                runningStatus = NoRunningStatus;
                track.Skip(track.ReadVariableLength());
                continue;
            }

            if (status >= SystemStatus)
            {
                throw new NotReadableException();
            }

            runningStatus = status;
            int firstValue = isStatus ? track.ReadByte() & DataMask : firstByte & DataMask;
            int secondValue = TakesOneValue(status) ? 0 : track.ReadByte() & DataMask;
            if (MessageOf(tick, status, firstValue, secondValue) is { } message)
            {
                contents.Messages.Add(message);
            }
        }

        contents.EndTick = Math.Max(contents.EndTick, tick);
        return trackName;
    }

    // Reads one meta event, keeping a tempo change, and answers whether it ends the track and the name it gives the track, where it is one that does.
    private static (bool IsEndOfTrack, string? TrackName) ReadMetaEvent(ByteCursor track, FileContents contents, long tick)
    {
        int type = track.ReadByte();
        var data = track.ReadBytes(track.ReadVariableLength());
        if (type == EndOfTrackType)
        {
            return (true, null);
        }

        if (type == TempoType && data.Length >= TempoLength)
        {
            int microsecondsPerQuarterNote = data[0] << TwoBytesShift | data[1] << ByteUnits.BitsPerByte | data[2];
            if (microsecondsPerQuarterNote > 0)
            {
                contents.Tempos.Add(new MidiTempoChange(tick, microsecondsPerQuarterNote));
            }
        }

        if (type != TrackNameType)
        {
            return (false, null);
        }

        string name = Encoding.Latin1.GetString(data).Trim();
        return (false, name.Length > 0 ? name : null);
    }

    // Whether a channel message's status carries one data value rather than two.
    private static bool TakesOneValue(int status)
    {
        return (status & MessageKindMask) is ProgramChangeStatus or ChannelPressureStatus;
    }

    // The channel message a status and its values make, or null for one the import does not read.
    private static MidiMessage? MessageOf(long tick, int status, int firstValue, int secondValue)
    {
        int channel = status & ChannelMask;
        return (status & MessageKindMask) switch
        {
            NoteOffStatus => new MidiMessage(tick, channel, MidiMessageKind.NoteOff, firstValue, secondValue),
            NoteOnStatus => new MidiMessage(tick, channel, MidiMessageKind.NoteOn, firstValue, secondValue),
            ControlChangeStatus => new MidiMessage(tick, channel, MidiMessageKind.ControlChange, firstValue, secondValue),
            ProgramChangeStatus => new MidiMessage(tick, channel, MidiMessageKind.ProgramChange, firstValue, secondValue),
            PitchBendStatus => new MidiMessage(tick, channel, MidiMessageKind.PitchBend, secondValue << PitchBendHighShift | firstValue, 0),
            _ => null,
        };
    }

    // What the tracks read so far hold together.
    private sealed class FileContents
    {
        public List<MidiMessage> Messages { get; } = [];
        public List<MidiTempoChange> Tempos { get; } = [];
        public long EndTick { get; set; }
        public string? SongName { get; set; }
    }

    // A position in the file's bytes that reads big-endian numbers forward and refuses to read past its own end.
    private sealed class ByteCursor
    {
        private readonly byte[] _bytes;
        private readonly int _end;

        // A cursor over all of the bytes, at the first.
        public ByteCursor(byte[] bytes) : this(bytes, 0, bytes.Length)
        {
        }

        private ByteCursor(byte[] bytes, int position, int end)
        {
            _bytes = bytes;
            _end = end;
            Position = position;
        }

        public int Position { get; set; }

        // How many bytes the whole file holds.
        public int Length
        {
            get
            {
                return _bytes.Length;
            }
        }

        // How many bytes are left before the cursor's end.
        public int BytesLeft
        {
            get
            {
                return _end - Position;
            }
        }

        // A cursor from this one's position to an earlier end of its own.
        public ByteCursor Within(int end)
        {
            return new ByteCursor(_bytes, Position, end);
        }

        // The next byte.
        public int ReadByte()
        {
            if (Position >= _end)
            {
                throw new NotReadableException();
            }

            return _bytes[Position++];
        }

        // The next bytes, as many as asked for.
        public byte[] ReadBytes(int count)
        {
            if (count < 0 || count > BytesLeft)
            {
                throw new NotReadableException();
            }

            var read = _bytes.AsSpan(Position, count).ToArray();
            Position += count;
            return read;
        }

        // Moves past as many bytes as asked for.
        public void Skip(int count)
        {
            ReadBytes(count);
        }

        // The next two bytes as one number.
        public int ReadUInt16()
        {
            return ReadByte() << ByteUnits.BitsPerByte | ReadByte();
        }

        // The four characters naming a chunk.
        public string ReadChunkId()
        {
            return Encoding.ASCII.GetString(ReadBytes(ChunkIdLength));
        }

        // The four bytes giving a chunk's length, refused where they count past what a file can hold.
        public int ReadChunkLength()
        {
            long length = (long)ReadUInt16() << TwoBytesShift | (uint)ReadUInt16();
            if (length > int.MaxValue)
            {
                throw new NotReadableException();
            }

            return (int)length;
        }

        // A number written seven bits a byte, the top bit of each saying another byte follows.
        public int ReadVariableLength()
        {
            int value = 0;
            for (int byteIndex = 0; byteIndex < MaxVariableLengthBytes; byteIndex++)
            {
                int read = ReadByte();
                value = value << VariableLengthShift | read & DataMask;
                if ((read & VariableLengthContinueBit) == 0)
                {
                    return value;
                }
            }

            throw new NotReadableException();
        }
    }

    // The bytes stopped being a Standard MIDI File this reader can take.
    private sealed class NotReadableException : Exception
    {
    }
}
