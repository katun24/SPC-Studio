using Avalonia;
using Avalonia.Media;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The roll's drawing: the row stripes and the grid, every channel's notes with their cuts, the wash past the end, the selection box and
// the placement preview, and the brush set each instrument's notes are drawn with.
public sealed partial class PianoRoll
{
    private const double SelectedFillLift = 0.45;
    private const double OtherChannelFillFade = 0.7;
    private const double VibratoDarkening = 0.6;
    private const double TremoloLift = 0.75;
    private const double SweepLineThickness = 1.2;
    private const double SelectionLineThickness = 1;
    private const double SelectionDashLength = 4;
    private const double MinSelectionBoxWidth = 1;

    private readonly Brush _whiteRowBrush = new SolidColorBrush(Palette.RollWhiteRow);
    private readonly Brush _blackRowBrush = new SolidColorBrush(Palette.RollBlackRow);
    private readonly Brush _cRowBrush = new SolidColorBrush(Palette.RollCRow);
    private readonly Brush _selectionSweepBrush = new SolidColorBrush(Palette.RollSelectionSweep);

    // The dashed line a selection sweep and the box left round what it selected are drawn with.
    private readonly Pen _selectionPen = new(new SolidColorBrush(Palette.PaleGray), SelectionLineThickness, new DashStyle([SelectionDashLength, Space.Px3], 0));

    // The brush set each note color is drawn with, built the first time a color is drawn.
    private readonly Dictionary<int, NoteBrushes> _brushesByColor = [];

    // Draws the whole roll, culled to the part the viewer shows.
    public override void Render(DrawingContext dc)
    {
        double gridWidth = GridWidth;
        double gridHeight = RollMetrics.GridHeight;
        var gridBounds = new Rect(0, 0, gridWidth, gridHeight);
        int columns = _song.RollColumnCount;
        var (left, right) = _horizontalView.VisibleRange(gridWidth);
        var (top, bottom) = _verticalView.VisibleRange(gridHeight);

        dc.DrawRectangle(DrawnSurface.Background, null, gridBounds);
        RollMetrics.DrawPointerSpacePastEnd(dc, _song, gridHeight);
        DrawRows(dc, gridWidth, top, bottom);
        DrawGridLines(dc, gridWidth, gridHeight, left, right, columns * Song.GridTicks);
        DrawChannelNotes(dc, _selectedChannel, left, right, gridBounds);
        if (_isShowingOtherChannels)
        {
            for (int channel = 0; channel < _song.ChannelCount; channel++)
            {
                if (channel != _selectedChannel)
                {
                    DrawChannelNotes(dc, channel, left, right, gridBounds);
                }
            }
        }

        DrawCuts(dc, left, right, gridBounds);
        DrawEditedChannelBendNodes(dc, left, right, gridBounds);
        DrawPastEnd(dc, gridWidth, gridHeight);
        DrawSelectionBox(dc);
        DrawPlacementPreview(dc, left, right, new Rect(0, 0, RollMetrics.SurfaceWidth(_song), gridHeight));
        DrawNodeInsertPreview(dc, gridBounds);
    }

    // Draws the stripe and the line above it of every row between two heights.
    private void DrawRows(DrawingContext dc, double gridWidth, double top, double bottom)
    {
        int firstRow = Math.Max(0, (int)(top / RollMetrics.CellHeight));
        int lastRow = Math.Min(Song.RollRowCount - 1, (int)(bottom / RollMetrics.CellHeight));
        for (int row = firstRow; row <= lastRow; row++)
        {
            double y = row * RollMetrics.CellHeight;
            dc.DrawRectangle(RowBrushOf(RollMetrics.SemitoneOfRow(row)), null, new Rect(0, y, gridWidth, RollMetrics.CellHeight));
            dc.DrawLine(DrawnSurface.GridLinePen, new Point(0, y), new Point(gridWidth, y));
        }
    }

    // The stripe a semitone's row is drawn in: tinted on every C, darker on a black key.
    private IBrush RowBrushOf(int semitone)
    {
        var (_, noteInOctave) = SnesPitch.OctaveAndSemitone(semitone);
        if (noteInOctave == 0)
        {
            return _cRowBrush;
        }

        return RollMetrics.IsBlackKey[noteInOctave] ? _blackRowBrush : _whiteRowBrush;
    }

