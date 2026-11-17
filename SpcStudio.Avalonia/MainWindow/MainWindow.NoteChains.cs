using System.Collections.Immutable;
using Avalonia.Interactivity;
using SpcStudio.Controls;
using SpcStudio.Model;

namespace SpcStudio;

// The note panel's Volume and Pan sections, which are the selected notes handed to the shared volume and pan rows; and what every chain section of
// the note panel shares, the selected notes as a chain's subjects.
public partial class MainWindow
{
    private ChainRows<int>? _noteVolumeRowsEditor;
    private ChainRows<PanState>? _notePanRowsEditor;

    // The Volume section's rows, built the first time they are asked for.
    private ChainRows<int> NoteVolumeRowsEditor
    {
        get
        {
            return _noteVolumeRowsEditor ??= FadeRows.Volume(NotePanelValues.VolumeSection,
                () => SelectedNoteSubjects<IFadeHost<int>>(selected => new NoteFadeHost<int>(this, selected, NoteVolume.Lane)),
                LaneClaim(NoteVolume.Lane, NotePanelValues.VolumeRow.Switch));
        }
    }

    // The Pan section's rows, built the first time they are asked for.
    private ChainRows<PanState> NotePanRowsEditor
    {
        get
        {
            return _notePanRowsEditor ??= FadeRows.Pan(NotePanelValues.PanSection,
                () => SelectedNoteSubjects<IFadeHost<PanState>>(selected => new NoteFadeHost<PanState>(this, selected, NotePan.Lane)),
                LaneClaim(NotePan.Lane, NotePanelValues.PanRow.Switch));
        }
    }

    // Splits the last volume row of every selected note with room for it, moving the notes onto their own volume first in the same undo step.
    private void OnAddNoteVolume(object? sender, RoutedEventArgs e)
    {
        ClaimAndAddFadeRow(NoteVolumeRowsEditor, NoteVolume.Lane);
    }

    // Splits the last pan row of every selected note with room for it, moving the notes onto their own pan first in the same undo step.
    private void OnAddNotePan(object? sender, RoutedEventArgs e)
    {
        ClaimAndAddFadeRow(NotePanRowsEditor, NotePan.Lane);
    }

    // Moves the selected notes onto their own rows and splits their last row - or, where they all stood over the lane and showed no row to split, gives each its instrument's rows.
    private void ClaimAndAddFadeRow<TState>(ChainRows<TState> editor, NoteFadeLane<TState> lane) where TState : struct
    {
        if (Roll.Selection.Any(one => lane.LiveSourceOf(_song, one.Channel, one.Note) != LaneSource.None))
        {
            ClaimAndAddRow(editor);
            return;
        }

        editor.AddRow();
    }

    // Moves the selected notes onto their own chain the way a press on an inert row does, and then adds a row to the chains.
    private static void ClaimAndAddRow<T, THost>(EntryRows<T, THost> editor) where T : struct, IEquatable<T> where THost : IRowHost
    {
        editor.InertClaim?.ClaimRows(EditStep.ClaimSource);
        editor.AddRow();
    }

    // The selected notes as the subjects a section's rows edit.
    private RowSubjects<THost> SelectedNoteSubjects<THost>(Func<(int Channel, Note Note), THost> subjectOf) where THost : IRowHost
    {
        return new RowSubjects<THost>([.. Roll.Selection.Select(subjectOf)], CommitToSelection);
    }

    // Runs a write to the selected notes and, where it changed anything, records it as one edit under the key and redraws the roll; then shows the note panel again.
    private void CommitToSelection(Func<bool> write, object? undoKey)
    {
        if (!Roll.IsDraggingNotes && RecordSelectionEdit(undoKey, write))
        {
            Roll.Refresh();
        }

        ShowNotePanel();
    }

    // What one selected note as the subject of any of the note panel's chains shares: the note, the room its chain has, that it is a length of its own, and how a chain is written onto it.
    private abstract class NoteChainHost(MainWindow window, (int Channel, Note Note) selected)
    {
        // The room the note's chain has, which is the note's length.
        public int CapacityTicks
        {
            get
            {
                return selected.Note.LengthTicks;
            }
        }

        // That the note is a length of its own.
        public bool HasOwnLength
        {
            get
            {
                return true;
            }
        }

        // The note's channel.
        protected int Channel
        {
            get
            {
                return selected.Channel;
            }
        }

        // The note as it stood when the subjects were read.
        protected Note Note
        {
            get
            {
                return selected.Note;
            }
        }

        // The song the window is editing.
        protected Song Song
        {
            get
            {
                return window._song;
            }
        }
    }

    // One selected note as the subject of its volume or pan rows, which are whichever of its three rungs is in force, written onto the note.
    private sealed class NoteFadeHost<TState>(MainWindow window, (int Channel, Note Note) selected, NoteFadeLane<TState> lane) : NoteChainHost(window, selected), IFadeHost<TState>
        where TState : struct
    {
        // The rows the note plays, from whichever of its rungs is in force.
        public ImmutableArray<NoteChainRegion<TState>> Chain
        {
            get
            {
                return lane.PlayedChain(Song, Channel, Note);
            }
        }

        // The note's own instrument's rows, cut to its length.
        public ImmutableArray<NoteChainRegion<TState>> InstrumentRows
        {
            get
            {
                return lane.InstrumentChainForNoteLength(Song.InstrumentOf(Note), Note.LengthTicks);
            }
        }

        // The note's own instrument's first row, which is what the row above the section goes back to.
        public TState BaseLeadState
        {
            get
            {
                return lane.LeadState(Song.InstrumentById(Note.InstrumentId));
            }
        }

        // Writes the rows onto the note.
        public bool Write(ImmutableArray<NoteChainRegion<TState>> chain)
        {
            lane.ApplyOverride(Song, Channel, Note, chain);
            return true;
        }
    }
}
