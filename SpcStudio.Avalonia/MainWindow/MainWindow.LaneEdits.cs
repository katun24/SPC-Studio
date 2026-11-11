using Avalonia.Controls;
using SpcStudio.Controls;
using SpcStudio.Model;

namespace SpcStudio;

// What an edit on a lane writes into the song: a region lane's dividers placed, moved, removed and typed into, and a lane of units' edits.
public partial class MainWindow
{
    private const int NoFadeDestination = 0;

    // The panel rows showing and setting what a lane holds at the song's start: the Song panel's song volume, tempo and base echo, and the channel
    // panel's volume and pan.
    private LaneStartField[] _laneStartFields = [];

    // Every lane of units, which the four editing keys ask in turn.
    private IUnitLane[] UnitLanes
    {
        get
        {
            return [Staccatos, ChannelEchoes, EchoUnits];
        }
    }

    // The check boxes saying whether a lane of units holds a unit at the song's start: the Song panel's Base Staccato and the channel panel's Base Echo.
    private (IUnitLane Lane, CheckBox Check)[] StartUnitChecks
    {
        get
        {
            return [(Staccatos, BaseStaccatoCheck), (ChannelEchoes, ChannelEchoCheck)];
        }
    }

    // Shows the dragged notes in the note panel, moves the note loop along with a drag, and re-aims the pitch of each dragged note the playhead is inside while the song plays.
    private void OnNotesDragged(IReadOnlyList<(int Channel, Note Note)> dragged)
    {
        ShowNotePanel();
        RefreshNoteRun();
        if (_audio is not { } audio || PlayingTick() is not double tick)
        {
            return;
        }

        foreach (var (channel, note) in dragged)
        {
            if (!note.Contains((int)tick) || _song.InstrumentOf(note) is not { } instrument)
            {
                continue;
            }

            audio.Mixer.ReaimVoicePitch(channel, SnesPitch.NotePitch.Of(note, instrument, _song.SoundingTuningOf(instrument)), note.Bends);
        }
    }

    // Writes a region lane's value at a tick with its fade, the pan lane's surround pair and the echo volume lane's right side, which null leaves at the base's.
    private void SetChange(ChangeLane lane, int tick, int value, int fadeTicks, int fadeTo, SnesSurround surround, int? echoRightVolume, int echoRightFadeTo)
    {
        bool fades = SnesFade.DoesFade(fadeTicks);
        if (lane == Tempos)
        {
            SetTempoRegion(tick, value, fades, fadeTo);
        }
        else if (lane == Volumes)
        {
            SetMasterVolumeRegion(tick, value, fades, fadeTo);
        }
        else if (lane == ChannelVolumes)
        {
            SetChannelVolumeRegion(tick, value, fades, fadeTo);
        }
        else if (lane == EchoVolumes)
        {
            SetEchoVolumeRegion(tick, value, fades, fadeTo, echoRightVolume, echoRightFadeTo);
        }
        else if (lane == Pans)
        {
            SetPanRegion(tick, value, fades, fadeTo, surround);
        }
    }

    // Writes the tempo lane's region starting at a tick.
    private void SetTempoRegion(int tick, int value, bool fades, int fadeTo)
    {
        SetRegion(tick, value, fades, fadeTo, SnesTiming.TempoRange, baseValue => _song.BaseTempo = baseValue,
            (changeTick, changeValue, changeFades, changeFadeTo) => _song.SetTempoChange(changeTick, changeValue, changeFades, changeFadeTo), _song.RemoveTempoChange);
    }

    // Writes the song volume lane's region starting at a tick.
    private void SetMasterVolumeRegion(int tick, int value, bool fades, int fadeTo)
    {
        SetRegion(tick, value, fades, fadeTo, SnesVolume.MasterVolumeRange, baseValue => _song.BaseMasterVolume = baseValue,
            (changeTick, changeValue, changeFades, changeFadeTo) => _song.SetVolumeChange(changeTick, changeValue, changeFades, changeFadeTo), _song.RemoveVolumeChange);
    }

