using System.Collections.Immutable;

namespace SpcStudio.Import;

// An $FB in any of its forms - a note list, the trill, the glissando or the off switch - as its first byte, its step length and its raw delta bytes.
public sealed record MmlArpeggio(int Tick, int Form, int StepTicks, ImmutableArray<int> Deltas, int Line) : MmlEvent(Tick, Line)
{
    public const int OffForm = 0x00;
    public const int FirstSpecialForm = 0x80;
    public const int TrillForm = FirstSpecialForm;
    public const int NoteListArpType = 0;
    public const int LoopPointDelta = 0x80;
    public const int FirstDeltaArgument = 2;
    private const int SpecialFormTypeMask = 0x7F;
    private const int SpecialFormDeltaCount = 1;

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

    // How many delta bytes follow the step length under a form: as many as a note list counts, and one for a special form.
    public static int DeltaCountOf(int form)
    {
        return form < FirstSpecialForm ? form : SpecialFormDeltaCount;
    }

    // An $FB read from its form and step length and as many delta bytes as the form says, each taken from the reader by its index.
    public static MmlArpeggio Read(int tick, int form, int stepTicks, Func<int, int> readDelta, int line)
    {
        return new MmlArpeggio(tick, form, stepTicks, [.. Enumerable.Range(0, DeltaCountOf(form)).Select(readDelta)], line);
    }
}
