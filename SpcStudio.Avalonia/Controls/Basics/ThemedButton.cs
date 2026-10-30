using Avalonia.Controls;

namespace SpcStudio.Controls;

// A button of the app's own that the theme styles as an ordinary button, so every button style reaches it.
public abstract class ThemedButton : Button
{
    // Themed as an ordinary button.
    protected override Type StyleKeyOverride
    {
        get
        {
            return typeof(Button);
        }
    }
}
