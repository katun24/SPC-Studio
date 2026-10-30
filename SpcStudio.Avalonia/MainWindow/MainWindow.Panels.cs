using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

using SpcStudio.Controls;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio;

// The right-hand panel column: its deck of tabs, the panels taken out into windows of their own or into the column's own window, the divider that
// sets the column's width, and the layout a song saves of all of it.
public partial class MainWindow
{
    private const string PanelWidthResource = "PanelWidth";
    private const string PanelMinWidthResource = "PanelMinWidth";
    private const string DividerThicknessResource = "DividerThickness";
    private const double PanelWidthTolerance = 0.5;

    private PanelSlot _instrumentSlot = null!;
    private PanelSlot _noteSlot = null!;
    private PanelSlot _songSlot = null!;
    private PanelDeck? _groupDeck;
    private PanelWindow? _groupWindow;
    private PanelPlacement _instrumentPlacement = new();
    private PanelPlacement _notePlacement = new();
    private PanelPlacement _songPlacement = new();
    private PanelPlacement _groupPlacement = new();
    private double _panelWidth;

    // The three panel slots in the order the tab strip gives them.
    private IEnumerable<PanelSlot> PanelSlots
    {
        get
        {
            return [_songSlot, _instrumentSlot, _noteSlot];
        }
    }

    // The column's width where a song carries none of its own.
    private double DefaultPanelWidth
    {
        get
        {
            return ThemeResource.Of<double>(PanelWidthResource);
        }
    }

    // The narrowest the column can be dragged.
    private double MinPanelWidth
    {
        get
        {
            return ThemeResource.Of<double>(PanelMinWidthResource);
        }
    }

    // The divider's own thickness, which is the gap between two framed surfaces.
    private double DividerThickness
    {
        get
        {
            return ThemeResource.Of<double>(DividerThicknessResource);
        }
    }

    // Builds the three panel slots once the controls exist, stands the panels in the column's deck and wires the corner buttons and the divider the
    // markup does not.
    private void WirePanels()
    {
        ColumnDivider.DriveSizeOf(() => _panelWidth, ApplyPanelWidth);
        _songSlot = new PanelSlot(SongPanel, PanelTab.Song, WindowTitles.SongPanel, SongPanel.ShowPoppedOut);
        _instrumentSlot = new PanelSlot(Instruments, PanelTab.Instrument, WindowTitles.InstrumentPanel, isPoppedOut => Instruments.IsPoppedOut = isPoppedOut);
        _noteSlot = new PanelSlot(NotePanel, PanelTab.Note, WindowTitles.NotePanel, NotePanel.ShowPoppedOut);
        foreach (var slot in PanelSlots)
        {
            PanelStack.Add(slot.Tab, slot.Panel);
            slot.ShowCornerButton(false);
        }

        PanelStack.CornerPressed += () => GroupPanels(PanelStack.HeldTabs.Select(SlotOf).ToList());
        Instruments.PopOutToggled += () => TogglePanelPoppedOut(_instrumentSlot);
        UpdatePanelColumnVisibility();
    }

    // Takes the note panel out into a window of its own, or docks it back.
    private void OnNotePopOutClick(object? sender, RoutedEventArgs e)
    {
        TogglePanelPoppedOut(_noteSlot);
    }

    // Takes the song panel out into a window of its own, or docks it back.
    private void OnSongPopOutClick(object? sender, RoutedEventArgs e)
    {
        TogglePanelPoppedOut(_songSlot);
    }

    // Takes a panel out into a window of its own, or docks it back in the column where it already stands in one.
    private void TogglePanelPoppedOut(PanelSlot slot)
    {
        if (slot.IsPoppedOut)
        {
            DockPanelInColumn(slot);
            return;
        }

        PopOutPanel(slot);
    }

    // Where a panel stands, to be put back there next time: its own window's placement, or only whether it stood in the column's window.
    private PanelPlacement CapturePanelPlacement(PanelSlot slot)
    {
        if (slot.Window is { } window)
        {
            return window.CapturePlacement();
        }

        return new PanelPlacement { Grouped = _groupDeck?.DoesHold(slot.Tab) == true };
    }

