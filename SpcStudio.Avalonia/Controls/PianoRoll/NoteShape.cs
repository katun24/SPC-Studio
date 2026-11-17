using System.Collections.Immutable;
using Avalonia;
using Avalonia.Media;
using SpcStudio.Model;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// How one note is drawn on the roll and where a press reaches it: the plain block, the band its $DD chain plays, the handles on that chain's joints,
// and the vibrato wave and the tremolo line drawn inside it.
public static class NoteShape
{
    private const int MinBandPointCount = 2;
    private const double PastNoteEndOpacity = 0.35;
    private const double RampThicknessShare = 0.5;
    private const double MinRampThickness = 2;
    private const double MinSweepCyclePixels = 8;
    private const double SweepSampleStepPixels = 1;
    private const double HalfPhasePeriod = SnesSweep.PhasePeriod / 2.0;

    // Whether a position is on the stretch of a bent note's band that is the note's own, drawn at an inset.
    public static bool IsOnNoteOwnBand(Note note, Point position, double inset)
    {
        return NoteOwnBandOf(note) is List<Point> bandPoints && IsOnBand(bandPoints, position, HalfBandHeight(inset));
    }

    // Half the height of a note's block or band drawn at an inset, which is also the radius of its bend joints' handles.
    public static double HalfBandHeight(double inset)
    {
        return (RollMetrics.CellHeight - inset * 2) / 2;
    }

    // A bent note's band in the grid's own pixels, with the index of the vertex on the note's own end, or null for a note that bends no pitch.
    private static (List<Point> Points, int NoteEndIndex)? BandOf(Note note)
    {
        var bendPoints = BendPath.PointsOf(note);
        if (bendPoints.Length < MinBandPointCount)
        {
            return null;
        }

        double noteStartX = note.StartTick * RollMetrics.PixelsPerTick;
        var bandPoints = new List<Point>(bendPoints.Length + 1);
        foreach (var bendPoint in bendPoints)
        {
            bandPoints.Add(new Point(noteStartX + bendPoint.Tick * RollMetrics.PixelsPerTick, RollMetrics.PitchCenterY(bendPoint.Semitone)));
        }

        int noteEndIndex = PutVertexOnNoteEnd(note, bendPoints, bandPoints, noteStartX);
        InsetFlatEndsByGutter(bandPoints, noteEndIndex);
        return (bandPoints, noteEndIndex);
    }

    // The stretch of a bent note's band that is the note's own, up to its end, or null for a note that bends no pitch.
    private static List<Point>? NoteOwnBandOf(Note note)
    {
        if (BandOf(note) is not (List<Point> bandPoints, int noteEndIndex))
        {
            return null;
        }

        return bandPoints.GetRange(0, noteEndIndex + 1);
    }

    // Whether a position is on a band: inside one of its blocks, or within a joint's radius of one of its ramps.
    private static bool IsOnBand(IReadOnlyList<Point> bandPoints, Point position, double halfHeight)
    {
        var positionRect = new Rect(position, position);
        foreach (var (from, to) in SegmentsOf(bandPoints))
        {
            bool isOnSegment = IsFlatSegment(from, to) ? IsSegmentTouchedByRect(from, to, halfHeight, positionRect) : IsWithinDistanceOfLine(from, to, position, halfHeight);
            if (isOnSegment)
            {
                return true;
            }
        }

        return false;
    }

    // Whether any of what a note is drawn as on its own ticks, drawn at an inset, lies inside a rectangle.
    public static bool IsTouchedByRect(Note note, Rect rect, double inset)
    {
        if (note.Bends.IsEmpty)
        {
            var blockRect = BlockRectOf(note, inset);
            return IsSegmentTouchedByRect(new Point(blockRect.Left, blockRect.Center.Y), new Point(blockRect.Right, blockRect.Center.Y), blockRect.Height / 2, rect);
        }

        if (NoteOwnBandOf(note) is not List<Point> bandPoints)
        {
            return false;
        }

        double halfHeight = HalfBandHeight(inset);
        foreach (var (from, to) in SegmentsOf(bandPoints))
        {
            if (IsSegmentTouchedByRect(from, to, SegmentHalfThickness(from, to, halfHeight), rect))
            {
                return true;
            }
        }

        return false;
    }

