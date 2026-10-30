using SpcStudio.Audio;
using SpcStudio.Export;
using SpcStudio.Model;

namespace SpcStudio.Import;

// The importer's instruments and samples: #instruments, #samples, the built-ins, and the instruments made for noise and for sample loads.
public static partial class MmlImporter
{
    // The noise instrument a note under an n plays: the five bytes in force on the noise generator, created on first use and shared by every note needing the same five.
    private static int NoiseInstrumentId(Song song, Dictionary<InstrumentBytes, int> made, int instrument)
    {
        var instrumentInForce = song.InstrumentById(instrument);
        if (instrumentInForce is { IsNoise: true })
        {
            return instrument;
        }

        var bytes = BytesInForce(instrumentInForce);
        if (made.TryGetValue(bytes, out int alreadyMadeId))
        {
            return alreadyMadeId;
        }

        var noise = NewNoiseInstrument(song, bytes);
        made[bytes] = noise.Id;
        return noise.Id;
    }

    // The instrument a $F3 leaves the channel playing: the one in force with its sample and its tuning's integer half swapped, the song's own where one
    // already holds those six, and otherwise created on first use and shared by every load needing the same six.
    private static int SampleLoadInstrumentId(Song song, Dictionary<(int, InstrumentBytes), int> made, int instrument, Sample sample, byte tuningHigh)
    {
        var bytes = BytesInForce(song.InstrumentById(instrument)) with { TuningHigh = tuningHigh };
        var key = (sample.Id, bytes);
        if (made.TryGetValue(key, out int alreadyMadeId))
        {
            return alreadyMadeId;
        }

        // The song's own instrument already holding the sample and the five bytes, or a new one where none does.
        var loadedInstrument = song.Instruments.FirstOrDefault(candidate => candidate.SampleId == sample.Id && candidate.Bytes == bytes)
            ?? NewSampledInstrument(song, sample.NameStem, sample, bytes);
        made[key] = loadedInstrument.Id;
        return loadedInstrument.Id;
    }

    // The song's samples and instruments: every sample the #samples block lists, an instrument for each #instruments entry an @ can reach and each
    // built-in the channels play, and the sample each $F3 names.
    private static BoundLibrary BindLibrary(
        Song song, MmlFile file, Func<string, byte[]?> readSample, Func<string, IReadOnlyList<string>?>? readGroup, Action<string, int, int>? progress = null,
        CancellationToken cancel = default)
    {
        var binder = new LibraryBinder(song, file, readSample, readGroup, progress, cancel);
        binder.LoadListedSamples();
        var customInstruments = binder.CustomInstruments();
        var sampleLoads = binder.SampleLoads();
        var builtinInstrumentIds = binder.BuiltinInstruments();
        return new BoundLibrary(customInstruments, builtinInstrumentIds, sampleLoads, binder.SilencedInstruments, binder.SynthesizedSampleIds);
    }

    // The line of the first note a channel plays before it names an instrument, or null where it names one first, reaches the noise generator first, or plays nothing at all.
    private static int? FirstNoteLineBeforeInstrument(MmlChannel channel)
    {
        foreach (var channelEvent in channel.Events)
        {
            if (channelEvent is MmlInstrumentChange or MmlNoise)
            {
                return null;
            }

            if (channelEvent is MmlNote)
            {
                return channelEvent.Line;
            }
        }

        return null;
    }

    // The sample group a file's re-export names: the first group its #samples block writes, the default group where the block has no entries at all, and
    // none where it lists samples of its own and no group.
    private static string? SampleGroupOf(MmlFile file)
    {
        if (file.Samples.Count == 0)
        {
            return SampleGroups.DefaultGroup;
        }

        return file.Samples.FirstOrDefault(entry => entry.IsGroup)?.Name;
    }

    // The instrument an @ selects: a built-in by its number, or one of the song's own, numbered by position in the #instruments block.
    private static int SelectedInstrumentId(BoundLibrary bound, MmlInstrumentChange change)
    {
        if (MmlBuiltins.IsBuiltin(change.Number))
        {
            return bound.BuiltinInstrumentIds[change.Number];
        }

        int index = change.Number - MmlExporter.FirstCustomInstrument;
        if (index < 0 || index >= bound.CustomInstruments.Count)
        {
            throw MmlImportException.InstrumentPastBlock(change.Number, bound.CustomInstruments.Count, change.Line);
        }

        return bound.CustomInstruments[index]!.Id;
    }

