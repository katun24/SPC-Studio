using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SpcStudio.Controls;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio;

// The roll's scrolling, its snap size and its zoom, what the ruler numbers its bar lines with, and what the wheel does over the roll, the ruler and the lanes.
public partial class MainWindow
{
    private const double ColumnsPerScrollNotch = 3.0;

    private bool _isWritingSnapBox;
    private bool _isFitZoomOwed;

    // The width the grid and the space past its end have to fit into: the roll's viewer less the gutter the roll keeps at its right.
    private double RollFitWidth
    {
        get
        {
            return RollScroll.Viewport.Width - Roll.Margin.Right;
        }
    }

    // Puts the song's snap size in the drop-down.
    private void ShowSnap()
    {
        FlagGuard.RunWithFlagSet(ref _isWritingSnapBox, () => SnapBox.SelectedItem = SnapSize.ForTicks(_song.SnapTicks));
    }

    // Puts the song's time signature in its two fields.
    private void ShowTimeSignature()
    {
        FlagGuard.RunWithFlagSet(ref _isWritingFieldsFromSong, () =>
        {
            BeatsPerBarBox.Text = FieldInput.TextOf(_song.TimeSignature.BeatsPerBar);
            BeatUnitBox.Text = FieldInput.TextOf(_song.TimeSignature.BeatUnit);
        });
    }

    // Puts each time signature field's own key handler behind its arrow keys and its wheel, and gives it the default time signature's number as the value a right-click puts it back at.
    private void WireTimeSignatureFields()
    {
        FieldSteps.OnKeys(BeatsPerBarBox, OnBeatsPerBarKeyDown);
        FieldSteps.OnKeys(BeatUnitBox, OnBeatUnitKeyDown);
        FieldResets.ResetsTo(BeatsPerBarBox, () => TimeSignature.Default.BeatsPerBar);
        FieldResets.ResetsTo(BeatUnitBox, () => TimeSignature.Default.BeatUnit);
    }

    // Applies the time signature as either of its numbers is typed and redraws the bar lines, a number no time signature can hold changing nothing.
    private void OnTimeSignatureTyped(object? sender, TextChangedEventArgs e)
    {
        if (_isWritingFieldsFromSong || TypedTimeSignature() is not { } timeSignature || timeSignature == _song.TimeSignature)
        {
            return;
        }

        _song.TimeSignature = timeSignature;
        RefreshGrid();
        MarkModified(sender);
    }

    // The time signature the two fields spell, or null where either holds a number no time signature can hold.
    private TimeSignature? TypedTimeSignature()
    {
        if (!FieldInput.TryReadValue(BeatsPerBarBox.Text, TimeSignature.BeatsPerBarRange, out int beatsPerBar)
            || !FieldInput.TryReadValue(BeatUnitBox.Text, new ValueRange(TimeSignature.BeatUnits[0], TimeSignature.BeatUnits[^1]), out int beatUnit))
        {
            return null;
        }

        var timeSignature = new TimeSignature(beatsPerBar, beatUnit);
        return timeSignature.IsValid ? timeSignature : null;
    }

    // Puts the song's time signature back in its fields once one of them loses the focus.
    private void OnTimeSignatureCommitted(object? sender, RoutedEventArgs e)
    {
        ShowTimeSignature();
    }

    // Steps the top number from the song's by an arrow key or a wheel notch.
    private void OnBeatsPerBarKeyDown(object? sender, KeyEventArgs e)
    {
        StepSongValueField(BeatsPerBarBox, _song.TimeSignature.BeatsPerBar, TimeSignature.BeatsPerBarRange, e);
    }

    // Steps the bottom number from the song's to the next note value along by an arrow key or a wheel notch.
    private void OnBeatUnitKeyDown(object? sender, KeyEventArgs e)
    {
        StepSongValueField(BeatUnitBox, step => TimeSignature.BeatUnitSteppedBy(_song.TimeSignature.BeatUnit, step), e);
    }

