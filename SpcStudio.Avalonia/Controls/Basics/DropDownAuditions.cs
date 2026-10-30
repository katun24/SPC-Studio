using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.VisualTree;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// A play button at the right of every row of a drop-down's list, shown while the pointer is over the row, which sounds the row's item for as long as it is
// held without picking the row or closing the list.
public static class DropDownAuditions
{
    private static readonly FuncDataTemplate<object?> FallbackItemTextTemplate = new((item, _) => new TextBlock { Text = item?.ToString() });

    // Puts a play button with the tooltip given on every row of a drop-down, running one action with the row's index on its press and another on its
    // release; the face goes on showing the selected item as it did.
    public static void Hook(ComboBox dropDown, string playTooltip, Action<int> onPress, Action onRelease)
    {
        var itemTemplate = dropDown.ItemTemplate ?? FallbackItemTextTemplate;
        dropDown.SelectionBoxItemTemplate = itemTemplate;
        dropDown.ItemTemplate = new FuncDataTemplate<object?>((item, _) => BuildRow(dropDown, item, itemTemplate, playTooltip, onPress, onRelease));
    }

    // A row's content: its item as the drop-down shows it, and the play button at its right.
    private static DockPanel BuildRow(ComboBox dropDown, object? item, IDataTemplate itemTemplate, string playTooltip, Action<int> onPress, Action onRelease)
    {
        var playButton = RowIconButton.Build(AppIcons.Play);
        ToolTip.SetTip(playButton, playTooltip);
        HeldPress.HookPressAndRelease(playButton, () => PressRow(dropDown, playButton, onPress), onRelease);
        playButton.AttachedToVisualTree += (_, _) => ReachOverRowPadding(playButton);
        DockPanel.SetDock(playButton, Dock.Right);

        var itemView = new ContentControl { Content = item, ContentTemplate = itemTemplate, VerticalAlignment = VerticalAlignment.Center };
        return new DockPanel { Children = { playButton, itemView } };
    }

    // Runs a press's action with the index of the row its play button stands on.
    private static void PressRow(ComboBox dropDown, Button playButton, Action<int> onPress)
    {
        if (playButton.FindAncestorOfType<ComboBoxItem>() is not { } row)
        {
            return;
        }

        int index = dropDown.IndexFromContainer(row);
        if (index < 0)
        {
            return;
        }

        onPress(index);
    }

    // Lets a play button run out over its row's padding at the top, the bottom and the right, so it makes the row no taller and stands at its right edge.
    private static void ReachOverRowPadding(Button playButton)
    {
        if (playButton.FindAncestorOfType<ComboBoxItem>() is not { } row)
        {
            return;
        }

        var padding = row.Padding;
        playButton.Margin = new Thickness(0, -padding.Top, -padding.Right, -padding.Bottom);
    }
}