    // The five bytes of the instrument in force, which before a channel's first @ are @0's own.
    private static InstrumentBytes BytesInForce(Instrument? instrumentInForce)
    {
        if (instrumentInForce is null)
        {
            return MmlBuiltins.EntryFor(MmlBuiltins.DefaultNumber).Bytes;
        }

        return instrumentInForce.Bytes;
    }

    // Writes the five bytes onto an instrument and answers it.
    private static Instrument WithInstrumentBytes(Instrument instrument, InstrumentBytes bytes)
    {
        instrument.Bytes = bytes;
        return instrument;
    }

    // A new instrument of the song's on a sample, under a name made from a stem, holding the five bytes.
    private static Instrument NewSampledInstrument(Song song, string nameStem, Sample sample, InstrumentBytes bytes)
    {
        return WithInstrumentBytes(song.CreateInstrument(song.FreeInstrumentName(nameStem), sample), bytes);
    }

    // A new instrument of the song's on the noise generator, holding the five bytes.
    private static Instrument NewNoiseInstrument(Song song, InstrumentBytes bytes)
    {
        return WithInstrumentBytes(song.CreateNamedNoiseInstrument(), bytes);
    }

    // Binds a file's samples and instruments into the song, holding what has been read so that each file is read once and each copy made once.
    private sealed class LibraryBinder
    {
        private const string SamplesLoadedStage = "samples loaded";
        private const string SampleLoadPlayer = "a sample load command";

        private readonly Song _song;
        private readonly MmlFile _file;
        private readonly Func<string, byte[]?> _readSample;
        private readonly Func<string, IReadOnlyList<string>?>? _readGroup;
        private readonly Action<string, int, int>? _progress;
        private readonly CancellationToken _cancel;
        private readonly Dictionary<string, Sample> _samplesByPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _placeholderPaths = new(StringComparer.OrdinalIgnoreCase);
        private List<string>? _sampleTablePaths;
        private int _sampleCountShown;
        private int _loadedSampleCount;

        // What a blank position plays, by the id of the sample its directory entry points at.
        private readonly Dictionary<int, Sample> _samplesPlayedFromStart = [];

        // A binder over a file that has read nothing yet.
        public LibraryBinder(
            Song song, MmlFile file, Func<string, byte[]?> readSample, Func<string, IReadOnlyList<string>?>? readGroup, Action<string, int, int>? progress,
            CancellationToken cancel)
        {
            _song = song;
            _file = file;
            _readSample = readSample;
            _readGroup = readGroup;
            _progress = progress;
            _cancel = cancel;
        }

        // The ids of the instruments built over a blank position whose own envelope can never open.
        public List<int> SilencedInstruments { get; } = [];

        // The ids of the samples added to play a blank position, which are this reader's own rather than files the song listed.
        public HashSet<int> SynthesizedSampleIds { get; } = [];

        // Loads every sample the #samples block lists by name, counting them on the progress bar.
        public void LoadListedSamples()
        {
            _sampleCountShown = _file.Samples.Count(entry => !entry.IsGroup);
            _progress?.Invoke(SamplesLoadedStage, 0, _sampleCountShown);
            foreach (var entry in _file.Samples.Where(entry => !entry.IsGroup))
            {
                ImportSample(Path.Combine(_file.SamplePath, entry.Name), entry.Name, entry.Line);
            }
        }

        // One instrument per #instruments entry in the block's own order, null where an entry names a blank position no @ selects.
        public List<Instrument?> CustomInstruments()
        {
            List<Instrument?> instruments = [];
            foreach (var entry in _file.Instruments)
            {
                instruments.Add(CustomInstrument(entry, MmlExporter.FirstCustomInstrument + instruments.Count));
            }

            return instruments;
        }

