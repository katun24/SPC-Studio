namespace SpcStudio.Model;

// Everything an edit can change about a song, captured whole - every lane, every channel's notes, the markers, the song's settings and its
// instruments - and put back into the song on an undo or a redo.
public sealed class SongSnapshot : IUndoState<SongSnapshot>
{
    private readonly int _tempo;
    private readonly int _snapTicks;
    private readonly TimeSignature _timeSignature;
    private readonly LaneDivider<int>[] _tempoChanges;
    private readonly int _masterVolume;
    private readonly LaneDivider<int>[] _volumeChanges;
    private readonly StaccatoUnit[] _staccatoUnits;
    private readonly int _endTick;
    private readonly int _loopTick;
    private readonly bool _doesLoop;
    private readonly bool _isTempoImmune;
    private readonly bool _usesSmwVelocities;
    private readonly bool _isEndEnabled;
    private readonly SpcTags _spcTags;
    private readonly string? _sampleGroup;
    private readonly Note[][] _notes;
    private readonly bool[] _isMuted;
    private readonly bool[] _isSoloed;
    private readonly int[] _channelVolumes;
    private readonly LaneDivider<int>[][] _channelVolumeChanges;
    private readonly int[] _pans;
    private readonly SnesSurround[] _surround;
    private readonly PanChange[][] _panChanges;
    private readonly int[] _amplify;
    private readonly LaneValueChange[][] _amplifyChanges;
    private readonly EchoRun[] _echoRuns;
    private readonly int[] _echoBases;
    private readonly LaneDivider<int>[][] _echoChanges;
    private readonly bool[] _isChannelEchoOn;
    private readonly LaneValueChange[][] _channelEchoChanges;
    private readonly long[] _filters;
    private readonly Instrument[] _instruments;

    private SongSnapshot(
        int tempo,
        int snapTicks,
        TimeSignature timeSignature,
        LaneDivider<int>[] tempoChanges,
        int masterVolume,
        LaneDivider<int>[] volumeChanges,
        StaccatoUnit[] staccatoUnits,
        int endTick,
        int loopTick,
        bool doesLoop,
        bool isTempoImmune,
        bool usesSmwVelocities,
        bool isEndEnabled,
        SpcTags spcTags,
        string? sampleGroup,
        Note[][] notes,
        bool[] isMuted,
        bool[] isSoloed,
        int[] channelVolumes,
        LaneDivider<int>[][] channelVolumeChanges,
        int[] pans,
        SnesSurround[] surround,
        PanChange[][] panChanges,
        int[] amplify,
        LaneValueChange[][] amplifyChanges,
        EchoRun[] echoRuns,
        int[] echoBases,
        LaneDivider<int>[][] echoChanges,
        bool[] isChannelEchoOn,
        LaneValueChange[][] channelEchoChanges,
        long[] filters,
        Instrument[] instruments)
    {
        _tempo = tempo;
        _snapTicks = snapTicks;
        _timeSignature = timeSignature;
        _tempoChanges = tempoChanges;
        _masterVolume = masterVolume;
        _volumeChanges = volumeChanges;
        _staccatoUnits = staccatoUnits;
        _endTick = endTick;
        _loopTick = loopTick;
        _doesLoop = doesLoop;
        _isTempoImmune = isTempoImmune;
        _usesSmwVelocities = usesSmwVelocities;
        _isEndEnabled = isEndEnabled;
        _spcTags = spcTags;
        _sampleGroup = sampleGroup;
        _notes = notes;
        _isMuted = isMuted;
        _isSoloed = isSoloed;
        _channelVolumes = channelVolumes;
        _channelVolumeChanges = channelVolumeChanges;
        _pans = pans;
        _surround = surround;
        _panChanges = panChanges;
        _amplify = amplify;
        _amplifyChanges = amplifyChanges;
        _echoRuns = echoRuns;
        _echoBases = echoBases;
        _echoChanges = echoChanges;
        _isChannelEchoOn = isChannelEchoOn;
        _channelEchoChanges = channelEchoChanges;
        _filters = filters;
        _instruments = instruments;
    }

    // Copies the song's whole editable state into a snapshot.
    public static SongSnapshot Capture(Song song)
    {
        var channelIndices = Enumerable.Range(0, song.ChannelCount).ToArray();
        return new(
            song.BaseTempo,
            song.SnapTicks,
            song.TimeSignature,
            [.. song.TempoChanges],
            song.BaseMasterVolume,
            [.. song.VolumeChanges],
            [.. song.StaccatoUnits],
            song.EndTick,
            song.LoopTick,
            song.IsLooping,
            song.HasTempoImmunity,
            song.UsesSmwVelocities,
            song.IsEndEnabled,
            song.SpcTags,
            song.SampleGroup,
            [.. channelIndices.Select(channel => song.Notes(channel).ToArray())],
            [.. channelIndices.Select(song.Muting.IsMuted)],
            [.. channelIndices.Select(song.Muting.IsSoloed)],
            [.. channelIndices.Select(song.BaseChannelVolume)],
            [.. channelIndices.Select(channel => song.ChannelVolumeChanges(channel).ToArray())],
            [.. channelIndices.Select(song.BasePan)],
            [.. channelIndices.Select(song.BaseSurround)],
            [.. channelIndices.Select(channel => song.PanChanges(channel).ToArray())],
            [.. channelIndices.Select(song.BaseAmplify)],
            [.. channelIndices.Select(channel => song.AmplifyChanges(channel).ToArray())],
            [.. song.EchoRuns],
            [.. SnesEcho.VolumeSettings.Select(song.BaseEchoVolume)],
            [.. SnesEcho.VolumeSettings.Select(setting => song.EchoChanges(setting).ToArray())],
            [.. channelIndices.Select(song.IsBaseChannelEchoOn)],
            [.. channelIndices.Select(channel => song.ChannelEchoChanges(channel).ToArray())],
            [.. song.FilterTable],
            [.. song.Instruments.Select(instrument => instrument.Clone())]);
    }

