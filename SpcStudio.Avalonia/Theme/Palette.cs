using Avalonia.Media;
using SpcStudio.Controls;

namespace SpcStudio.Theme;

// Every color the app draws in, one entry per value, which the theme's brushes and every drawn surface read rather than writing a color of their own.
public static class Palette
{
    private const byte RollSelectionSweepAlpha = 0x1C;
    private const byte EnvelopeCurveFillAlpha = 0x38;
    private const byte EnvelopeOverlayCurveAlpha = 0x70;
    private const byte WaveformSelectionAlpha = 0x22;
    private const byte PopupShadowAlpha = 0x80;
    private const byte HoverOverlayAlpha = 0x33;
    private const byte PressOverlayAlpha = 0x33;
    private const byte DisabledOverlayAlpha = 0x4C;
    private const byte PastEndWashAlpha = 0x60;
    private const byte PanelFadeClearAlpha = 0x00;
    private const double PopupShadowOffsetY = 3;
    private const double PopupShadowBlur = 14;
    private const double SectionTintLift = 0.03;
    private const double SectionTintChroma = 0.025;
    private const double LaneCellLightness = 0.71;
    private const double LaneCellChroma = 0.1;
    private const double LaneCellSelectedLightness = 0.85;
    private const double LaneCellSelectedChroma = 0.065;
    private const int BlueHue = 250;
    private const int YellowHue = 100;
    private const int RedHue = 25;
    private const int GreenHue = 145;
    private const int OrangeHue = 60;
    private const int PurpleHue = 305;

    // How tall the fade is where a panel's content moves up under its header.
    public const double PanelFadeHeight = PopupShadowBlur;

    public static readonly Color Black = Color.Parse("#000000");
    public static readonly Color NearBlack = Color.Parse("#16161A");
    public static readonly Color CharcoalGray = Color.Parse("#2A2A32");
    public static readonly Color GraphiteGray = Color.Parse("#303036");
    public static readonly Color SlateGray = Color.Parse("#3A3A46");
    public static readonly Color SteelGray = Color.Parse("#44444E");
    public static readonly Color AshGray = Color.Parse("#808088");
    public static readonly Color SilverGray = Color.Parse("#A0A0AC");
    public static readonly Color PaleGray = Color.Parse("#C8C8D4");
    public static readonly Color White = Color.Parse("#FFFFFF");
    public static readonly Color Amber = Color.Parse("#E8C060");
    public static readonly Color SkyBlue = Color.Parse("#6CC8E0");
    public static readonly Color PanelBackground = Color.Parse("#22222A");
    public static readonly Color ControlBackground = Color.Parse("#2A2A33");
    public static readonly Color SunkenBackground = Color.Parse("#20202A");
    public static readonly Color PopupBackground = Color.Parse("#262630");
    public static readonly Color PopupShadowColor = ChannelPalette.WithAlpha(Black, PopupShadowAlpha);
    public static readonly Color BorderHover = Color.Parse("#5A5A6E");
    public static readonly Color FocusBorder = Color.Parse("#6A6A80");
    public static readonly Color TextMuted = Color.Parse("#78788A");
    public static readonly Color TextDisabled = Color.Parse("#5A5A64");
    public static readonly Color Error = Color.Parse("#E06464");
    public static readonly Color ThumbHover = Color.Parse("#4E4E5C");
    public static readonly Color ThumbDrag = Color.Parse("#62627A");
    public static readonly Color HoverOverlay = ChannelPalette.WithAlpha(White, HoverOverlayAlpha);
    public static readonly Color PressOverlay = ChannelPalette.WithAlpha(Black, PressOverlayAlpha);
    public static readonly Color DisabledOverlay = ChannelPalette.WithAlpha(Black, DisabledOverlayAlpha);
    public static readonly Color DrawnSurfaceBackground = Color.Parse("#1E1E22");
    public static readonly Color GridLine = Color.Parse("#383840");
    public static readonly Color BarLine = Color.Parse("#585864");
    public static readonly Color LoopMarker = Color.Parse("#58C870");
    public static readonly Color LoopMarkerOff = Color.Parse("#4C6052");
    public static readonly Color EndMarker = Color.Parse("#D85858");
    public static readonly Color EndMarkerOff = Color.Parse("#604C4C");
    public static readonly Color PastEndWash = ChannelPalette.WithAlpha(Color.Parse("#101014"), PastEndWashAlpha);
    public static readonly Color WhiteKey = Color.Parse("#E8E8EC");
    public static readonly Color BlackKeyLabel = Color.Parse("#C8C8D0");
    public static readonly Color RollWhiteRow = Color.Parse("#2A2A30");
    public static readonly Color RollBlackRow = Color.Parse("#222228");
    public static readonly Color RollCRow = Color.Parse("#2C303E");
    public static readonly Color LaneOverridden = Color.Parse("#7CC6E0");
    public static readonly Color EchoValueInsideRun = Color.Parse("#121A1E");
    public static readonly Color MuteOn = Color.Parse("#E04B4B");
    public static readonly Color SoloOn = Color.Parse("#E8B02C");
    public static readonly Color EnvelopeHandleEdge = Color.Parse("#302814");
    public static readonly Color SpcTraceBackground = Color.Parse("#18181C");
    public static readonly Color SpcTraceRow = Color.Parse("#1C1C20");
    public static readonly Color SpcTraceAlternateRow = Color.Parse("#202026");
    public static readonly Color SpcTraceOctaveLine = Color.Parse("#2E2E38");
    public static readonly Color SpcPitchSemitoneTick = Color.Parse("#484852");
    public static readonly Color SpcPitchOctaveTick = Color.Parse("#80808C");

