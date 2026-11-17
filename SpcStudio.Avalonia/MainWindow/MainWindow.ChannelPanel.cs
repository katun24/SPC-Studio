using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using SpcStudio.Controls;
using SpcStudio.Model;

namespace SpcStudio;

// The channel panel: which channel is picked, the hue its button is drawn at, selecting its notes, and its base surround pair and echo send; its base
// volume and pan are first-region rows, wired with the Song panel's.
public partial class MainWindow
{
    private const string ChannelRowWord = "Channel";

    // The colors the channel drop-down's rows were last built in, one per channel, so the rows are built again only where a channel came or went or was recolored.
    private List<Color> _shownChannelRowColors = [];

    // Gives the channel panel's color button the channel hues and fills its Base Surround drop-down.
    private void WireChannelPanel()
    {
        ChannelColorButton.Scale = ChannelPalette.ChannelHues;
        ChannelColorButton.HueChanged += OnChannelHueChanged;
        SurroundField.FillDropDown(ChannelSurroundBox);
    }

    // Brings the channel panel to the selected channel: its row in the drop-down, its color, its select button and its four base rows.
    private void ShowChannelPanel()
    {
        int channel = Roll.SelectedChannel;
        ShowChannelPick();
        ChannelColorButton.ColorRgb = ChannelPalette.ToPackedRgb(ChannelPalette.ColorOfChannel(_song, channel));
        ShowPanelRowsOf(ChannelVolumes, null);
        ShowPanelRowsOf(Pans, null);
        ShowPanelRowsOf(ChannelEchoes, null);
        ShowWhetherChannelHasNotes();
    }

    // Builds the channel drop-down's rows again where a channel came or went or was recolored since they were last built, and selects the selected channel's.
    private void ShowChannelPick()
    {
        var channelColors = Enumerable.Range(0, _song.ChannelCount).Select(channel => ChannelPalette.ColorOfChannel(_song, channel)).ToList();
        FlagGuard.RunWithFlagSet(ref _isWritingFieldsFromSong, () =>
        {
            if (!channelColors.SequenceEqual(_shownChannelRowColors))
            {
                _shownChannelRowColors = channelColors;
                ChannelBox.ItemsSource = channelColors.Select((color, channel) => new SwatchRow(channel, $"{ChannelRowWord} {Song.ChannelNumberOf(channel)}", new SolidColorBrush(color))).ToList();
            }

            ChannelBox.SelectedIndex = Roll.SelectedChannel;
        });
    }

    // Writes the selected channel's base surround pair into the Base Surround drop-down.
    private void ShowChannelSurround()
    {
        FlagGuard.RunWithFlagSet(ref _isWritingFieldsFromSong, () => ChannelSurroundBox.SelectedIndex = SurroundField.IndexOf(_song.BaseSurround(Pans.Channel)));
    }

    // Shows the select button only where the selected channel holds a note.
    private void ShowWhetherChannelHasNotes()
    {
        SelectChannelNotesButton.IsVisible = _song.Notes(Roll.SelectedChannel).Count > 0;
    }

    // Selects the channel picked in the drop-down, as its channel button does.
    private void OnChannelPicked(object? sender, SelectionChangedEventArgs e)
    {
        int pickedChannel = ChannelBox.SelectedIndex;
        if (_isWritingFieldsFromSong || pickedChannel < 0 || pickedChannel == Roll.SelectedChannel)
        {
            return;
        }

        SelectChannel(pickedChannel);
    }

    // Draws the selected channel's button, its drop-down row and the color button at the hue under the strip's bar.
    private void OnChannelHueChanged(HueButton colorButton)
    {
        int channel = Roll.SelectedChannel;
        if (_song.ChannelHue(channel) == colorButton.Hue)
        {
            return;
        }

        _song.SetChannelHue(channel, colorButton.Hue);
        MarkModified(colorButton);
        UpdateChannelButtons();
        ShowChannelPanel();
    }

    // Selects every note on the selected channel.
    private void OnSelectChannelNotesClick(object? sender, RoutedEventArgs e)
    {
        Roll.SelectAllOnEditedChannel();
    }

    // Writes the surround pair picked into the selected channel's pan base, keeping its pan and its fade.
    private void OnChannelSurroundPicked(object? sender, SelectionChangedEventArgs e)
    {
        var pickedSurround = SurroundField.PairAt(ChannelSurroundBox.SelectedIndex);
        if (_isWritingFieldsFromSong || ChannelSurroundBox.SelectedIndex < 0 || pickedSurround == _song.BaseSurround(Pans.Channel))
        {
            return;
        }

        ApplyFirstRegion(Pans, ChannelSurroundBox, Pans.BaseValue, pickedSurround, null);
    }
}
