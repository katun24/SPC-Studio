using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SpcStudio.Theme;

namespace SpcStudio.Controls;

// The ways a value control is stepped without typing into it - the arrow keys, the wheel over it and a drag with the wheel held - hung in one place so a
// control cannot have one without the others; and the inert state, in which a control is shown and pressable but takes none of them.
public static class FieldSteps
{
    private const double SliderStepsPerTravel = 100;
    private const double DragPixelsPerStep = 1;

    private static readonly ConditionalWeakTable<Control, EventHandler<KeyEventArgs>> KeyHandlersOfFields = [];
    private static readonly ConditionalWeakTable<Slider, Control> FieldsOfSliders = [];

    // Records the field a slider writes, so a notch the slider cannot step itself is handed to that field.
    public static void PairSliderWithField(Slider slider, Control field)
    {
        FieldsOfSliders.AddOrUpdate(slider, field);
    }

    // The field a slider writes, or null for a slider standing alone.
    public static Control? FieldOf(Slider slider)
    {
        return FieldsOfSliders.TryGetValue(slider, out var field) ? field : null;
    }

    // Makes a control inert - shown and pressable, but taking neither a wheel notch, a wheel drag nor an arrow key until it is pressed - or ordinary again.
    public static void SetInert(Control control, bool isInert)
    {
        control.Classes.Set(ThemeClasses.Inert, isInert);
    }

    // Whether a control is inert.
    public static bool IsInert(Control control)
    {
        return control.Classes.Contains(ThemeClasses.Inert);
    }

    // Makes a control inert and out of the tab order, or ordinary and back in it.
    public static void SetInertOutOfTabOrder(Control control, bool isInert)
    {
        SetInert(control, isInert);
        control.IsTabStop = !isInert;
    }

    // Whether a press on an inert control claims it, which only the left button's does.
    public static bool DoesPressClaimInert(PointerPressedEventArgs e)
    {
        return e.GetCurrentPoint(null).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed;
    }

    // Whether a press landed on a control that is inert and claims it.
    public static bool IsClaimingPress(object? sender, PointerPressedEventArgs e)
    {
        return sender is Control control && IsInert(control) && DoesPressClaimInert(e);
    }

    // The step an arrow key asks for: one up, one down, and none for any other key.
    public static int StepOf(KeyEventArgs e)
    {
        return e.Key switch
        {
            Key.Up => 1,
            Key.Down => -1,
            _ => 0,
        };
    }

    // Takes the step a key asks for, marking the key handled, or answers false for a key that asks for none.
    public static bool TryTakeStep(KeyEventArgs e, out int step)
    {
        step = StepOf(e);
        if (step == 0)
        {
            return false;
        }

        e.Handled = true;
        return true;
    }

    // The step a wheel notch asks for: one away from the user, one toward, and none for a notch with no vertical movement.
    public static int StepOf(PointerWheelEventArgs e)
    {
        return Math.Sign(e.Delta.Y);
    }

    // Hangs a value field's own key handler behind the arrow keys, the wheel over it and the wheel drag alike, so each steps the field as Up and Down do; and takes the text menu off it.
    public static void OnKeys(Control field, EventHandler<KeyEventArgs> keyHandler)
    {
        KeyHandlersOfFields.AddOrUpdate(field, keyHandler);
        field.ContextFlyout = null;
        field.AddHandler(InputElement.KeyDownEvent, (sender, e) => HandleFieldKey(field, keyHandler, sender, e), RoutingStrategies.Tunnel);
        field.AddHandler(InputElement.PointerWheelChangedEvent, (sender, e) => TakeNotch(field, e, steps => StepField(field, keyHandler, sender, steps)), RoutingStrategies.Tunnel);
        field.AddHandler(InputElement.PointerPressedEvent, (_, e) => StartWheelDrag(field, e, steps => StepField(field, keyHandler, field, steps)), RoutingStrategies.Tunnel);
    }

