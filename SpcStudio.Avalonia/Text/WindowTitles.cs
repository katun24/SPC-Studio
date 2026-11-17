using System.Globalization;

namespace SpcStudio;

// The title of every window the app opens - its own windows, the panel windows, the message and progress boxes and the file and folder pickers.
public static class WindowTitles
{
    public const string App = "SPC Studio";
    public const string SpcPlayer = "SPC Player";
    public const string SongList = "Song List";
    public const string SampleEditor = "Sample Editor";
    public const string InstrumentPicker = "Instrument Picker";
    public const string Preferences = "Preferences";
    public const string MmlExportOptions = "MML Export Options";
    public const string SamplePath = "Sample Path";
    public const string SelectMultipleNotes = "Select Multiple Notes";
    public const string LoopOptions = "Loop Options";
    public const string CreateChordSample = "Create Chord Sample";
    public const string About = "About " + App;
    public const string CheckForUpdates = "Check For Updates";
    public const string Manual = "Manual";
    public const string SongPanel = "Song";
    public const string ChannelPanel = "Channel";
    public const string InstrumentPanel = "Instrument";
    public const string NotePanel = "Note";
    public const string PanelGroup = "Panels";
    public const string SpcPlayerPanels = "SPC Player Panels";
    public const string OpenSongPicker = "Open Song";
    public const string SaveSongAsPicker = "Save Song As";
    public const string ImportMml = "Import MML";
    public const string ImportMidi = "Import MIDI";
    public const string ImportFromSmwc = "Import From SMWCentral";
    public const string AddFromSmwc = "Add From SMWCentral";
    public const string AddSongFromSmwc = "Add Song From SMWCentral";
    public const string AddFromSpcPicker = "Add From SPC";
    public const string AddingSongs = "Adding Songs";
    public const string AddingSongsFromSpc = "Adding Songs From SPC";
    public const string ExtractRom = "Extract Songs From ROM";
    public const string SelectExtractionFolderPicker = "Select Extraction Folder";
    public const string ExtractingSongsFromRom = "Extracting Songs From ROM";
    public const string ExportToMmlPicker = "Export To MML";
    public const string AddSongToSongListPicker = "Add Song To Song List";
    public const string ExtractSong = "Extract Song";
    public const string ExportToSpc = "Export To SPC";
    public const string ExportToWav = "Export To WAV";
    public const string ImportSamples = "Import Samples";
    public const string ImportSample = "Import Sample";
    public const string ExportSample = "Export Sample";
    public const string ExportSampleToWavPicker = "Export Sample To WAV";
    public const string ImportInstrumentsPicker = "Import Instruments";
    public const string ExportInstrumentPicker = "Export Instrument";
    public const string SelectExportFolderPicker = "Select Export Folder";
    public const string OpenSpc = "Open SPC";
    public const string TranscribeFromSpcPicker = "Transcribe From SPC";
    public const string TranscribingFromSpc = "Transcribing From SPC";
    public const string SelectAmkFolderPicker = "Select AddmusicK Folder";
    public const string SelectRomPicker = "Select ROM";
    public const string DownloadingUpdate = "Downloading Update";
    public const string InstrumentsWord = "Instruments";
    public const string SamplesWord = "Samples";
    public const string MmlSourceWord = "MML";
    public const string SmwcSongSourceWord = "SMWCentral Song";
    public const string SpcSourceWord = "SPC";
    public const string ExtractWord = "Extract";
    public const string AddWord = "Add";
    public const string ExportWord = "Export";
    public const string BoxOpeningMark = "...";
    public const string InsertSongsIntoRomText = "Insert Songs Into ROM";
    private const string ToCurrentSongWords = " To Current Song";
    private const string SongsExtractedFromRomText = "Songs Extracted From ROM";
    private const string DocumentSeparator = " - ";
    private const string ModifiedMarker = "*";

    // A window's title with the name of what it has open after it.
    public static string WithDocument(string windowTitle, string documentName)
    {
        return windowTitle + DocumentSeparator + documentName;
    }

    // The title of an extraction of instruments or samples from a source, which its menu item, its picker or song number box, its progress box and the
    // box choosing what to extract all wear.
    public static string ExtractFrom(string extractedWord, string sourceWord)
    {
        return $"{ExtractWord} {extractedWord}{FromSourceWords(sourceWord)}";
    }

    // The title of an addition of instruments or samples from a source to the open song, which its menu item, its picker or song number box, its
    // progress box and the box choosing what to add all wear.
    public static string AddToCurrentSongFrom(string addedWord, string sourceWord)
    {
        return $"{AddWord} {addedWord}{FromSourceWords(sourceWord)}{ToCurrentSongWords}";
    }

    // The title of the SPC Player's extraction of the open SPC's instruments or samples, which its menu item, its progress box and the box choosing what
    // to extract all wear.
    public static string ExportFromSpcPlayer(string exportedWord)
    {
        return $"{ExportWord} {exportedWord}";
    }

