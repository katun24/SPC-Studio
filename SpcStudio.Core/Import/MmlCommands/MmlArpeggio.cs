using System.Collections.Immutable;

namespace SpcStudio.Import;

// An $FB in any of its forms - a note list, the trill, the glissando or the off switch - as its first byte, its step length and its raw delta bytes.
public sealed record MmlArpeggio(int Tick, int Form, int StepTicks, ImmutableArray<int> Deltas, int Line) : MmlEvent(Tick, Line)
{
    public const int OffForm = 0x00;
    public const int FirstSpecialForm = 0x80;
    public const int NoteListArpType = 0;
    private const int SpecialFormTypeMask = 0x7F;

    // Whether the command leaves the arpeggio running rather than switching it off.
    public bool IsRunning
    {
        get
        {
            return Form != OffForm;
        }
    }

    // The driver's arpeggio type for this form, which decides how a step moves the pitch.
    public int ArpType
    {
        get
        {
            if (Form < FirstSpecialForm)
            {
                return NoteListArpType;
            }

            return (Form & SpecialFormTypeMask) + 1;
        }
    }
}