        // The sample each $F3 in the file names, keyed the way the command carries it, refused where it names a position the block does not load.
        public Dictionary<(int Sample, string? FileName), Sample> SampleLoads()
        {
            var samplesByLoad = new Dictionary<(int Sample, string? FileName), Sample>();
            foreach (var load in _file.Channels.SelectMany(channel => channel.Events).OfType<MmlSampleLoad>())
            {
                var key = (load.SampleNumber, load.FileName);
                if (samplesByLoad.ContainsKey(key))
                {
                    continue;
                }

                int sampleTablePosition = load.FileName is { } fileName ? FirstPositionOf(SampleTablePaths(), Path.Combine(_file.SamplePath, fileName)) : load.SampleNumber;
                if (sampleTablePosition < 0)
                {
                    throw MmlImportException.SampleLoadFileNotLoaded(load.FileName, load.Line);
                }

                samplesByLoad[key] = PlayedSample(sampleTablePosition, SampleLoadPlayer, load.Line);
            }

            return samplesByLoad;
        }

        // An instrument for each built-in the channels play, keyed by its number, built in the order the channels first reach for them.
        public Dictionary<int, int> BuiltinInstruments()
        {
            var instrumentIds = new Dictionary<int, int>();
            foreach (var (number, line) in BuiltinsInPlayOrder(_file))
            {
                if (instrumentIds.ContainsKey(number))
                {
                    continue;
                }

                var entry = MmlBuiltins.EntryFor(number);
                var sample = PlayedSample(entry.SampleNumber, PlayedByInstrument(number), line);
                instrumentIds[number] = NewSampledInstrument(_song, $"SMW @{number}", sample, entry.Bytes).Id;
            }

            return instrumentIds;
        }

        // The built-in @ numbers each channel plays with the line each is reached on, channel by channel in stream order, @0 first where a channel plays a note before naming an instrument.
        private static List<(int Number, int Line)> BuiltinsInPlayOrder(MmlFile file)
        {
            List<(int Number, int Line)> used = [];
            foreach (var channel in file.Channels)
            {
                if (FirstNoteLineBeforeInstrument(channel) is { } line)
                {
                    used.Add((MmlBuiltins.DefaultNumber, line));
                }

                used.AddRange(channel.Events.OfType<MmlInstrumentChange>().Where(change => MmlBuiltins.IsBuiltin(change.Number)).Select(change => (change.Number, change.Line)));
            }

            return used;
        }

        // The song's samples in the order AddmusicK numbers them, as paths under the install's samples folder, one position per #samples entry and a group expanding to its own files where it stands.
        private static List<string> SamplePathsInNumberOrder(MmlFile file, Func<string, IReadOnlyList<string>?>? readGroup)
        {
            var sampleEntries = file.Samples.Count > 0 ? file.Samples : [new MmlSampleEntry(SampleGroups.DefaultGroup, true, 0)];
            List<string> paths = [];
            foreach (var entry in sampleEntries)
            {
                if (!entry.IsGroup)
                {
                    paths.Add(Path.Combine(file.SamplePath, entry.Name));
                    continue;
                }

                var groupPaths = readGroup?.Invoke(entry.Name)
                    ?? throw MmlImportException.SampleGroupNotInInstall(entry.Name, entry.Line);
                paths.AddRange(groupPaths);
            }

            return paths;
        }

        // The first position a path holds in the sample table, or -1 where it holds none.
        private static int FirstPositionOf(List<string> paths, string path)
        {
            return paths.FindIndex(listedPath => string.Equals(listedPath, path, StringComparison.OrdinalIgnoreCase));
        }

        // Whether a channel's @ names a number anywhere in it.
        private static bool DoesChannelSelect(MmlChannel channel, int number)
        {
            return channel.Events.OfType<MmlInstrumentChange>().Any(change => change.Number == number);
        }

        // Whether anything in a channel writes the envelope registers, or builds a fresh instrument out of the one in force, while an @ number is in force.
        private static bool DoesReachEnvelopeUnder(MmlChannel channel, int number)
        {
            int? numberInForce = null;
            foreach (var channelEvent in channel.Events)
            {
                if (channelEvent is MmlInstrumentChange change)
                {
                    numberInForce = change.Number;
                }

                if (numberInForce != number)
                {
                    continue;
                }

                if (channelEvent is MmlEnvelope or MmlEnvelopeGain or MmlEnvelopeRestore or MmlSampleLoad or MmlNoise)
                {
                    return true;
                }

                if (channelEvent is MmlDspWrite write && SnesDspRegister.Classify(write.Register) == DspRegisterKind.Envelope)
                {
                    return true;
                }
            }

            return false;
        }

