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

        Console.Error.WriteLine($"msixmgr: verb '{args[0]}' is not implemented yet (lands in Phase 7).");
        Console.Error.WriteLine("Run 'msixmgr --help' for usage.");
        return 2;
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
              -AddPackage <path>          Install an MSIX/APPX package.
              -RemovePackage <fullName>   Uninstall a package by full name.
              -FindPackage <pattern>      Query installed packages (supports * and ?).
              -Unpack <path> -Destination <dir>
                                          Extract a package without installing.

            Options:
              -h, --help                  Show this help.
              -v, --version               Show version information.
            """);
    }
}
