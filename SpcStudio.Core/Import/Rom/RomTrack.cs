using SpcStudio.Model;
using static SpcStudio.Import.RomTrackReader;

namespace SpcStudio.Import;

// One channel's stream walked byte by byte across the phrases it plays in: durations, notes, ties and rests, subroutine calls and subloops, and the
// read-ahead each fetched byte runs, turned into events. Its commands from $DA up are in the other file.
internal sealed partial class RomTrack
{
    private const int MostStepsInTrack = 1_000_000;
    private const int MostStepsInReadAhead = 100_000;
    private const int SubloopSeededPasses = -1;
    private const int CallBodyAddressOffset = 1;
    private const int CallPassCountOffset = 3;
    private const int SubloopArgumentOffset = 1;
    private const int SubloopStartArgument = 0;
    private const int LastPass = 1;

    private static readonly int CallLength = AmkCommands.LengthOf(AmkCommands.Call);
    private static readonly int SubloopMarkerLength = AmkCommands.LengthOf(AmkCommands.Subloop);

    private readonly RomSongReader _reader;
    private readonly int _channel;
    private readonly List<MmlEvent> _events = [];
    private readonly List<MmlCutTie> _cutTies = [];
    private readonly List<MmlLoopEdge> _loopEdges = [];
    private readonly List<int> _restBytes = [];
    private readonly List<int> _restTies = [];
    private int _durationTicks = 1;
    private int _quantization = Note.QuantizationOf(SnesTiming.DefaultGate, SnesVolume.DefaultVelocity);
    private int _lastByteStartTick;
    private int _openNoteIndex = -1;
    private CallFrame? _call;
    private SubloopFrame? _subloop;
    private int _openCutTieRunIndex = -1;
    private bool _hasSubloopPassFetched;
    private bool _wasLastFetchInCall;
    private bool _mayVoiceBeCutSinceNote;
    private bool _wasRestFetchedSinceNote;
    private bool _hasCallSinceFetch;
    private bool _hasCallPassFetched;
    private bool _doesPeekReachNextByte;
    private bool _hasFetchedAny;

    // A walk of one channel of the song.
    public RomTrack(RomSongReader reader, int channel)
    {
        _reader = reader;
        _channel = channel;
    }

    public IReadOnlyList<MmlCutTie> CutTies
    {
        get
        {
            return _cutTies;
        }
    }

    public IReadOnlyList<MmlLoopEdge> LoopEdges
    {
        get
        {
            return _loopEdges;
        }
    }

    public IReadOnlyList<int> RestBytes
    {
        get
        {
            return _restBytes;
        }
    }

    public IReadOnlyList<int> RestTies
    {
        get
        {
            return _restTies;
        }
    }

    // Where this track's clock has reached.
    public int Tick { get; private set; }

    // The events so far, which accumulate across every phrase.
    public List<MmlEvent> Events
    {
        get
        {
            return _events;
        }
    }

    // Whether the walk counts ticks only, reading no events and refusing nothing.
    public bool IsMeasuring { get; init; }

    // Whether this is a remote code block's body rather than a channel's stream.
    public bool IsRemoteBlock { get; init; }

    // Whether the read-ahead is walked inside loops here: under the hot patch, and not while measuring.
    private bool IsReadingIntoLoops
    {
        get
        {
            return _reader.DoesReadIntoLoops && !IsMeasuring;
        }
    }