    // Draws the vertical grid lines between two positions, heavier on a bar, and the line closing the last row.
    private void DrawGridLines(DrawingContext dc, double gridWidth, double gridHeight, double left, double right, int endTick)
    {
        foreach (var (tick, isBarLine) in RollMetrics.GridLinesBetween(_song, left, right, endTick))
        {
            double x = RollMetrics.XOfTick(tick);
            dc.DrawLine(isBarLine ? DrawnSurface.BarLinePen : DrawnSurface.GridLinePen, new Point(x, 0), new Point(x, gridHeight));
        }

        dc.DrawLine(DrawnSurface.GridLinePen, new Point(0, gridHeight), new Point(gridWidth, gridHeight));
    }

    // Draws a channel's notes, with the notes a drag holds out of the song drawn where the drag has them.
    private void DrawChannelNotes(DrawingContext dc, int channel, double left, double right, Rect gridBounds)
    {
        var notes = _song.Notes(channel);
        foreach (int index in IndicesInView(notes, left, right))
        {
            var note = notes[index];
            var brushes = BrushesFor(note.InstrumentId);
            DrawNoteWithMarks(dc, channel, note, FillOf(channel, note, brushes), brushes, gridBounds);
        }

        DrawNodeDragNote(dc, channel, gridBounds);
        DrawDraggedNotes(dc, channel, left, right, gridBounds);
    }

    // The fill a channel's note is drawn in: the selected fill wherever it is selected and no drag holds other notes in its place - or the drag keeps the
    // selection beside the notes it holds - and otherwise the plain fill on the channel being edited and the dimmed fill on the others.
    private Brush FillOf(int channel, Note note, NoteBrushes brushes)
    {
        if ((!IsDraggingNotes || IsDragKeepingSelection) && _selectedKeySet.Contains((channel, note.StartTick)))
        {
            return brushes.SelectedFill;
        }

        return channel == _selectedChannel ? brushes.Fill : brushes.OtherChannelFill;
    }

    // Draws the note whose bend joint is being dragged, which the drag holds out of the song, on the channel being edited.
    private void DrawNodeDragNote(DrawingContext dc, int channel, Rect gridBounds)
    {
        if (_bendNoteAtPress is null || channel != _selectedChannel)
        {
            return;
        }

        var brushes = BrushesFor(_draggedBendNote.InstrumentId);
        DrawNoteWithMarks(dc, channel, _draggedBendNote, brushes.SelectedFill, brushes, gridBounds);
    }

    // Draws the notes on a channel that a drag holds out of the song, selected, where the drag has carried them.
    private void DrawDraggedNotes(DrawingContext dc, int channel, double left, double right, Rect gridBounds)
    {
        if (!_isDragging)
        {
            return;
        }

        foreach (var (draggedChannel, note) in _draggedNotes)
        {
            if (draggedChannel != channel || IsOutsideView(note, left, right))
            {
                continue;
            }

            var brushes = BrushesFor(note.InstrumentId);
            DrawNoteWithMarks(dc, channel, note, brushes.SelectedFill, brushes, gridBounds);
        }
    }

    // Draws a channel's note in a fill with its two sweeps, bordered where the channel is the one being edited.
    private void DrawNoteWithMarks(DrawingContext dc, int channel, Note note, IBrush fill, NoteBrushes brushes, Rect gridBounds)
    {
        double inset = NoteInsetOf(channel);
        NoteShape.DrawNote(dc, note, fill, channel == _selectedChannel ? brushes.BorderPen : null, inset, gridBounds);
        NoteShape.DrawVibrato(dc, note, brushes.VibratoPen, inset);
        NoteShape.DrawTremolo(dc, note, brushes.TremoloPen, inset);
    }

    // Draws the bend handles of every note showing them, over the notes and their cut stripes, each held on its row crossed in the note's own fill.
    private void DrawEditedChannelBendNodes(DrawingContext dc, double left, double right, Rect gridBounds)
    {
        foreach (var note in NotesShowingBendNodes())
        {
            if (IsOutsideView(note, left, right))
            {
                continue;
            }

            var brushes = BrushesFor(note.InstrumentId);
            NoteShape.DrawBendNodes(dc, note, brushes.BorderPen.Brush, brushes.Fill, Space.Px2, gridBounds);
        }
    }

