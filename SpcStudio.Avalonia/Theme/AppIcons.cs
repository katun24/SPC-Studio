using Avalonia;
using Avalonia.Media;

namespace SpcStudio.Theme;

// Every icon the app draws, each a Material Design Icons path fitted to one icon size and centered in it.
public static class AppIcons
{
    public const double Size = 14;
    public const string MdiRepeatPath = "M17,17H7V14L3,18L7,22V19H19V13H17M7,7H17V10L21,6L17,2V5H5V11H7V7Z";
    private const double StopInkSize = 11;

    // MDI plus: creating something.
    public static readonly Geometry Plus = FittedAndCentered("M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z");

    // MDI playlist-plus: adding a row to a stack of rows.
    public static readonly Geometry PlaylistPlus = FittedAndCentered("M3,16H10V14H3M18,14V10H16V14H12V16H16V20H18V16H22V14M14,6H3V8H14V6M14,10H3V12H14V10Z");

    // MDI tray-arrow-down: reading a file into the song.
    public static readonly Geometry TrayDown = FittedAndCentered("M2 12H4V17H20V12H22V17C22 18.11 21.11 19 20 19H4C2.9 19 2 18.11 2 17V12M12 15L17.55 9.54L16.13 8.13L13 11.25V2H11V11.25L7.88 8.13" +
        "L6.46 9.55L12 15Z");

    // MDI tray-arrow-up: writing one out.
    public static readonly Geometry TrayUp = FittedAndCentered("M2 12H4V17H20V12H22V17C22 18.11 21.11 19 20 19H4C2.9 19 2 18.11 2 17V12M12 2L6.46 7.46L7.88 8.88L11 5.75V15H13V5.75L16.13 8.88" +
        "L17.55 7.45L12 2Z");

    // MDI file-plus-outline: starting a new song.
    public static readonly Geometry FilePlus = FittedAndCentered("M13.81 22H6C4.89 22 4 21.11 4 20V4C4 2.9 4.89 2 6 2H14L20 8V13.09C19.67 13.04 19.34 13 19 13S18.33 13.04 18 13.09V9H13V4H6V20H13.09" +
        "C13.21 20.72 13.46 21.39 13.81 22M23 18H20V15H18V18H15V20H18V23H20V20H23V18Z");

    // MDI content-save-outline: saving the song.
    public static readonly Geometry ContentSave = FittedAndCentered("M17 3H5C3.89 3 3 3.9 3 5V19C3 20.1 3.89 21 5 21H19C20.1 21 21 20.1 21 19V7L17 3M19 19H5V5H16.17L19 7.83V19M12 12C10.34 12 9 13.34 9 15" +
        "S10.34 18 12 18 15 16.66 15 15 13.66 12 12 12M6 6H15V10H6V6Z");

    // MDI close: a bend node held on its row.
    public static readonly Geometry Close = FittedAndCentered("M19,6.41L17.59,5L12,10.59L6.41,5L5,6.41L10.59,12L5,17.59L6.41,19L12,13.41L17.59,19L19,17.59L13.41,12L19,6.41Z");

    // MDI minus: a value the note leaves to the lane.
    public static readonly Geometry Minus = FittedAndCentered("M19,13H5V11H19V13Z");

    // MDI piano: a value the note takes from its instrument.
    public static readonly Geometry Piano = FittedAndCentered("M20 2H4C2.9 2 2 2.9 2 4V20C2 21.11 2.9 22 4 22H20C21.11 22 22 21.11 22 20V4C22 2.9 21.11 2 20 2M14.74 14H15V20H9V14H9.31" +
        "C9.86 14 10.3 13.56 10.3 13V4H13.75V13C13.75 13.56 14.19 14 14.74 14M4 4H6.8V13C6.8 13.56 7.24 14 7.79 14H8V20H4V4M20 20H16V14H16.26C16.81 14 17.25 13.56 17.25 13V4H20V20Z");

    // MDI music-note: a value the note carries itself.
    public static readonly Geometry MusicNote = FittedAndCentered("M12 3V13.55C11.41 13.21 10.73 13 10 13C7.79 13 6 14.79 6 17S7.79 21 10 21 14 19.21 14 17V7H18V3H12Z");

    // MDI folder-upload: writing every sample in the song out at once.
    public static readonly Geometry FolderUpload = FittedAndCentered("M20,18H4V8H20M20,6H12L10,4H4C2.89,4 2,4.89 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V8C22,6.89 21.1,6 20,6M16,17H14V13H11L15,9" +
        "L19,13H16V17Z");

    // MDI folder-open: choosing a file.
    public static readonly Geometry FolderOpen = FittedAndCentered("M19,20H4C2.89,20 2,19.1 2,18V6C2,4.89 2.89,4 4,4H10L12,6H19A2,2 0 0,1 21,8H21L4,8V18L6.14,10H23.21L20.93,18.5" +
        "C20.7,19.37 19.92,20 19,20Z");