    // Where a joint of a note's $DD chain is drawn and pressed, in the grid's own pixels.
    public static Point BendNodeCenter(Note note, BendNode node)
    {
        return new Point((note.StartTick + node.Tick) * RollMetrics.PixelsPerTick, RollMetrics.PitchCenterY(node.Semitone));
    }

    // The last tick anything of a note is drawn at: its own end, or where its $DD chain finishes where that is later.
    public static int DrawnEndTick(Note note)
    {
        return note.StartTick + Math.Max(note.LengthTicks, note.BendChainTicks);
    }

    // Draws a note: a block on its own row where it bends no pitch, and the band its $DD chain plays, clipped to the grid, where it does.
    public static void DrawNote(DrawingContext dc, Note note, IBrush fill, IPen? border, double inset, Rect gridBounds)
    {
        if (!note.Bends.IsEmpty)
        {
            DrawBentNote(dc, note, fill, border, inset, gridBounds);
            return;
        }

        dc.DrawRectangle(fill, border, BlockRectOf(note, inset));
    }

    // The outline of what a note is drawn as on its own ticks: its block where it bends no pitch, and its own stretch of its band where it does.
    public static Geometry OutlineOf(Note note, double inset)
    {
        if (note.Bends.IsEmpty)
        {
            return new RectangleGeometry(BlockRectOf(note, inset));
        }

        var outline = new GeometryGroup { FillRule = FillRule.NonZero };
        if (NoteOwnBandOf(note) is List<Point> bandPoints)
        {
            outline.Children.AddRange(BandPieces(bandPoints, HalfBandHeight(inset)));
        }

        return outline;
    }

    // Draws a note's bend nodes as circles a band high, clipped to the grid, each held on its row crossed.
    public static void DrawBendNodes(DrawingContext dc, Note note, IBrush? nodeBrush, IBrush crossBrush, double inset, Rect gridBounds)
    {
        var nodes = BendNodes.NodesOf(note);
        if (nodes.IsEmpty)
        {
            return;
        }

        using var clip = dc.PushClip(gridBounds);
        for (int nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
        {
            DrawBendNodeCircle(dc, note, nodes[nodeIndex], nodeBrush, inset);
            if (BendNodes.IsNodeHeldOnRow(note, nodeIndex))
            {
                DrawHeldNodeCross(dc, BendNodeCenter(note, nodes[nodeIndex]), HalfBandHeight(inset), crossBrush);
            }
        }
    }

    // Draws one joint of a note's $DD chain as a circle a band high, clipped to the grid.
    public static void DrawBendNode(DrawingContext dc, Note note, BendNode node, IBrush? nodeBrush, double inset, Rect gridBounds)
    {
        using var clip = dc.PushClip(gridBounds);
        DrawBendNodeCircle(dc, note, node, nodeBrush, inset);
    }

    // Draws one joint of a note's $DD chain as a circle a band high.
    private static void DrawBendNodeCircle(DrawingContext dc, Note note, BendNode node, IBrush? nodeBrush, double inset)
    {
        double radius = HalfBandHeight(inset);
        dc.DrawEllipse(nodeBrush, null, BendNodeCenter(note, node), radius, radius);
    }

    // Draws the cross that marks a node held on its row, its ink filling the square inside the node's circle.
    private static void DrawHeldNodeCross(DrawingContext dc, Point center, double radius, IBrush crossBrush)
    {
        double inkSide = radius * Math.Sqrt(2);
        double scale = inkSide / AppIcons.Size;
        using var placed = dc.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(center.X - inkSide / 2, center.Y - inkSide / 2));
        dc.DrawGeometry(crossBrush, null, AppIcons.Close);
    }