    // The title of the SPC Player's addition of the open SPC's instruments or samples to the open song, which its menu item, its progress box and the box
    // choosing what to add all wear.
    public static string AddToCurrentSongFromSpcPlayer(string addedWord)
    {
        return $"{AddWord} {addedWord}{ToCurrentSongWords}";
    }

    // The words naming the source an extraction or an addition reads from, after what it takes.
    private static string FromSourceWords(string sourceWord)
    {
        return $" From {sourceWord}";
    }

    // A menu item's header for an item opening a box of a title: the title with the mark of an item that opens a box.
    public static string BoxOpeningItemHeader(string title)
    {
        return title + BoxOpeningMark;
    }

    // A title marked as having unsaved changes.
    public static string Modified(string title)
    {
        return ModifiedMarker + title;
    }

    // The title of every message box that wears the app's own icon.
    public static class Plain
    {
        public static readonly PlainTitle InsertSongsIntoRom = new(InsertSongsIntoRomText);
        public static readonly PlainTitle AmkOutput = new("AddmusicK Output");
        public static readonly PlainTitle Welcome = new("Welcome To " + App);
    }

    // The title of every box that gives a notice or asks a question.
    public static class Notices
    {
        public static readonly NoticeTitle SaveChanges = new("Save Changes?");
        public static readonly NoticeTitle ImportedWithDifferences = new("Imported With Differences");
        public static readonly NoticeTitle SongsExtractedFromRom = new(SongsExtractedFromRomText);
        public static readonly NoticeTitle ExportSamples = new("Export Samples");
        public static readonly NoticeTitle OverwriteMml = new("Overwrite Existing MML?");
        public static readonly NoticeTitle OverwriteSamples = new("Overwrite Samples?");
        public static readonly NoticeTitle ChooseMmlFile = new("Choose MML File");
        public static readonly NoticeTitle ExportInstruments = new("Export Instruments");
        public static readonly NoticeTitle DeleteSample = new("Delete Sample");
        public static readonly NoticeTitle DeleteFolder = new("Delete Folder");
        public static readonly NoticeTitle DeleteInstrument = new("Delete Instrument");
        public static readonly NoticeTitle RemoveOriginalSmwInstruments = new("Remove Original SMW Instruments");
        public static readonly NoticeTitle DeleteChannel = new("Delete Channel");
        public static readonly NoticeTitle NoInstrument = new("No Instrument");
        public static readonly NoticeTitle SampleGroupMissing = new("Sample Group Missing");
        public static readonly NoticeTitle NoAmkFolder = new("No AddmusicK Folder");
        public static readonly NoticeTitle RunningAmk = new("Running AddmusicK");
        public static readonly NoticeTitle NoLoopFound = new("No Loop Found");
        public static readonly NoticeTitle UpdateAvailable = new("Update Available");
        public static readonly NoticeTitle NoUpdateAvailable = new("No Update Available");
        public static readonly NoticeTitle AddingInstruments = new("Adding Instruments");
        public static readonly NoticeTitle NoFreeSongSlots = new("No Free Song Slots");

        // The title of the notice that a source holds no instruments or samples of its own to extract or add, naming what and the act.
        public static NoticeTitle NoneToTake(string takenWord, string actWord)
        {
            return new($"No {takenWord} To {actWord}");
        }
    }

    // The title of every box that reports a success.
    public static class Successes
    {
        public static readonly SuccessTitle ExportComplete = new("Export Complete");
        public static readonly SuccessTitle SamplesExported = new("Samples Exported");
        public static readonly SuccessTitle InstrumentsExported = new("Instruments Exported");
        public static readonly SuccessTitle SongsExtractedFromRom = new(SongsExtractedFromRomText);
        public static readonly SuccessTitle SpcTranscribed = new("SPC Transcribed");
        public static readonly SuccessTitle InstrumentAdded = new("Instrument Added");
        public static readonly SuccessTitle SampleAdded = new("Sample Added");
        public static readonly SuccessTitle SongsInsertedIntoRom = new("Songs Inserted Into ROM");
    }

    // The title of every box that reports a failure.
    public static class Failures
    {
        private const string ImportLeadIn = "Failed To Import";
        private const string ExportLeadIn = "Failed To Export";
        private const string FailedToLeadIn = "Failed To";

