namespace SpcStudio.Model;

// How the export writes a held note: a tick short with a one-tick rest under the key-off remote block, or whole under the legato with a key-on remote block toggling it twice.
public enum HeldNoteShape
{
    OneTickRest,
    Legato,
}
