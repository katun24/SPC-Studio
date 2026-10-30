using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using SpcStudio.Controls;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio;

// The lane cell editor laid over the lanes: opening it over a region or an echo run, what each of its fields writes, and where it stands.
public partial class MainWindow
{
    private const string FadeToWord = "To";
    private const string EchoLeftFadeToWord = "To L";

    private static readonly string[] CellFadeToWords = [FadeToWord, EchoLeftFadeToWord];
    private static readonly ValueRange ByteRange = new(byte.MinValue, byte.MaxValue);
    private bool _doesCellFade;
    private bool _isUpdatingCell;
    private bool _areCellLabelsSized;

    // The word at the left of the value row on each lane that shows one.
    private (ChangeLane Lane, string Word)[] CellValueWords
    {
        get
        {
            return [(Tempos, SettingLabels.Tempo),
                (Volumes, "Volume"),
                (EchoVolumes, "Left"),
                (EchoUnits, SettingLabels.EchoDelay),
                (Amplifies, SettingLabels.Amplify),
                (Pans, SettingLabels.Pan),
                (ChannelVolumes, SettingLabels.ChannelVolume)];
        }
    }

    // Whether a drop-down anywhere in the editor has its list open.
    private bool IsCellDropDownOpen
    {
        get
        {
            return CellEditor.GetLogicalDescendants().OfType<ComboBox>().Any(box => box.IsDropDownOpen);
        }
    }

    // The surround pair the drop-down has picked.
    private SnesSurround CellSurroundPairPicked
    {
        get
        {
            return SurroundField.PairAt(CellSurroundBox.SelectedIndex);
        }
    }

    // The FIR coefficient boxes, the first coefficient's first.
    private TextBox[] FilterBoxes
    {
        get
        {
            return [CellFir0, CellFir1, CellFir2, CellFir3, CellFir4, CellFir5, CellFir6, CellFir7];
        }
    }

    // Opens the editor over the cell a lane selected, or closes it where the selection has gone.
    private void OnCellSelected(ChangeLane lane, int? tick)
    {
        if (ReferenceEquals(lane, EchoUnits))
        {
            ShowEchoUnitCell(tick);
            return;
        }

        ShowRegionCell(lane, tick);
    }

    // Opens the editor over a region: its value, its fade, and the surround pair or the right side where the lane has one.
    private void ShowRegionCell(ChangeLane lane, int? tick)
    {
        if (tick is not int at || ValueEditorOpensOn(lane, at) is not int value)
        {
            CloseCell();
            return;
        }

        TakeCell(lane, at);
        var fade = lane.FadeAt(at);
        FlagGuard.RunWithFlagSet(ref _isUpdatingCell, () =>
        {
            foreach (var slider in new[] { CellSlider, CellRightSlider, CellFadeToSlider, CellFadeToRightSlider })
            {
                SliderField.SetRail(slider, lane.ValueRange);
            }

            CellBox.Text = FieldInput.TextOf(value);
            CellFadeToBox.Text = FieldInput.TextOf(fade?.To ?? value);
        });

        _doesCellFade = fade is not null;
        ShowCellFade(lane);
        ShowCellLabels(lane);

        bool isPan = ReferenceEquals(lane, Pans);
        bool isEchoVolume = ReferenceEquals(lane, EchoVolumes);
        var shownRows = new List<Control> { CellValueRow };
        if (isPan)
        {
            shownRows.Add(CellSurroundRow);
        }

        if (isEchoVolume)
        {
            shownRows.Add(CellRightRow);
        }

        ShowOnlyCellRows(shownRows);
        if (isPan)
        {
            FlagGuard.RunWithFlagSet(ref _isUpdatingCell, () => CellSurroundBox.SelectedIndex = SurroundField.IndexOf(SurroundPairInSongAt(at)));
        }

        if (isEchoVolume)
        {
            ShowEchoRightSide(at);
        }

        OpenCellEditor();
    }

