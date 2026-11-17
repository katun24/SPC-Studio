using Avalonia.Media;

namespace SpcStudio;

// One row of a drop-down whose rows each carry a color: the id it stands for, the name it reads as and a swatch of its color.
public record SwatchRow(int Id, string Name, Brush Swatch) : DropDownRow(Id, Name);