    // Writes the selected channel's volume lane's region starting at a tick.
    private void SetChannelVolumeRegion(int tick, int value, bool fades, int fadeTo)
    {
        int channel = ChannelVolumes.Channel;
        SetRegion(tick, value, fades, fadeTo, SnesVolume.ChannelVolumeRange, baseValue => _song.SetBaseChannelVolume(channel, baseValue),
            (changeTick, changeValue, changeFades, changeFadeTo) => _song.SetChannelVolumeChange(channel, changeTick, changeValue, changeFades, changeFadeTo),
            changeTick => _song.RemoveChannelVolumeChange(channel, changeTick));
    }

    // Writes the echo volume lane's region starting at a tick, both sides together, the right side keeping the base's where none is given.
    private void SetEchoVolumeRegion(int tick, int leftValue, bool fades, int leftFadeTo, int? rightVolume, int rightFadeTo)
    {
        int rightValue = rightVolume ?? _song.BaseEchoVolume(EchoSetting.VolumeRight);
        SetRegion(tick, leftValue, fades, leftFadeTo, SnesEcho.VolumeRange,
            baseValue =>
            {
                _song.SetBaseEchoVolume(EchoSetting.VolumeLeft, baseValue);
                _song.SetBaseEchoVolume(EchoSetting.VolumeRight, rightValue);
            },
            (changeTick, changeValue, changeFades, changeFadeTo) =>
                _song.SetEchoVolumeChange(changeTick, changeValue, rightValue, changeFades, changeFadeTo, changeFades ? rightFadeTo : NoFadeDestination),
            _song.RemoveEchoVolumeChange);
    }

    // Writes the selected channel's pan lane's region starting at a tick, with the surround pair it holds.
    private void SetPanRegion(int tick, int value, bool fades, int fadeTo, SnesSurround surround)
    {
        int channel = Pans.Channel;
        SetRegion(tick, value, fades, fadeTo, SnesVolume.PanIndexRange,
            baseValue =>
            {
                _song.SetBasePan(channel, baseValue);
                _song.SetBaseSurround(channel, surround);
            },
            (changeTick, changeValue, changeFades, changeFadeTo) => _song.SetPanChange(channel, changeTick, changeValue, changeFades, changeFadeTo, surround),
            changeTick => _song.RemovePanChange(channel, changeTick));
    }

    // Writes one region lane's region: a change past tick 0, and at tick 0 the base held to its range with a change only for the fade.
    private static void SetRegion(int tick, int value, bool fades, int fadeTo, ValueRange range, Action<int> setBase, Action<int, int, bool, int> setChange, Func<int, bool> removeChange)
    {
        if (tick != 0)
        {
            setChange(tick, value, fades, fadeTo);
            return;
        }

        setBase(range.Clamp(value));
        if (fades)
        {
            setChange(0, value, true, fadeTo);
            return;
        }

        removeChange(0);
    }

    // Takes a region lane's divider at a tick away.
    private void RemoveChange(ChangeLane lane, int tick)
    {
        if (lane == Tempos)
        {
            _song.RemoveTempoChange(tick);
        }
        else if (lane == Volumes)
        {
            _song.RemoveVolumeChange(tick);
        }
        else if (lane == ChannelVolumes)
        {
            _song.RemoveChannelVolumeChange(ChannelVolumes.Channel, tick);
        }
        else if (lane == Pans)
        {
            _song.RemovePanChange(Pans.Channel, tick);
        }
        else if (lane == EchoVolumes)
        {
            _song.RemoveEchoVolumeChange(tick);
        }
    }

    // Makes a lane edit and records it, carrying the playhead across a tempo edit, and brings the panel rows showing the lane and the note panel along.
    private void ApplyLaneEdit(ChangeLane lane, object? source, Action edit)
    {
        if (lane == Tempos)
        {
            ApplyTimingEdit(source, edit);
        }
        else
        {
            edit();
            MarkModified(source);
        }

        ShowPanelRowsOf(lane, source);
        ShowNotePanel();
    }

