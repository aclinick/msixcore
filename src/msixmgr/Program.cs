using System.Reflection;

namespace MsixMgr;

/// <summary>
/// Entry point for the <c>msixmgr</c> command-line tool.
/// </summary>
public static class Program
{
    private static readonly CliVerb[] s_verbs =
    [
        new(
            "inspect",
            "<path> [--json]",
            "Show package identity and metadata.",
            InspectCommand.Run),
        new(
            "validate",
            "<path> [--json]",
            "Verify integrity (block map + signature); CI exit code.",
            ValidateCommand.Run),
        new(
            "unpack",
            "<path> -Destination <dir> [--json]",
            "Extract a package to a loose layout without installing.",
            UnpackCommand.Run),
        new(
            "pack",
            "<sourceDir> -o|--output <file.msix> [--overwrite] [--json]",
            "Build an unsigned MSIX package (alias: makemsix).",
            PackCommand.Run,
            ["makemsix"]),
    ];

    internal static IReadOnlyList<CliVerb> Verbs => s_verbs;

    /// <summary>Process entry point.</summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>Process exit code (0 = success).</returns>
    public static int Main(string[] args)
        => Run(args, Console.Out, Console.Error);

    internal static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (args.Count == 0 || IsHelp(args[0]))
        {
            PrintUsage(output);
            return 0;
        }

        if (IsVersion(args[0]))
        {
            output.WriteLine(GetVersion());
            return 0;
        }

        CliVerb? verb = Array.Find(
            s_verbs,
            candidate => candidate.Matches(args[0]));
        if (verb is null)
        {
            error.WriteLine($"msixmgr: unknown verb '{args[0]}'.");
            error.WriteLine("Run 'msixmgr --help' for usage.");
            return 2;
        }

        return verb.Run(args.Skip(1).ToArray(), output, error);
    }

    private static bool IsHelp(string arg) =>
        arg is "-h" or "--help" or "-?" or "/?";

    private static bool IsVersion(string arg) =>
        arg is "--version" or "-v";

    internal static string GetVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

    private static void PrintUsage(TextWriter output)
    {
        output.WriteLine(
            """
            msixmgr - MSIX Core (.NET) command-line tool

            Usage:
              msixmgr <verb> [options]

            Verbs:
            """);

        int usageWidth = s_verbs.Max(static verb => verb.Usage.Length) + 2;
        foreach (CliVerb verb in s_verbs)
        {
            output.WriteLine($"  {verb.Usage.PadRight(usageWidth)}{verb.Description}");
        }

        output.WriteLine();
        output.WriteLine(
            """
            For inspect, validate, and unpack, <path> may be a package file
            (.msix/.appx) or an unpacked directory. pack requires a source directory.

            Options:
              -h, --help                  Show this help.
              -v, --version               Show version information.
            """);
    }
}

internal sealed record CliVerb(
    string Name,
    string Arguments,
    string Description,
    Func<IReadOnlyList<string>, TextWriter, TextWriter, int> Run,
    IReadOnlyList<string>? Aliases = null)
{
    public string Usage => string.IsNullOrEmpty(Arguments) ? Name : $"{Name} {Arguments}";

    public bool Matches(string token) =>
        string.Equals(Name, token, StringComparison.Ordinal)
        || (Aliases?.Contains(token, StringComparer.Ordinal) ?? false);
}