    // Puts each panel back where the settings said it was, the column's window first.
    private void RestorePanels()
    {
        var savedPlacements = new Dictionary<PanelSlot, PanelPlacement>
        {
            [_songSlot] = _songPlacement,
            [_instrumentSlot] = _instrumentPlacement,
            [_noteSlot] = _notePlacement,
        };

        GroupPanels(PanelSlots.Where(slot => savedPlacements[slot] is { Grouped: true, PoppedOut: false }).ToList(), _groupPlacement, _song.PanelLayout.GroupTab);
        foreach (var slot in PanelSlots.Where(slot => savedPlacements[slot].PoppedOut))
        {
            PopOutPanel(slot, savedPlacements[slot]);
        }
    }

    // The deck a panel stands in, or null where it stands in a window of its own.
    private PanelDeck? DeckOf(PanelSlot slot)
    {
        if (PanelStack.DoesHold(slot.Tab))
        {
            return PanelStack;
        }

        if (_groupDeck?.DoesHold(slot.Tab) == true)
        {
            return _groupDeck;
        }

        return null;
    }

    // Takes a panel out of whichever deck holds it into a window of its own, standing where a placement says or centered on this window, and as wide as asked or as the song left it.
    private void PopOutPanel(PanelSlot slot, PanelPlacement? placement = null, double requestedWidth = 0)
    {
        if (slot.IsPoppedOut)
        {
            return;
        }

        var deck = DeckOf(slot)!;
        double deckHeight = HeightOf(deck);
        deck.Remove(slot.Tab);

        var window = CreatePanelWindow(slot.Title, placement, deckHeight, requestedWidth > 0 ? requestedWidth : SavedWindowWidthOf(slot), () => DockPanelInColumn(slot));
        window.Take(slot.Panel);
        slot.Window = window;
        window.ShowAt(placement, this);
        if (deck == _groupDeck)
        {
            CollapseGroup();
        }

        slot.ShowCornerButton(true);
        UpdatePanelColumnVisibility();
    }

    // Puts a panel standing in a window of its own back in the column, closes that window and shows the panel's tab.
    private void DockPanelInColumn(PanelSlot slot)
    {
        if (slot.Window is not { } window)
        {
            return;
        }

        slot.Window = null;
        window.Release();
        PanelStack.Add(slot.Tab, slot.Panel);
        window.CloseUnlessClosingItself();
        PanelStack.Select(slot.Tab);
        slot.ShowCornerButton(false);
        UpdatePanelColumnVisibility();
    }

    // How tall a deck stands: the column's own height for the column, and the client height of the column's window for that window's.
    private double HeightOf(PanelDeck deck)
    {
        if (deck == PanelStack)
        {
            return PanelColumn.Bounds.Height;
        }

        return _groupWindow!.ClientSize.Height;
    }

    // Takes panels out of the column into the column's own window, opening it where none is open and joining them to the panels there where one is, and shows a tab in it.
    private void GroupPanels(IReadOnlyList<PanelSlot> slots, PanelPlacement? placement = null, PanelTab? shownTab = null)
    {
        if (slots.Count == 0)
        {
            return;
        }

        bool isOpening = _groupDeck is null;
        if (isOpening && slots.Count == 1)
        {
            PopOutPanel(slots[0], placement);
            return;
        }

        var tabToShow = shownTab ?? PanelStack.SelectedTab;
        if (isOpening)
        {
            CreateGroupWindow(placement);
        }

        PanelStack.MoveInto(_groupDeck!, slots.Select(slot => slot.Tab), tabToShow);
        if (isOpening)
        {
            _groupWindow!.ShowAt(placement, this);
        }

        UpdatePanelColumnVisibility();
    }

    // Makes the column's own window and the deck in it, empty and not yet shown.
    private void CreateGroupWindow(PanelPlacement? placement)
    {
        _groupDeck = new PanelDeck { DoesCornerDock = true };
        _groupDeck.CornerPressed += DockGroup;
        _groupWindow = CreatePanelWindow(WindowTitles.PanelGroup, placement, HeightOf(PanelStack), _song.PanelLayout.GroupWindowWidth, DockGroup);
        _groupWindow.Take(_groupDeck);
    }

