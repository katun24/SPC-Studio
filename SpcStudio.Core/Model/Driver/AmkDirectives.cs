namespace SpcStudio.Model;

// The directive names AddmusicK reads behind a '#', with the names its #option and #spc directives take, and how a directive is written in a song's text.
public static class AmkDirectives
{
    public const string Amk = "amk";
    public const string Path = "path";
    public const string Samples = "samples";
    public const string Instruments = "instruments";
    public const string Spc = "spc";
    public const string Option = "option";
    public const string Louder = "louder";
    public const string Define = "define";
    public const string Undef = "undef";
    public const string Ifdef = "ifdef";
    public const string Ifndef = "ifndef";
    public const string If = "if";
    public const string Endif = "endif";
    public const string Error = "error";
    public const string Am4 = "am4";
    public const string Amm = "amm";
    public const string NoLoopOption = "noloop";
    public const string SmwVelocityTableOption = "smwvtable";
    public const string NspcVelocityTableOption = "nspcvtable";
    public const string TempoImmunityOption = "tempoimmunity";
    public const string Amk109HotPatchOption = "amk109hotpatch";
    public const string TitleField = "title";
    public const string AuthorField = "author";
    public const string GameField = "game";
    public const string CommentField = "comment";
    public const string LengthField = "length";
    private const string DirectiveMark = "#";

    // A directive or an #spc field as a song's text writes it, its '#' in front of its name.
    public static string Written(string name)
    {
        return DirectiveMark + name;
    }
}