    // Draws a note's vibrato inside it: a wave along each stretch that sweeps, and a line at each stretch's start and end inside the note.
    public static void DrawVibrato(DrawingContext dc, Note note, IPen pen, double inset)
    {
        DrawSweep<NoteVibrato, VibratoState>(dc, note, note.Vibratos, pen, inset, (stretch, trackAt, startX, endX) => DrawVibratoWave(dc, pen, stretch, trackAt, startX, endX));
    }

    // Draws a note's tremolo inside it: a line hanging from the top along each stretch that ducks, and a line at each stretch's start and end inside the note.
    public static void DrawTremolo(DrawingContext dc, Note note, IPen pen, double inset)
    {
        DrawSweep<NoteTremolo, TremoloState>(dc, note, note.Tremolos, pen, inset, (stretch, trackAt, startX, endX) => DrawTremoloLine(dc, pen, stretch, trackAt, startX, endX));
    }

    // The rectangle a note is drawn as on its own row, a gutter short of its two ends and inset at the top and bottom.
    private static Rect BlockRectOf(Note note, double inset)
    {
        return NoteBlock.BlockRect(note.StartTick * RollMetrics.PixelsPerTick, note.LengthTicks * RollMetrics.PixelsPerTick, RollMetrics.RowOfSemitone(note.Semitone) * RollMetrics.CellHeight,
            RollMetrics.CellHeight, inset);
    }

    // Puts a vertex on the note's own end where the chain runs past it, answering the index of the vertex there.
    private static int PutVertexOnNoteEnd(Note note, ImmutableArray<BendPoint> bendPoints, List<Point> bandPoints, double noteStartX)
    {
        for (int index = 0; index < bendPoints.Length; index++)
        {
            if (bendPoints[index].Tick < note.LengthTicks)
            {
                continue;
            }

            if (bendPoints[index].Tick > note.LengthTicks)
            {
                var (before, after) = (bendPoints[index - 1], bendPoints[index]);
                double fraction = (double)(note.LengthTicks - before.Tick) / (after.Tick - before.Tick);
                double semitoneAtNoteEnd = before.Semitone + (after.Semitone - before.Semitone) * fraction;
                bandPoints.Insert(index, new Point(noteStartX + note.LengthTicks * RollMetrics.PixelsPerTick, RollMetrics.PitchCenterY(semitoneAtNoteEnd)));
            }

            return index;
        }

        return bandPoints.Count - 1;
    }

    // Pulls each end of the band that is a block in by the note's gutter, so it starts and ends where a flat note of the same span would.
    private static void InsetFlatEndsByGutter(List<Point> bandPoints, int noteEndIndex)
    {
        double gutter = Space.Px2;
        if (IsFlatSegment(bandPoints[0], bandPoints[1]))
        {
            bandPoints[0] = new Point(bandPoints[0].X + gutter, bandPoints[0].Y);
        }

        if (IsFlatSegment(bandPoints[noteEndIndex - 1], bandPoints[noteEndIndex]))
        {
            bandPoints[noteEndIndex] = new Point(Math.Max(bandPoints[noteEndIndex].X - gutter, bandPoints[0].X), bandPoints[noteEndIndex].Y);
        }

        if (noteEndIndex < bandPoints.Count - 1 && IsFlatSegment(bandPoints[^2], bandPoints[^1]))
        {
            bandPoints[^1] = new Point(Math.Max(bandPoints[^1].X - gutter, bandPoints[noteEndIndex].X), bandPoints[^1].Y);
        }
    }

    // Every segment of a band, from one point to the next, in order.
    private static IEnumerable<(Point From, Point To)> SegmentsOf(IReadOnlyList<Point> bandPoints)
    {
        for (int index = 1; index < bandPoints.Count; index++)
        {
            yield return (bandPoints[index - 1], bandPoints[index]);
        }
    }

