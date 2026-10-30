using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The fixed-width slot between a panel row's label and its rail, holding the switch that says where the row's value comes from, a plain button the markup puts there instead, or nothing.
public sealed class FieldOverride : Decorator
{
    private readonly IconSwitch _iconSwitch;

    // Whether the slot stands after the row's field rather than before its rail, as a property the theme can set.
    public static readonly StyledProperty<bool> TrailsProperty = AvaloniaProperty.Register<FieldOverride, bool>(nameof(Trails));

    // Builds the slot holding a hidden switch, sized to the theme's square button and spaced by its gap between two controls.
    public FieldOverride()
    {
        _iconSwitch = new IconSwitch { IsVisible = false, VerticalAlignment = VerticalAlignment.Center };
        _iconSwitch.Cycled += state => Cycled?.Invoke(state);
        Child = _iconSwitch;
        Width = ThemeResource.FieldHeight;
        PlaceGapOnFacingSide();
    }

    // Raised with the state a press on the switch asked for.
    public event Action<int>? Cycled;

    // The glyphs the switch cycles through, in order; setting a non-empty run is what shows the switch.
    public IReadOnlyList<Geometry>? Icons
    {
        get
        {
            return _iconSwitch.Icons;
        }
        set
        {
            _iconSwitch.Icons = value;
            _iconSwitch.IsVisible = value is { Count: > 0 };
        }
    }

    // Where a press out of the switch's mixed state goes.
    public int MixedGoesTo
    {
        get
        {
            return _iconSwitch.MixedGoesTo;
        }
        set
        {
            _iconSwitch.MixedGoesTo = value;
        }
    }

    // Which state the switch shows, or null for its mixed state.
    public int? State
    {
        get
        {
            return _iconSwitch.State;
        }
        set
        {
            _iconSwitch.State = value;
        }
    }

    // Whether the slot stands after the row's field rather than before its rail, which moves its gap to its other side.
    public bool Trails
    {
        get
        {
            return GetValue(TrailsProperty);
        }
        set
        {
            SetValue(TrailsProperty, value);
        }
    }

    // Moves the gap when the slot changes sides.
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TrailsProperty)
        {
            PlaceGapOnFacingSide();
        }
    }

    // Puts the gap on the side facing the rest of the row: the right before a rail, the left after a field.
    private void PlaceGapOnFacingSide()
    {
        Margin = Trails ? new Thickness(Space.Px8, 0, 0, 0) : new Thickness(0, 0, Space.Px8, 0);
    }
}