    // Writes the echo volume's right side and where its ramp lands into their two boxes.
    private void ShowEchoRightSide(int tick)
    {
        var rightChange = _song.EchoChangeAt(EchoSetting.VolumeRight, tick);
        int rightLevelAtTick = tick == 0 || rightChange is null ? _song.BaseEchoVolume(EchoSetting.VolumeRight) : rightChange.Value.Value;
        int rightRampEndLevel = rightChange is { Fades: true } fading ? fading.FadeTo : ChangeMap.ForEcho(_song, EchoSetting.VolumeRight).InForceAt(tick);
        FlagGuard.RunWithFlagSet(ref _isUpdatingCell, () =>
        {
            CellRight.Text = FieldInput.TextOf(rightLevelAtTick);
            CellFadeToRight.Text = FieldInput.TextOf(rightRampEndLevel);
        });
    }

    // Opens the editor over an echo run: its delay, its feedback, and the coefficients of the filter it names.
    private void ShowEchoUnitCell(int? tick)
    {
        if (tick is not int at || _song.EchoRunAt(at) is not { } run)
        {
            CloseCell();
            return;
        }

        TakeCell(EchoUnits, at);
        _doesCellFade = false;
        ShowCellLabels(EchoUnits);
        ShowCellFade(EchoUnits);
        ShowOnlyCellRows([CellValueRow, CellFeedbackRow, CellFilterRow]);
        FlagGuard.RunWithFlagSet(ref _isUpdatingCell, () =>
        {
            SliderField.SetRail(CellSlider, EchoUnits.ValueRange);
            CellBox.Text = FieldInput.TextOf(run.State.Delay);
            CellFeedback.Text = FieldInput.TextOf(run.State.Feedback);
            ShowCoefficients(_song.FilterCoefficients(run.State.Filter));
        });

        OpenCellEditor();
    }

    // Puts the editor on a lane's cell, closing it first where it stood on another lane.
    private void TakeCell(ChangeLane lane, int tick)
    {
        if (!ReferenceEquals(_cellLane, lane))
        {
            CloseCell();
        }

        _cellLane = lane;
        _cellTick = tick;
    }

    // Shows the given rows of the editor and hides its others.
    private void ShowOnlyCellRows(IReadOnlyCollection<Control> shownRows)
    {
        foreach (var row in new Control[] { CellValueRow, CellRightRow, CellSurroundRow, CellFeedbackRow, CellFilterRow })
        {
            row.IsVisible = shownRows.Contains(row);
        }
    }

    // Shows the editor, puts it over its cell and gives its value field the focus where the editor is to take it.
    private void OpenCellEditor()
    {
        CellEditor.IsVisible = true;
        PlaceCellEditor();
        if (_doesCellTakeFocus)
        {
            FieldInput.FocusField(CellBox);
        }
    }

    // Writes one of the three settings of the echo run the editor is open on.
    private void ApplyEchoRunSetting(EchoSetting setting, int value, Control? source)
    {
        if (_song.EchoRunAt(_cellTick) is not { } run)
        {
            return;
        }

        ApplyLaneEdit(EchoUnits, source, () =>
        {
            _song.EditEchoRun([run.StartTick], [run with { State = run.State.With(setting, value) }]);
            _song.CompactFilters();
        });
        EchoUnits.Refresh();
    }

    // The value the editor opens on at a lane's tick: the region's on a lane drawn as regions, the change's otherwise.
    private static int? ValueEditorOpensOn(ChangeLane lane, int tick)
    {
        return lane.IsDrawnAsRegions ? lane.RegionValue(tick) : lane.ChangeValueAt(tick);
    }

    // Writes the lane's own words at the left of the value row and the destination row.
    private void ShowCellLabels(ChangeLane lane)
    {
        CellValueRow.Label = CellValueWords.FirstOrDefault(entry => ReferenceEquals(entry.Lane, lane)).Word;
        CellFadeToRow.Label = ReferenceEquals(lane, EchoVolumes) ? EchoLeftFadeToWord : FadeToWord;
    }