        public static readonly FailureTitle FailedToStart = new("Failed To Start");
        public static readonly FailureTitle FailedToOpenSong = new("Failed To Open Song");
        public static readonly FailureTitle FailedToSaveSong = new("Failed To Save Song");
        public static readonly FailureTitle FailedToImportMml = new("Failed To Import MML");
        public static readonly FailureTitle FailedToImportMidi = new("Failed To Import MIDI");
        public static readonly FailureTitle FailedToImportFromSmwc = new("Failed To Import From SMWCentral");
        public static readonly FailureTitle FailedToAddFromSmwc = new("Failed To Add From SMWCentral");
        public static readonly FailureTitle FailedToExtractSong = new("Failed To Extract Song");
        public static readonly FailureTitle FailedToExtractMusic = new("Failed To Extract Music");
        public static readonly FailureTitle FailedToExportToMml = new("Failed To Export To MML");
        public static readonly FailureTitle FailedToOpenExportedMml = new("Failed To Open The Exported MML");
        public static readonly FailureTitle FailedToOpenMml = new("Failed To Open MML");
        public static readonly FailureTitle FailedToExportSamples = new("Failed To Export Samples");
        public static readonly FailureTitle FailedToOpenSongList = new("Failed To Open The Song List");
        public static readonly FailureTitle FailedToSaveSongList = new("Failed To Save The Song List");
        public static readonly FailureTitle FailedToPickSong = new("Failed To Pick Song");
        public static readonly FailureTitle FailedToRunAmk = new("Failed To Run AddmusicK");
        public static readonly FailureTitle InvalidAmkFolder = new("Invalid AddmusicK Folder");
        public static readonly FailureTitle InvalidRom = new("Invalid ROM");
        public static readonly FailureTitle FailedToExportToSpc = new("Failed To Export To SPC");
        public static readonly FailureTitle FailedToExportToWav = new("Failed To Export To WAV");
        public static readonly FailureTitle FailedToImportSamples = new("Failed To Import Samples");
        public static readonly FailureTitle FailedToExportSample = new("Failed To Export Sample");
        public static readonly FailureTitle FailedToExportSampleToWav = new("Failed To Export Sample To WAV");
        public static readonly FailureTitle FailedToExtractSample = new("Failed To Extract Sample");
        public static readonly FailureTitle FailedToExportInstrument = new("Failed To Export Instrument");
        public static readonly FailureTitle FailedToRenameSample = new("Failed To Rename Sample");
        public static readonly FailureTitle FailedToRenameInstrument = new("Failed To Rename Instrument");
        public static readonly FailureTitle FailedToRenameFolder = new("Failed To Rename Folder");
        public static readonly FailureTitle FailedToMoveInstrument = new("Failed To Move Instrument");
        public static readonly FailureTitle FailedToMoveFolder = new("Failed To Move Folder");
        public static readonly FailureTitle FailedToDelete = new("Failed To Delete");
        public static readonly FailureTitle FailedToAddFolder = new("Failed To Add Folder");
        public static readonly FailureTitle FailedToDuplicateInstrument = new("Failed To Duplicate Instrument");
        public static readonly FailureTitle FailedToDuplicateFolder = new("Failed To Duplicate Folder");
        public static readonly FailureTitle FailedToUndo = new("Failed To Undo");
        public static readonly FailureTitle FailedToRedo = new("Failed To Redo");
        public static readonly FailureTitle FailedToUndoInstrumentDuplication = new("Failed To Undo Instrument Duplication");
        public static readonly FailureTitle FailedToUndoFolderDuplication = new("Failed To Undo Folder Duplication");
        public static readonly FailureTitle FailedToRedoInstrumentDuplication = new("Failed To Redo Instrument Duplication");
        public static readonly FailureTitle FailedToRedoFolderDuplication = new("Failed To Redo Folder Duplication");
        public static readonly FailureTitle FailedToOpenSpc = new("Failed To Open SPC");
        public static readonly FailureTitle FailedToPlaySpc = new("Failed To Play SPC");
        public static readonly FailureTitle FailedToTranscribeSpc = new("Failed To Transcribe SPC");
        public static readonly FailureTitle FailedToCheckForUpdates = FailedTo(CheckForUpdates);
        public static readonly FailureTitle FailedToOpenManual = FailedTo($"Open {Manual}");
        public static readonly FailureTitle FailedToUpdate = new("Failed To Update");

        // The title over the failure of an action, named by the action's own title.
        public static FailureTitle FailedTo(string actionTitle)
        {
            return new($"{FailedToLeadIn} {actionTitle}");
        }

        // The title over the refusals of importing some files, naming them by their noun.
        public static FailureTitle FailedToImport(int count, string noun)
        {
            return TitledAfterNoun(ImportLeadIn, count, noun);
        }

        // The title over the failures of exporting some files, naming them by their noun.
        public static FailureTitle FailedToExport(int count, string noun)
        {
            return TitledAfterNoun(ExportLeadIn, count, noun);
        }

        // A title of a lead-in followed by a noun, the noun capitalized as every word of a title is.
        private static FailureTitle TitledAfterNoun(string leadIn, int count, string noun)
        {
            return new($"{leadIn} {CultureInfo.InvariantCulture.TextInfo.ToTitleCase(CountedNoun.NounFor(count, noun))}");
        }
    }
}
