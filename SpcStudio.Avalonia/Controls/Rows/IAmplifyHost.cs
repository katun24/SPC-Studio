using SpcStudio.Model;

namespace SpcStudio.Controls;

// What a list of amplify rows is editing on one subject - a note's rows or an instrument's: the amplify a right-click puts the first row back at.
public interface IAmplifyHost : IListHost<AmplifyRow>
{
    // The amplify the first row goes back to, which is the one the row above the section goes back to.
    int BaseAmplify { get; }
}