        // Whether an envelope can never open from the zero a key-on starts it at: a GAIN of direct zero or either decrease mode, never ADSR.
        private static bool IsEnvelopeNeverOpen(EnvelopeState envelope)
        {
            return !envelope.UsesAdsr
                && (envelope.GainMode is GainMode.LinearDecrease or GainMode.ExponentialDecrease || envelope.GainMode is GainMode.Direct && envelope.GainValue == 0);
        }

        // The instrument one #instruments entry builds under the @ number it is declared as, or null where it names a blank position no @ selects.
        private Instrument? CustomInstrument(MmlInstrument entry, int declaredNumber)
        {
            if (entry.NoiseClock is not null)
            {
                return NewNoiseInstrument(_song, entry.Bytes);
            }

            if (SampleOfEntry(entry, declaredNumber) is not { } entrySample)
            {
                return null;
            }

            var instrument = NewSampledInstrument(_song, entrySample.Sample.NameStem, entrySample.Sample, entry.Bytes);
            if (entrySample.IsOverBlank && IsEnvelopeNeverOpen(instrument.Envelope) && IsOwnEnvelopeFinal(declaredNumber))
            {
                SilencedInstruments.Add(instrument.Id);
            }

            return instrument;
        }

        // The sample an entry plays, or null where it names a blank position no @ selects.
        private EntrySample? SampleOfEntry(MmlInstrument entry, int declaredNumber)
        {
            if (entry.SampleCopiedFrom is { } builtinNumber)
            {
                return CopiedSampleOfEntry(entry, builtinNumber, declaredNumber);
            }

            return NamedSampleOfEntry(entry, declaredNumber);
        }

        // The sample an entry copying a built-in's plays: the built-in's position in the #samples block, or what a blank one there points at.
        private EntrySample? CopiedSampleOfEntry(MmlInstrument entry, int builtinNumber, int declaredNumber)
        {
            int position = MmlBuiltins.SampleNumberOf(builtinNumber);
            if (ListedSampleAt(position, PlayedByInstrument(builtinNumber), entry.Line) is { } copied)
            {
                return new EntrySample(copied, false);
            }

            if (!IsSelectedByAnyChannel(declaredNumber))
            {
                return null;
            }

            return new EntrySample(PlayedSample(position, PlayedByInstrument(declaredNumber), entry.Line), true);
        }

        // The sample an entry naming a file plays: the file, or what the blank position it names points at.
        private EntrySample? NamedSampleOfEntry(MmlInstrument entry, int declaredNumber)
        {
            string path = Path.Combine(_file.SamplePath, entry.FileName!);
            if (!_placeholderPaths.Contains(path))
            {
                var namedSample = _song.SampleNamed(entry.FileName!) ?? throw MmlImportException.InstrumentSampleNotListed(entry.FileName, entry.Line);
                return new EntrySample(namedSample, false);
            }

            if (!IsSelectedByAnyChannel(declaredNumber))
            {
                return null;
            }

            return new EntrySample(PlayedSample(FirstPositionOf(SampleTablePaths(), path), PlayedByInstrument(declaredNumber), entry.Line), true);
        }

        // The sample table, counted out the first time something names a sample by number.
        private List<string> SampleTablePaths()
        {
            return _sampleTablePaths ??= SamplePathsInNumberOrder(_file, _readGroup);
        }

