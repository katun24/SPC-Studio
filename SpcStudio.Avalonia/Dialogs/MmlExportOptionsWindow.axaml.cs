using Avalonia.Controls;
using Avalonia.Interactivity;
using SpcStudio.Controls;
using SpcStudio.Export;

namespace SpcStudio;

// The box that turns each of the MML export's optimizations on and off and sets how its lines are broken, reporting the whole set as each one changes.
public partial class MmlExportOptionsWindow : DialogWindow
{
    private const int LineLengthLimitStep = 1;

    private MmlExportOptions _options = MmlExportOptions.Default;
    private readonly BoundedField _lineLengthLimitField;
    private readonly BoundedField _barsPerLineField;

    // Builds the box.
    public MmlExportOptionsWindow()
    {
        InitializeComponent();
        _lineLengthLimitField = BoundedField.ForNumber(LineLengthLimitBox, MmlExportOptions.LineLengthLimitRange, MmlExportOptions.DefaultLineLengthLimit, () => LineLengthLimitStep);
        _barsPerLineField = BoundedField.ForNumberOrNone(BarsPerLineBox, MmlExportOptions.BarsPerLineRange);
    }

    // Builds the box showing the options given.
    public MmlExportOptionsWindow(MmlExportOptions options) : this()
    {
        _options = options;
        LoopsBox.IsChecked = options.Loops;
        TransposedLoopsBox.IsChecked = options.TransposedLoops;
        LongNotesAndRestsAsLoopsBox.IsChecked = options.LongNotesAndRestsAsLoops;
        ShowWhetherLoopOptionsApply();
        KeyOnRemoteCodeBox.IsChecked = options.KeyOnRemoteCode;
        RepeatedMidNoteCommandsAsRemoteCodeBox.IsChecked = options.RepeatedMidNoteCommandsAsRemoteCode;
        HeldNotesAsLegatoBox.IsChecked = options.HeldNotesAsLegato;
        ArpeggiosAsFbBox.IsChecked = options.ArpeggiosAsFb;
        PitchEnvelopesBox.IsChecked = options.PitchEnvelopes;
        SingleChannelEchoChangesAsF403Box.IsChecked = options.SingleChannelEchoChangesAsF403;
        FuseSongLoopEndWithIntroBox.IsChecked = options.FuseSongLoopEndWithIntro;
        ShortenSongLoopToOneRepeatBox.IsChecked = options.ShortenSongLoopToOneRepeat;
        TrimOneShotSamplesBox.IsChecked = options.TrimOneShotSamples;
        _lineLengthLimitField.WriteValue(options.LineLengthLimit);
        if (options.BarsPerLine is int barsPerLine)
        {
            _barsPerLineField.WriteValue(barsPerLine);
        }

        _lineLengthLimitField.ValueChanged += OnLineFieldChanged;
        _barsPerLineField.ValueChanged += OnLineFieldChanged;
    }

    // Raised with the whole set as a box is ticked or a field changes.
    public event Action<MmlExportOptions>? OptionsChanged;

    // Reads every box into the set and reports it.
    private void OnOptionBoxChanged(object? sender, RoutedEventArgs e)
    {
        _options = _options with
        {
            Loops = LoopsBox.IsChecked == true,
            TransposedLoops = TransposedLoopsBox.IsChecked == true,
            LongNotesAndRestsAsLoops = LongNotesAndRestsAsLoopsBox.IsChecked == true,
            KeyOnRemoteCode = KeyOnRemoteCodeBox.IsChecked == true,
            RepeatedMidNoteCommandsAsRemoteCode = RepeatedMidNoteCommandsAsRemoteCodeBox.IsChecked == true,
            HeldNotesAsLegato = HeldNotesAsLegatoBox.IsChecked == true,
            ArpeggiosAsFb = ArpeggiosAsFbBox.IsChecked == true,
            PitchEnvelopes = PitchEnvelopesBox.IsChecked == true,
            SingleChannelEchoChangesAsF403 = SingleChannelEchoChangesAsF403Box.IsChecked == true,
            FuseSongLoopEndWithIntro = FuseSongLoopEndWithIntroBox.IsChecked == true,
            ShortenSongLoopToOneRepeat = ShortenSongLoopToOneRepeatBox.IsChecked == true,
            TrimOneShotSamples = TrimOneShotSamplesBox.IsChecked == true,
        };
        ShowWhetherLoopOptionsApply();
        OptionsChanged?.Invoke(_options);
    }

    // Grays the boxes of the loops' own variations out while the loops' box is unticked: the transposed loops and the long notes and rests as loops.
    private void ShowWhetherLoopOptionsApply()
    {
        bool areLoopsWritten = LoopsBox.IsChecked == true;
        TransposedLoopsBox.IsEnabled = areLoopsWritten;
        LongNotesAndRestsAsLoopsBox.IsEnabled = areLoopsWritten;
    }

    // Reads both line fields into the set and reports it, the bar field's none reading as no breaks by bars.
    private void OnLineFieldChanged()
    {
        _options = _options with
        {
            LineLengthLimit = _lineLengthLimitField.Value,
            BarsPerLine = _barsPerLineField.Value,
        };
        OptionsChanged?.Invoke(_options);
    }
}
