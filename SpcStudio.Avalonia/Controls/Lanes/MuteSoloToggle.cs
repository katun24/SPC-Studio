using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

using ShapePath = Avalonia.Controls.Shapes.Path;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// One mute or solo toggle of a channel, a voice or a sample: the button and the icon it carries, a filled body under a stroked mark, drawn in the
// color of its state.
public sealed class MuteSoloToggle
{
    private const double IconSize = 14;
    private const double MarkThickness = 1.3;

    private static readonly Geometry MuteToggleSpeakerBody = Geometry.Parse("M 1,5 H 4 L 8,1.5 V 12.5 L 4,9 H 1 Z");
    private static readonly Geometry UnmutedSpeakerWaves = Geometry.Parse("M 10,5 A 3,3 0 0 1 10,9 M 11.8,3 A 5.6,5.6 0 0 1 11.8,11");
    private static readonly Geometry MutedSpeakerCross = Geometry.Parse("M 10,5 L 13.5,9 M 13.5,5 L 10,9");
    private static readonly Geometry SoloToggleHeadphoneCups = Geometry.Parse("M 0.5,8 H 3.5 V 13 H 0.5 Z M 10.5,8 H 13.5 V 13 H 10.5 Z");
    private static readonly Geometry SoloToggleHeadphoneBand = Geometry.Parse("M 2,9 V 7 A 5,5 0 0 1 12,7 V 9");
    private readonly ShapePath _body;
    private readonly ShapePath _mark;
    private readonly Geometry _markWhenOff;
    private readonly Geometry _markWhenOn;
    private readonly IBrush _brushWhenOn;
    private bool _isShowingState;

    // Builds the button and its icon out of a body and the mark for each state, reporting a press that changes its checked state to the handler
    // given, with the member and whether the toggle is now on.
    private MuteSoloToggle(int member, Geometry body, Geometry markWhenOff, Geometry markWhenOn, Color colorWhenOn, Action<int, bool> toggled)
    {
        _markWhenOff = markWhenOff;
        _markWhenOn = markWhenOn;
        _brushWhenOn = new SolidColorBrush(colorWhenOn);
        _body = new ShapePath { Data = body, Stretch = Stretch.None };
        _mark = new ShapePath { Data = markWhenOff, Stretch = Stretch.None, StrokeThickness = MarkThickness, StrokeLineCap = PenLineCap.Round };

        var icon = new Grid { Width = IconSize, Height = IconSize };
        icon.Children.Add(_body);
        icon.Children.Add(_mark);

        Button = new ToggleButton
        {
            Classes = { ThemeClasses.ChannelButtonIcon },
            Content = icon,
        };
        Button.IsCheckedChanged += (_, _) =>
        {
            if (!_isShowingState)
            {
                toggled(member, Button.IsChecked == true);
            }
        };
    }

    public ToggleButton Button { get; }

    // A mute toggle, the speaker with its waves while off and its cross while on.
    public static MuteSoloToggle CreateMute(int member, Action<int, bool> toggled)
    {
        return new MuteSoloToggle(member, MuteToggleSpeakerBody, UnmutedSpeakerWaves, MutedSpeakerCross, Palette.MuteOn, toggled);
    }

    // A solo toggle, the headphones in both states.
    public static MuteSoloToggle CreateSolo(int member, Action<int, bool> toggled)
    {
        return new MuteSoloToggle(member, SoloToggleHeadphoneCups, SoloToggleHeadphoneBand, SoloToggleHeadphoneBand, Palette.SoloOn, toggled);
    }

    // Puts the toggle in a state: checked or not, the mark for that state, and the whole icon in the state's color or the idle brush given.
    public void ShowState(bool isOn, IBrush idleBrush)
    {
        FlagGuard.RunWithFlagSet(ref _isShowingState, () => Button.IsChecked = isOn);
        _mark.Data = isOn ? _markWhenOn : _markWhenOff;

        var brush = isOn ? _brushWhenOn : idleBrush;
        _body.Fill = brush;
        _mark.Stroke = brush;
    }
}
