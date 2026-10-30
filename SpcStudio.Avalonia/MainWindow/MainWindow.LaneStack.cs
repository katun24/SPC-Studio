using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using SpcStudio.Controls;
using SpcStudio.Theme;

namespace SpcStudio;

// Which lanes show and in what order, how tall each is and the stack they add up to, the labels beside them, the button that adds a lane, the divider above the stack and the wheel over it.
public partial class MainWindow
{
    private const int RollRowIndex = 0;
    private const int LaneStackRowIndex = 2;
    private const double LaneLabelFontSize = 10;
    private const double SameHeightTolerance = 0.01;

    private double _rollMinHeight;
    private double _laneDividerHeight;
    private bool _isFittingLanes;
    private readonly ListRowDrag _laneReorderDrag;

    // Every lane, shown or hidden, in the order the stack holds them.
    private readonly List<LaneRow> _stackedLaneRows = [];

    // The add button's menu of the hidden lanes, which stays open as lanes are picked from it.
    private readonly ButtonMenu _addLaneMenu = ChoiceMenus.NewAbove();

    // The lanes that are showing, in the order they are stacked.
    private List<LaneRow> ShownLaneRows
    {
        get
        {
            return [.. _stackedLaneRows.Where(row => row.IsVisible)];
        }
    }

    // How tall the stack is drawn: the shown lanes at their drawn heights plus everything the stack holds that is not a lane.
    private double DrawnLaneStackHeight
    {
        get
        {
            return ShownLaneRows.Sum(row => row.Lane.LaneHeight) + LaneStackExtraHeight;
        }
    }

    // How tall the stack is asked to be: the shown lanes at their wanted heights plus everything the stack holds that is not a lane.
    private double WantedLaneStackHeight
    {
        get
        {
            return ShownLaneRows.Sum(row => row.WantedHeight) + LaneStackExtraHeight;
        }
    }

    // The height the stack holds that is not a lane: the frame's borders, less the pixel each closed lane gives up.
    private double LaneStackExtraHeight
    {
        get
        {
            return -ClosedLaneCount * RollMetrics.DevicePixel + LaneFrame.BorderThickness.Top + LaneFrame.BorderThickness.Bottom;
        }
    }

    // The smallest the stack can be with the lanes it is showing, every one at a lane's minimum.
    private double MinLaneStackHeight
    {
        get
        {
            return ShownLaneRows.Count * RollMetrics.MinLaneHeight + LaneStackExtraHeight;
        }
    }

    // The largest the stack can be with the lanes it is showing, every one at a lane's maximum.
    private double MaxLaneStackHeight
    {
        get
        {
            return ShownLaneRows.Count * RollMetrics.MaxLaneHeight + LaneStackExtraHeight;
        }
    }

    // The tallest the stack can be drawn: the room beside a roll at its minimum, held between the stack's own two ends.
    private double LaneStackCapHeight
    {
        get
        {
            return Math.Clamp(LaneGrid.Bounds.Height - _rollMinHeight - _laneDividerHeight, MinLaneStackHeight, MaxLaneStackHeight);
        }
    }

    // How many shown lanes the surface under them closes.
    private int ClosedLaneCount
    {
        get
        {
            return _laneRows.Count(row => row.IsVisible && row.Lane.IsClosedBelow);
        }
    }

    // The roll's own row of the grid it shares with the lane stack.
    private RowDefinition RollRow
    {
        get
        {
            return LaneGrid.RowDefinitions[RollRowIndex];
        }
    }

    // The lane stack's own row of that grid.
    private RowDefinition LaneStackRow
    {
        get
        {
            return LaneGrid.RowDefinitions[LaneStackRowIndex];
        }
    }

    // The lanes in their fixed order, the song's first and the channel's after, each with the name its label and the add button's menu show.
    private LaneRow[] NewLaneRows()
    {
        return
        [
            new(Volumes, "Song Volume"),
            new(Tempos, SettingLabels.Tempo),
            new(EchoUnits, "Echo Unit"),
            new(EchoVolumes, "Echo Volume"),
            new(Staccatos, "Staccato"),
            new(ChannelVolumes, "Channel Volume"),
            new(Pans, SettingLabels.Pan),
            new(ChannelEchoes, "Channel Echo"),
            new(Amplifies, SettingLabels.Amplify),
        ];
    }

