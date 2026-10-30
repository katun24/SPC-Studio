using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using SpcStudio.Controls;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio;

// The note panel's Multiselect box: a check box for every channel and every instrument of the song, a range of positions, tones and lengths, and a
// three-way box for each of six effects, answering the filter they describe.
public partial class MultiselectDialog : DialogWindow
{
    private const double SwatchSize = 12;
    private const int ShortestLengthBound = 0;
    private const int NoNoteLength = 0;

    private static readonly ValueRange LengthBoundRange = new(ShortestLengthBound, NotePlaceLimits.MaxLength);
    private readonly Song _song;
    private readonly BoundedRange _positions;
    private readonly BoundedRange _tones;
    private readonly BoundedRange _lengths;
    private readonly List<(CheckBox Box, Func<int, Note, bool> HasEffect)> _effects = [];
    private bool _isWritingBoxes;

    // Builds the box over an empty song.
    public MultiselectDialog() : this(new Song())
    {
    }

    // Builds the box over the song as it stands: every channel checked and no instrument, each range the whole of what the song holds, and every effect left out of the filter.
    private MultiselectDialog(Song song)
    {
        InitializeComponent();
        _song = song;
        FlagGuard.RunWithFlagSet(ref _isWritingBoxes, BuildLists);
        ChannelsHeader.AllBox.IsCheckedChanged += (_, _) => CheckWholeList(ChannelList, ChannelsHeader.AllBox);
        InstrumentsHeader.AllBox.IsCheckedChanged += (_, _) => CheckWholeList(InstrumentList, InstrumentsHeader.AllBox);

        int longestNoteLength = song.NotesWithChannels().Select(one => one.Note.LengthTicks).DefaultIfEmpty(NoNoteLength).Max();
        _positions = new BoundedRange(
            PositionField(LowestPositionBox, NotePlaceLimits.MinPosition), PositionField(HighestPositionBox, song.LastNoteEndTick));
        _tones = new BoundedRange(BoundedField.ForPitch(LowestToneBox, Song.LowestSemitone), BoundedField.ForPitch(HighestToneBox, Song.HighestSemitone));
        _lengths = new BoundedRange(LengthField(LowestLengthBox, ShortestLengthBound), LengthField(HighestLengthBox, longestNoteLength));
        foreach (var range in new[] { _positions, _tones, _lengths })
        {
            range.ValueChanged += ShowCount;
        }

        BuildEffectBoxes();
        ShowAllBoxesAndCount();
    }

    // Shows the box over a window and answers the filter it describes, or null where it was cancelled.
    public static Task<Func<int, Note, bool>?> Show(Window owner, Song song)
    {
        return new MultiselectDialog(song).ShowDialog<Func<int, Note, bool>?>(owner);
    }

    // One end of the position range, stepped by the snap size.
    private BoundedField PositionField(TextBox box, int baseTick)
    {
        return BoundedField.ForNumber(box, NotePlaceLimits.PositionRange, baseTick, () => _song.SnapTicks);
    }

    // One end of the length range, stepped by the snap size.
    private BoundedField LengthField(TextBox box, int baseTicks)
    {
        return BoundedField.ForNumber(box, LengthBoundRange, baseTicks, () => _song.SnapTicks);
    }

    // Fills the two lists with a checked box for every channel and an unchecked one for every instrument.
    private void BuildLists()
    {
        for (int channel = 0; channel < _song.ChannelCount; channel++)
        {
            var box = BuildFilterBox(new TextBlock { Text = (channel + 1).ToString(CultureInfo.InvariantCulture) }, channel);
            box.IsChecked = true;
            ChannelList.Children.Add(box);
        }

        foreach (var instrument in _song.Instruments)
        {
            InstrumentList.Children.Add(BuildFilterBox(SwatchRow(ChannelPalette.FromPackedRgb(instrument.ColorRgb), instrument.Name), instrument.Id));
        }
    }