    // The driver's channels' colors, red at the first through to violet at the last.
    public static readonly Color[] Channels =
    [
        Color.Parse("#E85858"),
        Color.Parse("#E8BA58"),
        Color.Parse("#B5E858"),
        Color.Parse("#58E85D"),
        Color.Parse("#58E8BF"),
        Color.Parse("#58AFE8"),
        Color.Parse("#6258E8"),
        Color.Parse("#C458E8"),
    ];

    // The wash inside a selection sweep in progress on the roll, the selection's outline color at a low opacity.
    public static readonly Color RollSelectionSweep = ChannelPalette.WithAlpha(PaleGray, RollSelectionSweepAlpha);

    // The area under the envelope graph's curve, the trace color at a low opacity.
    public static readonly Color EnvelopeCurveFill = ChannelPalette.WithAlpha(SkyBlue, EnvelopeCurveFillAlpha);

    // The envelope graph's faint curves, one per envelope where a selection holds several, the trace color half see-through.
    public static readonly Color EnvelopeOverlayCurve = ChannelPalette.WithAlpha(SkyBlue, EnvelopeOverlayCurveAlpha);

    // The wash over the waveform's block selection, the playhead's color at a low opacity.
    public static readonly Color WaveformSelection = ChannelPalette.WithAlpha(Amber, WaveformSelectionAlpha);

    // The clear end of the fade where a panel's content moves up under its header, the panel's own background with no opacity.
    public static readonly Color PanelFadeClear = ChannelPalette.WithAlpha(PanelBackground, PanelFadeClearAlpha);

    // The Envelope sections' banner, the plain banner's gray brightened and leaned towards blue.
    public static readonly Color EnvelopeSectionBanner = ChannelPalette.AtHue(ControlBackground, SectionTintLift, SectionTintChroma, BlueHue);

    // The Envelope sections' open body, the plain body's gray brightened and leaned towards blue.
    public static readonly Color EnvelopeSectionBody = ChannelPalette.AtHue(SunkenBackground, SectionTintLift, SectionTintChroma, BlueHue);

    // The Volume sections' banner, the plain banner's gray brightened and leaned towards yellow.
    public static readonly Color VolumeSectionBanner = ChannelPalette.AtHue(ControlBackground, SectionTintLift, SectionTintChroma, YellowHue);

    // The Volume sections' open body, the plain body's gray brightened and leaned towards yellow.
    public static readonly Color VolumeSectionBody = ChannelPalette.AtHue(SunkenBackground, SectionTintLift, SectionTintChroma, YellowHue);

    // The Pan sections' banner, the plain banner's gray brightened and leaned towards red.
    public static readonly Color PanSectionBanner = ChannelPalette.AtHue(ControlBackground, SectionTintLift, SectionTintChroma, RedHue);

    // The Pan sections' open body, the plain body's gray brightened and leaned towards red.
    public static readonly Color PanSectionBody = ChannelPalette.AtHue(SunkenBackground, SectionTintLift, SectionTintChroma, RedHue);

