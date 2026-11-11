using Avalonia.Controls;
using Avalonia.Interactivity;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The row at the foot of a box that asks for something: a button that confirms, which is the box's default, and Cancel beside it, what each press does being the box's.
public sealed class ConfirmButtonRow : StackPanel
{
    private const string DefaultConfirmText = "OK";
    private const string CancelText = "Cancel";

    private readonly Button _confirmButton = new() { Content = DefaultConfirmText, IsDefault = true, Classes = { ThemeClasses.DialogButton, ThemeClasses.Emphasized } };

    // Builds the two buttons.
    public ConfirmButtonRow()
    {
        Classes.Add(ThemeClasses.DialogButtonRow);
        var cancelButton = new Button { Content = CancelText, Classes = { ThemeClasses.DialogButton } };
        _confirmButton.Click += (_, e) => ConfirmClick?.Invoke(this, e);
        cancelButton.Click += (_, e) => CancelClick?.Invoke(this, e);
        Children.Add(_confirmButton);
        Children.Add(cancelButton);
    }

    // Raised by a press on the button that confirms.
    public event EventHandler<RoutedEventArgs>? ConfirmClick;

    // Raised by a press on Cancel.
    public event EventHandler<RoutedEventArgs>? CancelClick;

    // What the button that confirms says.
    public string? ConfirmText
    {
        get
        {
            return _confirmButton.Content as string;
        }
        set
        {
            _confirmButton.Content = value;
        }
    }

    // Whether the button that confirms can be pressed.
    public bool IsConfirmEnabled
    {
        get
        {
            return _confirmButton.IsEnabled;
        }
        set
        {
            _confirmButton.IsEnabled = value;
        }
    }

    // Themed as an ordinary stack panel, so the theme's button row rule lays it out.
    protected override Type StyleKeyOverride
    {
        get
        {
            return typeof(StackPanel);
        }
    }
}
