using Avalonia.Controls;

namespace SpcStudio.Controls;

// An on-or-off value row's parts: its check box and its rung switch.
public sealed record ToggleRowParts(CheckBox Check, FieldOverride Switch);
