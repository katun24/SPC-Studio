using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SpcStudio.Audio;
using SpcStudio.Controls;
using SpcStudio.Model;

namespace SpcStudio;

// The keys, the transport and the markers: the window's shortcuts, starting, pausing and stopping the song, the playback position and the view that follows it, and the loop and end markers.
public partial class MainWindow
{
    private const int ChannelOfKeyZero = 9;

    private readonly PlayheadFollower _rollFollower;
    private readonly HeldShiftKey _heldShiftKey;

    // The playback position in grid columns, kept while stopped as where Play starts from.
    private double _positionColumns;

    // The playback position in ticks.
    private double PositionTicks
    {
        get
        {
            return _positionColumns * Song.GridTicks;
        }
    }

    // The playback position rounded to the nearest tick, never before the song's start.
    private int RoundedPositionTick
    {
        get
        {
            return Math.Max(0, (int)Math.Round(PositionTicks));
        }
    }

    // Follows a marker drag in the ruler, the dragged marker carrying the other along where it comes too close, and redraws the roll's length.
    private void OnMarkerMoved(TimeRuler.Marker marker, int tick)
    {
        if (marker == TimeRuler.Marker.End)
        {
            MoveEndMarkerPushingLoop(tick);
        }
        else
        {
            MoveLoopMarkerPushingEnd(tick);
        }

        RefreshRollAndExtent();
    }

    // Moves the end marker to a tick, pushing the loop marker ahead of it where the two would come too close.
    private void MoveEndMarkerPushingLoop(int tick)
    {
        int gap = _song.MinimumMarkerGapTicks;
        if (tick < _song.LoopTick + gap)
        {
            _song.SetMarkers(_song.EndTick, Math.Max(0, tick - gap));
        }

        _song.SetEndTick(tick);
    }

    // Moves the loop marker to a tick, pushing the end marker and the roll ahead of it where the two would come too close.
    private void MoveLoopMarkerPushingEnd(int tick)
    {
        int gap = _song.MinimumMarkerGapTicks;
        if (tick > _song.EndTick - gap)
        {
            _song.SetMarkers(tick + gap, _song.LoopTick);
        }

        _song.SetLoopTick(tick);
    }

    // Turns the end marker on or off, as an undo step of its own.
    private void OnEndToggled()
    {
        _song.IsEndEnabled = !_song.IsEndEnabled;
        ShowMarkerSwitch();
    }

    // Redraws the ruler, the markers and the roll after the end marker or the loop was switched on or off, as an undo step.
    private void ShowMarkerSwitch()
    {
        RefreshRulerAndMarkers();
        Roll.Refresh();
        MarkModified();
    }

    // Handles a key pressed in this window, ahead of whatever has the focus.
    protected override void HandleWindowKeyDown(KeyEventArgs e)
    {
        HandleKey(e, FocusManager?.GetFocusedElement(), PanelStack);
    }

    // Handles a key pressed in this window or in a panel window, given the element that has the focus there and the deck of panels standing there.
    private void HandleKey(KeyEventArgs e, IInputElement? focused, PanelDeck? keyWindowDeck)
    {
        _heldShiftKey.TakeKeyDown(e);
        if (TryHandleAccelerator(e, focused) || TryHandleAuditionKey(e, focused))
        {
            return;
        }

        if (WindowKeys.IsPlayKey(e, focused))
        {
            TogglePlayback();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.None)
        {
            ShowNextPanelOnTab(keyWindowDeck, focused);
            return;
        }

        if (focused is TextBox or ComboBox)
        {
            return;
        }

        if (e.Key == Key.C && e.KeyModifiers == KeyModifiers.None)
        {
            _isChannelStepKeyDown = true;
        }

        if (e.Key == Key.B && e.KeyModifiers == KeyModifiers.None)
        {
            Roll.SetNodeInsertKey(true);
        }

        if (TryHandleSingleKey(e))
        {
            e.Handled = true;
        }
    }

    // Tells the roll, the lanes of units and the note and instrument panels' play buttons whether Shift is held, which turns a drag on a note's or a
    // unit's end into a resize and each play button into a loop.
    private void ShowShiftHeld()
    {
        bool isShiftHeld = _heldShiftKey.IsHeld;
        ShowPlayNoteButton();
        ShowInstrumentPlayButton();
        Roll.SetResizeModifier(isShiftHeld);
        foreach (var lane in UnitLanes)
        {
            lane.SetResizeModifier(isShiftHeld);
        }
    }