    // Ties each lane-start row's slider to its field, applies the field as it is typed, steps it from the song's own value by the arrows and the wheel,
    // puts the song's value back once the field loses the focus, and gives the field the default a right-click puts it back at.
    private void WireLaneStartFields()
    {
        _laneStartFields =
        [
            FirstRegionField(Volumes, VolumeSlider, VolumeBox),
            FirstRegionField(Tempos, TempoSlider, TempoBox),
            FirstRegionField(ChannelVolumes, ChannelVolumeSlider, ChannelVolumeBox),
            FirstRegionField(Pans, ChannelPanSlider, ChannelPanBox),
            .. BaseEchoFields(),
        ];
        foreach (var laneStartField in _laneStartFields)
        {
            SliderField.Tie(laneStartField.Slider, laneStartField.Box, laneStartField.Range);
            FieldResets.ResetsTo(laneStartField.Box, () => laneStartField.DefaultValue());
            FieldSteps.OnKeys(laneStartField.Box, (_, e) => StepSongValueField(laneStartField.Box, laneStartField.SongValue(), laneStartField.Range, e));
            laneStartField.Box.TextChanged += (_, _) => OnLaneStartFieldTyped(laneStartField);
            laneStartField.Box.LostFocus += (_, _) => ShowLaneStartField(laneStartField);
        }
    }

    // A row showing and setting a lane's first region, its range, its default and its value the lane's own.
    private LaneStartField FirstRegionField(ChangeLane lane, Slider slider, TextBox box)
    {
        return new(lane, slider, box, lane.ValueRange, () => lane.BaseValue, () => lane.DefaultValue, (source, value) => ApplyFirstRegion(lane, source, value, null, null));
    }

    // Applies a lane-start row's value as it is typed, a value out of its range changing nothing.
    private void OnLaneStartFieldTyped(LaneStartField laneStartField)
    {
        if (_isWritingFieldsFromSong || !FieldInput.TryReadValue(laneStartField.Box.Text, laneStartField.Range, out int value))
        {
            return;
        }

        laneStartField.WriteValue(laneStartField.Box, value);
    }

    // Writes the song's value into a lane-start row's field.
    private void ShowLaneStartField(LaneStartField laneStartField)
    {
        FlagGuard.RunWithFlagSet(ref _isWritingFieldsFromSong, () => laneStartField.Box.Text = FieldInput.TextOf(laneStartField.SongValue()));
    }

    // Writes what a lane holds at the song's start into the panel rows showing it, leaving alone the control the edit came from: its lane-start rows, its
    // start-unit check box, for the pan lane the Base Surround drop-down and for the echo unit lane the rest of BASE ECHO.
    private void ShowPanelRowsOf(ChangeLane lane, object? source)
    {
        foreach (var laneStartField in _laneStartFields.Where(candidateField => candidateField.Lane == lane && !ReferenceEquals(source, candidateField.Box)))
        {
            ShowLaneStartField(laneStartField);
        }

        foreach (var (unitLane, check) in StartUnitChecks.Where(entry => ReferenceEquals(entry.Lane, lane) && !ReferenceEquals(source, entry.Check)))
        {
            ShowStartUnitCheck(unitLane, check);
        }

        if (lane == Pans && !ReferenceEquals(source, ChannelSurroundBox))
        {
            ShowChannelSurround();
        }
        else if (lane == EchoUnits)
        {
            ShowBaseEchoRun(source);
        }
    }

    // Writes a lane's first region from a panel, with the surround pair the pan lane takes and the right side the echo volume lane takes - each left as it
    // stands where none is given - keeping the fade out of it, and shows it on the lane and in a cell editor open on that region.
    private void ApplyFirstRegion(ChangeLane lane, Control source, int value, SnesSurround? surround, int? echoRightVolume)
    {
        var fade = lane.FadeAt(0);
        var surroundPair = surround ?? _song.BaseSurround(Pans.Channel);
        int rightVolume = echoRightVolume ?? _song.BaseEchoVolume(EchoSetting.VolumeRight);
        int rightFadeTo = _song.EchoChangeAt(EchoSetting.VolumeRight, 0)?.FadeTo ?? NoFadeDestination;
        ApplyLaneEdit(lane, source, () => SetChange(lane, 0, value, fade?.Ticks ?? SnesFade.NoFade, fade?.To ?? NoFadeDestination, surroundPair, rightVolume, rightFadeTo));
        lane.Refresh();
        ReopenCellOnSongStart(lane);
    }

    // Opens the cell editor again where it stands on a lane's cell at the song's start, which writes a value a panel row has just changed into its boxes.
    private void ReopenCellOnSongStart(ChangeLane lane)
    {
        if (ReferenceEquals(_cellLane, lane) && _cellTick == 0)
        {
            ReopenCell(lane, 0, Roll.SelectedChannel, doesTakeFocus: false);
        }
    }