    // Hangs a field that puts its value's own spelling back on Enter and moves the value by a step on the arrow keys, the wheel and the wheel drag.
    public static void OnStepsAndEnter(Control field, Action<int> step, Action respell)
    {
        OnKeys(field, (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                respell();
                e.Handled = true;
                return;
            }

            if (TryTakeStep(e, out int steps))
            {
                step(steps);
            }
        });
    }

    // Gives every slider and every drop-down in the app the wheel and the wheel drag, present and future.
    public static void WireWheelToSlidersAndDropDowns()
    {
        InputElement.PointerWheelChangedEvent.AddClassHandler<Slider>((slider, e) => TakeNotch(slider, e, steps => StepSlider(slider, steps)), RoutingStrategies.Tunnel);
        InputElement.PointerPressedEvent.AddClassHandler<Slider>((slider, e) => StartWheelDrag(slider, e, steps => StepSlider(slider, steps)), RoutingStrategies.Tunnel);
        InputElement.PointerWheelChangedEvent.AddClassHandler<ComboBox>(OnComboWheel, RoutingStrategies.Tunnel);
        InputElement.PointerPressedEvent.AddClassHandler<ComboBox>(OnComboPressed, RoutingStrategies.Tunnel);
    }

    // Passes a key to a field's handler unless the field is inert.
    private static void HandleFieldKey(Control field, EventHandler<KeyEventArgs> keyHandler, object? sender, KeyEventArgs e)
    {
        if (IsInert(field))
        {
            return;
        }

        keyHandler(sender, e);
    }

    // Steps a control by a notch that is its to take - with Ctrl held, or with the control focused - and swallows the notch where the control is inert.
    private static void TakeNotch(Control control, PointerWheelEventArgs e, Action<int> step)
    {
        if (!control.IsEffectivelyEnabled || !e.KeyModifiers.HasFlag(KeyModifiers.Control) && !control.IsKeyboardFocusWithin)
        {
            return;
        }

        if (IsInert(control))
        {
            e.Handled = true;
            return;
        }

        int notches = StepOf(e);
        if (notches == 0)
        {
            return;
        }

        step(notches);
        e.Handled = true;
    }

    // Takes a wheel press on a control and steps the control as the pointer is dragged, one step up for every few pixels upward and one down for every
    // few downward; an inert control takes the press and nothing else.
    private static void StartWheelDrag(Control control, PointerPressedEventArgs e, Action<int> step)
    {
        if (!control.IsEffectivelyEnabled || !WheelDrag.IsWheelPress(e))
        {
            return;
        }

        e.Handled = true;
        if (IsInert(control))
        {
            return;
        }

        double unsteppedUpward = 0;
        WheelDrag.Follow(control, e, moved =>
        {
            unsteppedUpward -= moved.Y;
            int steps = (int)(unsteppedUpward / DragPixelsPerStep);
            if (steps == 0)
            {
                return;
            }

            unsteppedUpward -= steps * DragPixelsPerStep;
            step(steps);
        });
    }

    // Hands a field's key handler one Up or one Down for each step asked for.
    private static void StepField(Control field, EventHandler<KeyEventArgs> keyHandler, object? sender, int steps)
    {
        Key key = steps > 0 ? Key.Up : Key.Down;
        for (int count = 0; count < Math.Abs(steps); count++)
        {
            keyHandler(sender, new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = sender ?? field });
        }
    }

    // Steps a slider: by one tick a step where it snaps to them and a fixed share of its travel where it does not, or by the paired field's step where it shows no one value.
    private static void StepSlider(Slider slider, int steps)
    {
        if (slider.Classes.Contains(ThemeClasses.Mixed) && FieldsOfSliders.TryGetValue(slider, out var field)
            && KeyHandlersOfFields.TryGetValue(field, out var fieldKeyHandler))
        {
            StepField(field, fieldKeyHandler, field, steps);
            return;
        }

        double stepSize = slider.IsSnapToTickEnabled && slider.TickFrequency > 0 ? slider.TickFrequency : (slider.Maximum - slider.Minimum) / SliderStepsPerTravel;
        slider.Value = SliderField.HeldToRail(slider, slider.Value + steps * stepSize);
    }

    // Moves a closed drop-down's selection by a notch.
    private static void OnComboWheel(ComboBox box, PointerWheelEventArgs e)
    {
        if (!IsComboSteppable(box))
        {
            return;
        }

        TakeNotch(box, e, steps => StepCombo(box, steps));
    }

    // Starts a wheel drag on a closed drop-down.
    private static void OnComboPressed(ComboBox box, PointerPressedEventArgs e)
    {
        if (!IsComboSteppable(box))
        {
            return;
        }

        StartWheelDrag(box, e, steps => StepCombo(box, steps));
    }

    // Whether a drop-down is closed and has items to step through.
    private static bool IsComboSteppable(ComboBox box)
    {
        return !box.IsDropDownOpen && box.ItemCount > 0;
    }

    // Moves a drop-down's selection one item down the list for each step toward the user and one up for each step away, from the top where nothing is selected.
    private static void StepCombo(ComboBox box, int steps)
    {
        int index = box.SelectedIndex < 0 ? 0 : box.SelectedIndex - steps;
        box.SelectedIndex = Math.Clamp(index, 0, box.ItemCount - 1);
    }
}