    // Gives every label in the editor the width of the widest word it can show on any lane, plus the gap between two controls, the first time it opens.
    private void SizeCellLabels()
    {
        if (_areCellLabelsSized)
        {
            return;
        }

        var editorLabels = CellEditor.GetLogicalDescendants().OfType<TextBlock>().Where(block => block.Classes.Contains(ThemeClasses.FieldLabel)).ToArray();
        if (editorLabels.Length == 0)
        {
            return;
        }

        var wordsAnyLabelCanShow = editorLabels.Select(block => block.Text ?? "").Concat(CellValueWords.Select(entry => entry.Word)).Concat(CellFadeToWords);
        double width = AppFonts.WidestUiTextWidth(wordsAnyLabelCanShow, editorLabels[0].FontSize) + Space.Px8;
        foreach (var label in editorLabels)
        {
            label.Width = width;
        }

        _areCellLabelsSized = true;
    }

    // The value the echo volume's right side opens the next region on, or null where the region at a tick is the last.
    private int? NextEchoRightRegionValue(int tick)
    {
        return EchoRightRegionValueBeside(tick, 1);
    }

    // The value a right-click puts the echo volume's right side back at: the region before it's, and the default for the first region.
    private int EchoRightResetValue(int tick)
    {
        return EchoRightRegionValueBeside(tick, -1) ?? SnesEcho.Default(EchoSetting.VolumeRight);
    }

    // The value the echo volume's right side opens a region on some regions along from the one starting at a tick, or null where there is none.
    private int? EchoRightRegionValueBeside(int tick, int regionsAlong)
    {
        var regions = LaneRegions.EchoVolume(_song).ToList();
        int index = regions.FindIndex(region => region.Left.Start == tick);
        if (index < 0)
        {
            return null;
        }

        int besideIndex = index + regionsAlong;
        return besideIndex >= 0 && besideIndex < regions.Count ? regions[besideIndex].Right.Value : null;
    }

    // The value a right-click puts the editor's first row back at: the echo run's default delay, or what the region's lane resets it to.
    private int? CellValueResetValue()
    {
        if (_cellLane is not { } lane)
        {
            return null;
        }

        return ReferenceEquals(lane, EchoUnits) ? SnesEcho.Default(EchoSetting.Delay) : lane.RegionResetValue(_cellTick);
    }

    // The value a field in the lane's range holds, or null where it holds none.
    private int? CellLaneValueIn(TextBox box)
    {
        return _cellLane is { } lane && FieldInput.TryReadValue(box.Text, lane.ValueRange, out int value) ? value : null;
    }

    // Lays out the editor's fade half for a lane: the button where the lane can fade, and the destination rows while the region fades.
    private void ShowCellFade(ChangeLane lane)
    {
        bool canFade = lane.IsDrawnAsRegions && lane.CanFade;
        CellFadeToggle.IsVisible = canFade;
        CellFadeToRow.IsVisible = canFade && _doesCellFade;
        CellEditor.Classes.Set(ThemeClasses.HasFadeButton, canFade);
        CellFadeToRightRow.IsVisible = ReferenceEquals(lane, EchoVolumes) && CellFadeToRow.IsVisible;
        (CellFadeToggleIcon.Data, string fadeTooltip) = _doesCellFade ? (AppIcons.TrendingNeutral, Tooltips.RemoveFade) : (AppIcons.TrendingUp, Tooltips.ApplyFade);
        ToolTip.SetTip(CellFadeToggle, fadeTooltip);
    }

    // Turns a flat region into a fade up to the region after it, or a fading one back into a flat region.
    private void OnCellFadeToggled(object? sender, RoutedEventArgs e)
    {
        if (_cellLane is not { IsDrawnAsRegions: true } lane)
        {
            return;
        }

        _doesCellFade = !_doesCellFade;
        if (_doesCellFade)
        {
            ShowFadeLandingOnNextRegion(lane);
        }

        ShowCellFade(lane);
        PlaceCellEditor();
        ApplyCell(sender!);
        FieldInput.FocusField(_doesCellFade ? CellFadeToBox : CellBox);
    }