    // Splits a region where the lane was right-clicked.
    private void OnChangePlaced(ChangeLane lane, int tick)
    {
        PlaceDivider(lane, tick);
        lane.Refresh();
    }

    // Splits a region at a tick with a flat region starting at the value the split region ends on, the split region left as it was, recorded under the
    // lane so the drag the same press holds it for joins its step.
    private void PlaceDivider(ChangeLane lane, int tick)
    {
        var regions = OwnRegionsOf(lane);
        int regionIndexAtTick = regions.FindIndex(region => region.Start <= tick && tick < region.End);
        if (regionIndexAtTick < 0 || regions[regionIndexAtTick].End <= regions[regionIndexAtTick].Start)
        {
            return;
        }

        int value = regions[regionIndexAtTick].EndValue;
        var splitRegionSurround = lane == Pans ? LaneRegions.PanOwn(_song, Pans.Channel)[regionIndexAtTick].Surround : default;
        int? splitRegionEchoRightEnd = lane == EchoVolumes ? LaneRegions.EchoVolume(_song)[regionIndexAtTick].Right.EndValue : null;
        ApplyLaneEdit(lane, lane, () => SetChange(lane, tick, value, SnesFade.NoFade, NoFadeDestination, splitRegionSurround, splitRegionEchoRightEnd, NoFadeDestination));
    }

    // The lane's own regions, without the notes laid over it, for whichever of the region lanes this is; empty for any other lane.
    private List<LaneRegion> OwnRegionsOf(ChangeLane lane)
    {
        return lane == Tempos ? [.. LaneRegions.Tempo(_song)]
            : lane == Volumes ? [.. LaneRegions.MasterVolume(_song)]
            : lane == ChannelVolumes ? [.. LaneRegions.ChannelVolumeOwn(_song, ChannelVolumes.Channel)]
            : lane == Pans ? [.. LaneRegions.PanOwn(_song, Pans.Channel).Select(pair => pair.Region)]
            : lane == EchoVolumes ? [.. LaneRegions.EchoVolume(_song).Select(sides => sides.Left)]
            : [];
    }

    // Takes away the divider a lane asked to remove.
    private void OnChangeRemoved(ChangeLane lane, int tick)
    {
        ApplyLaneEdit(lane, null, () => RemoveChange(lane, tick));
        lane.Refresh();
    }

    // Moves a divider to the tick a drag has carried it to, with its fade, its surround pair and its right side, recorded under the lane so a divider
    // the same press placed and every step of the drag are one undo step.
    private void OnChangeMoved(ChangeLane lane, int fromTick, int toTick)
    {
        if (lane.ChangeValueAt(fromTick) is not int value)
        {
            return;
        }

        var fade = lane.FadeAt(fromTick);
        var dividerSurround = lane == Pans ? _song.PanChangeAt(Pans.Channel, fromTick)?.Surround ?? SnesSurround.None : default;
        var dividerEchoRightChange = lane == EchoVolumes ? _song.EchoChangeAt(EchoSetting.VolumeRight, fromTick) : null;
        ApplyLaneEdit(lane, lane, () =>
        {
            RemoveChange(lane, fromTick);
            SetChange(lane, toTick, value, fade?.Ticks ?? SnesFade.NoFade, fade?.To ?? value, dividerSurround, dividerEchoRightChange?.Value, dividerEchoRightChange?.FadeTo ?? NoFadeDestination);
        });

        lane.Refresh();
    }

    // Keeps the cell editor over a cell being dragged along its lane, remembering the tick it has reached.
    private void OnCellDragged(ChangeLane lane, int? tick)
    {
        if (!ReferenceEquals(_cellLane, lane))
        {
            return;
        }

        _cellDragTick = tick;
        PlaceCellEditor();
    }

    // The tick the cursor is over on the roll or any lane, or null where it is over none of them.
    private int? CursorTick()
    {
        if (Roll.CursorTick is int rollTick)
        {
            return rollTick;
        }

        foreach (var lane in _lanes)
        {
            if (lane.CursorTick is int laneTick)
            {
                return laneTick;
            }
        }

        return null;
    }

    // The lane of units holding the selection, or null where none does.
    private IUnitLane? UnitLaneHoldingSelection()
    {
        return UnitLanes.FirstOrDefault(lane => lane.HasSelection);
    }

    // The lane of units holding something copied, or null where none does.
    private IUnitLane? UnitLaneHoldingClipboard()
    {
        return UnitLanes.FirstOrDefault(lane => lane.HasClipboard);
    }

