using SpcStudio.Model;

namespace SpcStudio.Controls;

// What a list of envelope rows is editing on one subject - a note's rows or an instrument's: the envelope a right-click puts a row's fields back at.
public interface IEnvelopeHost : IListHost<EnvelopeRow>
{
    // The envelope a row taking over a number of ticks after key-on goes back to.
    EnvelopeState BaseEnvelopeAt(int offsetTicks);
}