    // The Pitch Bend sections' banner, the plain banner's gray brightened and leaned towards green.
    public static readonly Color BendSectionBanner = ChannelPalette.AtHue(ControlBackground, SectionTintLift, SectionTintChroma, GreenHue);

    // The Pitch Bend sections' open body, the plain body's gray brightened and leaned towards green.
    public static readonly Color BendSectionBody = ChannelPalette.AtHue(SunkenBackground, SectionTintLift, SectionTintChroma, GreenHue);

    // The Vibrato sections' banner, the plain banner's gray brightened and leaned towards orange.
    public static readonly Color VibratoSectionBanner = ChannelPalette.AtHue(ControlBackground, SectionTintLift, SectionTintChroma, OrangeHue);

    // The Vibrato sections' open body, the plain body's gray brightened and leaned towards orange.
    public static readonly Color VibratoSectionBody = ChannelPalette.AtHue(SunkenBackground, SectionTintLift, SectionTintChroma, OrangeHue);

    // The Tremolo sections' banner, the plain banner's gray brightened and leaned towards purple.
    public static readonly Color TremoloSectionBanner = ChannelPalette.AtHue(ControlBackground, SectionTintLift, SectionTintChroma, PurpleHue);

    // The Tremolo sections' open body, the plain body's gray brightened and leaned towards purple.
    public static readonly Color TremoloSectionBody = ChannelPalette.AtHue(SunkenBackground, SectionTintLift, SectionTintChroma, PurpleHue);

    // A lane cell's yellow.
    public static readonly Color PastelYellow = ChannelPalette.FromOklch(LaneCellLightness, LaneCellChroma, YellowHue);

    // A selected lane cell's yellow.
    public static readonly Color PaleYellow = ChannelPalette.FromOklch(LaneCellSelectedLightness, LaneCellSelectedChroma, YellowHue);

    // A lane cell's blue.
    public static readonly Color PastelBlue = ChannelPalette.FromOklch(LaneCellLightness, LaneCellChroma, BlueHue);

    // A selected lane cell's blue.
    public static readonly Color PaleBlue = ChannelPalette.FromOklch(LaneCellSelectedLightness, LaneCellSelectedChroma, BlueHue);

    // A cell of the tempo lane, in green.
    public static readonly Color TempoCell = ChannelPalette.FromOklch(LaneCellLightness, LaneCellChroma, GreenHue);

    // A selected cell of the tempo lane, a lighter green.
    public static readonly Color TempoCellSelected = ChannelPalette.FromOklch(LaneCellSelectedLightness, LaneCellSelectedChroma, GreenHue);

    // A cell of the pan lane, in red.
    public static readonly Color PanCell = ChannelPalette.FromOklch(LaneCellLightness, LaneCellChroma, RedHue);

    // A selected cell of the pan lane, a lighter red.
    public static readonly Color PanCellSelected = ChannelPalette.FromOklch(LaneCellSelectedLightness, LaneCellSelectedChroma, RedHue);

    // A cell of the amplify lane, in orange.
    public static readonly Color AmplifyCell = ChannelPalette.FromOklch(LaneCellLightness, LaneCellChroma, OrangeHue);

    // A selected cell of the amplify lane, a lighter orange.
    public static readonly Color AmplifyCellSelected = ChannelPalette.FromOklch(LaneCellSelectedLightness, LaneCellSelectedChroma, OrangeHue);

    // A cell of the staccato lane, in purple.
    public static readonly Color StaccatoCell = ChannelPalette.FromOklch(LaneCellLightness, LaneCellChroma, PurpleHue);

    // A selected cell of the staccato lane, a lighter purple.
    public static readonly Color StaccatoCellSelected = ChannelPalette.FromOklch(LaneCellSelectedLightness, LaneCellSelectedChroma, PurpleHue);

    // The shadow a popup casts on what is under it, in the popup shadow's color.
    public static readonly BoxShadows PopupShadow = new(new BoxShadow { OffsetY = PopupShadowOffsetY, Blur = PopupShadowBlur, Color = PopupShadowColor });

    // How far the disabled text color stands from the primary text color towards the panel's background.
    private static readonly double DisabledTextFade = (PaleGray.R - TextDisabled.R) / (double)(PaleGray.R - PanelBackground.R);

    // A text color dulled as far towards the panel's background as the disabled text color is from the primary one.
    public static Color AsDisabledText(Color textColor)
    {
        return ChannelPalette.Blend(textColor, PanelBackground, DisabledTextFade);
    }
}