    // A name with a swatch of its color before it, as the two drop-downs draw an instrument.
    private static StackPanel SwatchRow(Color color, string name)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Rectangle { Width = SwatchSize, Height = SwatchSize, VerticalAlignment = VerticalAlignment.Center, Fill = new SolidColorBrush(color) });
        row.Children.Add(new TextBlock { Text = name, Margin = new Thickness(Space.Px6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    // One of the lists' check boxes, tagged with the channel or instrument it stands for.
    private CheckBox BuildFilterBox(Control content, int tag)
    {
        var check = new CheckBox { Content = content, Tag = tag };
        check.IsCheckedChanged += OnFilterBoxChanged;
        return check;
    }

    // Fills the effect list with a three-way box for each effect a note can play with, each left out of the filter.
    private void BuildEffectBoxes()
    {
        AddEffectBox(SettingLabels.Legato, (_, note) => note.IsLegato);
        AddEffectBox(SettingLabels.Hold, (_, note) => note.IsHeld);
        AddEffectBox(SettingLabels.EchoSend, (channel, note) => NoteEchoSend.IsEchoOn(_song, channel, note));
        AddEffectBox("Pitch Bend", (_, note) => note.HasBend);
        AddEffectBox("Vibrato", (_, note) => note.HasVibrato);
        AddEffectBox("Tremolo", (_, note) => note.HasTremolo);
    }

    // Adds one effect's three-way box: mixed leaves the effect out, checked asks for notes that play with it and clear for notes that do not.
    private void AddEffectBox(string name, Func<int, Note, bool> hasEffect)
    {
        var box = new CheckBox { Content = name, IsThreeState = true, IsChecked = null };
        box.IsCheckedChanged += (_, _) => ShowCount();
        EffectList.Children.Add(box);
        _effects.Add((box, hasEffect));
    }

    // Brings the two All boxes and the count up to date after a box in a list is clicked.
    private void OnFilterBoxChanged(object? sender, RoutedEventArgs e)
    {
        if (_isWritingBoxes)
        {
            return;
        }

        ShowAllBoxesAndCount();
    }

    // Turns a whole list on or off from its All box, a list part checked going on.
    private void CheckWholeList(Panel list, CheckBox allBox)
    {
        if (_isWritingBoxes)
        {
            return;
        }

        var boxes = BoxesIn(list);
        if (boxes.Count == 0)
        {
            return;
        }

        bool isOn = FieldInput.IsAskedOn(allBox, boxes, IsBoxChecked);
        FlagGuard.RunWithFlagSet(ref _isWritingBoxes, () =>
        {
            foreach (var box in boxes)
            {
                box.IsChecked = isOn;
            }

            allBox.IsChecked = isOn;
        });
        ShowCount();
    }

    // Puts the two All boxes at what their lists say, part checked included, and writes the count.
    private void ShowAllBoxesAndCount()
    {
        FlagGuard.RunWithFlagSet(ref _isWritingBoxes, () =>
        {
            ChannelsHeader.AllBox.IsChecked = FieldInput.SharedFlagOrOff(BoxesIn(ChannelList), IsBoxChecked);
            InstrumentsHeader.AllBox.IsChecked = FieldInput.SharedFlagOrOff(BoxesIn(InstrumentList), IsBoxChecked);
        });
        ShowCount();
    }

    // A list's check boxes.
    private static List<CheckBox> BoxesIn(Panel list)
    {
        return list.Children.OfType<CheckBox>().ToList();
    }

    // Whether a check box is checked, part checked counting as not.
    private static bool IsBoxChecked(CheckBox box)
    {
        return box.IsChecked == true;
    }

    // Writes how many notes the filter as it stands would select.
    private void ShowCount()
    {
        var filter = Filter();

        int matchingNoteCount = _song.NotesWithChannels().Count(one => filter(one.Channel, one.Note));
        CountText.Text = CountedNoun.Of(matchingNoteCount, CountedNoun.NoteNoun);
    }

    // The filter the box describes: a note whose channel and instrument are both checked, whose start, tone and length lie in their ranges, and which
    // plays with or without each effect as its box asks.
    private Func<int, Note, bool> Filter()
    {
        var channels = CheckedTagsIn(ChannelList);
        var instrumentIds = CheckedTagsIn(InstrumentList);

        // Each effect whose box asks for it, with whether a note must play with it or without it.
        var askedEffects = _effects.Where(effect => effect.Box.IsChecked is not null).Select(effect => (effect.HasEffect, IsWanted: effect.Box.IsChecked == true)).ToList();
        return (channel, note) => channels.Contains(channel) && instrumentIds.Contains(note.InstrumentId)
            && _positions.Contains(note.StartTick) && _tones.Contains(note.Semitone) && _lengths.Contains(note.LengthTicks)
            && askedEffects.All(effect => effect.HasEffect(channel, note) == effect.IsWanted);
    }

    // The tags of a list's checked boxes.
    private static HashSet<int> CheckedTagsIn(Panel list)
    {
        return BoxesIn(list).Where(IsBoxChecked).Select(box => (int)box.Tag!).ToHashSet();
    }

    // Closes the box answering its filter.
    private void OnSelectClick(object? sender, RoutedEventArgs e)
    {
        Close(Filter());
    }
}