    // The channel a digit key picks, in the order the digits stand along the keyboard, or null for any other key.
    private static int? ChannelOfDigitKey(Key key)
    {
        if (key is >= Key.D1 and <= Key.D9)
        {
            return key - Key.D1;
        }

        return key == Key.D0 ? ChannelOfKeyZero : null;
    }

    // Runs the shortcut a single key stands for while no field has the focus, answering whether there was one.
    private bool TryHandleSingleKey(KeyEventArgs e)
    {
        if (ChannelOfDigitKey(e.Key) is int channel)
        {
            if (channel < _song.ChannelCount)
            {
                SelectChannel(channel);
            }

            return true;
        }

        if (WindowKeys.IsDeleteKey(e.Key))
        {
            DeleteSelection();
            return true;
        }

        return TryScrollRollToEdge(e);
    }

    // Deletes the selected units, or else the selected notes.
    private void DeleteSelection()
    {
        if (UnitLaneHoldingSelection() is { } lane)
        {
            lane.DeleteSelected();
            return;
        }

        if (Roll.DeleteSelected())
        {
            MarkModified();
        }
    }

    // Scrolls the roll to its top, bottom, start or end for Page Up, Page Down, Home or End, answering whether the key was one of those.
    private bool TryScrollRollToEdge(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.PageUp:
                RollScroll.Offset = RollScroll.Offset.WithY(0);
                return true;
            case Key.PageDown:
                RollScroll.Offset = RollScroll.Offset.WithY(RollScroll.ScrollBarMaximum.Y);
                return true;
            default:
                return ViewEdges.TryPanToEdgeOfKey(_rollView, e, RollEdgeActionOf);
        }
    }

    // What Home or End does at its edge with modifiers held: Shift puts the playback position there, Ctrl the loop marker and Alt the end marker, a
    // marker going no closer to the other than it may.
    private Action<ViewEdge>? RollEdgeActionOf(KeyModifiers modifiers)
    {
        int gap = _song.MinimumMarkerGapTicks;
        return modifiers switch
        {
            KeyModifiers.Shift => edge => SetPosition(ViewEdges.ValueAt(edge, 0, _song.RollLengthTicks)),
            KeyModifiers.Control => edge => DropMarker(TimeRuler.Marker.Loop, ViewEdges.ValueAt(edge, 0, _song.EndTick - gap)),
            KeyModifiers.Alt => edge => DropMarker(TimeRuler.Marker.End, ViewEdges.ValueAt(edge, _song.LoopTick + gap, _song.RollLengthTicks)),
            _ => null,
        };
    }

    // Moves a marker to a tick as a drag dropped there does, as an undo step of its own.
    private void DropMarker(TimeRuler.Marker marker, int tick)
    {
        OnMarkerMoved(marker, tick);
        MarkModified();
    }

    // Runs the Ctrl shortcut a key stands for, answering whether there was one, which is also where the key means nothing else.
    private bool TryHandleAccelerator(KeyEventArgs e, IInputElement? focused)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return false;
        }

        bool isShiftHeld = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool isAltHeld = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        switch (e.Key)
        {
            case Key.A or Key.C when WindowKeys.IsOnSelectableText(focused):
                return false;
            case Key.X or Key.V when focused is TextBox:
                return false;
            case Key.A when isAltHeld && !isShiftHeld:
                Roll.SelectAllChannels();
                break;
            case Key.A when !isShiftHeld:
                Roll.SelectAllOnEditedChannel();
                break;
            case Key.X when !isShiftHeld:
                CutSelection();
                break;
            case Key.C when !isShiftHeld:
                CopySelection();
                break;
            case Key.V when !isShiftHeld:
                PasteClipboard();
                break;
            case Key.N when !isShiftHeld:
                OnNewSong(this, e);
                break;
            case Key.O when !isShiftHeld:
                OnOpenSong(this, e);
                break;
            case Key.S when !isShiftHeld:
                OnSaveSong(this, e);
                break;
            case Key.S:
                OnSaveSongAs(this, e);
                break;
            case Key.Z or Key.Y when WindowKeys.IsUndo(e):
                OnUndo(this, e);
                break;
            case Key.Z or Key.Y when WindowKeys.IsRedo(e):
                OnRedo(this, e);
                break;
            default:
                return false;
        }

        e.Handled = true;
        return true;
    }

    // Hands a cut or a copy to the unit lane holding a selection, emptying the roll's clipboard, and answers whether a lane held one.
    private bool TryClipboardOnLane(Action<IUnitLane> cutOrCopy)
    {
        if (UnitLaneHoldingSelection() is not { } lane)
        {
            return false;
        }

        PianoRoll.ClearClipboard();
        cutOrCopy(lane);
        return true;
    }

    // Cuts the selected units, or else the selected notes, the playback position then moving to the end of what was cut.
    private void CutSelection()
    {
        if (TryClipboardOnLane(lane => lane.CutSelected()))
        {
            return;
        }

        int? selectionEndTick = SelectionEndTick();
        if (!Roll.CutSelected())
        {
            return;
        }

        LaneClipboard.Clear();
        MarkModified();
        MovePositionAfterClipboard(selectionEndTick);
    }

    // Copies the selected units, or else the selected notes, the playback position then moving to the end of what was copied.
    private void CopySelection()
    {
        if (TryClipboardOnLane(lane => lane.CopySelected()))
        {
            return;
        }

        if (!Roll.CopySelected())
        {
            return;
        }

        LaneClipboard.Clear();
        MovePositionAfterClipboard(SelectionEndTick());
    }

    // Pastes the units at the cursor, or else the notes at the playback position while stopped, the position then moving to the end of what was laid down.
    private void PasteClipboard()
    {
        if (UnitLaneHoldingClipboard() is { } lane)
        {
            if (CursorTick() is int cursorTick)
            {
                lane.Paste(cursorTick);
            }

            return;
        }

        if (_audio?.Mixer.IsPlaying == true || !Roll.Paste(RoundedPositionTick))
        {
            return;
        }

        MarkModified();
        MovePositionAfterClipboard(SelectionEndTick());
    }

    // Handles a key let go in this window, ahead of whatever has the focus.
    protected override void HandleWindowKeyUp(KeyEventArgs e)
    {
        ReleaseKey(e);
    }

    // Lets go of the four held keys a press tracks, Shift, the space bar, C and B, from this window or a panel window.
    private void ReleaseKey(KeyEventArgs e)
    {
        _heldShiftKey.TakeKeyUp(e);
        if (e.Key == Key.Space)
        {
            ReleaseAuditionSpace();
        }

        if (e.Key == Key.C)
        {
            _isChannelStepKeyDown = false;
        }

        if (e.Key == Key.B)
        {
            Roll.SetNodeInsertKey(false);
        }
    }

    // Starts the song from the playback position, or pauses it where it is playing.
    private void TogglePlayback()
    {
        if (_audio is null)
        {
            return;
        }

        if (_audio.Mixer.IsPlaying)
        {
            PausePlayback();
            return;
        }

        StartPlaybackAtPosition();
    }

    // Plays from the playback position, the events built afresh.
    private void StartPlaybackAtPosition()
    {
        if (_audio is null)
        {
            return;
        }

        var (events, length) = SequenceBuilder.Build(_song, PlaybackEndTick(_positionColumns));
        _audio.Mixer.StartSequence(events, length, SamplesAtTick(PositionTicks), PlaybackLoopSample(_positionColumns));
        _playStart = _positionColumns;
        Transport.Buttons.ShowPause();
        _playheadTimer.Start();
    }

    // Stops the audio and leaves the playback position where the song had got to, so Play resumes from there.
    private void PausePlayback()
    {
        if (_audio is null)
        {
            return;
        }

        if (_audio.Mixer.IsPlaying)
        {
            _audio.Mixer.PauseSequence();
            ShowMixerPosition(_audio);
        }

        ShowPlaybackStopped();
    }

    // Stops the audio and takes the playback position back to the song's start.
    private void StopPlayback()
    {
        _audio?.Mixer.StopSequence();
        ShowPlaybackStopped();
        SetPosition(0);
    }

    // Stops the playhead and turns the transport's button back to a play.
    private void ShowPlaybackStopped()
    {
        _playheadTimer.Stop();
        Transport.Buttons.ShowPlay();
    }

    // Moves the playback position to a tick, jumping a run that is playing there.
    private void SetPosition(int tick)
    {
        ShowPosition(tick / (double)Song.GridTicks);
        if (_audio?.Mixer.IsPlaying == true)
        {
            StartPlaybackAtPosition();
        }
    }

    // Where the selection stops, or null with nothing selected.
    private int? SelectionEndTick()
    {
        return Roll.SelectedNotes is { Count: > 0 } notes ? notes.Max(note => note.EndTick()) : null;
    }

    // Moves the playback position to the end of what the clipboard just took or laid down, while stopped.
    private void MovePositionAfterClipboard(int? endTick)
    {
        if (endTick is not int tick || _audio?.Mixer.IsPlaying == true)
        {
            return;
        }

        SetPosition(tick);
    }

    // Brings the ruler and the lanes to the roll's length, and the playback position back onto it.
    private void RefreshRollExtent()
    {
        RefreshRulerAndLanes();
        ClampPositionToRoll();
    }

    // Brings the playback position back onto the roll where an edit shortened the roll past it.
    private void ClampPositionToRoll()
    {
        if (_positionColumns > _song.RollColumnCount)
        {
            ShowPosition(_song.RollColumnCount);
        }
    }

    // Puts the playback position at a column and draws it over the roll, the ruler and the lanes and on the transport bar, and hands it to the
    // instrument panel, whose envelope graph is drawn at the tempo there.
    private void ShowPosition(double columns)
    {
        _positionColumns = columns;
        Markers.PlayheadStep = columns;
        ShowPlaybackTime();
        Instruments.ShowPlaybackPosition(PositionTicks);
    }

    // Puts the playback position where the mixer's run has got to.
    private void ShowMixerPosition(AudioEngine audio)
    {
        ShowPosition(TickAtMixerPosition(audio) / Song.GridTicks);
    }

    // The tick the mixer's run has got to, under the tempo map as it stands.
    private double TickAtMixerPosition(AudioEngine audio)
    {
        return TempoMap.For(_song).TickAt(audio.Mixer.PlayPositionSeconds);
    }

    // Writes the transport bar's timer - where playback is and how long the song runs - and puts its position slider where the line is.
    private void ShowPlaybackTime()
    {
        var tempos = TempoMap.For(_song);
        Transport.ShowPosition(PositionTicks, _song.RollLengthTicks, tempos.SecondsAt(PositionTicks), tempos.SecondsAt(_song.LastNoteEndTick));
    }

    // Hands the transport bar's playback sliders to the mixer, and its position slider to the playback position.
    private void WireTransport()
    {
        Transport.PlaybackChanged += ApplyPlayback;
        Transport.PositionMoved += ticks => SetPosition((int)Math.Round(ticks));
    }

    // Lines the transport bar's sliders up with the piano keys and its timer with the panels' right edge.
    private void AlignTransport()
    {
        Transport.AlignEnds(Keys, PanelStack.IsVisible ? PanelStack : null);
    }

    // Hands the three playback values to the mixer.
    private void ApplyPlayback()
    {
        if (_audio is null)
        {
            return;
        }

        _audio.Mixer.SetPlaybackVolume(Transport.VolumeGain);
        _audio.Mixer.SetPlaybackSpeed(Transport.SpeedMultiplier);
        _audio.Mixer.SetPlaybackTranspose(Transport.TransposeSemitones);
    }

    // Moves the playhead with the run, or takes it back to where the run started once the song has reached its end.
    private void OnPlayheadTick(object? sender, EventArgs e)
    {
        if (_audio is null)
        {
            return;
        }

        if (_audio.Mixer.IsPlaying)
        {
            ShowMixerPosition(_audio);
        }
        else
        {
            ShowPlaybackStopped();
            ShowPosition(_playStart);
        }

        FollowPlayhead();
    }

    // Scrolls the playback line to the view's left edge where the run has taken it out of view, unless that is turned off or the view is moved by hand.
    private void FollowPlayhead()
    {
        if (!FollowPlaybackItem.IsChecked || IsViewMovedByHand())
        {
            return;
        }

        _rollFollower.KeepInView(_positionColumns * RollMetrics.CellWidth);
    }

    // Whether the user is moving the view, besides by its scroll bar: panning the roll, sweeping a selection or dragging one of the ruler's flags.
    private bool IsViewMovedByHand()
    {
        return Roll.IsPanning || Roll.IsSweeping || Ruler.IsDraggingMarker;
    }

    // The tick playback stops at for a run starting at a column.
    private int PlaybackEndTick(double startColumn)
    {
        return _song.PlaybackEndTick(startColumn * Song.GridTicks);
    }

    // Where a run starting at a column goes back to on reaching the end, in samples, or null for a run that stops there.
    private long? PlaybackLoopSample(double startColumn)
    {
        if (!_song.IsLooping || !_song.IsEndEnabled || startColumn * Song.GridTicks >= _song.EndTick)
        {
            return null;
        }

        return SamplesAtTick(_song.LoopTick);
    }

    // Starts or pauses the song.
    private void OnPlayPauseClick(object? sender, RoutedEventArgs e)
    {
        TogglePlayback();
    }

    // Stops the song and every audition.
    private void OnStopClick(object? sender, RoutedEventArgs e)
    {
        StopPlayback();
        StopAllAuditions();
    }
}