    // Whether a segment of a band holds one pitch, which is drawn as a block.
    private static bool IsFlatSegment(Point from, Point to)
    {
        return from.Y == to.Y;
    }

    // Half how thick a segment of a band is drawn up and down: a block's half height where it is flat, and a ramp's half thickness where it is not.
    private static double SegmentHalfThickness(Point from, Point to, double halfHeight)
    {
        return IsFlatSegment(from, to) ? halfHeight : RampHalfThickness(halfHeight);
    }

    // Half how thick a ramp is drawn up and down, for a band of a half height.
    private static double RampHalfThickness(double halfHeight)
    {
        return halfHeight * RampThicknessShare;
    }

    // Whether a rectangle reaches a segment drawn a half thickness above and below its line at every position along it.
    private static bool IsSegmentTouchedByRect(Point from, Point to, double halfThickness, Rect rect)
    {
        double left = Math.Max(from.X, rect.Left);
        double right = Math.Min(to.X, rect.Right);
        if (left > right)
        {
            return false;
        }

        var (leftY, rightY) = to.X > from.X ? (LineYAt(from, to, left), LineYAt(from, to, right)) : (from.Y, to.Y);
        return Math.Min(leftY, rightY) - halfThickness <= rect.Bottom && Math.Max(leftY, rightY) + halfThickness >= rect.Top;
    }

    // Whether a position is within a distance of the line between two points.
    private static bool IsWithinDistanceOfLine(Point from, Point to, Point position, double distance)
    {
        double deltaX = to.X - from.X;
        double deltaY = to.Y - from.Y;
        double fraction = Math.Clamp(((position.X - from.X) * deltaX + (position.Y - from.Y) * deltaY) / (deltaX * deltaX + deltaY * deltaY), 0, 1);
        double offsetX = from.X + deltaX * fraction - position.X;
        double offsetY = from.Y + deltaY * fraction - position.Y;
        return offsetX * offsetX + offsetY * offsetY <= distance * distance;
    }

    // The height of the line between two points at a position, the start's own where the line has no width.
    private static double LineYAt(Point from, Point to, double x)
    {
        double fraction = to.X > from.X ? (x - from.X) / (to.X - from.X) : 0;
        return from.Y + (to.Y - from.Y) * fraction;
    }

    // Draws a bent note's band, clipped to the grid, with the stretch past the note's own end faded and unbordered.
    private static void DrawBentNote(DrawingContext dc, Note note, IBrush fill, IPen? border, double inset, Rect gridBounds)
    {
        if (BandOf(note) is not (List<Point> bandPoints, int noteEndIndex))
        {
            return;
        }

        double halfHeight = HalfBandHeight(inset);
        using var clip = dc.PushClip(gridBounds);
        DrawBand(dc, bandPoints.GetRange(0, noteEndIndex + 1), halfHeight, fill, border);
        if (noteEndIndex == bandPoints.Count - 1)
        {
            return;
        }

        using var faded = dc.PushOpacity(PastNoteEndOpacity);
        DrawBand(dc, bandPoints.GetRange(noteEndIndex, bandPoints.Count - noteEndIndex), halfHeight, fill, null);
    }

    // Draws a band, piece by piece.
    private static void DrawBand(DrawingContext dc, List<Point> bandPoints, double halfHeight, IBrush fill, IPen? border)
    {
        foreach (var piece in BandPieces(bandPoints, halfHeight))
        {
            dc.DrawGeometry(fill, border, piece);
        }
    }

    // The pieces a band is drawn as, in drawing order: its ramps as one outline, then every run of flat segments as a block either side of its row.
    private static IEnumerable<Geometry> BandPieces(List<Point> bandPoints, double halfHeight)
    {
        var runs = SegmentRunsOf(bandPoints);
        if (RampOutline(bandPoints, runs, RampHalfThickness(halfHeight)) is Geometry ramps)
        {
            yield return ramps;
        }

        foreach (var run in runs)
        {
            double width = bandPoints[run.EndIndex].X - bandPoints[run.StartIndex].X;
            if (!run.IsFlat || width <= 0)
            {
                continue;
            }

            yield return new RectangleGeometry(new Rect(bandPoints[run.StartIndex].X, bandPoints[run.StartIndex].Y - halfHeight, width, halfHeight * 2));
        }
    }

