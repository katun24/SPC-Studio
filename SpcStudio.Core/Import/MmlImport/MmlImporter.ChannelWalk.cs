using SpcStudio.Model;

namespace SpcStudio.Import;

// The importer's walk through one channel's events.
public static partial class MmlImporter
{
    // One channel's stream walked in order, its notes placed on the song and every other command gathered for the steps after the walk.
    private sealed class ChannelWalk
    {
        private readonly Song _song;
        private readonly MmlFile _file;
        private readonly MmlChannel _channel;
        private readonly BoundLibrary _bound;
        private readonly GlobalTransposes _globalTransposes;
        private readonly Lazy<Func<int, int>> _waitTimeAt;
        private readonly WalkedCommands _walked;
        private readonly Action _countEventRead;
        private readonly List<(int Tick, int Value)> _transposeChanges = [];
        private readonly Lazy<bool[]> _isBoundIntoByLegato;
        private int _instrument;
        private Note? _previous;
        private int _order;
        private int _transpose;
        private MmlPitchEnvelope? _pitchEnvelope;
        private int _transposeAtLoop;
        private bool _hasTransposedSinceLoop;
        private int? _firstNoteTickSinceLoop;
        private int _noteIndex = -1;
        private EnvelopeState _registers = MmlBuiltins.DefaultEnvelope;
        private EnvelopeState _backupTable = MmlBuiltins.DefaultEnvelope;
        private int? _noiseClock;
        private int _position = -1;

        // A walk at the channel's start, on the instrument the song load leaves there.
        public ChannelWalk(
            Song song, MmlFile file, MmlChannel channel, BoundLibrary bound, GlobalTransposes globalTransposes, Lazy<Func<int, int>> waitTimeAt, WalkedCommands walked,
            Action countEventRead)
        {
            _song = song;
            _file = file;
            _channel = channel;
            _bound = bound;
            _globalTransposes = globalTransposes;
            _waitTimeAt = waitTimeAt;
            _walked = walked;
            _countEventRead = countEventRead;
            _isBoundIntoByLegato = new Lazy<bool[]>(() => LegatoBoundNotes(channel));
            _instrument = FirstNoteLineBeforeInstrument(channel) is not null ? bound.BuiltinInstrumentIds[MmlBuiltins.DefaultNumber] : -1;
        }

        // The number of the channel walked.
        private int Number
        {
            get
            {
                return _channel.Number;
            }
        }

        // Reads every event of the channel, then where it ends and what its transposes did across the loop point.
        public void Run()
        {
            foreach (var channelEvent in _channel.Events)
            {
                _position++;
                _countEventRead();
                ReadEvent(channelEvent);
            }

            _walked.EndTick = _walked.EndTick is { } endTick ? Math.Min(endTick, _channel.EndTick) : _channel.EndTick;
            if (_transposeChanges.Count > 0)
            {
                _walked.TransposesAcrossLoop.Add((Number, _transposeAtLoop, _firstNoteTickSinceLoop, _transposeChanges));
            }
        }

