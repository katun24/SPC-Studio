namespace SpcStudio.Model;

// What the MML's #spc block holds: the four text fields AddmusicK writes into the compiled SPC's own header, none of which reaches the driver.
public sealed record SpcTags(string Title, string Author, string Game, string Comment)
{
    // What a new song holds: four fields nobody has typed into.
    public static readonly SpcTags Empty = new("", "", "", "");

    // Whether every field is blank.
    public bool IsEmpty
    {
        get
        {
            return string.IsNullOrWhiteSpace(Title) && string.IsNullOrWhiteSpace(Author) && string.IsNullOrWhiteSpace(Game) && string.IsNullOrWhiteSpace(Comment);
        }
    }
}