    // Every longest run of segments of a band that are all flat or all ramps, in order.
    private static List<SegmentRun> SegmentRunsOf(List<Point> bandPoints)
    {
        var runs = new List<SegmentRun>();
        int runStartIndex = 0;
        for (int index = 1; index < bandPoints.Count; index++)
        {
            bool isFlat = IsFlatSegment(bandPoints[index - 1], bandPoints[index]);
            if (index == bandPoints.Count - 1 || IsFlatSegment(bandPoints[index], bandPoints[index + 1]) != isFlat)
            {
                runs.Add(new SegmentRun(runStartIndex, index, isFlat));
                runStartIndex = index;
            }
        }

        return runs;
    }

    // The ramps of a band as one outline: each run of ramps a strip a thickness tall at every position along it and never thinner than the thinnest ramp drawn, or null with no ramp.
    private static Geometry? RampOutline(List<Point> bandPoints, List<SegmentRun> runs, double halfThickness)
    {
        var strips = new StreamGeometry();
        var centerLines = new StreamGeometry();
        bool hasRamp = false;
        using (var stripContext = strips.Open())
        using (var lineContext = centerLines.Open())
        {
            stripContext.SetFillRule(FillRule.NonZero);
            foreach (var run in runs)
            {
                if (run.IsFlat)
                {
                    continue;
                }

                AddRampStrip(stripContext, bandPoints, run, halfThickness);
                StraightEdges.AddLine(lineContext, RunPoints(bandPoints, run));
                hasRamp = true;
            }
        }

        if (!hasRamp)
        {
            return null;
        }

        var thinnestStroke = new Pen(Brushes.Black, MinRampThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        if (centerLines.GetWidenedGeometry(thinnestStroke) is not Geometry thinnest)
        {
            return strips;
        }

        return new CombinedGeometry(GeometryCombineMode.Union, strips, thinnest);
    }

    // Adds a run of ramps as a filled strip reaching a half thickness above and below its line at every position.
    private static void AddRampStrip(StreamGeometryContext context, List<Point> bandPoints, SegmentRun run, double halfThickness)
    {
        var runPoints = RunPoints(bandPoints, run);
        var topEdge = runPoints.Select(point => point.WithY(point.Y - halfThickness));
        var bottomEdgeBack = Enumerable.Reverse(runPoints).Select(point => point.WithY(point.Y + halfThickness));
        StraightEdges.AddFilled(context, [.. topEdge, .. bottomEdgeBack]);
    }

    // The band's points a run of ramps passes through, its first and last included.
    private static List<Point> RunPoints(List<Point> bandPoints, SegmentRun run)
    {
        return bandPoints.GetRange(run.StartIndex, run.EndIndex - run.StartIndex + 1);
    }

    // Draws one of a note's two sweeps inside it, clipped to the note, with each stretch drawn by the sweep's own drawing.
    private static void DrawSweep<TStretch, TState>(DrawingContext dc, Note note, ImmutableArray<TStretch> stretches, IPen pen, double inset, StretchDrawing<TStretch> drawStretch)
        where TStretch : struct, ISweepStretch<TStretch, TState>
        where TState : struct, ISweepState<TState>
    {
        if (stretches.IsEmpty || BlockRectOf(note, inset).Height - 2 * Space.Px1Point5 <= 0)
        {
            return;
        }

        var (trackAt, bounds) = SweepTrack(note, inset);
        using var clip = dc.PushClip(bounds);
        int stretchEndTick = 0;
        foreach (var stretch in stretches)
        {
            int previousEndTick = stretchEndTick;
            int stretchStartTick = previousEndTick + stretch.DelayTicks;
            stretchEndTick = stretchStartTick + stretch.LengthTicks;
            double startX = (note.StartTick + stretchStartTick) * RollMetrics.PixelsPerTick;
            double endX = (note.StartTick + stretchEndTick) * RollMetrics.PixelsPerTick;
            if (stretch.IsActive && endX > startX)
            {
                drawStretch(stretch, trackAt, startX, endX);
            }

            if (stretchStartTick > previousEndTick)
            {
                DrawStretchEdgeMark(dc, pen, trackAt, startX);
            }

            if (stretchEndTick < note.LengthTicks)
            {
                DrawStretchEdgeMark(dc, pen, trackAt, endX);
            }
        }
    }

    // The line a note's sweeps are drawn along and the box that holds all of it: the block's own middle, or the band's where the note bends.
    private static (SweepTrackAt TrackAt, Rect Bounds) SweepTrack(Note note, double inset)
    {
        var blockRect = BlockRectOf(note, inset);
        double halfHeight = blockRect.Height / 2;
        if (BandOf(note) is not (List<Point> bandPoints, _))
        {
            return (_ => (blockRect.Y + halfHeight, halfHeight), blockRect);
        }

        double top = bandPoints.Min(point => point.Y) - halfHeight;
        double bottom = bandPoints.Max(point => point.Y) + halfHeight;
        return (x => BandTrackAt(bandPoints, x, halfHeight), new Rect(blockRect.X, top, blockRect.Width, bottom - top));
    }

    // A band's center line and half thickness at a position, the band's own ends standing for anything beyond them.
    private static (double CenterY, double HalfThickness) BandTrackAt(List<Point> bandPoints, double x, double halfHeight)
    {
        if (x <= bandPoints[0].X)
        {
            return (bandPoints[0].Y, SegmentHalfThickness(bandPoints[0], bandPoints[1], halfHeight));
        }

        foreach (var (from, to) in SegmentsOf(bandPoints))
        {
            if (x > to.X)
            {
                continue;
            }

            return (LineYAt(from, to, x), SegmentHalfThickness(from, to, halfHeight));
        }

        return (bandPoints[^1].Y, SegmentHalfThickness(bandPoints[^2], bandPoints[^1], halfHeight));
    }

    // Draws the line across a note where a stretch of one of its sweeps begins or ends, the note's whole thickness at that point.
    private static void DrawStretchEdgeMark(DrawingContext dc, IPen pen, SweepTrackAt trackAt, double x)
    {
        var (centerY, halfThickness) = trackAt(x);
        dc.DrawLine(pen, new Point(x, centerY - halfThickness), new Point(x, centerY + halfThickness));
    }

    // How many pixels one cycle of a sweep at a rate is drawn across, never fewer than the shortest cycle drawn, and infinite where the rate never moves.
    private static double CyclePixels(int rate)
    {
        return Math.Max(SnesSweep.PeriodTicks(rate) * RollMetrics.PixelsPerTick, MinSweepCyclePixels);
    }

    // The curve through heights sampled along it every few pixels from one position to another, both ends included.
    private static StreamGeometry SampledCurve(double startX, double endX, Func<double, double> yAt)
    {
        var points = new List<Point> { new(startX, yAt(startX)) };
        for (double x = startX + SweepSampleStepPixels; x < endX; x += SweepSampleStepPixels)
        {
            points.Add(new Point(x, yAt(x)));
        }

        points.Add(new Point(endX, yAt(endX)));
        return StraightEdges.Line(points);
    }

    // Draws the vibrato wave of one stretch between two positions.
    private static void DrawVibratoWave(DrawingContext dc, IPen pen, NoteVibrato stretch, SweepTrackAt trackAt, double startX, double endX)
    {
        double cyclePixels = CyclePixels(stretch.Rate);
        dc.DrawGeometry(null, pen, SampledCurve(startX, endX, x => VibratoWaveY(stretch, trackAt, startX, cyclePixels, x)));
    }

    // Where the vibrato wave stands at a position: around the note's own line there, and only as far out as the note is thick there.
    private static double VibratoWaveY(NoteVibrato stretch, SweepTrackAt trackAt, double startX, double cyclePixels, double x)
    {
        var (centerY, halfThickness) = trackAt(x);
        if (double.IsInfinity(cyclePixels))
        {
            return centerY;
        }

        double reach = Math.Max(halfThickness - Space.Px1Point5, 0);
        return centerY - VibratoAmplitude(stretch, startX, x, reach) * Math.Sin(2 * Math.PI * (x - startX) / cyclePixels);
    }

    // How far the wave swings at a position within a reach: the extent byte over its full range, widened through the stretch's fade where it has one.
    private static double VibratoAmplitude(NoteVibrato stretch, double startX, double x, double reach)
    {
        int fadeStepsRun = RollMetrics.TickAtX(x - startX) + 1;
        int extent = stretch.FadeTicks == SnesVibrato.MinFadeTicks ? stretch.Extent : SnesVibrato.FadedExtent(stretch.Extent, stretch.FadeTicks, fadeStepsRun);
        return reach * extent / SnesVibrato.MaxExtent;
    }

    // Draws the tremolo line of one stretch between two positions.
    private static void DrawTremoloLine(DrawingContext dc, IPen pen, NoteTremolo stretch, SweepTrackAt trackAt, double startX, double endX)
    {
        double cyclePixels = CyclePixels(stretch.Rate);
        dc.DrawGeometry(null, pen, SampledCurve(startX, endX, x => TremoloLineY(stretch, trackAt, startX, cyclePixels, x)));
    }

    // Where the tremolo line stands at a position: hanging from the top of the note's own line there, as far down as the note is thick there.
    private static double TremoloLineY(NoteTremolo stretch, SweepTrackAt trackAt, double startX, double cyclePixels, double x)
    {
        var (centerY, halfThickness) = trackAt(x);
        double top = centerY - halfThickness + Space.Px1Point5;
        if (double.IsInfinity(cyclePixels))
        {
            return top;
        }

        double reach = Math.Max(halfThickness * 2 - 2 * Space.Px1Point5, 0);
        double depth = (double)stretch.Amount / SnesTremolo.MaxAmount;
        return top + reach * depth * TriangleAt(TremoloPhase(stretch, startX, cyclePixels, x));
    }

    // The tremolo's phase at a position, held where it pins at the full amount and wrapped round its period otherwise.
    private static double TremoloPhase(NoteTremolo stretch, double startX, double cyclePixels, double x)
    {
        double phase = SnesSweep.PhasePeriod * (x - startX) / cyclePixels;
        if (stretch.Amount == SnesTremolo.MaxAmount)
        {
            return Math.Min(phase, SnesTremolo.PinnedPhase);
        }

        return phase % SnesSweep.PhasePeriod;
    }

    // The driver's triangle at a phase as a fraction of its peak: up to the peak at the halfway point and back down to nothing at the wrap.
    private static double TriangleAt(double phase)
    {
        return phase < HalfPhasePeriod ? phase / HalfPhasePeriod : (SnesSweep.PhasePeriod - phase) / HalfPhasePeriod;
    }

    // A note's sweep line and half its thickness at a position along it.
    private delegate (double CenterY, double HalfThickness) SweepTrackAt(double x);

    // Draws one stretch of a sweep between two positions, along the note's sweep line.
    private delegate void StretchDrawing<TStretch>(TStretch stretch, SweepTrackAt trackAt, double startX, double endX);

    // A longest run of a band's segments that are all flat or all ramps, from the index of its first point to that of its last.
    private readonly record struct SegmentRun(int StartIndex, int EndIndex, bool IsFlat);
}