    // What a channel's notes are drawn short of their row by, top and bottom: the edited channel's inset, or the narrower bar of the others.
    private double NoteInsetOf(int channel)
    {
        return channel == _selectedChannel ? Space.Px2 : Space.Px6;
    }

    // Draws stripes over the part of every note that the notes about to land would cut away.
    private void DrawCuts(DrawingContext dc, double left, double right, Rect gridBounds)
    {
        var shownLandingNotes = LandingNotes().Where(landing => landing.Channel == _selectedChannel || _isShowingOtherChannels).ToList();
        var cuts = LandingCuts.Of(shownLandingNotes, landing => (landing.Note.StartTick, landing.Note.EndTick()),
            landing => _song.Notes(landing.Channel).Select(note => (landing.Channel, Note: note)), (earlier, landing) => earlier.Channel == landing.Channel, left, right);
        foreach (var (cut, cutter) in cuts)
        {
            DrawCut(dc, cut.Note, cutter.Note, NoteInsetOf(cutter.Channel), gridBounds);
        }
    }

    // The notes about to land: the notes a drag holds, or the placement preview.
    private IReadOnlyList<(int Channel, Note Note)> LandingNotes()
    {
        return IsDraggingNotes ? NotesHeldByDrag : [.. _placementPreview.Members.Select(member => member.ChannelAndNote)];
    }

    // Draws the stripes over the stretch of a note that a landing note covers, in the note's own cut stripe color.
    private void DrawCut(DrawingContext dc, Note note, Note cutter, double inset, Rect gridBounds)
    {
        if (NoteBlock.CutRect(note.StartTick, note.EndTick(), cutter.StartTick, cutter.EndTick(), gridBounds.Height) is not Rect cut)
        {
            return;
        }

        NoteBlock.DrawCutStripes(dc, NoteShape.OutlineOf(note, inset), cut, BrushesFor(note.InstrumentId).CutStripePen);
    }

    // Draws the wash past the song's end.
    private void DrawPastEnd(DrawingContext dc, double gridWidth, double gridHeight)
    {
        double endX = RollMetrics.EndMarkerX(_song, gridWidth);
        dc.DrawRectangle(RollMetrics.PastEndBrush, null, new Rect(endX, 0, gridWidth - endX, gridHeight));
    }

    // Draws the rectangle a selection sweep in progress covers, or the box round what is selected where no sweep is running.
    private void DrawSelectionBox(DrawingContext dc)
    {
        if (_hasSweepMoved)
        {
            dc.DrawRectangle(_selectionSweepBrush, _selectionPen, SweepRect());
            return;
        }

        if (SelectionBounds() is Rect bounds)
        {
            dc.DrawRectangle(null, _selectionPen, bounds);
        }
    }

    // The smallest rectangle of whole rows holding every selected note and the pitches its bends reach, or null with nothing selected.
    private Rect? SelectionBounds()
    {
        var notes = SelectedNotes;
        if (notes.Count == 0)
        {
            return null;
        }

        int firstTick = int.MaxValue;
        int endTick = int.MinValue;
        int topRow = int.MaxValue;
        int bottomRow = int.MinValue;
        foreach (var note in notes)
        {
            firstTick = Math.Min(firstTick, note.StartTick);
            endTick = Math.Max(endTick, note.EndTick());
            var (lowestOffset, highestOffset) = PitchBend.Reach(note.Bends);
            topRow = Math.Min(topRow, RollMetrics.RowOfSemitone(note.Semitone + highestOffset));
            bottomRow = Math.Max(bottomRow, RollMetrics.RowOfSemitone(note.Semitone + lowestOffset));
        }

        double left = RollMetrics.XOfTick(firstTick);
        double top = topRow * RollMetrics.CellHeight;
        return new Rect(left, top, Math.Max(RollMetrics.XOfTick(endTick) - left, MinSelectionBoxWidth), (bottomRow + 1) * RollMetrics.CellHeight - top);
    }