        // The sample a #samples path loads, read once however often it is listed or reached, or null where the file is a zero-byte placeholder.
        private Sample? ImportSample(string path, string name, int line)
        {
            if (_samplesByPath.TryGetValue(path, out var alreadyLoaded))
            {
                return alreadyLoaded;
            }

            if (_placeholderPaths.Contains(path))
            {
                return null;
            }

            byte[] sampleBytes = _readSample(path) ?? throw MmlImportException.SampleUnreadable(path, line);
            if (sampleBytes.Length == 0)
            {
                _placeholderPaths.Add(path);
                return null;
            }

            _cancel.ThrowIfCancellationRequested();
            try
            {
                var stored = _song.AddSampleUnderFreeName(Sample.FromBytes(name, sampleBytes));
                _samplesByPath[path] = stored;
                _loadedSampleCount++;
                _sampleCountShown = Math.Max(_sampleCountShown, _loadedSampleCount);
                _progress?.Invoke(SamplesLoadedStage, _loadedSampleCount, _sampleCountShown);
                return stored;
            }
            catch (Exception exception) when (exception is not MmlImportException)
            {
                throw MmlImportException.NotBrrSample(path, line);
            }
        }

        // The sample at a position in the sample table, or null where the position holds a placeholder, refused past the table's end.
        private Sample? ListedSampleAt(int position, string playedBy, int line)
        {
            var paths = SampleTablePaths();
            if (position < 0 || position >= paths.Count)
            {
                throw MmlImportException.SamplePastTable(playedBy, position, paths.Count, line);
            }

            string path = paths[position];
            return ImportSample(path, Path.GetFileName(path), line);
        }

        // An instrument as the player of a sample, as a refusal names it.
        private static string PlayedByInstrument(int instrumentNumber)
        {
            return $"@{instrumentNumber}";
        }

        // The sample a position plays, blank or not: what the block loads there, or what a blank one's directory entry points at.
        private Sample PlayedSample(int position, string playedBy, int line)
        {
            return ListedSampleAt(position, playedBy, line)
                ?? SampleFollowingBlank(position, line)
                ?? throw MmlImportException.SamplePlaceholderOnly(playedBy, position, line);
        }

        // What a blank position plays: the first sample after it that writes data of its own, from its first block, or null where the block loads nothing after it.
        private Sample? SampleFollowingBlank(int position, int line)
        {
            var paths = SampleTablePaths();
            for (int laterPosition = FirstPositionOf(paths, paths[position]) + 1; laterPosition < paths.Count; laterPosition++)
            {
                if (FirstPositionOf(paths, paths[laterPosition]) < laterPosition)
                {
                    continue;
                }

                if (ImportSample(paths[laterPosition], Path.GetFileName(paths[laterPosition]), line) is { } nextSample)
                {
                    return PlayedFromStart(nextSample);
                }
            }

            return null;
        }

        // A sample played from its first block on every pass: itself where its loop already starts there, and otherwise a copy of it looping from there, made once.
        private Sample PlayedFromStart(Sample sample)
        {
            if (_samplesPlayedFromStart.TryGetValue(sample.Id, out var alreadyMade))
            {
                return alreadyMade;
            }

            var played = sample.Decoded.LoopStartPoint == 0
                ? sample
                : _song.AddSampleUnderFreeName(sample.WithLoopBlock(0));
            if (played.Id != sample.Id)
            {
                SynthesizedSampleIds.Add(played.Id);
            }

            _samplesPlayedFromStart[sample.Id] = played;
            return played;
        }

        // Whether any channel's @ names an entry.
        private bool IsSelectedByAnyChannel(int number)
        {
            return _file.Channels.Any(channel => DoesChannelSelect(channel, number));
        }

        // Whether an entry's own three envelope bytes are the last word everywhere it is in force.
        private bool IsOwnEnvelopeFinal(int number)
        {
            foreach (var channel in _file.Channels)
            {
                if (!DoesChannelSelect(channel, number))
                {
                    continue;
                }

                if (channel.Events.OfType<MmlLegato>().Any() || DoesReachEnvelopeUnder(channel, number))
                {
                    return false;
                }
            }

            return true;
        }
    }

    // The sample an #instruments entry plays, and whether it was resolved over a blank position.
    private readonly record struct EntrySample(Sample Sample, bool IsOverBlank);

    // The instruments an @ can select and the samples a $F3 can load, with what the import takes out again at its end.
    private sealed record BoundLibrary(
        List<Instrument?> CustomInstruments,
        Dictionary<int, int> BuiltinInstrumentIds,
        Dictionary<(int Sample, string? FileName), Sample> SamplesByLoad,
        List<int> SilencedInstruments,
        HashSet<int> SynthesizedSampleIds);
}