        // Reads one event of the channel.
        private void ReadEvent(MmlEvent channelEvent)
        {
            switch (channelEvent)
            {
                case MmlInstrumentChange change:
                    ReadInstrumentChange(change);
                    break;

                case MmlSampleLoad load:
                    ReadSampleLoad(load);
                    break;

                case MmlEnvelope command:
                    PushBackupTable(command.Tick, SnesEnvelopeOverride.WithCommandApplied(_backupTable, command.FirstArgument, command.SecondArgument),
                        EnvelopeWrite.Override, command.Line);
                    break;

                case MmlEnvelopeGain command:
                    PushBackupTable(command.Tick, _backupTable.With(EnvelopeSetting.Gain, command.Gain).WithUsesAdsr(false), EnvelopeWrite.Gain, command.Line);
                    break;

                case MmlEnvelopeRestore command:
                    PushBackupTable(command.Tick, InstrumentEnvelopeRestored(), EnvelopeWrite.Restore, command.Line);
                    break;

                case MmlKeyOnRestore command:
                    ReadKeyOnRestore(command);
                    break;

                case MmlDspWrite command when SnesDspRegister.Classify(command.Register) == DspRegisterKind.Envelope:
                    ReadEnvelopeRegisterWrite(command);
                    break;

                case MmlDspWrite command:
                    _walked.ChipRegisterWrites.Add((command, Number, _order++));
                    break;

                case MmlRemoteGain command:
                    WriteRegistersOnly(command.Tick, _registers with { Adsr1 = MmlRemoteGain.Adsr1, Gain = command.Gain }, EnvelopeWrite.RemoteGain, command.Line);
                    break;

                case MmlNoise command:
                    ReadNoise(command);
                    break;

                case MmlNote note:
                    ReadNote(note);
                    break;

                case MmlCommand command:
                    ReadLetteredCommand(command);
                    break;

                case MmlEchoCommand command:
                    _walked.EchoCommands.Add((command, Number, _order++));
                    break;

                case MmlEchoToggle toggle:
                    _walked.EchoToggles.Add((toggle.Tick, Number, _order++, toggle.Line));
                    break;

                case MmlEchoFade fade:
                    _walked.EchoFades.Add((fade, Number, _order++));
                    break;

                case MmlFirFilter filter:
                    _walked.FirFilters.Add((filter, Number, _order++));
                    break;

                case MmlEchoDelay echoDelay:
                    _walked.EchoDelays.Add((echoDelay, Number, _order++));
                    break;

                case MmlVibrato vibrato:
                    _walked.Vibratos[Number].Add(vibrato);
                    break;

                case MmlVibratoFade fade:
                    _walked.Vibratos[Number].Add(fade);
                    break;

                case MmlTremolo tremolo:
                    _walked.Tremolos[Number].Add(tremolo);
                    break;

                case MmlStaccato toggle:
                    _walked.StaccatoToggles.Add(toggle);
                    break;

                case MmlTempoImmunity:
                    _walked.HasTempoImmunity = true;
                    break;

                case MmlVelocityTable command:
                    _walked.VelocityTableSwitches.Add((command.Tick, Number, _order++, command.UsesSmwVelocities, command.Line));
                    break;

                case MmlLegato:
                    break;

                case MmlAmplify command:
                    _walked.Amplifies[Number].Add(command);
                    break;

                case MmlIgnored command:
                    _walked.IgnoredCommands.Add((Number, command));
                    break;

                case MmlTranspose command:
                    _transpose = command.Semitones;
                    _transposeChanges.Add((command.Tick, command.Semitones));
                    _hasTransposedSinceLoop = true;
                    break;

                case MmlGlobalTranspose:
                    break;

                case MmlPitchEnvelope command:
                    _pitchEnvelope = command.LengthTicks > 0 ? command : null;
                    break;

                case MmlLoopMark mark:
                    ReadLoopMark(mark);
                    break;
            }
        }

        // Moves the channel onto an instrument, starting or ending a noise stretch and pushing the instrument's envelope.
        private void ReadInstrumentChange(MmlInstrumentChange change)
        {
            _instrument = SelectedInstrumentId(_bound, change);
            _noiseClock = _song.InstrumentById(_instrument) is { IsNoise: true } ? SnesNoise.MinClock : null;
            if (_noiseClock is { } entryClock)
            {
                _walked.NoiseClockPushes.Add((change.Tick, Number, _order++, entryClock));
            }

            PushBackupTable(change.Tick, _song.InstrumentById(_instrument)?.Envelope ?? EnvelopeState.Unset, EnvelopeWrite.Instrument, change.Line);
        }

        // Moves the channel onto the instrument a $F3 makes of the one in force, unless the channel is on noise.
        private void ReadSampleLoad(MmlSampleLoad load)
        {
            if (_noiseClock is not null)
            {
                return;
            }

            _instrument = SampleLoadInstrumentId(
                _song, _walked.SampleLoadInstruments, _instrument, _bound.SamplesByLoad[(load.SampleNumber, load.FileName)], (byte)load.TuningHigh);
            PushBackupTable(load.Tick, _backupTable, EnvelopeWrite.SampleLoad, load.Line);
        }

        // The envelope the instrument in force has, or the song load's before the channel's first instrument.
        private EnvelopeState InstrumentEnvelopeRestored()
        {
            if (_instrument < 0)
            {
                return MmlBuiltins.DefaultEnvelope;
            }

            return _song.InstrumentById(_instrument)?.Envelope ?? EnvelopeState.Unset;
        }

        // Puts the registers back to what the backup table holds, where something has moved them off it.
        private void ReadKeyOnRestore(MmlKeyOnRestore command)
        {
            if (_registers == _backupTable)
            {
                return;
            }

            WriteRegistersOnly(command.Tick, _backupTable, EnvelopeWrite.KeyOnRestore, command.Line);
        }

