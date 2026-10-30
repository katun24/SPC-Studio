using Avalonia.Controls;
using Avalonia.Layout;
using SpcStudio.Model;

namespace SpcStudio.Controls;

// The surround pair as one drop-down: the four states the pair can be in, named and ordered once for every control that shows them.
public static class SurroundField
{
    private static readonly IReadOnlyList<SnesSurround> StatesInDropDownOrder =
    [
        SnesSurround.None,
        new SnesSurround(true, false),
        new SnesSurround(false, true),
        new SnesSurround(true, true),
    ];

    private static readonly IReadOnlyList<string> StateNamesInDropDownOrder = ["None", "Inv L", "Inv R", "Inv L+R"];

    // Which item stands for a pair.
    public static int IndexOf(SnesSurround pair)
    {
        for (int index = 0; index < StatesInDropDownOrder.Count; index++)
        {
            if (StatesInDropDownOrder[index] == pair)
            {
                return index;
            }
        }

        return 0;
    }

    // The pair an item stands for, or no surround off either end.
    public static SnesSurround PairAt(int index)
    {
        return index >= 0 && index < StatesInDropDownOrder.Count ? StatesInDropDownOrder[index] : SnesSurround.None;
    }

    // A drop-down over the four states with nothing wired to it.
    public static ComboBox NewDropDown()
    {
        var dropDown = new ComboBox { VerticalAlignment = VerticalAlignment.Center };
        FillDropDown(dropDown);
        return dropDown;
    }

    // Puts the four items into a drop-down, one built here or declared in markup.
    public static void FillDropDown(ComboBox dropDown)
    {
        dropDown.ItemsSource = StateNamesInDropDownOrder;
    }
}