    // Walks from an address until the clock reaches a tick or the track reads its $00.
    public void DecodeUntil(int startAddress, int limitTick)
    {
        int address = startAddress;
        _call = null;
        _subloop = null;
        for (int step = 0; Tick < limitTick; step++)
        {
            if (step > MostStepsInTrack)
            {
                throw MmlImportException.TrackNeverEnds(_channel + 1);
            }

            if (!_reader.IsInsideSong(address))
            {
                throw MmlImportException.TrackLeavesSong(_channel + 1, address);
            }

            byte trackByte = _reader.ByteAt(address);
            if (trackByte == TrackEnd)
            {
                if (ReturnFromCall(address) is not { } returnAddress)
                {
                    return;
                }

                address = returnAddress;
                continue;
            }

            if (trackByte < FirstNoteByte)
            {
                address = ReadDuration(address);
                continue;
            }

            if (trackByte < AmkCommands.First)
            {
                address = ReadNoteByte(address, trackByte);
                continue;
            }

            int length = _reader.CommandLengthAt(address);
            if (length == 0)
            {
                throw MmlImportException.ByteWithoutDispatch(_channel + 1, trackByte, address);
            }

            if (trackByte == AmkCommands.Call)
            {
                address = EnterCall(address, length);
                continue;
            }

            if (trackByte == AmkCommands.Subloop)
            {
                address = PassSubloopMarker(address, length);
                continue;
            }

            if (!IsMeasuring)
            {
                ReadCommand(address, trackByte);
            }

            address += length;
        }

        if (IsReadingIntoLoops && Tick == limitTick && _reader.IsInsideSong(address))
        {
            ReadAheadInsideLoops(address);
        }
    }

    // A phrase this channel has no pointer in.
    public void SitOutPhrase()
    {
        _wasRestFetchedSinceNote = false;
    }

    // Puts the clock exactly on a phrase boundary, shortening a note still sounding across it.
    public void CutAtPhraseEnd(int phraseEndTick)
    {
        AddLoopEdge(phraseEndTick, ReadAheadStop.KeyOff);

        _hasCallSinceFetch = true;
        _doesPeekReachNextByte = false;
        if (_openNoteIndex >= 0 && _events[_openNoteIndex] is MmlNote note && note.EndTick > phraseEndTick)
        {
            _events[_openNoteIndex] = note.ShortenedTo(phraseEndTick - note.Tick);
        }

        Tick = phraseEndTick;
    }

    // A $00: the end of a subroutine body's pass, answering where the walk goes on from, or the end of the track, answering null.
    private int? ReturnFromCall(int address)
    {
        _doesPeekReachNextByte = false;
        if (_call is not { } call)
        {
            _mayVoiceBeCutSinceNote = true;
            AddLoopEdge(Tick, ReadAheadStop.KeyOff);

            return null;
        }

        if (_hasCallPassFetched)
        {
            var readAheadStop = IsReadingIntoLoops ? ReadAheadInsideLoops(address) : ReadAheadStop.KeyOff;
            _mayVoiceBeCutSinceNote |= !IsReadingIntoLoops || DoesReadAheadKeyOff(readAheadStop);
            _wasLastFetchInCall = true;
            AddLoopEdge(Tick, readAheadStop);
        }

        int passesLeft = call.PassesLeft - 1;
        if (passesLeft == 0)
        {
            _call = null;
            _hasCallSinceFetch = true;
            return call.ReturnAddress;
        }

        _call = call with { PassesLeft = passesLeft };
        _hasCallPassFetched = false;
        return call.BodyAddress;
    }

    // An $E9 subroutine call, answering the address the walk goes on from.
    private int EnterCall(int address, int length)
    {
        _doesPeekReachNextByte = false;
        if (_call is not null)
        {
            throw MmlImportException.CallInsideCall();
        }

        int bodyAddress = _reader.WordAt(address + CallBodyAddressOffset);
        int passes = _reader.ByteAt(address + CallPassCountOffset);
        if (passes == 0)
        {
            return address + length;
        }

        var readAheadStop = IsReadingIntoLoops ? ReadAheadInsideLoops(address) : ReadAheadOverLoops(address + length);
        if (!_hasCallSinceFetch)
        {
            AddLoopEdge(Tick, readAheadStop);

            if (IsReadingIntoLoops)
            {
                _mayVoiceBeCutSinceNote |= DoesReadAheadKeyOff(readAheadStop);
            }
        }

        _hasCallSinceFetch = true;
        _hasCallPassFetched = false;
        _call = new CallFrame(address + length, passes, bodyAddress);
        return bodyAddress;
    }

