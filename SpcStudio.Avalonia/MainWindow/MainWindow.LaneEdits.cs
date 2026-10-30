using Avalonia.Controls;
using SpcStudio.Controls;
using SpcStudio.Model;

namespace SpcStudio;

// What an edit on a lane writes into the song: a region lane's dividers placed, moved, removed and typed into, and a lane of units' edits.
public partial class MainWindow
{
    private const int NoFadeDestination = 0;

    // Every lane of units, which the four editing keys ask in turn.
    private IUnitLane[] UnitLanes
    {
        get
        {
            return [Staccatos, ChannelEchoes, EchoUnits];
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

            audio.Mixer.ReaimVoicePitch(channel, SnesPitch.NotePitch.Of(note, _song.SoundingTuningOf(instrument)), note.Bends);
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
        else if (lane == Amplifies)
        {
            SetAmplifyRegion(tick, value);
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

    // Writes the selected channel's amplify lane's region starting at a tick, which is always flat.
    private void SetAmplifyRegion(int tick, int value)
    {
        int channel = Amplifies.Channel;
        SetRegion(tick, value, false, NoFadeDestination, SnesVolume.AmplifyRange, baseValue => _song.SetBaseAmplify(channel, baseValue),
            (changeTick, changeValue, _, _) => _song.SetAmplifyChange(channel, changeTick, changeValue), changeTick => _song.RemoveAmplifyChange(channel, changeTick));
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
        else if (lane == Amplifies)
        {
            _song.RemoveAmplifyChange(Amplifies.Channel, tick);
        }
        else if (lane == EchoVolumes)
        {
            _song.RemoveEchoVolumeChange(tick);
        }
    }

    // Makes a lane edit and records it, carrying the playhead across a tempo edit, and brings the lane's Song panel field and the note panel along.
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

        ShowSongValueFieldOf(lane, source);
        ShowNotePanel();
    }

    // Writes a song-wide lane's first region into its Song panel field, unless that field is what the edit was typed into.
    private void ShowSongValueFieldOf(ChangeLane lane, object? source)
    {
        if (lane == Tempos && !ReferenceEquals(source, TempoBox))
        {
            ShowTempoField();
        }
        else if (lane == Volumes && !ReferenceEquals(source, VolumeBox))
        {
            ShowMasterVolumeField();
        }
    }

    // Writes a value typed into the Song panel into a song-wide lane's first region, keeping the fade out of it, and shows it on the lane and in a cell editor open on that region.
    private void ApplyFirstRegionValue(ChangeLane lane, TextBox source, int value)
    {
        var fade = lane.FadeAt(0);
        ApplyLaneEdit(lane, source, () => SetChange(lane, 0, value, fade?.Ticks ?? SnesFade.NoFade, fade?.To ?? NoFadeDestination, default, null, NoFadeDestination));
        lane.Refresh();
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

    // Splits a region at a tick with a flat region starting at the value the split region ends on, the split region left as it was.
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
        ApplyLaneEdit(lane, null, () => SetChange(lane, tick, value, SnesFade.NoFade, NoFadeDestination, splitRegionSurround, splitRegionEchoRightEnd, NoFadeDestination));
    }

    // The lane's own regions, without the notes laid over it, for whichever of the region lanes this is; empty for any other lane.
    private List<LaneRegion> OwnRegionsOf(ChangeLane lane)
    {
        return lane == Tempos ? [.. LaneRegions.Tempo(_song)]
            : lane == Volumes ? [.. LaneRegions.MasterVolume(_song)]
            : lane == ChannelVolumes ? [.. LaneRegions.ChannelVolumeOwn(_song, ChannelVolumes.Channel)]
            : lane == Amplifies ? [.. LaneRegions.Amplify(_song, Amplifies.Channel)]
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

    // Moves a divider to the tick a drag dropped it on, with its fade, its surround pair and its right side, replacing whatever stood there.
    private void OnChangeMoved(ChangeLane lane, int fromTick, int toTick)
    {
        if (lane.ChangeValueAt(fromTick) is not int value)
        {
            return;
        }

        var fade = lane.FadeAt(fromTick);
        var dividerSurround = lane == Pans ? _song.PanChangeAt(Pans.Channel, fromTick)?.Surround ?? SnesSurround.None : default;
        var dividerEchoRightChange = lane == EchoVolumes ? _song.EchoChangeAt(EchoSetting.VolumeRight, fromTick) : null;
        ApplyLaneEdit(lane, null, () =>
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

    // Takes the light staccato units a lane edit removed away and puts the ones it placed down, as one undo step.
    private void OnStaccatoEdited(UnitLane<StaccatoUnit> lane, IReadOnlyList<int> removed, IReadOnlyList<StaccatoUnit> placed)
    {
        ApplyUnitLaneEdit(lane, () => _song.EditStaccato(removed, placed));
    }

    // Takes the echo send units a lane edit removed away and puts the ones it placed down, as one undo step.
    private void OnChannelEchoEdited(UnitLane<EchoSendUnit> lane, IReadOnlyList<int> removed, IReadOnlyList<EchoSendUnit> placed)
    {
        ApplyUnitLaneEdit(lane, () => _song.EditChannelEcho(ChannelEchoes.Channel, removed, placed));
    }

    // Takes the echo runs a lane edit removed away and puts the ones it placed down, dropping the filters no run uses any more, as one undo step.
    private void OnEchoUnitEdited(UnitLane<EchoRun> lane, IReadOnlyList<int> removed, IReadOnlyList<EchoRun> placed)
    {
        ApplyUnitLaneEdit(lane, () =>
        {
            if (!_song.EditEchoRun(removed, placed))
            {
                return false;
            }

            _song.CompactFilters();
            return true;
        });
    }

    // Makes a lane of units' edit and, where it changed the song, redraws the lane and the roll, records it and shows the note panel again.
    private void ApplyUnitLaneEdit(ChangeLane lane, Func<bool> edit)
    {
        if (!edit())
        {
            return;
        }

        lane.Refresh();
        Roll.Refresh();
        MarkModified();
        ShowNotePanel();
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