        // Writes one of this voice's envelope registers, refusing a write to another voice's.
        private void ReadEnvelopeRegisterWrite(MmlDspWrite command)
        {
            int voice = SnesDspRegister.VoiceOf(command.Register);
            if (voice != Number)
            {
                throw MmlImportException.DspWriteToOtherVoice(Number, command.Register, voice, command.Line);
            }

            WriteRegistersOnly(
                command.Tick, _registers.With(SnesDspRegister.EnvelopeSettingOf(command.Register), command.Value), EnvelopeWrite.DspWrite, command.Line);
        }

        // Records an envelope write that goes into the backup table and is pushed from there to the registers.
        private void PushBackupTable(int tick, EnvelopeState written, EnvelopeWrite wrote, int line)
        {
            WriteRegistersOnly(tick, written, wrote, line);
            _backupTable = written;
        }

        // Records an envelope write that reaches the registers and leaves the backup table alone.
        private void WriteRegistersOnly(int tick, EnvelopeState written, EnvelopeWrite wrote, int line)
        {
            _walked.EnvelopeWrites[Number].Add(new EnvelopeRegisterWrite(tick, _registers, written, wrote, _instrument, line));
            _registers = written;
        }

        // Switches the channel onto noise at a clock, marking where that lands inside a sounding note.
        private void ReadNoise(MmlNoise command)
        {
            if (_previous is { } sounding && command.Tick > sounding.StartTick && command.Tick < sounding.EndTick())
            {
                int sampledInstrument = _instrument;
                _walked.CutsInsideNotes[Number].Add((command.Tick, CutReason.Noise, () => new NoteCut(
                    command.Tick, NoiseInstrumentId(_song, _walked.NoiseInstruments, sampledInstrument), SnesNoise.SemitoneForClock(command.Clock))));
            }

            _noiseClock = command.Clock;
            _walked.NoiseClockPushes.Add((command.Tick, Number, _order++, command.Clock));
        }

        // Gathers a t, w, v or y onto its lane's list.
        private void ReadLetteredCommand(MmlCommand command)
        {
            switch (command.Letter)
            {
                case 't':
                    _walked.Tempos.Add((command, Number));
                    break;

                case 'w':
                    _walked.MasterVolumes.Add((command, Number));
                    break;

                case 'v':
                    _walked.ChannelVolumes[Number].Add(command);
                    break;

                default:
                    _walked.Pans[Number].Add(command);
                    break;
            }
        }

        // Records the channel's loop point, refusing one in a song that does not loop.
        private void ReadLoopMark(MmlLoopMark mark)
        {
            if (!_file.DoesLoop)
            {
                throw MmlImportException.LoopPointUnderNoLoop(mark.Line);
            }

            _walked.LoopTicksByChannel[Number] = mark.Tick;
            _walked.LoopTick = mark.Tick;
            _transposeAtLoop = _transpose;
            _hasTransposedSinceLoop = false;
            _firstNoteTickSinceLoop = null;
        }

        // Places a note, on noise or at its pitch.
        private void ReadNote(MmlNote note)
        {
            _noteIndex++;
            if (_noiseClock is { } clock)
            {
                PlaceNoiseNote(note, clock);
                return;
            }

            PlacePitchedNote(note);
        }

        // Places a note under noise on the row its clock names, on a noise instrument.
        private void PlaceNoiseNote(MmlNote note, int clock)
        {
            int row = SnesNoise.SemitoneForClock(clock);
            var noiseNote = new Note(
                note.Tick, note.LengthTicks, row, NoiseInstrumentId(_song, _walked.NoiseInstruments, _instrument), Note.GateOfQuantization(note.Quantization),
                Note.VelocityOfQuantization(note.Quantization), note.FineTune, IsHeld: note.IsHeld)
            {
                Bends = ResolvedBends(note, row, 0, null, null).Bends,
            };

            MarkNoteReached(note.Tick);
            PlaceNote(noiseNote, note);
        }

