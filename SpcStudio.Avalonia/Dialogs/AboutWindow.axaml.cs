namespace SpcStudio;

// The About box: the app's name, version and maker, the Discord server, and the tools and resources it credits.
public partial class AboutWindow : DialogWindow
{
    private const string VersionPrefix = "v";
    private const string ByWord = " by ";

    // The line inviting the reader to the Discord server.
    private static readonly LinkLine DiscordLine = new("Join the Discord server", new Uri("https://discord.gg/DyHNRgdJFr"), " for updates, help, or feedback!");

    // Every tool and resource the box credits, in the order it lists them.
    private static readonly LinkLine[] Credits =
    [
        Credit("snes_spc", "https://github.com/blarggs-audio-libraries/snes_spc", "blargg"),
        Credit("AddmusicK", "https://github.com/KungFuFurby/AddMusicKFF", "Kipernal and KungFuFurby"),
        Credit("Asar", "https://github.com/RPGHacker/asar", "Alcaro"),
        Credit("fullsnes", "https://problemkaputt.de/fullsnes.htm", "nocash"),
        Credit("miniaudio", "https://miniaud.io", "mackron"),
        Credit("Avalonia", "https://avaloniaui.net", "the Avalonia team"),
        Credit("Inter", "https://rsms.me/inter", "rsms"),
        Credit("Material Design Icons", "https://pictogrammers.com/library/mdi", "Pictogrammers"),
        Credit("Oklab", "https://bottosson.github.io/posts/oklab", "Björn Ottosson"),
    ];

    // Builds the box, showing the app's version, the Discord line and the credits.
    public AboutWindow()
    {
        InitializeComponent();
        VersionLine.Text = VersionPrefix + AppVersion.Text;
        DiscordRow.Content = DiscordLine;
        CreditList.ItemsSource = Credits;
    }

    // A credit's line: the thing's name as a link to its source, then who made it.
    private static LinkLine Credit(string name, string source, string maker)
    {
        return new LinkLine(name, new Uri(source), ByWord + maker);
    }
}