    // Reads the roll's minimum height and the divider's height once, and lays the stack out again whenever the room for it changes.
    private void ReadLaneStackLimits()
    {
        _rollMinHeight = RollRow.MinHeight;
        _laneDividerHeight = DividerThickness;
        LaneGrid.SizeChanged += (_, _) => LayOutLaneStack();
    }

    // Builds each lane's label in the strip beside the stack, stacks the lanes in their fixed order, and sizes the column the labels stand in.
    private void BuildLaneLabels()
    {
        foreach (var row in _laneRows)
        {
            AddLaneLabel(row);
        }

        _stackedLaneRows.AddRange(_laneRows);
        AddLaneButton.Flyout = _addLaneMenu;
        SizeLaneLabelColumn();
    }

    // Adds a lane's label to the strip, one lane tall: the lane's name against the keys and the button that hides the lane before it, shown while the
    // pointer is over the label, a press on the rest of the label dragging the lane up or down the stack.
    private void AddLaneLabel(LaneRow row)
    {
        var removeButton = RowIconButton.Build(AppIcons.Minus);
        removeButton.Click += (_, _) => HideLane(row);
        ToolTip.SetTip(removeButton, Tooltips.HideLane);
        removeButton.Margin = new Thickness(Space.Px4, 0, 0, 0);
        DockPanel.SetDock(removeButton, Dock.Left);
        var nameText = new TextBlock
        {
            Text = row.Name,
            FontSize = LaneLabelFontSize,
            Margin = new Thickness(Space.Px4, 0, Space.Px10, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeResource.TextSecondaryBrush,
        };
        var labelRow = new Border { Height = row.Lane.LaneHeight, Child = new DockPanel { Children = { removeButton, nameText } } };
        ListRows.MakeListRow(labelRow);
        labelRow.PointerPressed += (_, e) => OnLaneLabelPressed(labelRow, e);
        _laneReorderDrag.Hook(labelRow);
        row.LabelRow = labelRow;
        LaneLabels.Children.Add(labelRow);
    }

    // Gives the lanes' column of labels the width of the remove button and the longest lane name, the stack reaching out left of the roll by what that adds to the keys' width.
    private void SizeLaneLabelColumn()
    {
        double labelColumnWidth = LabelWidthBesideName(_laneRows[0].LabelRow!) + AppFonts.WidestUiTextWidth(_laneRows.Select(row => row.Name), LaneLabelFontSize);
        LaneLabelColumn.Width = labelColumnWidth;
        LaneLabels.Width = labelColumnWidth;
        LaneFrame.Margin = new Thickness(RollMetrics.KeyWidth - labelColumnWidth, 0, 0, 0);
    }

    // The width a lane's label takes beside its name: the margins of its parts and the remove button.
    private static double LabelWidthBesideName(Border labelRow)
    {
        double width = 0;
        foreach (var part in ((DockPanel)labelRow.Child!).Children)
        {
            width += part.Margin.Left + part.Margin.Right;
            if (part is Button)
            {
                width += ListRows.RowHeight;
            }
        }

        return width;
    }

    // Starts dragging a lane up or down the stack from a single left press on its label.
    private void OnLaneLabelPressed(Border labelRow, PointerPressedEventArgs e)
    {
        if (e.ClickCount != 1 || !e.GetCurrentPoint(labelRow).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _laneReorderDrag.Start(labelRow, LaneScroll, LaneLabels, e);
    }

    // Moves the dragged lane to the shown lane at a height down the strip of labels, once the pointer would stand over the dragged lane there.
    private void MoveDraggedLaneTo(double y)
    {
        var shown = ShownLaneRows;
        if (shown.Find(row => row.LabelRow == _laneReorderDrag.DraggedRow) is not { } dragged)
        {
            return;
        }

        int fromIndex = shown.IndexOf(dragged);
        int toIndex = LaneSlotAt(shown, fromIndex, y);
        if (toIndex == fromIndex)
        {
            return;
        }

        int stackIndex = _stackedLaneRows.IndexOf(shown[toIndex]);
        _stackedLaneRows.Remove(dragged);
        _stackedLaneRows.Insert(stackIndex, dragged);
        ApplyLaneStack();
    }

    // The index of the shown lane the lane dragged from an index would move to for a height down the strip: the lane under the pointer where the
    // dragged lane moved there would still be under it, and the index it was dragged from otherwise.
    private static int LaneSlotAt(List<LaneRow> shown, int fromIndex, double y)
    {
        double draggedHeight = shown[fromIndex].LabelRow!.Height;
        double top = 0;
        for (int index = 0; index < shown.Count; index++)
        {
            double bottom = top + shown[index].LabelRow!.Height;
            bool isLast = index + 1 == shown.Count;
            if (y < bottom || isLast)
            {
                bool isPastSwapPoint = index > fromIndex ? y >= bottom - draggedHeight : y < top + draggedHeight;
                return isPastSwapPoint ? index : fromIndex;
            }

            top = bottom;
        }

        return fromIndex;
    }

    // Stacks the lanes and their labels in the order the stack holds them.
    private void ApplyLaneOrder()
    {
        for (int index = 0; index < _stackedLaneRows.Count; index++)
        {
            MoveChildTo(LaneHost.Children, _stackedLaneRows[index].Lane, index);
            MoveChildTo(LaneLabels.Children, _stackedLaneRows[index].LabelRow!, index);
        }
    }

    // Moves a panel's child to an index among its children.
    private static void MoveChildTo(AvaloniaList<Control> children, Control child, int index)
    {
        int currentIndex = children.IndexOf(child);
        if (currentIndex != index)
        {
            children.Move(currentIndex, index);
        }
    }

    // Hides a lane, as its label's remove button is pressed.
    private void HideLane(LaneRow row)
    {
        row.IsVisible = false;
        ApplyLaneStack();
    }

    // Shows a hidden lane at the bottom of the stack, as it is picked from the add button's menu, the menu placed against the button again once the window has laid the taller stack out.
    private void ShowLaneAtBottom(LaneRow row)
    {
        _stackedLaneRows.Remove(row);
        _stackedLaneRows.Add(row);
        row.IsVisible = true;
        ApplyLaneStack();
        Dispatcher.UIThread.Post(_addLaneMenu.PlaceAgain, DispatcherPriority.Background);
    }

    // Stacks the lanes in the order the song carries and shows every lane the song has a cell in, on any channel, hiding the rest.
    private void RevealUsedLanes()
    {
        ShowLaneOrder();
        ShowLanesWhere(row => row.Lane.HasChanges);
    }

    // Hides every lane.
    private void HideAllLanes()
    {
        ShowLanesWhere(_ => false);
    }

    // Shows each lane a test passes and hides the rest.
    private void ShowLanesWhere(Func<LaneRow, bool> isShown)
    {
        foreach (var row in _laneRows)
        {
            row.IsVisible = isShown(row);
        }

        ApplyLaneStack();
    }

    // Puts the stack, its labels and the add button's menu in step with which lanes are shown and in what order, closing the cell editor and laying the stack out again.
    private void ApplyLaneStack()
    {
        ApplyLaneOrder();
        ShowHiddenLanesInAddMenu();
        CloseCell();
        LayOutLaneStack();
    }

    // Has the add button's menu offer every hidden lane in its fixed order, the song's apart from the channel's, and greys the button out and shuts the menu where none is hidden.
    private void ShowHiddenLanesInAddMenu()
    {
        var hiddenRows = _laneRows.Where(row => !row.IsVisible).ToArray();
        ChoiceMenus.Fill(_addLaneMenu, [hiddenRows.Where(row => row.IsGlobal).Select(AddLaneChoice), hiddenRows.Where(row => !row.IsGlobal).Select(AddLaneChoice)]);
        ChoiceMenus.KeepOpenOnPick(_addLaneMenu);
        AddLaneButton.IsEnabled = hiddenRows.Length > 0;
        if (hiddenRows.Length == 0)
        {
            _addLaneMenu.Hide();
        }
    }

    // A hidden lane's entry in the add button's menu, showing the lane at the bottom of the stack.
    private (string Name, Action<MenuItem> Pick) AddLaneChoice(LaneRow row)
    {
        return (row.Name, _ => ShowLaneAtBottom(row));
    }

    // Stacks the lanes in the order the song carries, the lanes it does not name after them in their fixed order.
    private void ShowLaneOrder()
    {
        var rowsInSongOrder = _song.LaneOrder.Select(name => Array.Find(_laneRows, row => row.Name == name)).OfType<LaneRow>();
        _stackedLaneRows.Clear();
        _stackedLaneRows.AddRange(rowsInSongOrder.Concat(_laneRows).Distinct());
    }

    // Marks the lanes the surface under them closes, which is the last of the stack.
    private void MarkClosedLanes()
    {
        var shown = ShownLaneRows;
        for (int i = 0; i < shown.Count; i++)
        {
            shown[i].Lane.IsClosedBelow = i + 1 == shown.Count;
        }
    }

    // Puts each shown label's row at the height of the lane it names, less the pixel a closed lane gives up.
    private void ApplyLaneLabelHeights()
    {
        foreach (var row in ShownLaneRows)
        {
            row.LabelRow!.Height = row.Lane.LaneHeight - (row.Lane.IsClosedBelow ? RollMetrics.DevicePixel : 0);
        }
    }

    // Lays the lane stack out afresh and redraws every lane, after the lanes' heights or the screen's scale changed.
    private void LayOutAndRefreshLanes()
    {
        LayOutLaneStack();
        RefreshLanes();
    }

    // Draws every shown lane at its wanted height, scaled down together where the window has not the room, and gives the stack's row what they come to.
    private void LayOutLaneStack()
    {
        if (_isFittingLanes)
        {
            return;
        }

        FlagGuard.RunWithFlagSet(ref _isFittingLanes, () =>
        {
            var shown = ShownLaneRows;
            bool isAnyShown = shown.Count > 0;
            LaneFrame.IsVisible = isAnyShown;
            LaneSplitter.IsVisible = isAnyShown;
            MarkClosedLanes();

            double dividerHeight = isAnyShown ? _laneDividerHeight : 0;
            double extraHeight = isAnyShown ? LaneStackExtraHeight : 0;
            double minHeight = isAnyShown ? MinLaneStackHeight : 0;
            double capHeight = isAnyShown ? LaneStackCapHeight : 0;
            double drawnStackHeight = isAnyShown ? DrawLanesWithin(shown, capHeight - extraHeight) + extraHeight : 0;
            RollRow.MinHeight = Math.Max(0, Math.Min(_rollMinHeight, LaneGrid.Bounds.Height - dividerHeight - drawnStackHeight));
            LaneStackRow.MinHeight = minHeight;
            LaneStackRow.MaxHeight = capHeight;
            LaneStackRow.Height = new GridLength(drawnStackHeight);
            ApplyLaneLabelHeights();
            PlaceCellEditor();
        });
    }

    // Draws the lanes at their wanted heights, scaled down together where a height will not hold them, and answers what they came to once snapped.
    private static double DrawLanesWithin(List<LaneRow> shown, double availableHeight)
    {
        ShareHeightOver(shown, Math.Min(availableHeight, shown.Sum(row => row.WantedHeight)), (row, height) => row.Lane.LaneHeight = height);
        return shown.Sum(row => row.Lane.LaneHeight);
    }

    // Shares a height out over lanes in proportion to what each asks for, pinning a lane that would pass either end of a lane's range there, and hands each share to a setter.
    private static void ShareHeightOver(List<LaneRow> shown, double totalHeight, Action<LaneRow, double> setHeight)
    {
        var unpinned = shown.ToList();
        double heightLeft = totalHeight;
        while (unpinned.Count > 0)
        {
            double unpinnedWantedHeight = unpinned.Sum(row => row.WantedHeight);
            double scale = unpinnedWantedHeight > 0 ? heightLeft / unpinnedWantedHeight : 1;
            var rowsScaledPastRange = unpinned.Where(row => row.WantedHeight * scale < RollMetrics.MinLaneHeight || row.WantedHeight * scale > RollMetrics.MaxLaneHeight).ToList();
            if (rowsScaledPastRange.Count == 0)
            {
                foreach (var row in unpinned)
                {
                    setHeight(row, row.WantedHeight * scale);
                }

                return;
            }

            foreach (var row in rowsScaledPastRange)
            {
                double height = Math.Clamp(row.WantedHeight * scale, RollMetrics.MinLaneHeight, RollMetrics.MaxLaneHeight);
                setHeight(row, height);
                heightLeft -= height;
                unpinned.Remove(row);
            }
        }
    }

    // Has a drag of the divider between the roll and the lane stack ask the stack for the height it has reached, up being taller.
    private void WireLaneDivider()
    {
        LaneSplitter.DriveSizeOf(() => DrawnLaneStackHeight, height => TrySetLaneStackHeight(height));
    }

    // Asks the stack for a height overall, every shown lane scaling to fill it, answering whether the height moved.
    private bool TrySetLaneStackHeight(double height)
    {
        var shown = ShownLaneRows;
        if (shown.Count == 0)
        {
            return false;
        }

        double clampedHeight = Math.Clamp(height, MinLaneStackHeight, LaneStackCapHeight);
        if (Math.Abs(clampedHeight - WantedLaneStackHeight) < SameHeightTolerance)
        {
            return false;
        }

        ShareHeightOver(shown, clampedHeight - LaneStackExtraHeight, (row, wantedHeight) => row.WantedHeight = wantedHeight);
        LayOutLaneStack();
        return true;
    }

    // Keeps the strip of labels level with the lanes it names, the cell editor over its cell and the playback marker down the lanes shown.
    private void OnLaneScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        Canvas.SetTop(LaneLabels, -LaneScroll.Offset.Y);
        PlaceCellEditor();
        Markers.InvalidateVisual();
    }

    // Does what the wheel means over the lanes: the bare wheel takes the whole stack, Alt the lane under the pointer, and the rest as over the roll.
    private void OnLaneScrollWheel(object? sender, PointerWheelEventArgs e)
    {
        HandleGridWheel(e, ZoomLaneUnderCursor, notchEvent => ZoomWholeLaneStack(NotchZoomFactor(notchEvent)));
    }

    // The shown lane at a height down the stack, or the nearest shown lane where the height is past either end of one.
    private LaneRow? LaneRowAt(double y)
    {
        LaneRow? nearest = null;
        foreach (var row in ShownLaneRows)
        {
            if (y < row.Lane.Bounds.Bottom)
            {
                return row;
            }

            nearest = row;
        }

        return nearest;
    }

    // Takes the lane under the pointer by a notch, the lanes beside it giving way and the stack moving only for what they cannot absorb.
    private void ZoomLaneUnderCursor(PointerWheelEventArgs e)
    {
        if (LaneRowAt(e.GetPosition(LaneHost).Y) is not { } target)
        {
            return;
        }

        var shown = ShownLaneRows;
        double[] wantedHeights = [.. shown.Select(row => row.WantedHeight)];
        double heightStackCanGrow = Math.Max(0, LaneStackCapHeight - WantedLaneStackHeight);
        if (!StackedHeights.TryZoomOne(wantedHeights, shown.IndexOf(target), NotchZoomFactor(e), RollMetrics.MinLaneHeight, RollMetrics.MaxLaneHeight, heightStackCanGrow))
        {
            return;
        }

        for (int index = 0; index < shown.Count; index++)
        {
            shown[index].WantedHeight = wantedHeights[index];
        }

        LayOutAndRefreshLanes();
    }

    // Grows or shrinks every shown lane by a zoom factor, each held to a lane's range, as one height asked of the whole stack.
    private void ZoomWholeLaneStack(double zoomFactor)
    {
        double zoomedStackHeight = ShownLaneRows.Sum(row => Math.Clamp(row.WantedHeight * zoomFactor, RollMetrics.MinLaneHeight, RollMetrics.MaxLaneHeight)) + LaneStackExtraHeight;
        if (TrySetLaneStackHeight(zoomedStackHeight))
        {
            RefreshLanes();
        }
    }

    // Asks each lane for the height the song carries for it, or a lane's opening height where it carries none, and redraws the stack.
    private void ShowLaneHeights()
    {
        foreach (var row in _laneRows)
        {
            row.WantedHeight = _song.LaneHeights.TryGetValue(row.Name, out double height) ? Math.Max(RollMetrics.MinLaneHeight, height) : RollMetrics.BaseLaneHeight;
        }

        LayOutAndRefreshLanes();
    }

    // The height each lane is asked for, by its name, for the song to save.
    private Dictionary<string, double> CaptureLaneHeights()
    {
        return _laneRows.ToDictionary(row => row.Name, row => row.WantedHeight);
    }

    // The name of every lane, shown or hidden, in the order the stack holds them, for the song to save.
    private List<string> CaptureLaneOrder()
    {
        return [.. _stackedLaneRows.Select(row => row.Name)];
    }
}
