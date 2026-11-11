using Avalonia.Controls;
using SpcStudio.Controls;
using SpcStudio.Model;

namespace SpcStudio;

// The Song panel's BASE ECHO: the Echo Unit cell at the song's start, put down by the banner's '+' and taken away by its revert, its delay, feedback
// and filter, and the Echo Volume lane's first region.
public partial class MainWindow
{
    private const int StartRunRowCount = 1;
    private const int NoStartRunRowCount = 0;

    // BASE ECHO's four slider rows: the start cell's delay and feedback, and the echo volume's two sides at the song's start.
    private LaneStartField[] BaseEchoFields()
    {
        return
        [
            BaseEchoRunField(EchoSetting.Delay, BaseEchoDelaySlider, BaseEchoDelayBox),
            BaseEchoRunField(EchoSetting.Feedback, BaseEchoFeedbackSlider, BaseEchoFeedbackBox),
            FirstRegionField(EchoVolumes, BaseEchoLeftSlider, BaseEchoLeftBox),
            new(EchoVolumes, BaseEchoRightSlider, BaseEchoRightBox, EchoVolumes.ValueRange, () => _song.BaseEchoVolume(EchoSetting.VolumeRight),
                () => SnesEcho.Default(EchoSetting.VolumeRight), (source, value) => ApplyFirstRegion(EchoVolumes, source, EchoVolumes.BaseValue, null, value)),
        ];
    }

    // A row showing and setting one setting of the Echo Unit cell at the song's start, its range and default the driver's.
    private LaneStartField BaseEchoRunField(EchoSetting setting, Slider slider, TextBox box)
    {
        return new(EchoUnits, slider, box, SnesEcho.Range(setting), () => _song.EchoUnitStateAt(0)[setting], () => SnesEcho.Default(setting),
            (source, value) => ApplyBaseEchoRunSetting(setting, value, source));
    }

    // Hooks up the banner's '+' and revert, and the coefficient boxes' typing and their losing the focus.
    private void WireBaseEcho()
    {
        BaseEchoSection.AddRequested += (_, _) => EchoUnits.SetStartUnit(true, BaseEchoSection);
        BaseEchoSection.RevertRequested += (_, _) => EchoUnits.SetStartUnit(false, BaseEchoSection);
        BaseEchoFilter.Typed += OnBaseEchoFilterTyped;
        BaseEchoFilter.LostFocus += (_, _) => ShowBaseEchoRun(null);
    }

    // Writes whether the Echo Unit lane holds a cell at the song's start into the banner, and that cell's filter into the coefficient boxes, leaving alone
    // the box an edit came from.
    private void ShowBaseEchoRun(object? source)
    {
        bool hasStartRun = EchoUnits.HasStartUnit;
        BaseEchoSection.RowCount = hasStartRun ? StartRunRowCount : NoStartRunRowCount;
        BaseEchoSection.CanRevert = hasStartRun;
        FlagGuard.RunWithFlagSet(ref _isWritingFieldsFromSong, () => BaseEchoFilter.Show(_song.FilterCoefficients(_song.EchoUnitStateAt(0).Filter), source));
    }

    // Points the cell at the song's start at the filter the coefficient boxes describe, as they are typed.
    private void OnBaseEchoFilterTyped(TextBox box)
    {
        if (_isWritingFieldsFromSong || FilterIndexIn(BaseEchoFilter) is not int filterIndex)
        {
            return;
        }

        ApplyBaseEchoRunSetting(EchoSetting.Filter, filterIndex, box);
    }

    // Writes one setting of the Echo Unit cell at the song's start, and into a cell editor open on that cell.
    private void ApplyBaseEchoRunSetting(EchoSetting setting, int value, Control source)
    {
        ApplyEchoRunSetting(0, setting, value, source);
        ReopenCellOnSongStart(EchoUnits);
    }
}
