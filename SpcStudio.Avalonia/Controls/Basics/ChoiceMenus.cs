using Avalonia.Controls;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The menu a button opens to add one of the things not added yet: the choices in groups parted by a line, each running its own action on the item picked.
public static class ChoiceMenus
{
    // An empty menu that opens above its button, the drop-down gap between the button and the menu's frame.
    public static ButtonMenu NewAbove()
    {
        return new ButtonMenu { Placement = PlacementMode.TopEdgeAlignedLeft, VerticalOffset = DropDownOffset.Above };
    }

    // Fills a menu afresh with each group's choices in order, a line between every two groups that hold any.
    public static void Fill(MenuFlyout menu, IEnumerable<IEnumerable<(string Name, Action<MenuItem> Pick)>> groups)
    {
        menu.Items.Clear();
        foreach (var group in groups)
        {
            var choices = group.ToArray();
            if (choices.Length == 0)
            {
                continue;
            }

            if (menu.Items.Count > 0)
            {
                menu.Items.Add(new Separator());
            }

            foreach (var (name, pick) in choices)
            {
                var item = new MenuItem { Header = name };
                item.Click += (_, _) => pick(item);
                menu.Items.Add(item);
            }
        }
    }

    // Keeps a menu open when one of its choices is picked.
    public static void KeepOpenOnPick(MenuFlyout menu)
    {
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            item.StaysOpenOnClick = true;
        }
    }
}
