using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SpcStudio.Controls;
using SpcStudio.Theme;

namespace SpcStudio;

// The application: it loads the theme, hangs the input rules every window shares and opens the main window.
public partial class App : Application
{
    // Loads the markup and hangs the app-wide rules for the Alt key, the wheel and its drag, the reset click, undo steps, empty drop-downs, presses and tooltips.
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ReadoutMargin.AddToTheme(this);
        SuppressAltMenu();
        FieldSteps.WireWheelToSlidersAndDropDowns();
        FieldResets.WireRightPressToResets();
        ScrollPans.WireWheelDragToScrollViewers();
        EditStep.StartGestureOnEveryPress();
        DropDowns.HideWhileEmpty();
        Presses.MakeEveryControlActOnPress(this);
        StillPointerTooltips.ShowOnlyAfterStillPointer();
    }

    // Makes the app's own folders, moves an older version's settings into the app's folder, deletes the files a past update replaced and opens the main window.
    public override void OnFrameworkInitializationCompleted()
    {
        AppPaths.CreateMissingAppFolders();
        AppSettings.MoveFromProfileAfterUpdate();
        UpdateInstall.DeleteReplacedFiles();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Takes the bare Alt key away from the menu bar in every window, ahead of the access key handler.
    private static void SuppressAltMenu()
    {
        InputElement.KeyDownEvent.AddClassHandler<Window>(SwallowBareAlt, RoutingStrategies.Tunnel);
        InputElement.KeyUpEvent.AddClassHandler<Window>(SwallowBareAlt, RoutingStrategies.Tunnel);
    }

    // Marks an Alt key event handled, so nothing after it opens the menu.
    private static void SwallowBareAlt(Window window, KeyEventArgs e)
    {
        if (e.Key is Key.LeftAlt or Key.RightAlt)
        {
            e.Handled = true;
        }
    }
}
