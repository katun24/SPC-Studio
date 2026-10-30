using Avalonia.Media;
using SpcStudio.Controls;
using SpcStudio.Model;

namespace SpcStudio;

// One row of the two instrument drop-downs: the instrument's id, its name and a swatch of its color.
public sealed record InstrumentItem(int Id, string Name, Brush Swatch) : DropDownRow(Id, Name)
{
    // The drop-down row for an instrument.
    public static InstrumentItem Of(Instrument instrument)
    {
        return new InstrumentItem(instrument.Id, instrument.Name, new SolidColorBrush(ChannelPalette.FromPackedRgb(instrument.ColorRgb)));
    }
}
