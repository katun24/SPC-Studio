using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SpcStudio.Controls;

// The right-click that puts a field or a slider back at its base value, hung on every field and slider in the app in one place; a control is
// given its base value where it is wired, and one with none takes the click as nothing.
public static class FieldResets
{
    private static readonly ConditionalWeakTable<Control, Action> ResetsOfControls = [];

    // Gives a field or a slider the reset a right-click on it runs.
    public static void OnReset(Control control, Action reset)
    {
        ResetsOfControls.AddOrUpdate(control, reset);
    }

    // Gives a field a base value, written into it as typing would write it, or nothing where there is none to write.
    public static void ResetsTo(TextBox box, Func<int?> baseValueOf)
    {
        OnReset(box, () =>
        {
            if (baseValueOf() is int baseValue)
            {
                FieldInput.WriteValue(box, baseValue);
            }
        });
    }

    // Gives a slider standing alone a base value, which the reset moves it to.
    public static void ResetsTo(Slider slider, Func<double> baseValueOf)
    {
        OnReset(slider, () => slider.Value = SliderField.HeldToRail(slider, baseValueOf()));
    }

    // Whether a press is the right button's own.
    private static bool IsResetPress(PointerPressedEventArgs e)
    {
        return e.GetCurrentPoint(null).Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed;
    }

    // Hangs the right-click on every field and slider in the app, present and future.
    public static void WireRightPressToResets()
    {
        InputElement.PointerPressedEvent.AddClassHandler<TextBox>(OnPressed, RoutingStrategies.Tunnel);
        InputElement.PointerPressedEvent.AddClassHandler<Slider>(OnPressed, RoutingStrategies.Tunnel);
    }

    // Runs a control's reset on a right-click, a slider beside a field running the field's, and takes the click either way where the control is inert.
    private static void OnPressed(Control control, PointerPressedEventArgs e)
    {
        if (!IsResetPress(e))
        {
            return;
        }

        if (FieldSteps.IsInert(control))
        {
            e.Handled = true;
            return;
        }

        if (!control.IsEffectivelyEnabled || ResetOf(control) is not { } reset)
        {
            return;
        }

        reset();
        e.Handled = true;
    }

    // The reset a right-click on a control runs: its own, or the field's beside a slider that has none, or null where there is none.
    private static Action? ResetOf(Control control)
    {
        if (ResetsOfControls.TryGetValue(control, out var reset))
        {
            return reset;
        }

        if (control is Slider slider && FieldSteps.FieldOf(slider) is { } field && ResetsOfControls.TryGetValue(field, out var fieldReset))
        {
            return fieldReset;
        }

        return null;
    }
}
