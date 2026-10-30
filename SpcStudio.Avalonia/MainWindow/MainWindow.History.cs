using Avalonia;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SpcStudio.Audio;
using SpcStudio.Controls;
using SpcStudio.Model;

namespace SpcStudio;

// Undo and redo, recording every edit into the history, and handing a running song each edit.
public partial class MainWindow
{
    // Enables the Edit menu's Undo and Redo where there is a step to take and no note is being dragged.
    private void ShowUndoState()
    {
        UndoItem.IsEnabled = _history.CanUndo && !Roll.IsDraggingNotes;
        RedoItem.IsEnabled = _history.CanRedo && !Roll.IsDraggingNotes;
    }

    // Steps the song back one edit, unless a note is being dragged.
    private void OnUndo(object? sender, RoutedEventArgs e)
    {
        if (!_history.CanUndo || Roll.IsDraggingNotes)
        {
            return;
        }

        var from = _history.Current;
        RestoreSnapshot(from, _history.Undo());
    }

    // Steps the song forward one undone edit, unless a note is being dragged.
    private void OnRedo(object? sender, RoutedEventArgs e)
    {
        if (!_history.CanRedo || Roll.IsDraggingNotes)
        {
            return;
        }

        var from = _history.Current;
        RestoreSnapshot(from, _history.Redo());
    }

    // Puts a state from the history back into the song and brings every control to it, keeping the selection and the cell editor where they still apply.
    private void RestoreSnapshot(SongSnapshot from, SongSnapshot to)
    {
        var cellLane = _cellLane;
        int cellTick = _cellTick;
        int cellChannel = Roll.SelectedChannel;
        bool isFocusInCellEditor = CellEditor.IsVisible && FocusManager?.GetFocusedElement() is Visual focused && CellEditor.IsVisualAncestorOf(focused);
        _isKeepingCell = cellLane is not null;
        to.Restore(_song);
        if (from.FirstChannelWithDifferentNotes(to) is int channel)
        {
            Roll.SelectedChannel = channel;
        }

        KeepSelectedChannelInSong();
        if (_channelButtons.Count != _song.ChannelCount)
        {
            KeyOffExtraChannels();
        }

        Roll.PruneSelection();
        ShowSongState();
        _isKeepingCell = false;
        ReopenCell(cellLane, cellTick, cellChannel, isFocusInCellEditor);
        RefreshRulerAndMarkers();
        UpdateTitle();
        ClampPositionToRoll();
        RefreshSequence();
        SilenceMutedVoices();
        ShowUndoState();
    }

    // Opens the cell editor again on a cell it stood on, with the lane's highlight, or closes it where the cell's channel is no longer selected.
    private void ReopenCell(ChangeLane? lane, int tick, int channel, bool doesTakeFocus)
    {
        if (lane is null)
        {
            return;
        }

        if (lane.IsPerChannel && Roll.SelectedChannel != channel)
        {
            CloseCell();
            return;
        }

        _doesCellTakeFocus = doesTakeFocus;
        try
        {
            OnCellSelected(lane, tick);
        }
        finally
        {
            _doesCellTakeFocus = true;
        }

        if (ReferenceEquals(_cellLane, lane))
        {
            lane.SelectSilently(tick);
        }
    }

    // Brings the roll's extent and a running song along with an edit, and records it as an undo step keyed on the control it came from.
    private void MarkModified(object? source = null, long? playPosition = null)
    {
        RefreshRollExtent();
        RefreshSequence(playPosition);
        Instruments.ShowWhetherInstrumentHasNotes();
        if (!_history.Record(_song, EditStep.UndoKeyFor(source)))
        {
            return;
        }

        UpdateTitle();
        ShowUndoState();
    }

    // Hands the note panel's run, the instrument a press is holding and a running song the song as it now stands, moving the run's position where an edit
    // changed the tick rate.
    private void RefreshSequence(long? playPosition = null)
    {
        RefreshNoteRun();
        RefreshInstrumentAudition();
        if (_audio is null || !_audio.Mixer.IsPlaying)
        {
            return;
        }

        var (events, length) = SequenceBuilder.Build(_song, PlaybackEndTick(_playStart));
        _audio.Mixer.ReplaceSequence(events, length, playPosition, PlaybackLoopSample(_playStart));
    }
}
