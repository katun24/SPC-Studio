using System.Collections.Immutable;

namespace SpcStudio.Import;

// What a Standard MIDI File holds once read: how it counts its ticks, every channel message and tempo change of every track in the order they play,
// the tick its last track ends at, and its song name where it has one.
public sealed record MidiFile(MidiTimeBase TimeBase, ImmutableArray<MidiMessage> Messages, ImmutableArray<MidiTempoChange> Tempos, long EndTick, string? SongName);
