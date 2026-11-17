namespace SpcStudio.Controls;

// The SPC Player's list column: the file's tags, its voices and its sample directory under one strip of tabs.
public sealed class SpcDeck() : DockableDeck<SpcTab>([
    (SpcTab.Song, "SONG"),
    (SpcTab.Voices, "VOICES"),
    (SpcTab.Samples, "SAMPLES"),
]);