    // MDI content-copy: making a second thing just like the one showing.
    public static readonly Geometry ContentCopy = FittedAndCentered("M19,21H8V7H19M19,5H8A2,2 0 0,0 6,7V21A2,2 0 0,0 8,23H19A2,2 0 0,0 21,21V7A2,2 0 0,0 19,5M16,1H4A2,2 0 0,0 2,3V17H4V3H16V1Z");

    // MDI delete-outline: deleting something.
    public static readonly Geometry Bin = FittedAndCentered("M6,19A2,2 0 0,0 8,21H16A2,2 0 0,0 18,19V7H6V19M8,9H16V19H8V9M15.5,4L14.5,3H9.5L8.5,4H5V6H19V4H15.5Z");

    // MDI play: sounding something without playing the song.
    public static readonly Geometry Play = FittedAndCentered("M8,5.14V19.14L19,12.14L8,5.14Z");

    // MDI select-all: selecting every note of something.
    public static readonly Geometry SelectAll = FittedAndCentered("M9,9H15V15H9M7,17H17V7H7M15,5H17V3H15M15,21H17V19H15M19,17H21V15H19M19,9H21V7H19M19,21A2,2 0 0,0 21,19H19M19,13H21V11H19M11,21H13V19H11" +
        "M9,3H7V5H9M3,17H5V15H3M5,21V19H3A2,2 0 0,0 5,21M19,3V5H21A2,2 0 0,0 19,3M13,3H11V5H13M3,9H5V7H3M7,21H9V19H7M3,13H5V11H3M3,5H5V3A2,2 0 0,0 3,5Z");

    // MDI check: something being so.
    public static readonly Geometry Check = FittedAndCentered("M21,7L9,19L3.5,13.5L4.91,12.09L9,16.17L19.59,5.59L21,7Z");

    // MDI stop, its ink drawn smaller than the other icons': stopping a note that sounds on its own.
    public static readonly Geometry Stop = FittedAndCentered("M18,18H6V6H18V18Z", StopInkSize);

    // MDI repeat: playing the selected note over and over.
    public static readonly Geometry Repeat = FittedAndCentered(MdiRepeatPath);

    // MDI waveform: opening the instrument's sample in the sample editor.
    public static readonly Geometry Waveform = FittedAndCentered("M22 12L20 13L19 14L18 13L17 16L16 13L15 21L14 13L13 15L12 13L11 17L10 13L9 22L8 13L7 19L6 13L5 14L4 13L2 12L4 11L5 10L6 11L7 5L8 11" +
        "L9 2L10 11L11 7L12 11L13 9L14 11L15 3L16 11L17 8L18 11L19 10L20 11L22 12Z");

    // MDI pencil-outline: renaming the instrument.
    public static readonly Geometry Pencil = FittedAndCentered("M14.06,9L15,9.94L5.92,19H5V18.08L14.06,9M17.66,3C17.41,3 17.15,3.1 16.96,3.29L15.13,5.12L18.88,8.87L20.71,7.04" +
        "C21.1,6.65 21.1,6 20.71,5.63L18.37,3.29C18.17,3.09 17.92,3 17.66,3M14.06,6.19L3,17.25V21H6.75L17.81,9.94L14.06,6.19Z");

    // MDI restore: putting what a mixed section's selection holds back where it came from.
    public static readonly Geometry Restore = FittedAndCentered("M13,3A9,9 0 0,0 4,12H1L4.89,15.89L4.96,16.03L9,12H6A7,7 0 0,1 13,5A7,7 0 0,1 20,12A7,7 0 0,1 13,19C11.07,19 9.32,18.21 8.06,16.94" +
        "L6.64,18.36C8.27,20 10.5,21 13,21A9,9 0 0,0 22,12A9,9 0 0,0 13,3Z");

    // MDI refresh: building the install again and recounting the song's ARAM.
    public static readonly Geometry Refresh = FittedAndCentered("M17.65,6.35C16.2,4.9 14.21,4 12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20C15.73,20 18.84,17.45 19.73,14H17.65C16.83,16.33 14.61,18 12,18" +
        "A6,6 0 0,1 6,12A6,6 0 0,1 12,6C13.66,6 15.14,6.69 16.22,7.78L13,11H20V4L17.65,6.35Z");

    // MDI open-in-new: taking a panel out into a window of its own.
    public static readonly Geometry OpenInNew = FittedAndCentered("M14,3V5H17.59L7.76,14.83L9.17,16.24L19,6.41V10H21V3M19,19H5V5H12V3H5C3.89,3 3,3.9 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V12H19" +
        "V19Z");

    // MDI arrow-collapse-right: putting a panel taken out back in the main window.
    public static readonly Geometry ArrowCollapseRight = FittedAndCentered("M12.08,4.08L20,12L12.08,19.92L10.67,18.5L16.17,13H2V11H16.17L10.67,5.5L12.08,4.08M20,12V22H22V2H20V12Z");

    // MDI chevron-down: a panel section standing open.
    public static readonly Geometry ChevronDown = FittedAndCentered("M7.41,8.58L12,13.17L16.59,8.58L18,10L12,16L6,10L7.41,8.58Z");

