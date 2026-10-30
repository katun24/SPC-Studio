using Avalonia.Controls;

namespace SpcStudio.Controls;

// The header over a list of check boxes: its word at the left and the All box that checks the whole list at the right.
public sealed class CheckListHeader : HeaderRow
{
    private const string AllBoxText = "All";

    // Builds the header's word and its All box.
    public CheckListHeader()
    {
        Children.Add(AllBox);
    }

    // The box that checks or clears the whole list under the header.
    public CheckBox AllBox { get; } = new() { Content = AllBoxText };
}
