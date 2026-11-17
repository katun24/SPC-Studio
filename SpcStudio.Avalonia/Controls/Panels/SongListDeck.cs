namespace SpcStudio.Controls;

// The song list window's two tables, the local songs and the global ones, under one strip of tabs.
public sealed class SongListDeck() : TabDeck<SongListTab>([
    (SongListTab.Local, "LOCAL"),
    (SongListTab.Global, "GLOBAL"),
]);