    // Puts the song's zooms and lane heights on the grid, fitting a song that carries no zoom of its own to the viewer.
    private void ShowZoom()
    {
        double viewportWidth = RollFitWidth;
        RollMetrics.UpdateMinZoom(_song, viewportWidth);
        RollMetrics.Zoom = _song.HorizontalZoom ?? RollMetrics.FitZoom(_song, viewportWidth);
        RollMetrics.VerticalZoom = _song.VerticalZoom;
        ShowLaneHeights();
        _isFitZoomOwed = _song.HorizontalZoom is null && viewportWidth <= 0;
        Keys.RedrawAtRowHeight();
        RefreshGrid();
    }

    // Scrolls the roll so the reference row stands in the middle of the view.
    private void ScrollToReferenceRow()
    {
        double center = RollMetrics.PitchCenterY(PianoRoll.ReferenceSemitone);
        RollScroll.Offset = RollScroll.Offset.WithY(center - RollScroll.Viewport.Height / 2);
    }

    // Scrolls the roll all the way to its left end.
    private void ScrollToSongStart()
    {
        RollScroll.Offset = RollScroll.Offset.WithX(0);
    }

    // Redraws the roll, the ruler, the markers and the lanes, which share the grid's columns.
    private void RefreshGrid()
    {
        Roll.Refresh();
        RefreshRulerAndLanes();
    }