    // Drops the selection on every lane of units.
    private void ClearUnitSelections()
    {
        foreach (var lane in UnitLanes)
        {
            lane.ClearUnitSelection();
        }
    }

    // Drops every lane of units' selection where a lane of regions has selected a region.
    private void OnRegionSelected(ChangeLane lane, int? tick)
    {
        if (tick is null)
        {
            return;
        }

        ClearUnitSelections();
    }

    // Takes the light staccato units a lane edit removed away and puts the ones it placed down, as one undo step.
    private void OnStaccatoEdited(UnitLane<StaccatoUnit> lane, IReadOnlyList<int> removed, IReadOnlyList<StaccatoUnit> placed, object? undoSource)
    {
        ApplyUnitLaneEdit(lane, undoSource, () => _song.EditStaccato(removed, placed));
    }

    // Takes the echo send units a lane edit removed away and puts the ones it placed down, as one undo step.
    private void OnChannelEchoEdited(UnitLane<EchoSendUnit> lane, IReadOnlyList<int> removed, IReadOnlyList<EchoSendUnit> placed, object? undoSource)
    {
        ApplyUnitLaneEdit(lane, undoSource, () => _song.EditChannelEcho(ChannelEchoes.Channel, removed, placed));
    }

    // Takes the echo runs a lane edit removed away and puts the ones it placed down, dropping the filters no run uses any more once no drag is running,
    // as one undo step.
    private void OnEchoUnitEdited(UnitLane<EchoRun> lane, IReadOnlyList<int> removed, IReadOnlyList<EchoRun> placed, object? undoSource)
    {
        ApplyUnitLaneEdit(lane, undoSource, () =>
        {
            if (!_song.EditEchoRun(removed, placed))
            {
                return false;
            }

            if (!lane.IsDragging)
            {
                _song.CompactFilters();
            }

            return true;
        });
    }

    // Makes a lane of units' edit and, where it changed the song, redraws the lane and the roll, records it under the source it came with and shows
    // the panel rows and the note panel again.
    private void ApplyUnitLaneEdit(ChangeLane lane, object? undoSource, Func<bool> edit)
    {
        if (!edit())
        {
            return;
        }

        lane.Refresh();
        Roll.Refresh();
        MarkModified(undoSource);
        ShowPanelRowsOf(lane, undoSource);
        ShowNotePanel();
    }

    // Puts a unit at its lane's start or takes it away as each start-unit check box is ticked or unticked.
    private void WireStartUnitChecks()
    {
        foreach (var (lane, check) in StartUnitChecks)
        {
            check.IsCheckedChanged += (_, _) => OnStartUnitCheckToggled(lane, check);
        }
    }

    // Puts a unit at a lane's start or takes it away as its check box asks, as an undo step of its own, unless the box is being written from the song.
    private void OnStartUnitCheckToggled(IUnitLane lane, CheckBox check)
    {
        if (_isWritingFieldsFromSong)
        {
            return;
        }

        lane.SetStartUnit(check.IsChecked == true, check);
    }

    // Writes whether a lane holds a unit at the song's start into its check box.
    private void ShowStartUnitCheck(IUnitLane lane, CheckBox check)
    {
        FlagGuard.RunWithFlagSet(ref _isWritingFieldsFromSong, () => check.IsChecked = lane.HasStartUnit);
    }

    // Redraws the roll and brings its extent along after a lane drag or a marker changed how long the roll is.
    private void RefreshRollAndExtent()
    {
        Roll.Refresh();
        RefreshRollExtent();
    }

    // Closes the cell editor and redraws every lane after the song was changed from outside them.
    private void RefreshLanes()
    {
        CloseCell();
        RefreshEachLane();
    }

    // Redraws the ruler, the markers and every lane, which share the roll's columns.
    private void RefreshRulerAndLanes()
    {
        RefreshRulerAndMarkers();
        RefreshEachLane();
    }

    // Redraws the ruler and the markers after the song or the zoom changed.
    private void RefreshRulerAndMarkers()
    {
        Ruler.Refresh();
        Markers.InvalidateVisual();
    }

    // Redraws every lane.
    private void RefreshEachLane()
    {
        foreach (var lane in _lanes)
        {
            lane.Refresh();
        }
    }
}
