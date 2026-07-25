using System.Reflection;

namespace MsixMgr;

/// <summary>
/// Entry point for the <c>msixmgr</c> command-line tool. Phase 0 wires up help/version and the
/// verb surface; full verb behavior lands in Phase 7.
/// </summary>
public static class Program
{
    /// <summary>Process entry point.</summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>Process exit code (0 = success).</returns>
    public static int Main(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintUsage();
            return 0;
        }

        if (IsVersion(args[0]))
        {
            Console.WriteLine(GetVersion());
            return 0;
        }

        string[] rest = args[1..];
        switch (args[0])
        {
            case "inspect":
                return InspectCommand.Run(rest, Console.Out, Console.Error);
            case "validate":
                return ValidateCommand.Run(rest, Console.Out, Console.Error);
            case "unpack":
                return UnpackCommand.Run(rest, Console.Out, Console.Error);
            case "pack":
            case "makemsix":
                return PackCommand.Run(rest, Console.Out, Console.Error);
            default:
                Console.Error.WriteLine($"msixmgr: verb '{args[0]}' is not implemented yet.");
                Console.Error.WriteLine("Run 'msixmgr --help' for usage.");
                return 2;
        }
    }

    private static bool IsHelp(string arg) =>
        arg is "-h" or "--help" or "-?" or "/?";

    private static bool IsVersion(string arg) =>
        arg is "--version" or "-v";

    internal static string GetVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            msixmgr - MSIX Core (.NET) command-line tool

            Usage:
              msixmgr <verb> [options]

            Verbs (implemented incrementally):
              inspect <path> [--json]     Show package identity and metadata.
              validate <path> [--json]    Verify integrity (block map + signature); CI exit code.
              unpack <path> -Destination <dir> [--json]
                                          Extract a package to a loose layout without installing.
              pack <sourceDir> -o <file.msix> [--overwrite] [--json]
                                          Build an unsigned MSIX package (alias: makemsix).
              -AddPackage <path>          Install an MSIX/APPX package.
              -RemovePackage <fullName>   Uninstall a package by full name.
              -FindPackage <pattern>      Query installed packages (supports * and ?).

            <path> may be a package file (.msix/.appx) or an unpacked directory.

            Options:
              -h, --help                  Show this help.
              -v, --version               Show version information.
            """);
    }
}
