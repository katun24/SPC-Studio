using Avalonia.Controls;
using Avalonia.Media;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The button that sits inside a list row: an icon at the row's own height with no frame, its glyph colored by the theme.
public static class RowIconButton
{
    // A row button showing an icon.
    public static Button Build(Geometry icon)
    {
        return new Button { Classes = { ThemeClasses.RowIcon }, Content = new IconShape { Data = icon } };
    }

    // A row's '+', running an add on its click.
    public static Button BuildAdd(Action add, string tooltip)
    {
        var addButton = Build(AppIcons.Plus);
        addButton.Click += (_, _) => add();
        ToolTip.SetTip(addButton, tooltip);
        return addButton;
    }

    // A row's play button, running an audition for as long as it is held.
    public static Button BuildPlay(Action startAudition, Action releaseAudition, string tooltip)
    {
        var playButton = Build(AppIcons.Play);
        HeldPress.HookPressAndRelease(playButton, startAudition, releaseAudition);
        ToolTip.SetTip(playButton, tooltip);
        return playButton;
    }
}