    // An $E6 subloop marker, the start or the end, answering the address the walk goes on from.
    private int PassSubloopMarker(int address, int length)
    {
        _doesPeekReachNextByte = false;
        int argument = _reader.ByteAt(address + SubloopArgumentOffset);
        if (argument == SubloopStartArgument)
        {
            _subloop = new SubloopFrame(address + length, SubloopSeededPasses);
            _hasSubloopPassFetched = false;
            return address + length;
        }

        if (IsReadingIntoLoops)
        {
            var readAheadStop = ReadAheadInsideLoops(address);
            _mayVoiceBeCutSinceNote |= DoesReadAheadKeyOff(readAheadStop);
            AddLoopEdge(Tick, readAheadStop);
        }
        else if (!IsMeasuring && !_wasLastFetchInCall)
        {
            AddLoopEdge(Tick, ReadAheadOverLoops(address + length));
        }

        if (_subloop is { } subloop)
        {
            int passesLeft = subloop.PassesLeft == SubloopSeededPasses ? argument : subloop.PassesLeft - 1;
            if (passesLeft > 0)
            {
                _subloop = subloop with { PassesLeft = passesLeft };
                _hasSubloopPassFetched = false;
                return subloop.StartAddress;
            }

            _subloop = null;
        }

        return address + length;
    }

    // A duration byte, and the q byte behind it where there is one.
    private int ReadDuration(int address)
    {
        _durationTicks = _reader.ByteAt(address);
        address++;
        if (_reader.IsInsideSong(address) && _reader.ByteAt(address) is > 0 and < FirstNoteByte)
        {
            _quantization = _reader.ByteAt(address);
            address++;
        }

        return address;
    }

    // A note, a tie, a rest or a percussion note, answering the address after it.
    private int ReadNoteByte(int address, byte noteByte)
    {
        _doesPeekReachNextByte = true;
        _hasFetchedAny = true;
        _hasCallSinceFetch = false;
        bool doesOpenLoopPass = !_reader.DoesReadIntoLoops && ((_call is not null && !_hasCallPassFetched) || (_subloop is not null && !_hasSubloopPassFetched));

        _hasCallPassFetched = true;
        _hasSubloopPassFetched = true;
        _wasLastFetchInCall = false;
        if (noteByte == Tie && !IsMeasuring)
        {
            RecordCutTie(doesOpenLoopPass);
        }

        if (noteByte != Tie)
        {
            _openCutTieRunIndex = -1;
        }

        if (noteByte >= FirstPercussion)
        {
            ReadPercussion(noteByte);
        }
        else if (noteByte < Tie)
        {
            AddNote(noteByte - FirstNoteByte);
        }
        else if (noteByte == Tie)
        {
            ReadTie();
        }
        else
        {
            ReadRest();
        }

        return address + 1;
    }

    // Lists a loop's edge at a tick with what the read-ahead stops at there, which only a pass that keeps events does.
    private void AddLoopEdge(int tick, ReadAheadStop readAheadStop)
    {
        if (!IsMeasuring)
        {
            _loopEdges.Add(new(tick, readAheadStop));
        }
    }

    // A tie a loop's edge may have keyed the voice off in front of, listed for the loop-edge ties to decide, and the q of each later tie in its run.
    private void RecordCutTie(bool doesOpenLoopPass)
    {
        if (doesOpenLoopPass || _mayVoiceBeCutSinceNote)
        {
            _cutTies.Add(new MmlCutTie(Tick, _durationTicks, _wasRestFetchedSinceNote, 0, _quantization));
            _openCutTieRunIndex = _openNoteIndex < 0 && !_wasRestFetchedSinceNote ? _cutTies.Count - 1 : -1;
        }
        else if (_openCutTieRunIndex >= 0 && _cutTies[_openCutTieRunIndex].Quantization != _quantization)
        {
            _cutTies[_openCutTieRunIndex] = _cutTies[_openCutTieRunIndex] with { Quantization = null };
        }
    }

