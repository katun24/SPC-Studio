namespace SpcStudio;

// One row of a drop-down of the song's samples or instruments: the id it stands for and the name it reads as.
public record DropDownRow(int Id, string Name)
{
    // The name, which is what the row reads as.
    public sealed override string ToString()
    {
        return Name;
    }
}