    // Writes the destination boxes of a fade just turned on, at what the next region opens on or where the region starts.
    private void ShowFadeLandingOnNextRegion(ChangeLane lane)
    {
        FlagGuard.RunWithFlagSet(ref _isUpdatingCell, () =>
        {
            int? lands = lane.NextRegionValue(_cellTick);
            CellFadeToBox.Text = lands is null ? CellBox.Text : FieldInput.TextOf(lands);
            if (ReferenceEquals(lane, EchoVolumes))
            {
                int? rightLands = NextEchoRightRegionValue(_cellTick);
                CellFadeToRight.Text = rightLands is null ? CellRight.Text : FieldInput.TextOf(rightLands);
            }
        });
    }

    // Puts the editor just above the selected cell, held inside the visible strip of the lanes.
    private void PlaceCellEditor()
    {
        if (_cellLane is not { } lane)
        {
            return;
        }

        var cellTopCenter = lane.CellTopCenter(_cellDragTick ?? _cellTick) * lane.TransformToVisual(EditorLayer)!.Value;
        SizeCellLabels();
        SpaceCellRows();
        if (!IsCellDropDownOpen)
        {
            CellEditor.Width = double.NaN;
            CellEditor.Measure(Size.Infinity);
            CellEditor.Width = CellEditor.DesiredSize.Width;
        }

        CellEditor.Measure(Size.Infinity);
        var size = CellEditor.DesiredSize;
        Canvas.SetLeft(CellEditor, LeftClampedToLanes(cellTopCenter.X - size.Width / 2, size.Width));
        Canvas.SetTop(CellEditor, cellTopCenter.Y - size.Height - Space.Px10);
    }

    // Takes the gap off the bottom of the last row showing, the editor's own padding standing under it.
    private void SpaceCellRows()
    {
        var rows = CellEditor.GetLogicalDescendants().OfType<FieldRow>().ToArray();
        var lastShownRow = rows.LastOrDefault(IsShownInCellEditor);
        foreach (var row in rows)
        {
            if (ReferenceEquals(row, lastShownRow))
            {
                row.Margin = default;
            }
            else
            {
                row.ClearValue(MarginProperty);
            }
        }
    }

    // Whether a control in the editor and everything holding it inside the editor are set to show.
    private bool IsShownInCellEditor(Control control)
    {
        return control.GetSelfAndLogicalAncestors().TakeWhile(ancestor => !ReferenceEquals(ancestor, CellEditor)).OfType<Visual>().All(ancestor => ancestor.IsVisible);
    }

    // The editor's left edge, centered on its cell where it fits and pushed in at either end of the lanes' visible strip where it does not.
    private double LeftClampedToLanes(double left, double width)
    {
        if (LaneScroll.TranslatePoint(default, EditorLayer) is not { } origin)
        {
            return left;
        }

        double min = origin.X;
        double max = origin.X + LaneScroll.Viewport.Width - width;
        return max <= min ? min : Math.Clamp(left, min, max);
    }

    // Closes the editor and drops the lane's selection with it, unless a restore is holding it open.
    private void CloseCell()
    {
        if (_isKeepingCell)
        {
            return;
        }

        _cellLane?.ClearSelectionSilently();
        _cellLane = null;
        _cellDragTick = null;
        CellEditor.IsVisible = false;
    }

    // Writes every field of a region's cell into the song at once, or nothing where any of them is out of the lane's range.
    private void ApplyCell(object source)
    {
        if (_isUpdatingCell || _cellLane is not { } lane)
        {
            return;
        }

        bool isEveryFieldRead = FieldInput.TryReadValue(CellBox.Text, lane.ValueRange, out int value);
        int fadeTicks = SnesFade.NoFade;
        int fadeTo = 0;
        if (_doesCellFade)
        {
            fadeTicks = SnesFade.MinFadeTicks;
            isEveryFieldRead &= FieldInput.TryReadValue(CellFadeToBox.Text, lane.ValueRange, out fadeTo);
        }

        int? right = null;
        int fadeToRight = 0;
        if (ReferenceEquals(lane, EchoVolumes))
        {
            isEveryFieldRead &= FieldInput.TryReadValue(CellRight.Text, lane.ValueRange, out int rightVolume);
            right = rightVolume;
            if (_doesCellFade)
            {
                isEveryFieldRead &= FieldInput.TryReadValue(CellFadeToRight.Text, lane.ValueRange, out fadeToRight);
            }
        }

        if (!isEveryFieldRead)
        {
            return;
        }

        if (ReferenceEquals(lane, EchoUnits))
        {
            ApplyEchoRunSetting(EchoSetting.Delay, value, source as Control);
            return;
        }

        int tick = _cellTick;
        var surround = CellSurroundPairPicked;
        ApplyLaneEdit(lane, source, () => SetChange(lane, tick, value, fadeTicks, fadeTo, surround, right, fadeToRight));
        lane.Refresh();
    }