    // Puts every panel in the column's window back in the column, with the tab it was showing, and closes the window.
    private void DockGroup()
    {
        if (_groupDeck is null)
        {
            return;
        }

        _groupDeck.MoveAllInto(PanelStack);
        CloseGroup();
        UpdatePanelColumnVisibility();
    }

    // Keeps the column's window from holding one panel alone, making the last one's own window of it where one is left and closing it where none is.
    private void CollapseGroup()
    {
        if (_groupDeck is not { } deck)
        {
            return;
        }

        var heldTabs = deck.HeldTabs.ToList();
        if (heldTabs.Count > 1)
        {
            return;
        }

        if (heldTabs.Count == 1)
        {
            var window = _groupWindow!;
            PopOutPanel(SlotOf(heldTabs[0]), window.CapturePlacement(), window.Width);
            return;
        }

        CloseGroup();
    }

    // Closes the column's window, which holds no panel by now.
    private void CloseGroup()
    {
        var window = _groupWindow!;
        _groupDeck = null;
        _groupWindow = null;
        window.Release();
        window.CloseUnlessClosingItself();
    }

    // A window for a panel or for the column's deck, as tall as a placement left it or as the deck its panels leave, whose keys are read as this window's.
    private PanelWindow CreatePanelWindow(string title, PanelPlacement? placement, double deckHeight, double width, Action dock)
    {
        var window = PanelWindow.Create(title, placement, deckHeight, width, dock);
        window.KeyPressed += e => HandleKey(e, window.FocusManager?.GetFocusedElement(), DeckIn(window));
        window.KeyReleased += ReleaseKey;
        return window;
    }

    // Hides the column's divider and deck while every panel has been taken out of it, leaving the gap on its right between the roll and the window's edge.
    private void UpdatePanelColumnVisibility()
    {
        bool isAnyPanelInColumn = !PanelStack.IsEmpty;
        ColumnDividerColumn.IsVisible = isAnyPanelInColumn;
        PanelStack.IsVisible = isAnyPanelInColumn;
    }

    // The slot a tab stands for.
    private PanelSlot SlotOf(PanelTab tab)
    {
        return tab switch
        {
            PanelTab.Song => _songSlot,
            PanelTab.Instrument => _instrumentSlot,
            _ => _noteSlot,
        };
    }

    // How wide the song left a panel's own window.
    private double SavedWindowWidthOf(PanelSlot slot)
    {
        if (slot == _songSlot)
        {
            return _song.PanelLayout.SongWindowWidth;
        }

        if (slot == _instrumentSlot)
        {
            return _song.PanelLayout.InstrumentWindowWidth;
        }

        return _song.PanelLayout.NoteWindowWidth;
    }