    // Draws the notes a click would place, translucent over everything and into the space past the end of the grid.
    private void DrawPlacementPreview(DrawingContext dc, double left, double right, Rect surfaceBounds)
    {
        foreach (var member in _placementPreview.Members)
        {
            if (IsOutsideView(member.Note, left, right))
            {
                continue;
            }

            var brushes = BrushesFor(member.Note.InstrumentId);
            NoteShape.DrawNote(dc, member.Note, brushes.PreviewFill, brushes.PreviewBorderPen, Space.Px2, surfaceBounds);
        }
    }

    // Draws the bend joint B would put on a note, in the solid color drawn over a note for what is about to land on it.
    private void DrawNodeInsertPreview(DrawingContext dc, Rect gridBounds)
    {
        if (_nodeInsertPreview is not BendNodeInsert preview)
        {
            return;
        }

        var node = BendNodes.NodesOf(preview.NoteWithNode)[preview.NodeIndex];
        var brushes = BrushesFor(preview.NoteWithNode.InstrumentId);
        NoteShape.DrawBendNode(dc, preview.NoteWithNode, node, brushes.PendingMarkFill, Space.Px2, gridBounds);
    }

    // The indices of the notes, in time order, that something is drawn of between two positions.
    private static IEnumerable<int> IndicesInView(IReadOnlyList<Note> notes, double left, double right)
    {
        for (int index = 0; index < notes.Count; index++)
        {
            if (IsStartRightOf(notes[index], right))
            {
                yield break;
            }

            if (!IsDrawnEndLeftOf(notes[index], left))
            {
                yield return index;
            }
        }
    }

    // Whether a note starts past a position.
    private static bool IsStartRightOf(Note note, double x)
    {
        return RollMetrics.XOfTick(note.StartTick) > x;
    }

    // Whether the last of a note that is drawn, its bends included, ends before a position.
    private static bool IsDrawnEndLeftOf(Note note, double x)
    {
        return RollMetrics.XOfTick(NoteShape.DrawnEndTick(note)) < x;
    }

    // Whether nothing of a note is drawn between two positions.
    private static bool IsOutsideView(Note note, double left, double right)
    {
        return IsStartRightOf(note, right) || IsDrawnEndLeftOf(note, left);
    }

    // The brush set an instrument's notes are drawn with, gray for an instrument that has gone.
    private NoteBrushes BrushesFor(int instrumentId)
    {
        int colorRgb = _song.InstrumentById(instrumentId)?.ColorRgb ?? ChannelPalette.ToPackedRgb(Palette.AshGray);
        if (_brushesByColor.TryGetValue(colorRgb, out var brushes))
        {
            return brushes;
        }

        Color color = ChannelPalette.FromPackedRgb(colorRgb);
        var pendingMarkFill = NoteBlock.PendingMarkFill(color);
        brushes = new NoteBrushes(
            new SolidColorBrush(color),
            NoteBlock.BorderPen(color),
            new SolidColorBrush(ChannelPalette.Blend(color, Palette.White, SelectedFillLift)),
            new SolidColorBrush(ChannelPalette.Blend(color, Palette.DrawnSurfaceBackground, OtherChannelFillFade)),
            NoteBlock.PreviewFill(color),
            NoteBlock.PreviewBorderPen(color),
            new Pen(new SolidColorBrush(ChannelPalette.Blend(color, Palette.Black, VibratoDarkening)), SweepLineThickness),
            new Pen(new SolidColorBrush(ChannelPalette.Blend(color, Palette.White, TremoloLift)), SweepLineThickness),
            pendingMarkFill,
            NoteBlock.CutStripePenOf(pendingMarkFill));
        _brushesByColor[colorRgb] = brushes;
        return brushes;
    }

    // How one note color is drawn: the fill, the border, the selected and the dimmed fills, the preview pair, the vibrato and tremolo lines, and the
    // solid color drawn over the note for what is about to land on it, alone for a bend joint's preview and as the pen of its cut stripes.
    private sealed record NoteBrushes(
        Brush Fill, Pen BorderPen, Brush SelectedFill, Brush OtherChannelFill, Brush PreviewFill, Pen PreviewBorderPen, Pen VibratoPen, Pen TremoloPen,
        Brush PendingMarkFill, Pen CutStripePen);
}