    // Sets what every edit steps by and redraws the grid's lines, as an undo step of its own.
    private void OnSnapSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_isWritingSnapBox || SnapBox.SelectedItem is not SnapSize snap)
        {
            return;
        }

        _song.SnapTicks = snap.Ticks;
        RefreshGrid();
        MarkModified();
    }

    // Draws or hides the channels the roll is not editing.
    private void OnShowOtherChannelsClick(object? sender, RoutedEventArgs e)
    {
        Roll.IsShowingOtherChannels = ShowOtherChannelsItem.IsChecked;
    }

    // Labels every piano key with its note name, or only each C.
    private void OnShowAllTonesClick(object? sender, RoutedEventArgs e)
    {
        Keys.IsShowingEveryNoteName = ShowAllTonesItem.IsChecked;
    }

    // Turns on or off whether a press on the grid sounds the note it lands on.
    private void OnAuditionNotesClick(object? sender, RoutedEventArgs e)
    {
        Roll.DoesAuditionNotes = AuditionNotesItem.IsChecked;
    }

    // Switches the ruler between numbering its bar lines with bars and with ticks.
    private void OnRulerUnitClick(object? sender, RoutedEventArgs e)
    {
        ShowRulerUnit(!Ruler.IsCountingTicks);
    }

    // Has the ruler number its bar lines with ticks or with bars, the button showing the letter of the one it uses and lit while it counts ticks.
    private void ShowRulerUnit(bool isCountingTicks)
    {
        Ruler.IsCountingTicks = isCountingTicks;
        RulerUnitIcon.Data = AppIcons.RulerUnitIcon(isCountingTicks);
        RulerUnitButton.Classes.Set(ThemeClasses.Lit, isCountingTicks);
    }

    // Follows a scroll, a resize or a change of the song's length: the zoom's floor, an owed fit, the width the surfaces fill, the keyboard, the viewports and the ruler's and lanes' offsets.
    private void OnRollScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        bool isGridResized = RollMetrics.UpdateMinZoom(_song, RollFitWidth);
        if (TryTakeOwedFitZoom())
        {
            isGridResized = true;
        }

        if (RollMetrics.UpdateFillWidth(RollFitWidth))
        {
            isGridResized = true;
        }

        if (isGridResized)
        {
            RefreshGrid();
        }

        ShowRollViewport();
        Roll.ContentMoved();
        foreach (var lane in UnitLanes)
        {
            lane.ContentMoved();
        }

        if (e.OffsetDelta.X == 0 && e.ExtentDelta.X == 0)
        {
            return;
        }

        double offsetX = RollScroll.Offset.X;
        RulerScroll.Offset = RulerScroll.Offset.WithX(offsetX);
        LaneScroll.Offset = LaneScroll.Offset.WithX(offsetX);
        PlaceCellEditor();
    }

    // Fits the launch song to the viewer once the viewer has a width, answering whether that moved the zoom.
    private bool TryTakeOwedFitZoom()
    {
        if (!_isFitZoomOwed || RollFitWidth <= 0)
        {
            return false;
        }

        _isFitZoomOwed = false;
        double fit = RollMetrics.FitZoom(_song, RollFitWidth);
        if (RollMetrics.Zoom == fit)
        {
            return false;
        }

        RollMetrics.Zoom = fit;
        return true;
    }

    // Tells the keyboard, the roll, the ruler and the lanes what part of the roll the viewer is showing, and redraws the markers over it.
    private void ShowRollViewport()
    {
        Markers.InvalidateVisual();
        Keys.VerticalOffset = RollScroll.Offset.Y;
        Keys.ViewportHeight = RollScroll.Viewport.Height;
        Roll.SetViewport(RollScroll.Offset.X, RollScroll.Offset.Y, RollScroll.Viewport.Width, RollScroll.Viewport.Height);
        Ruler.SetViewport(RollScroll.Offset.X, RollScroll.Viewport.Width);
        foreach (var lane in _lanes)
        {
            lane.SetViewport(RollScroll.Offset.X, RollScroll.Viewport.Width);
        }
    }

    // Does what the wheel means over the roll: Ctrl zooms the columns, Alt the rows, both at once, C steps the channel and Shift scrolls sideways.
    private void OnRollScrollWheel(object? sender, PointerWheelEventArgs e)
    {
        HandleGridWheel(e, ZoomRollRowsUnderCursor, null);
    }

    // Hands a notch over the keys on to the roll's viewer, where it does whatever a notch over the roll does.
    private void OnKeysWheel(object? sender, PointerWheelEventArgs e)
    {
        ScrollPans.PassNotchTo(RollScroll, e, e.KeyModifiers);
    }

    // Does what the wheel means over the ruler: Ctrl zooms the columns, C steps the channel and Shift scrolls sideways.
    private void OnRulerScrollWheel(object? sender, PointerWheelEventArgs e)
    {
        HandleGridWheel(e, null, null);
    }

    // Does what a notch over one of the grid's surfaces means for the modifiers held, given what that surface does with Alt and with no modifier.
    private void HandleGridWheel(PointerWheelEventArgs e, Action<PointerWheelEventArgs>? zoomRowsUnderCursor, Action<PointerWheelEventArgs>? takeUnmodifiedNotch)
    {
        if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Alt))
        {
            if (TryZoomRows(e, zoomRowsUnderCursor))
            {
                ZoomColumnsUnderCursor(e);
            }

            return;
        }

        if (e.KeyModifiers == KeyModifiers.Control)
        {
            ZoomColumnsUnderCursor(e);
            return;
        }

        if (e.KeyModifiers == KeyModifiers.Alt)
        {
            if (TryZoomRows(e, zoomRowsUnderCursor))
            {
                e.Handled = true;
            }

            return;
        }

        if (IsChannelStepNotch(e))
        {
            CycleChannel(e);
            return;
        }

        if (e.KeyModifiers == KeyModifiers.Shift)
        {
            ScrollColumns(e);
            return;
        }

        if (e.KeyModifiers != KeyModifiers.None || takeUnmodifiedNotch is null)
        {
            return;
        }

        takeUnmodifiedNotch(e);
        e.Handled = true;
    }

    // Zooms the rows under the cursor where the surface has rows to zoom, answering whether it did.
    private static bool TryZoomRows(PointerWheelEventArgs e, Action<PointerWheelEventArgs>? zoomRowsUnderCursor)
    {
        if (zoomRowsUnderCursor is null)
        {
            return false;
        }

        zoomRowsUnderCursor(e);
        return true;
    }

    // Whether a notch is the channel step, which is the wheel with C held and nothing else.
    private bool IsChannelStepNotch(PointerWheelEventArgs e)
    {
        return _isChannelStepKeyDown && e.KeyModifiers == KeyModifiers.None;
    }

    // What one notch multiplies a zoom or a lane's height by.
    private static double NotchZoomFactor(PointerWheelEventArgs e)
    {
        return RollMetrics.ZoomFactorForNotches(e.Delta.Y);
    }

    // Scrolls the grid sideways by a notch, holding off the resize Shift would otherwise start on whatever the pointer is over.
    private void ScrollColumns(PointerWheelEventArgs e)
    {
        Roll.SuppressResize();
        foreach (var lane in UnitLanes)
        {
            lane.SuppressResize();
        }

        double columns = ColumnsPerScrollNotch * e.Delta.Y;
        RollScroll.Offset = RollScroll.Offset.WithX(RollScroll.Offset.X - columns * RollMetrics.CellWidth);
        e.Handled = true;
    }

    // Scrolls the roll by however far a middle-drag over it or the keys has just moved, held to the roll's ends.
    private void OnRollPan(Vector moved)
    {
        ScrollPans.PanBy(RollScroll, moved);
    }

    // Scrolls the roll sideways and the lanes down by however far a middle-drag over a lane has just moved, each held to its ends.
    private void OnLanePan(Vector moved)
    {
        ScrollPans.PanBy(RollScroll, new Vector(moved.X, 0));
        ScrollPans.PanBy(LaneScroll, new Vector(0, moved.Y));
    }

    // Zooms the columns by a notch, keeping the tick under the cursor under the cursor, or the tick at the view's left edge where the cursor is left of it.
    private void ZoomColumnsUnderCursor(PointerWheelEventArgs e)
    {
        e.Handled = true;
        ZoomColumnsTo(RollMetrics.Zoom * NotchZoomFactor(e), Math.Max(e.GetPosition(Roll).X, RollScroll.Offset.X));
    }

    // Draws the grid at a zoom, holding the tick at a position in the roll's own coordinates where it is on screen.
    private void ZoomColumnsTo(double zoom, double anchorX)
    {
        double zoomBefore = RollMetrics.Zoom;
        RollMetrics.Zoom = zoom;
        if (RollMetrics.Zoom == zoomBefore)
        {
            return;
        }

        Roll.RescaleRememberedPositions(RollMetrics.Zoom / zoomBefore, 1);
        double heldOffsetX = RollMetrics.ViewStartHolding(RollScroll.Offset.X, anchorX - RollScroll.Offset.X, zoomBefore, RollMetrics.Zoom);
        RefreshGrid();
        RollScroll.UpdateLayout();
        RollScroll.Offset = RollScroll.Offset.WithX(heldOffsetX);
        PlaceCellEditor();
        Roll.ContentMoved();
    }

    // Zooms the roll's rows by a notch, keeping the row under the cursor under the cursor.
    private void ZoomRollRowsUnderCursor(PointerWheelEventArgs e)
    {
        ZoomRowsTo(RollMetrics.VerticalZoom * NotchZoomFactor(e), e.GetPosition(Roll).Y);
    }

    // Draws the rows at a zoom, holding the row at a position in the roll's own coordinates where it is on screen.
    private void ZoomRowsTo(double zoom, double anchorY)
    {
        double zoomBefore = RollMetrics.VerticalZoom;
        RollMetrics.VerticalZoom = zoom;
        if (RollMetrics.VerticalZoom == zoomBefore)
        {
            return;
        }

        Roll.RescaleRememberedPositions(1, RollMetrics.VerticalZoom / zoomBefore);
        double heldOffsetY = RollMetrics.ViewStartHolding(RollScroll.Offset.Y, anchorY - RollScroll.Offset.Y, zoomBefore, RollMetrics.VerticalZoom);
        Roll.Refresh();
        Keys.RedrawAtRowHeight();
        RollScroll.UpdateLayout();
        RollScroll.Offset = RollScroll.Offset.WithY(heldOffsetY);
        Roll.ContentMoved();
    }
}