        // Places a note at the pitch both transposes and the pitch envelope put it at, dropping or cutting short what sounds at no pitch.
        private void PlacePitchedNote(MmlNote note)
        {
            int globalTranspose = _globalTransposes.InForceAt(note.Tick, Number, _position);
            int transposedSum = note.NoteValue + _transpose + globalTranspose;
            int sounded = SnesPitch.SoundedNoteValue(transposedSum);
            if (sounded > SnesPitch.MaxPlayableNoteValue)
            {
                MarkSilent(note);
                return;
            }

            if (sounded != transposedSum && (note.Bends.Length > 0 || _pitchEnvelope is { LengthTicks: > 0 }))
            {
                throw MmlImportException.BendOutOfRangeAndBack(Number, note.Tick, _transpose, globalTranspose, note.Line);
            }

            MarkNoteReached(note.Tick);
            var envelopeBend = PitchEnvelopeBend(note, sounded);
            int startSemitone = envelopeBend.StartNoteValue - SnesPitch.C4NoteValue;

            var rampCeilingAtB7 = envelopeBend.IsTargetPastB7 || note.Bends.Any(bend => BendTarget(bend, globalTranspose) > SnesPitch.MaxPlayableNoteValue)
                ? new PitchCeiling(VibratoPeak(_walked.Vibratos[Number], _channel, note), note.LengthTicks)
                : null;
            var (bends, silentFromTick) = ResolvedBends(note, startSemitone, globalTranspose, envelopeBend.Bend, rampCeilingAtB7);
            if (silentFromTick is { } heardTicks)
            {
                if (heardTicks == 0)
                {
                    MarkSilent(note);
                    return;
                }

                _walked.CutShortNoteCount++;
                var cut = note.ShortenedTo(heardTicks);
                (bends, _) = ResolvedBends(cut, startSemitone, globalTranspose, envelopeBend.Bend, rampCeilingAtB7! with { SoundsToTick = heardTicks });
                note = NoteCutShortSounding(note, cut, heardTicks);
            }

            var placed = new Note(
                note.Tick, note.LengthTicks, startSemitone, _instrument, Note.GateOfQuantization(note.Quantization), Note.VelocityOfQuantization(note.Quantization),
                note.FineTune, IsHeld: note.IsHeld)
            {
                Bends = bends,
            };

            PlaceNote(placed, note);
        }

        // The bend the channel's pitch envelope arms on a note at a note value, and the note value the note keys on at.
        private (int StartNoteValue, (int DelayTicks, int LengthTicks, int TargetSemitone)? Bend, bool IsTargetPastB7) PitchEnvelopeBend(MmlNote note, int sounded)
        {
            if (_pitchEnvelope is not { LengthTicks: > 0 } pitched)
            {
                return (sounded, null, false);
            }

            int extent = (sbyte)pitched.Extent;
            int target = pitched.IsAttack ? sounded : (sounded + extent) & SnesPitch.NoteValueMask;
            int start = pitched.IsAttack ? sounded - extent : sounded;
            int checkedValue = pitched.IsAttack ? start : target;
            bool isPastRange = !SnesPitch.PlayableNoteValueRange.Contains(checkedValue);
            if (isPastRange && pitched.IsAttack)
            {
                throw MmlImportException.PitchEnvelopeOutOfRange(Number, note.Tick, extent, checkedValue, note.Line);
            }

            return (start, (pitched.DelayTicks, pitched.LengthTicks, target - SnesPitch.C4NoteValue), isPastRange);
        }

        // A note cut short where its slide goes silent, made to sound exactly as long as it is heard.
        private MmlNote NoteCutShortSounding(MmlNote note, MmlNote cut, int heardTicks)
        {
            bool isBoundInto = _isBoundIntoByLegato.Value[_noteIndex] && _previous?.EndTick() == note.Tick;
            if (note.IsHeld || isBoundInto)
            {
                return cut;
            }

            int furthestTick = MmlRemoteExpansion.FurthestGrowthTick(_channel.Events, _position, note.Tick + heardTicks, _channel.EndTick);
            return cut.RewrittenToSound(heardTicks, furthestTick, _waitTimeAt.Value);
        }

        // Places a note on the song, refusing one that overlaps the note in front.
        private void PlaceNote(Note placed, MmlNote readFrom)
        {
            if (_previous is { } earlier && earlier.Overlaps(placed))
            {
                throw MmlImportException.NotesOverlap(Number, placed.StartTick, earlier.StartTick, readFrom.Line);
            }

            _song.PlaceNote(Number, placed);
            _walked.NotesReadFrom[(Number, readFrom.Tick)] = readFrom;
            _walked.CutsInsideNotes[Number].AddRange(TieGateCuts(readFrom));
            _previous = placed;
        }

        // Drops a note the ROM keys on at no pitch.
        private void MarkSilent(MmlNote dropped)
        {
            MarkNoteReached(dropped.Tick);
            _walked.SilentNoteTicks[Number].Add(dropped.Tick);
        }

        // Records the first note the second pass reaches before a transpose sets the register again.
        private void MarkNoteReached(int tick)
        {
            if (!_hasTransposedSinceLoop)
            {
                _firstNoteTickSinceLoop ??= tick;
            }
        }
    }
}
