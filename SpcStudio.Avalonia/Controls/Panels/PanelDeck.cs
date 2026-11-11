using Avalonia.Controls;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The main window's right-hand panels under one strip of tabs, with a button left of the corner button that takes the panel showing out into a window of
// its own.
public sealed class PanelDeck : DockableDeck<PanelTab>
{
    private readonly Button _tabPopOutButton;

    // Builds the four tabs and the button that takes the panel showing out.
    public PanelDeck() : base([
        (PanelTab.Song, "SONG"),
        (PanelTab.Channel, "CHANNEL"),
        (PanelTab.Instrument, "INSTRUMENT"),
        (PanelTab.Note, "NOTE"),
    ])
    {
        _tabPopOutButton = AddCornerButton(AppIcons.PictureInPictureTopRightOutline, () => TabPopOutPressed?.Invoke(SelectedTab));
        OnSelectedPanelShown();
    }

    // Raised by the tab pop-out button, with the tab the strip shows.
    public event Action<PanelTab>? TabPopOutPressed;

    // Gives the tab pop-out button the tooltip naming the panel the strip shows.
    protected override void OnSelectedPanelShown()
    {
        ToolTip.SetTip(_tabPopOutButton, PopOutTooltipOf(SelectedTab));
    }

    // The tab pop-out button's tooltip while a tab shows.
    private static string PopOutTooltipOf(PanelTab tab)
    {
        return tab switch
        {
            PanelTab.Song => Tooltips.PopOutSongPanel,
            PanelTab.Channel => Tooltips.PopOutChannelPanel,
            PanelTab.Instrument => Tooltips.PopOutInstrumentPanel,
            PanelTab.Note => Tooltips.PopOutNotePanel,
            _ => throw new ArgumentOutOfRangeException(nameof(tab)),
        };
    }
}