    // A percussion note: the drum's instrument change and a note at its own forced pitch.
    private void ReadPercussion(byte noteByte)
    {
        if (IsMeasuring)
        {
            AdvanceClock();
            return;
        }

        int drumInstrumentNumber = MmlBuiltins.FirstPercussionNumber + (noteByte - FirstPercussion);
        if (!MmlBuiltins.IsPercussion(drumInstrumentNumber))
        {
            throw MmlImportException.PercussionPastDrums(_channel + 1, noteByte);
        }

        AddEvent(new MmlInstrumentChange(Tick, drumInstrumentNumber, 0));
        AddNote(MmlBuiltins.EntryFor(drumInstrumentNumber).ForcedNoteValue);
    }

    // A tie byte, which lengthens the open note by a piece of its own, or only moves the clock where no note is open.
    private void ReadTie()
    {
        if (_openNoteIndex < 0)
        {
            if (!IsMeasuring)
            {
                _restTies.Add(Tick);
            }

            AdvanceClock();
            return;
        }

        var tiedNote = (MmlNote)_events[_openNoteIndex];
        _events[_openNoteIndex] = tiedNote with
        {
            LengthTicks = tiedNote.LengthTicks + _durationTicks,
            Pieces = tiedNote.Pieces.Add(_durationTicks),
            TieGates = tiedNote.TieGatesWithTie(_quantization),
        };

        AdvanceClock();
    }

    // A rest byte, which is no event: it closes the open note and moves the clock.
    private void ReadRest()
    {
        _openNoteIndex = -1;
        _wasRestFetchedSinceNote = true;
        if (IsReadingIntoLoops)
        {
            _mayVoiceBeCutSinceNote = false;
        }

        if (!IsMeasuring)
        {
            _restBytes.Add(Tick);
        }

        AdvanceClock();
    }

    // A note at a note value, which opens it for the ties behind it.
    private void AddNote(int noteValue)
    {
        _mayVoiceBeCutSinceNote = false;
        _wasRestFetchedSinceNote = false;
        if (IsMeasuring)
        {
            AdvanceClock();
            return;
        }

        _openNoteIndex = _events.Count;
        _events.Add(new MmlNote(Tick, noteValue, _durationTicks, _quantization, 0) { Pieces = [_durationTicks] });
        AdvanceClock();
    }

    // Moves the clock past the byte just fetched.
    private void AdvanceClock()
    {
        _lastByteStartTick = Tick;
        Tick += _durationTicks;
    }

    // Whether a read-ahead inside loops that finds this keys a sounding voice off.
    private bool DoesReadAheadKeyOff(ReadAheadStop readAheadStop)
    {
        return _openNoteIndex >= 0 && readAheadStop != ReadAheadStop.Tie;
    }

    // What the read-ahead with no hot patch stops at, set going at an address: it steps over every command, a call and a subloop marker included, and a $00 is a key-off.
    private ReadAheadStop ReadAheadOverLoops(int address)
    {
        while (_reader.IsInsideSong(address))
        {
            byte trackByte = _reader.ByteAt(address);
            if (trackByte == TrackEnd)
            {
                return ReadAheadStop.KeyOff;
            }

            if (StepReadAheadPast(trackByte, ref address) is { } readAheadStop)
            {
                return readAheadStop;
            }
        }

        return ReadAheadStop.KeyOff;
    }