    // Writes the surround pair picked, unless it is the pair the cell already holds.
    private void OnCellSurroundChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (CellSurroundPairPicked == SurroundPairInSongAt(_cellTick))
        {
            return;
        }

        ApplyCell(sender!);
    }

    // The surround pair the pan lane holds at a tick: the channel's base pair at the first region.
    private SnesSurround SurroundPairInSongAt(int tick)
    {
        return tick == 0 ? _song.BaseSurround(Pans.Channel) : _song.PanChangeAt(Pans.Channel, tick)?.Surround ?? SnesSurround.None;
    }

    // Writes a filter's coefficients into the coefficient boxes as the hex the command writes.
    private void ShowCoefficients(long coefficients)
    {
        var boxes = FilterBoxes;
        for (int tap = 0; tap < SnesEcho.TapCount; tap++)
        {
            boxes[tap].Text = CoefficientText(coefficients, tap);
        }
    }

    // One of a filter's coefficients as the hex its box shows.
    private static string CoefficientText(long coefficients, int tap)
    {
        return FieldInput.HexTextOf((byte)SnesEcho.CoefficientAt(coefficients, tap));
    }

    // Points the echo run at the filter the coefficient boxes describe, as they are typed.
    private void OnCellFilterChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingCell || !ReferenceEquals(_cellLane, EchoUnits))
        {
            return;
        }

        var coefficients = new sbyte[SnesEcho.TapCount];
        var boxes = FilterBoxes;
        for (int tap = 0; tap < SnesEcho.TapCount; tap++)
        {
            if (!FieldInput.TryReadHex(boxes[tap].Text, ByteRange, out int coefficient))
            {
                return;
            }

            coefficients[tap] = (sbyte)coefficient;
        }

        ApplyEchoRunSetting(EchoSetting.Filter, _song.FilterIndexFor(SnesEcho.PackCoefficients(coefficients)), (TextBox)sender!);
    }

    // Writes the echo volume's right side or where its ramp lands, as the whole divider.
    private void OnCellEchoRightChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingCell || !ReferenceEquals(_cellLane, EchoVolumes))
        {
            return;
        }

        ApplyCell(CellBox);
    }

    // Writes the echo run's feedback as it is typed.
    private void OnCellFeedbackChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingCell || !ReferenceEquals(_cellLane, EchoUnits))
        {
            return;
        }

        if (!FieldInput.TryReadValue(CellFeedback.Text, SnesEcho.FeedbackRange, out int feedback))
        {
            return;
        }

        ApplyEchoRunSetting(EchoSetting.Feedback, feedback, CellFeedback);
    }

    // Writes the cell's value as it is typed.
    private void OnCellChanged(object? sender, TextChangedEventArgs e)
    {
        ApplyCell(CellBox);
    }

    // Writes where the cell's fade lands as it is typed.
    private void OnCellFadeToChanged(object? sender, TextChangedEventArgs e)
    {
        ApplyCell(CellFadeToBox);
    }

    // Steps the coefficient a box holds, as the signed byte it is.
    private void OnCellFilterKeyDown(object? sender, KeyEventArgs e)
    {
        var box = (TextBox)sender!;
        int step = FieldSteps.StepOf(e);
        if (_cellLane is null || step == 0 || !FieldInput.TryReadHex(box.Text, ByteRange, out int current))
        {
            return;
        }

        FieldInput.WriteText(box, FieldInput.HexTextOf((byte)Math.Clamp((sbyte)current + step, sbyte.MinValue, sbyte.MaxValue)));
        e.Handled = true;
    }

    // Steps one of the boxes whose range is the lane's.
    private void OnCellLaneValueKeyDown(object? sender, KeyEventArgs e)
    {
        if (_cellLane is { } lane)
        {
            StepCellBox(e, (TextBox)sender!, lane.ValueRange);
        }
    }

    // Steps the echo run's feedback box.
    private void OnCellFeedbackKeyDown(object? sender, KeyEventArgs e)
    {
        StepCellBox(e, CellFeedback, SnesEcho.FeedbackRange);
    }

    // Steps one of the editor's boxes from what the box says, held to a range.
    private void StepCellBox(KeyEventArgs e, TextBox box, ValueRange range)
    {
        int step = FieldSteps.StepOf(e);
        if (_cellLane is null || step == 0 || range.Max < range.Min || !FieldInput.TryReadValue(box.Text, range, out int current))
        {
            return;
        }

        FieldInput.WriteStepped(box, current, step, range);
        e.Handled = true;
    }

    // Closes the editor on Enter or Escape, from whichever of its controls has the focus.
    private void OnCellEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (_cellLane is null || IsCellDropDownOpen || e.Key is not (Key.Enter or Key.Escape))
        {
            return;
        }

        CloseCell();
        e.Handled = true;
    }

    // Closes the editor where the focus has left it for anything but one of its own controls or an open drop-down list.
    private void OnCellLostFocus(object? sender, RoutedEventArgs e)
    {
        if (IsCellDropDownOpen)
        {
            return;
        }

        if (FocusManager?.GetFocusedElement() is Visual next && CellEditor.IsVisualAncestorOf(next))
        {
            return;
        }

        CloseCell();
    }

    // Puts every numeric box of the cell editor behind its arrow keys, its wheel and its right-click, ties each slider to its field, and hooks up the editor's own keys and its envelope edits.
    private void WireCellEditorFields()
    {
        foreach (var box in new[] { CellBox, CellFadeToBox, CellRight, CellFadeToRight })
        {
            FieldSteps.OnKeys(box, OnCellLaneValueKeyDown);
        }

        FieldSteps.OnKeys(CellFeedback, OnCellFeedbackKeyDown);
        foreach (var box in FilterBoxes)
        {
            FieldSteps.OnKeys(box, OnCellFilterKeyDown);
        }

        SliderField.Tie(CellSlider, CellBox, ByteRange);
        SliderField.Tie(CellRightSlider, CellRight, ByteRange);
        SliderField.Tie(CellFadeToSlider, CellFadeToBox, ByteRange);
        SliderField.Tie(CellFadeToRightSlider, CellFadeToRight, ByteRange);
        SliderField.Tie(CellFeedbackSlider, CellFeedback, SnesEcho.FeedbackRange);
        WireCellEditorResets();
        CellEditor.AddHandler(KeyDownEvent, OnCellEditorKeyDown, RoutingStrategies.Tunnel);
    }

    // Gives every field of the cell editor the value a right-click puts it back at: a fade's destination the value it fades from, the echo
    // run's settings their defaults, and a region's value what its lane resets it to.
    private void WireCellEditorResets()
    {
        FieldResets.ResetsTo(CellBox, CellValueResetValue);
        FieldResets.ResetsTo(CellFadeToBox, () => CellLaneValueIn(CellBox));
        FieldResets.ResetsTo(CellRight, () => EchoRightResetValue(_cellTick));
        FieldResets.ResetsTo(CellFadeToRight, () => CellLaneValueIn(CellRight));
        FieldResets.ResetsTo(CellFeedback, () => SnesEcho.Default(EchoSetting.Feedback));
        var boxes = FilterBoxes;
        for (int tap = 0; tap < SnesEcho.TapCount; tap++)
        {
            var box = boxes[tap];
            string defaultCoefficientText = CoefficientText(SnesEcho.PackedPresets[SnesEcho.DefaultFilter], tap);
            FieldResets.OnReset(box, () => FieldInput.WriteText(box, defaultCoefficientText));
        }
    }
}
