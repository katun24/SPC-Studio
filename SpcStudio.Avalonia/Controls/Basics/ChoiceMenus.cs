using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The menu a button opens to add one of the things not added yet: the choices in groups parted by a line, each running its own action on the item picked.
public static class ChoiceMenus
{
    // An empty menu that opens above its button, the drop-down gap between the button and the menu's frame.
    public static ButtonMenu NewAbove()
    {
        return new ButtonMenu(PopupAnchor.Top) { VerticalOffset = DropDownOffset.Above };
    }

    // Fills a menu afresh with each group's choices in order, a line between every two groups that hold any.
    public static void Fill(MenuFlyout menu, IEnumerable<IEnumerable<(string Name, Action<MenuItem> Pick)>> groups)
    {
        menu.Items.Clear();
        foreach (var entry in DividedGroups.Join(groups.Select(group => group.Select(ItemOf)), () => new Separator()))
        {
            menu.Items.Add(entry);
        }
    }

    // A menu item for a choice, running its action on the item when it is picked.
    private static Control ItemOf((string Name, Action<MenuItem> Pick) choice)
    {
        var item = new MenuItem { Header = choice.Name };
        item.Click += (_, _) => choice.Pick(item);
        return item;
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