    // What the read-ahead stops at under the hot patch that sends it inside loops, set going at an address while the track plays its call and subloop.
    private ReadAheadStop ReadAheadInsideLoops(int address)
    {
        bool hasEnteredCall = false;
        bool doesEnteredCallRepeat = false;
        bool hasExitedCall = false;
        bool hasEnteredSubloop = false;
        bool isSubloopEndActive = false;
        int returnAddress = 0;
        int bodyAddress = 0;
        int subloopStartAddress = 0;
        for (int step = 0; step < MostStepsInReadAhead; step++)
        {
            if (!_reader.IsInsideSong(address))
            {
                return ReadAheadStop.KeyOff;
            }

            byte trackByte = _reader.ByteAt(address);
            if (trackByte == TrackEnd)
            {
                if (hasEnteredCall)
                {
                    hasExitedCall = !doesEnteredCallRepeat;
                    address = hasExitedCall ? returnAddress : bodyAddress;
                    doesEnteredCallRepeat = false;
                    continue;
                }

                if (hasExitedCall || _call is not { } playingCall)
                {
                    return ReadAheadStop.KeyOff;
                }

                hasExitedCall = true;
                address = playingCall.PassesLeft == LastPass ? playingCall.ReturnAddress : playingCall.BodyAddress;
                continue;
            }

            if (trackByte == AmkCommands.Call)
            {
                hasEnteredCall = true;
                bodyAddress = _reader.WordAt(address + CallBodyAddressOffset);
                if (_reader.ByteAt(address + CallPassCountOffset) != LastPass)
                {
                    doesEnteredCallRepeat = true;
                }

                returnAddress = address + CallLength;
                address = bodyAddress;
                continue;
            }

            if (trackByte == AmkCommands.Subloop)
            {
                if (_reader.ByteAt(address + SubloopArgumentOffset) == SubloopStartArgument)
                {
                    hasEnteredSubloop = true;
                    address += SubloopMarkerLength;
                    subloopStartAddress = address;
                    continue;
                }

                if (isSubloopEndActive)
                {
                    isSubloopEndActive = false;
                    address += SubloopMarkerLength;
                    continue;
                }

                isSubloopEndActive = true;
                if (hasEnteredSubloop)
                {
                    address = subloopStartAddress;
                    continue;
                }

                if (_subloop is not { } playingSubloop)
                {
                    throw MmlImportException.ReadAheadIntoUnplayedSubloop(_channel + 1, address);
                }

                address = playingSubloop.PassesLeft == LastPass ? address + SubloopMarkerLength : playingSubloop.StartAddress;
                continue;
            }

            if (StepReadAheadPast(trackByte, ref address) is { } readAheadStop)
            {
                return readAheadStop;
            }
        }

        throw MmlImportException.TrackEndsOnEmptyLoop(_channel + 1);
    }

    // Moves a read-ahead past a byte that is neither the track's end nor a call or a subloop marker, or says what it stops at: a note byte, or a key-off
    // where the driver has no command for the byte.
    private ReadAheadStop? StepReadAheadPast(byte trackByte, ref int address)
    {
        if (trackByte < FirstNoteByte)
        {
            address++;
            return null;
        }

        if (trackByte < AmkCommands.First)
        {
            return ReadAheadStopOfNoteByte(trackByte);
        }

        int length = _reader.CommandLengthAt(address);
        if (length == 0)
        {
            return ReadAheadStop.KeyOff;
        }

        address += length;
        return null;
    }

    // What a read-ahead finds where it stops on a note, a tie, a rest or a percussion byte.
    private static ReadAheadStop ReadAheadStopOfNoteByte(byte noteByte)
    {
        if (noteByte == Tie)
        {
            return ReadAheadStop.Tie;
        }

        return noteByte > Tie && noteByte < FirstPercussion ? ReadAheadStop.Rest : ReadAheadStop.Note;
    }

    // The subroutine call being played: where it returns to, how many passes it has left, and where its body starts.
    private readonly record struct CallFrame(int ReturnAddress, int PassesLeft, int BodyAddress);

    // The subloop being played: where its body starts, and how many passes it has left, seeded until its end marker is first reached.
    private readonly record struct SubloopFrame(int StartAddress, int PassesLeft);
}
