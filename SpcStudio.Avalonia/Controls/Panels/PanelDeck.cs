using SpcStudio.Model;

namespace SpcStudio.Controls;

// The main window's right-hand panels under one strip of tabs.
public sealed class PanelDeck() : DockableDeck<PanelTab>([
    (PanelTab.Song, "SONG"),
    (PanelTab.Instrument, "INSTRUMENT"),
    (PanelTab.Note, "NOTE"),
]);
