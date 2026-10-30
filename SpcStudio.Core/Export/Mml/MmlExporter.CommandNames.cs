using SpcStudio.Model;

namespace SpcStudio.Export;

// Turns a song into an AddmusicK MML file. This part holds the names the driver commands with no letter are written under, and the replacement directives at the head of the file that define them.
public static partial class MmlExporter
{
    private const string ClosingBracketFind = ")";
    private const int HexRadix = 16;

    internal static readonly CommandName AmpName = CommandName.ForBytes("Amp", true, AmkCommands.Parameter, AmkCommands.AmplifySubcommand);
    internal static readonly CommandName TransposeName = CommandName.ForBytes("Transpose", true, AmkCommands.Parameter, AmkCommands.TransposeSubcommand);
    internal static readonly CommandName DetuneName = CommandName.ForBytes("Detune", true, AmkCommands.FineTune);
    internal static readonly CommandName PanFadeName = CommandName.ForBytes("PanFade", true, AmkCommands.PanFade);
    internal static readonly CommandName VibFadeName = CommandName.ForBytes("VibFade", true, AmkCommands.VibratoFade);
    internal static readonly CommandName VibOffName = CommandName.ForBytes("VibOff", false, AmkCommands.VibratoOff);
    internal static readonly CommandName TremName = CommandName.ForBytes("Trem", true, AmkCommands.TremoloOn);
    internal static readonly CommandName TremOffName = CommandName.ForBytes("TremOff", false, AmkCommands.TremoloOff);
    internal static readonly CommandName EnvName = CommandName.ForBytes("Env", true, AmkCommands.Envelope);
    internal static readonly CommandName EnvResetName = CommandName.ForBytes("EnvReset", false, AmkCommands.Misc, AmkCommands.InstrumentRestoreSubcommand);
    internal static readonly CommandName LegatoName = CommandName.ForBytes("Legato", false, AmkCommands.Misc, AmkCommands.LegatoSubcommand);
    internal static readonly CommandName StaccatoName = CommandName.ForBytes("Staccato", false, AmkCommands.Misc, AmkCommands.LightStaccatoSubcommand);
    internal static readonly CommandName EchoName = CommandName.ForBytes("Echo", true, AmkCommands.EchoSend);
    internal static readonly CommandName EchoSetupName = CommandName.ForBytes("EchoSetup", true, AmkCommands.EchoUnit);
    internal static readonly CommandName EchoFadeName = CommandName.ForBytes("EchoFade", true, AmkCommands.EchoFade);
    internal static readonly CommandName EchoOffName = CommandName.ForBytes("EchoOff", false, AmkCommands.EchoOff);
    internal static readonly CommandName FirName = CommandName.ForBytes("Fir", true, AmkCommands.FirFilter);
    internal static readonly CommandName HoldOnName = new("HoldOn", HoldArmCommand, false, []);
    internal static readonly CommandName HoldOffName = new("HoldOff", HoldClearCommand, false, []);

    private static readonly CommandName[] CommandNamesInDefinitionOrder =
    [
        AmpName,
        TransposeName,
        DetuneName,
        PanFadeName,
        VibFadeName,
        VibOffName,
        TremName,
        TremOffName,
        EnvName,
        EnvResetName,
        LegatoName,
        StaccatoName,
        EchoName,
        EchoSetupName,
        EchoFadeName,
        EchoOffName,
        FirName,
        HoldOnName,
        HoldOffName,
    ];

    // Every driver command the export writes as raw hex, each as its command byte and the subcommand it picks where it picks one.
    internal static readonly int[][] HexCommandsWritten = [.. CommandNamesInDefinitionOrder.Select(name => name.CommandBytes).Where(commandBytes => commandBytes.Length > 0), [AmkCommands.PitchBend]];

    // The replacement directives for the names the text uses, with the closing bracket's behind them where any of those takes arguments, each find padded to the longest.
    private static IEnumerable<string> CommandNameDirectives(IEnumerable<string> texts)
    {
        var tokens = texts.SelectMany(text => text.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToHashSet(StringComparer.Ordinal);

        var usedNames = CommandNamesInDefinitionOrder.Where(name => tokens.Any(name.IsNameOf)).ToList();
        var pairs = usedNames.Select(name => (name.Find, name.Replacement)).ToList();
        if (usedNames.Any(name => name.HasArguments))
        {
            pairs.Add((ClosingBracketFind, ""));
        }

        return ReplacementDirectives(pairs);
    }

    // A driver command AddmusicK has no letter for, written under a name of its own that a replacement at the head of the file defines, with the driver
    // bytes it expands to where it expands to raw hex.
    internal sealed record CommandName(string Name, string Expansion, bool HasArguments, int[] CommandBytes)
    {
        // A command that expands to driver bytes, spelled as raw hex.
        public static CommandName ForBytes(string name, bool hasArguments, params int[] commandBytes)
        {
            return new CommandName(name, AmkCommands.HexSpelling(commandBytes), hasArguments, commandBytes);
        }

        // What the replacement directive matches.
        public string Find
        {
            get
            {
                return HasArguments ? Name + "(" : Name;
            }
        }

        // What the replacement directive stands for.
        public string Replacement
        {
            get
            {
                return HasArguments ? Expansion + " " : Expansion;
            }
        }

        // The command as a channel writes it, each argument a hex byte.
        public string Write(params int[] arguments)
        {
            return HasArguments ? $"{Name}({AmkCommands.HexSpelling(arguments)})" : Name;
        }

        // The argument bytes a token this command was written as carries, in order.
        public int[] ArgumentsOf(string token)
        {
            return [.. token[Find.Length..^ClosingBracketFind.Length].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(argument => Convert.ToInt32(argument[1..], HexRadix))];
        }

        // Whether a token of channel data is this command.
        public bool IsNameOf(string token)
        {
            return HasArguments ? token.StartsWith(Find, StringComparison.Ordinal) : token == Name;
        }
    }
}
