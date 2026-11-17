using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SpcStudio.Controls;

// A list section's parts: its frame, the list its rows are built into and the '+' under them.
public sealed record ListSectionParts(PanelSection Section, StackPanel Rows, AddRowButton AddButton)
{
    // Runs a handler when either the banner's '+' or the one under the rows is pressed.
    public void OnAddPressed(EventHandler<RoutedEventArgs> handler)
    {
        Section.AddRequested += handler;
        AddButton.Click += handler;
    }
}