    // MDI chevron-right: a panel section folded shut.
    public static readonly Geometry ChevronRight = FittedAndCentered("M8.59,16.58L13.17,12L8.59,7.41L10,6L16,12L10,18L8.59,16.58Z");

    // MDI trending-up: turning a flat region into a fade.
    public static readonly Geometry TrendingUp = FittedAndCentered("M16,6L18.29,8.29L13.41,13.17L9.41,9.17L2,16.59L3.41,18L9.41,12L13.41,16L19.71,9.71L22,12V6H16Z");

    // MDI trending-neutral: flattening a fading region back to one value.
    public static readonly Geometry TrendingNeutral = FittedAndCentered("M22,12L18,8V11H3V13H18V16L22,12Z");

    // MDI volume-high: the transport bar's playback volume.
    public static readonly Geometry Volume = FittedAndCentered("M14,3.23V5.29C16.89,6.15 19,8.83 19,12C19,15.17 16.89,17.84 14,18.7V20.77C18,19.86 21,16.28 21,12C21,7.72 18,4.14 14,3.23M16.5,12" +
        "C16.5,10.23 15.5,8.71 14,7.97V16C15.5,15.29 16.5,13.76 16.5,12M3,9V15H7L12,20V4L7,9H3Z");

    // MDI metronome: the transport bar's playback speed.
    public static readonly Geometry Metronome = FittedAndCentered("M12,1.75L8.57,2.67L4.06,19.53C4.03,19.68 4,19.84 4,20C4,21.11 4.89,22 6,22H18C19.11,22 20,21.11 20,20" +
        "C20,19.84 19.97,19.68 19.94,19.53L18.58,14.42L17,16L17.2,17H13.41L16.25,14.16L14.84,12.75L10.59,17H6.8L10.29,4H13.71L15.17,9.43L16.8,7.79L15.43,2.67L12,1.75M11.25,5V14.75L12.75,13.25V5" +
        "H11.25M19.79,7.8L16.96,10.63L16.25,9.92L14.84,11.34L17.66,14.16L19.08,12.75L18.37,12.04L21.2,9.21L19.79,7.8Z");

    // MDI music-accidental-sharp: the transport bar's playback transpose.
    public static readonly Geometry Sharp = FittedAndCentered("M17 9.5V7.5L15 8.1V5.5H13V8.7L11 9.3V6.5H9V9.9L7 10.5V12.5L9 11.9V13.9L7 14.5V16.5L9 15.9V18.5H11V15.3L13 14.7V17.5H15V14.1L17 13.5" +
        "V11.5L15 12.1V10.1L17 9.5M13 12.7L11 13.3V11.3L13 10.7V12.7Z");

    // MDI alpha-b: the ruler numbering its bar lines with bars.
    public static readonly Geometry AlphaB = FittedAndCentered("M15,10.5V9A2,2 0 0,0 13,7H9V17H13A2,2 0 0,0 15,15V13.5C15,12.7 14.3,12 13.5,12C14.3,12 15,11.3 15,10.5M13,15H11V13H13V15M13,11H11V9" +
        "H13V11Z");

    // MDI alpha-t: the ruler numbering its bar lines with ticks.
    public static readonly Geometry AlphaT = FittedAndCentered("M9,7V9H11V17H13V9H15V7H9Z");

    // The glyph of the ruler's button: the letter of what the ruler numbers its bar lines with.
    public static Geometry RulerUnitIcon(bool isCountingTicks)
    {
        return isCountingTicks ? AlphaT : AlphaB;
    }

    // Parses an MDI path and fits what its glyph covers into a square of an ink size, its top left corner at the drawing's origin.
    public static Geometry FittedAtOrigin(string path, double inkSize)
    {
        return Fitted(path, inkSize, _ => default);
    }

    // Parses an MDI path and fits what its glyph covers into a square of an ink size, centered on both axes inside a square of the icon size.
    private static Geometry FittedAndCentered(string path, double inkSize = Size)
    {
        return Fitted(path, inkSize, inkExtent => new Point((Size - inkExtent.Width) / 2, (Size - inkExtent.Height) / 2));
    }

    // Parses an MDI path, scales what its glyph covers to fit a square of an ink size, and puts that ink's top left corner where its scaled size says.
    private static Geometry Fitted(string path, double inkSize, Func<Avalonia.Size, Point> inkTopLeftOf)
    {
        var geometry = Geometry.Parse(path);
        var bounds = geometry.Bounds;
        double scale = inkSize / Math.Max(bounds.Width, bounds.Height);
        var inkTopLeft = inkTopLeftOf(bounds.Size * scale);
        double offsetX = inkTopLeft.X - bounds.X * scale;
        double offsetY = inkTopLeft.Y - bounds.Y * scale;
        geometry.Transform = new MatrixTransform(new Matrix(scale, 0, 0, scale, offsetX, offsetY));
        return geometry;
    }
}
