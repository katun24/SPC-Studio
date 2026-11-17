using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// A square icon button that cycles through a fixed run of states, its glyph saying which one it is in and the switch lit in every one of them, with
// a mixed state of its own where the things it stands over disagree.
public sealed class IconSwitch : ThemedButton
{
    // The glyphs the switch cycles through, as a styled property.
    public static readonly StyledProperty<IReadOnlyList<Geometry>?> IconsProperty = AvaloniaProperty.Register<IconSwitch, IReadOnlyList<Geometry>?>(nameof(Icons));

    // Which state the switch shows, as a styled property starting on the first state.
    public static readonly StyledProperty<int?> StateProperty = AvaloniaProperty.Register<IconSwitch, int?>(nameof(State), defaultValue: 0);

    // Builds the switch square, unpadded, wearing its own class, never taking the focus, its tooltip saying it switches where a value comes from.
    public IconSwitch()
    {
        Classes.Add(ThemeClasses.SquareIcon);
        Classes.Add(ThemeClasses.IconSwitch);
        Focusable = false;
        ToolTip.SetTip(this, Tooltips.ToggleSource);
    }

    // One glyph per state, in the order a press moves through them.
    public IReadOnlyList<Geometry>? Icons
    {
        get
        {
            return GetValue(IconsProperty);
        }
        set
        {
            SetValue(IconsProperty, value);
        }
    }

    // Which state is showing, or null for the mixed state; writing it raises nothing.
    public int? State
    {
        get
        {
            return GetValue(StateProperty);
        }
        set
        {
            SetValue(StateProperty, value);
        }
    }

    public int MixedGoesTo { get; set; }

    // Raised with the state a press asked for.
    public event Action<int>? Cycled;

    // Asks for the next state in the cycle, or for the mixed state's own target where the switch is mixed.
    protected override void OnClick()
    {
        base.OnClick();
        if (Icons is not { Count: > 0 } icons)
        {
            return;
        }

        Cycled?.Invoke(State is int currentState ? (currentState + 1) % icons.Count : MixedGoesTo);
    }

    // Redraws the switch when its state or its glyphs change.
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != StateProperty && change.Property != IconsProperty)
        {
            return;
        }

        ShowLitOrMixed();
        ShowStateGlyph();
    }

    // Marks the switch lit where a state is showing and mixed where none is.
    private void ShowLitOrMixed()
    {
        Classes.Set(ThemeClasses.Lit, State is not null);
        Classes.Set(ThemeClasses.Mixed, State is null);
    }

    // Shows the glyph of the state in force, the first state's where the switch is mixed, and none where there are no glyphs.
    private void ShowStateGlyph()
    {
        if (Icons is not { Count: > 0 } icons)
        {
            Content = null;
            return;
        }

        var glyph = icons[Math.Clamp(State ?? 0, 0, icons.Count - 1)];
        Content = new IconShape { Data = glyph };
    }
}
