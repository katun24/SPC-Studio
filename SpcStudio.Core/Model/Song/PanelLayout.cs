namespace SpcStudio.Model;

// How the right-hand panels were arranged when this song was last saved: the column's width, each popped-out window's own, which panel each tab strip
// showed, which folding sections were standing open, and which of the Instrument Picker's folders were.
public sealed class PanelLayout
{
    public double ColumnWidth { get; set; }
    public double InstrumentWindowWidth { get; set; }
    public double NoteWindowWidth { get; set; }
    public double SongWindowWidth { get; set; }
    public double GroupWindowWidth { get; set; }
    public PanelTab Tab { get; set; }
    public PanelTab GroupTab { get; set; }
    public bool InstrumentEnvelopeOpen { get; set; }
    public bool NoteVolumeOpen { get; set; }
    public bool NotePanOpen { get; set; }
    public bool NoteEnvelopeOpen { get; set; }
    public bool NoteVibratoOpen { get; set; }
    public bool NoteTremoloOpen { get; set; }
    public bool NoteBendOpen { get; set; }

    // Which of the Instrument Picker's folders were standing open, each as its path under the app's instruments folder with forward slashes.
    public List<string> OpenPickerFolders { get; set; } = [];

    // A copy of this layout.
    public PanelLayout Copy()
    {
        return new()
        {
            ColumnWidth = ColumnWidth,
            InstrumentWindowWidth = InstrumentWindowWidth,
            NoteWindowWidth = NoteWindowWidth,
            SongWindowWidth = SongWindowWidth,
            GroupWindowWidth = GroupWindowWidth,
            Tab = Tab,
            GroupTab = GroupTab,
            InstrumentEnvelopeOpen = InstrumentEnvelopeOpen,
            NoteVolumeOpen = NoteVolumeOpen,
            NotePanOpen = NotePanOpen,
            NoteEnvelopeOpen = NoteEnvelopeOpen,
            NoteVibratoOpen = NoteVibratoOpen,
            NoteTremoloOpen = NoteTremoloOpen,
            NoteBendOpen = NoteBendOpen,
            OpenPickerFolders = [.. OpenPickerFolders],
        };
    }
}