    // Puts this state back into the live song, copying the instruments on the way out as well as on the way in.
    public void Restore(Song song)
    {
        song.SetChannelCount(_notes.Length);
        song.BaseTempo = _tempo;
        song.SnapTicks = _snapTicks;
        song.TimeSignature = _timeSignature;
        song.SetTempoChanges(_tempoChanges);
        song.BaseMasterVolume = _masterVolume;
        song.SetVolumeChanges(_volumeChanges);
        song.SetStaccatoUnits(_staccatoUnits);
        song.SetMarkers(_endTick, _loopTick);
        song.IsLooping = _doesLoop;
        song.HasTempoImmunity = _isTempoImmune;
        song.UsesSmwVelocities = _usesSmwVelocities;
        song.IsEndEnabled = _isEndEnabled;
        song.SpcTags = _spcTags;
        song.SampleGroup = _sampleGroup;
        song.Muting.SetAll(_isMuted, _isSoloed);
        song.SetChannelVolumeState(_channelVolumes, _channelVolumeChanges);
        song.SetPanState(_pans, _surround, _panChanges);
        song.SetAmplifyState(_amplify, _amplifyChanges);
        song.SetEchoState(_echoBases, _echoChanges, _isChannelEchoOn, _channelEchoChanges, _filters);
        song.SetEchoRuns(_echoRuns);
        song.SetInstruments([.. _instruments.Select(instrument => instrument.Clone())]);
        for (int channel = 0; channel < _notes.Length; channel++)
        {
            song.SetChannelNotes(channel, _notes[channel]);
        }
    }

    // The lowest channel of the other snapshot whose notes this one differs on, a channel this one lacks holding none, or null when every one matches.
    public int? FirstChannelWithDifferentNotes(SongSnapshot other)
    {
        for (int channel = 0; channel < other._notes.Length; channel++)
        {
            Note[] ownNotesOnChannel = channel < _notes.Length ? _notes[channel] : [];
            if (!ownNotesOnChannel.SequenceEqual(other._notes[channel]))
            {
                return channel;
            }
        }

        return null;
    }

    // Whether the two snapshots hold the same song in every respect they capture.
    public bool HoldsSameStateAs(SongSnapshot other)
    {
        return _tempo == other._tempo
            && _snapTicks == other._snapTicks
            && _timeSignature == other._timeSignature
            && _tempoChanges.SequenceEqual(other._tempoChanges)
            && _masterVolume == other._masterVolume
            && _volumeChanges.SequenceEqual(other._volumeChanges)
            && _staccatoUnits.SequenceEqual(other._staccatoUnits)
            && _endTick == other._endTick
            && _loopTick == other._loopTick
            && _doesLoop == other._doesLoop
            && _isTempoImmune == other._isTempoImmune
            && _usesSmwVelocities == other._usesSmwVelocities
            && _isEndEnabled == other._isEndEnabled
            && _spcTags == other._spcTags
            && _sampleGroup == other._sampleGroup
            && _notes.Length == other._notes.Length
            && _isMuted.SequenceEqual(other._isMuted)
            && _isSoloed.SequenceEqual(other._isSoloed)
            && _channelVolumes.SequenceEqual(other._channelVolumes)
            && AreEqualPerChannel(_channelVolumeChanges, other._channelVolumeChanges)
            && _pans.SequenceEqual(other._pans)
            && AreEqualPerChannel(_panChanges, other._panChanges)
            && _amplify.SequenceEqual(other._amplify)
            && AreEqualPerChannel(_amplifyChanges, other._amplifyChanges)
            && _echoRuns.SequenceEqual(other._echoRuns)
            && _echoBases.SequenceEqual(other._echoBases)
            && AreEqualPerChannel(_echoChanges, other._echoChanges)
            && _isChannelEchoOn.SequenceEqual(other._isChannelEchoOn)
            && AreEqualPerChannel(_channelEchoChanges, other._channelEchoChanges)
            && _filters.SequenceEqual(other._filters)
            && _instruments.Length == other._instruments.Length
            && _instruments.Zip(other._instruments).All(pair => pair.First.HasSameValuesAs(pair.Second))
            && FirstChannelWithDifferentNotes(other) is null;
    }

    // Whether two arrays of lists hold the same lists in the same order.
    private static bool AreEqualPerChannel<TItem>(TItem[][] first, TItem[][] second)
    {
        return first.Zip(second).All(pair => pair.First.SequenceEqual(pair.Second));
    }
}