    // Gives the column's tab strip and the divider's spacer the toolbar's height.
    private void OnToolbarSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ColumnDividerSpacer.Height = e.NewSize.Height;
        PanelStack.StripHeight = e.NewSize.Height;
    }

    // Brings a panel to the front of whichever deck holds it.
    private void ShowPanel(PanelTab tab)
    {
        DeckOf(SlotOf(tab))?.Select(tab);
    }

    // The deck standing in a panel window: the column's window's own, or none in a window holding one panel alone.
    private PanelDeck? DeckIn(PanelWindow window)
    {
        return window == _groupWindow ? _groupDeck : null;
    }

    // Brings forward the instrument or the note panel, whichever a deck holds and is not showing, the instrument panel first, taking the focus off
    // the panel it hides.
    private void ShowNextPanelOnTab(PanelDeck? deck, IInputElement? focused)
    {
        if (deck?.HeldTabs.Where(tab => tab != PanelTab.Song && tab != deck.SelectedTab).Cast<PanelTab?>().FirstOrDefault() is not { } tabToShow)
        {
            return;
        }

        if (focused is Visual element && SlotOf(deck.SelectedTab).Panel.IsVisualAncestorOf(element))
        {
            TopLevel.GetTopLevel(element)?.FocusManager?.ClearFocus();
        }

        deck.Select(tabToShow);
    }

    // Keeps the column at a width the divider can reach after every layout pass.
    private void OnPanelColumnLayoutUpdated(object? sender, EventArgs e)
    {
        ApplyPanelWidth(_panelWidth);
    }

    // Lays the column out at a width, held between the narrowest the panels allow and half the window.
    private void ApplyPanelWidth(double width)
    {
        double widestColumnWidth = Bounds.Width > 0 ? Math.Max(MinPanelWidth, Bounds.Width / 2 - DividerThickness - Space.Px8) : double.PositiveInfinity;
        _panelWidth = Math.Clamp(width, MinPanelWidth, widestColumnWidth);
        if (!(Math.Abs(PanelStack.Width - _panelWidth) <= PanelWidthTolerance))
        {
            PanelStack.Width = _panelWidth;
        }
    }

    // Lays the panels out as the song just opened left them: the widths, the tabs showing, the sections open and the picker's open folders.
    private void ShowPanelLayout()
    {
        var layout = _song.PanelLayout;
        ShowPanelWidths(layout);
        PanelStack.Select(layout.Tab);
        _groupDeck?.Select(layout.GroupTab);
        ShowOpenSections(layout);
        _pickerOpenFolders = [.. layout.OpenPickerFolders];
        _picker?.ShowFolders(_pickerOpenFolders);
    }

    // Gives the column and every panel window standing open the widths a layout says.
    private void ShowPanelWidths(PanelLayout layout)
    {
        ApplyPanelWidth(layout.ColumnWidth > 0 ? layout.ColumnWidth : DefaultPanelWidth);
        ShowSavedWindowWidth(_songSlot.Window, layout.SongWindowWidth);
        ShowSavedWindowWidth(_instrumentSlot.Window, layout.InstrumentWindowWidth);
        ShowSavedWindowWidth(_noteSlot.Window, layout.NoteWindowWidth);
        ShowSavedWindowWidth(_groupWindow, layout.GroupWindowWidth);
    }

    // Gives a panel window a saved width, where it is open and the width was saved.
    private static void ShowSavedWindowWidth(PanelWindow? window, double width)
    {
        if (window is not null && width > 0)
        {
            window.Width = width;
        }
    }

    // Opens and folds the remembered folding sections as a layout says.
    private void ShowOpenSections(PanelLayout layout)
    {
        Instruments.IsEnvelopeOpen = layout.InstrumentEnvelopeOpen;
        NotePanelValues.VolumeSection.Section.IsOpen = layout.NoteVolumeOpen;
        NotePanelValues.PanSection.Section.IsOpen = layout.NotePanOpen;
        NotePanelValues.EnvelopeSection.IsOpen = layout.NoteEnvelopeOpen;
        NotePanelValues.VibratoSection.Section.IsOpen = layout.NoteVibratoOpen;
        NotePanelValues.TremoloSection.Section.IsOpen = layout.NoteTremoloOpen;
        NotePanelValues.BendSection.Section.IsOpen = layout.NoteBendOpen;
    }

    // Writes what the panels look like now onto the song, for it to be saved with.
    private void CapturePanelLayout()
    {
        _song.PanelLayout = new PanelLayout
        {
            ColumnWidth = _panelWidth,
            InstrumentWindowWidth = _instrumentSlot.Window?.Width ?? _song.PanelLayout.InstrumentWindowWidth,
            NoteWindowWidth = _noteSlot.Window?.Width ?? _song.PanelLayout.NoteWindowWidth,
            SongWindowWidth = _songSlot.Window?.Width ?? _song.PanelLayout.SongWindowWidth,
            GroupWindowWidth = _groupWindow?.Width ?? _song.PanelLayout.GroupWindowWidth,
            Tab = PanelStack.SelectedTab,
            GroupTab = _groupDeck?.SelectedTab ?? _song.PanelLayout.GroupTab,
            InstrumentEnvelopeOpen = Instruments.IsEnvelopeOpen,
            NoteVolumeOpen = NotePanelValues.VolumeSection.Section.IsOpen,
            NotePanOpen = NotePanelValues.PanSection.Section.IsOpen,
            NoteEnvelopeOpen = NotePanelValues.EnvelopeSection.IsOpen,
            NoteVibratoOpen = NotePanelValues.VibratoSection.Section.IsOpen,
            NoteTremoloOpen = NotePanelValues.TremoloSection.Section.IsOpen,
            NoteBendOpen = NotePanelValues.BendSection.Section.IsOpen,
            OpenPickerFolders = [.. _pickerOpenFolders.Order(StringComparer.Ordinal)],
        };
    }
}
