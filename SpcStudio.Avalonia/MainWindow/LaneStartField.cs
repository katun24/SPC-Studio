using Avalonia.Controls;
using SpcStudio.Controls;
using SpcStudio.Model;

namespace SpcStudio;

// A panel row showing and setting a value a lane holds at the song's start: the lane, the row's slider and field, the range the field takes, the value
// the song holds, the value a right-click puts the field back at, and the write of a typed value under the control it came from.
public sealed record LaneStartField(ChangeLane Lane, Slider Slider, TextBox Box, ValueRange Range, Func<int> SongValue, Func<int> DefaultValue, Action<Control, int> WriteValue);
