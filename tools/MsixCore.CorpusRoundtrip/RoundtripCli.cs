using System.Globalization;
using System.Text.Json;

namespace MsixCore.CorpusRoundtrip;

/// <summary>Command-line front end for the corpus round-trip harness.</summary>
public static class RoundtripCli
{
    /// <summary>Runs the command-line harness.</summary>
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (!TryParse(args, out CliOptions? options, out string? parseError))
        {
            await error.WriteLineAsync(parseError).ConfigureAwait(false);
            await error.WriteLineAsync("Usage: MsixCore.CorpusRoundtrip [--work <dir>] [--modes stored|optimal|both] [--report <path>] [--json] <package-or-dir> [...].").ConfigureAwait(false);
            return 2;
        }

        try
        {
            var harness = new RoundtripHarness();
            RoundtripReport report = harness.Run(options!.Inputs, options.WorkDirectory, options.Modes);
            string markdown = ReportFormatter.ToMarkdown(report);
            await output.WriteLineAsync(markdown).ConfigureAwait(false);

            if (options.Json)
            {
                await output.WriteLineAsync(JsonSerializer.Serialize(report, JsonOptions)).ConfigureAwait(false);
            }

            if (options.ReportPath is not null)
            {
                string reportPath = Path.GetFullPath(options.ReportPath);
                Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
                await File.WriteAllTextAsync(reportPath, markdown).ConfigureAwait(false);
                string jsonPath = Path.ChangeExtension(reportPath, ".json");
                await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(report, JsonOptions)).ConfigureAwait(false);
            }

            return report.Succeeded ? 0 : 1;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
        {
            await error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private static bool TryParse(string[] args, out CliOptions? options, out string? error)
    {
        string workDirectory = Path.Combine(Directory.GetCurrentDirectory(), ".corpus-roundtrip-work");
        string? reportPath = null;
        bool json = false;
        IReadOnlyList<RoundtripMode> modes = [RoundtripMode.Stored, RoundtripMode.Optimal];
        var inputs = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg == "--json")
            {
                json = true;
            }
            else if (arg == "--work")
            {
                if (!TryReadValue(args, ref i, "--work", out workDirectory, out error))
                {
                    options = null;
                    return false;
                }
            }
            else if (arg == "--report")
            {
                if (!TryReadValue(args, ref i, "--report", out reportPath, out error))
                {
                    options = null;
                    return false;
                }
            }
            else if (arg == "--modes")
            {
                if (!TryReadValue(args, ref i, "--modes", out string? modeText, out error)
                    || !TryParseModes(modeText, out modes))
                {
                    error ??= "Invalid --modes value. Expected stored, optimal, or both.";
                    options = null;
                    return false;
                }
            }
            else if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                options = null;
                error = "Unknown option '" + arg + "'.";
                return false;
            }
            else
            {
                inputs.Add(arg);
            }
        }

        if (inputs.Count == 0)
        {
            options = null;
            error = "At least one package file or unpacked directory is required.";
            return false;
        }

        options = new CliOptions(inputs, Path.GetFullPath(workDirectory), modes, reportPath, json);
        error = null;
        return true;
    }

    private static bool TryReadValue(string[] args, ref int index, string optionName, out string value, out string? error)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith('-'))
        {
            value = string.Empty;
            error = optionName + " requires a value.";
            return false;
        }

        index++;
        value = args[index];
        error = null;
        return true;
    }

    private static bool TryParseModes(string value, out IReadOnlyList<RoundtripMode> modes)
    {
        if (string.Equals(value, "stored", StringComparison.OrdinalIgnoreCase))
        {
            modes = [RoundtripMode.Stored];
            return true;
        }

        if (string.Equals(value, "optimal", StringComparison.OrdinalIgnoreCase))
        {
            modes = [RoundtripMode.Optimal];
            return true;
        }

        if (string.Equals(value, "both", StringComparison.OrdinalIgnoreCase))
        {
            modes = [RoundtripMode.Stored, RoundtripMode.Optimal];
            return true;
        }

        modes = [];
        return false;
    }

    private sealed record CliOptions(
        IReadOnlyList<string> Inputs,
        string WorkDirectory,
        IReadOnlyList<RoundtripMode> Modes,
        string? ReportPath,
        bool Json);
}

/// <summary>Formats round-trip reports for humans.</summary>
public static class ReportFormatter
{
    /// <summary>Formats a markdown report.</summary>
    public static string ToMarkdown(RoundtripReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        writer.WriteLine("# MSIX Core corpus round-trip report");
        writer.WriteLine();
        writer.WriteLine("makeappx: " + (report.MakeAppxAvailable ? report.MakeAppxPath : "makeappx not available"));
        writer.WriteLine("overall: " + (report.Succeeded ? "pass" : "diff/error"));
        writer.WriteLine();

        foreach (PackageRoundtripReport package in report.Packages)
        {
            writer.WriteLine("## " + package.InputPath);
            writer.WriteLine();
            writer.WriteLine("normalized source: " + package.NormalizedSource);
            foreach (ModeRoundtripReport mode in package.Modes)
            {
                WriteMode(writer, mode);
            }
        }

        return writer.ToString();
    }

    private static void WriteMode(StringWriter writer, ModeRoundtripReport mode)
    {
        writer.WriteLine();
        writer.WriteLine("### " + mode.Mode);
        writer.WriteLine();
        writer.WriteLine("- ours deterministic: " + (mode.OursDeterministic ? "yes" : "no"));
        writer.WriteLine("- ours time: " + mode.Ours.Duration.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture) + " ms");
        if (mode.MakeAppx.Skipped)
        {
            writer.WriteLine("- makeappx: makeappx not available");
            return;
        }

        writer.WriteLine("- makeappx time: " + mode.MakeAppx.Duration.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture) + " ms");
        if (!mode.MakeAppx.Succeeded)
        {
            writer.WriteLine("- makeappx failed: " + mode.MakeAppx.Message);
            return;
        }

        if (mode.Stored is not null)
        {
            writer.WriteLine("- stored byte-identical: " + (mode.Stored.ByteIdentical ? "yes" : "no"));
            writer.WriteLine("- first byte diff: " + (mode.Stored.FirstByteDifference?.ToString(CultureInfo.InvariantCulture) ?? "<none>"));
            WriteDiffs(writer, "ZIP structural diffs", mode.Stored.ZipDifferences.Select(static diff =>
                diff.EntryName + " " + diff.Field + ": " + diff.Left + " vs " + diff.Right + " (" + diff.Interpretation + ")"));
            WriteDiffs(writer, "Block-map semantic diffs", mode.Stored.BlockMapDifferences.Select(static diff =>
                diff.FileName + " " + diff.Field + ": " + diff.Left + " vs " + diff.Right + " (" + diff.Interpretation + ")"));
        }

        if (mode.Optimal is not null)
        {
            writer.WriteLine("- optimal equivalent: " + (mode.Optimal.Equivalent ? "yes" : "no"));
            writer.WriteLine("- package size delta (makeappx - ours): " + mode.Optimal.PackageSizeDelta.ToString(CultureInfo.InvariantCulture) + " bytes");
            WriteDiffs(writer, "Payload hash diffs", mode.Optimal.PayloadHashDifferences);
            WriteDiffs(writer, "Block-map semantic diffs", mode.Optimal.BlockMapDifferences.Select(static diff =>
                diff.FileName + " " + diff.Field + ": " + diff.Left + " vs " + diff.Right + " (" + diff.Interpretation + ")"));
        }
    }

    private static void WriteDiffs(StringWriter writer, string title, IEnumerable<string> differences)
    {
        string[] values = differences.Take(25).ToArray();
        writer.WriteLine("- " + title + ": " + (values.Length == 0 ? "none" : string.Empty));
        foreach (string difference in values)
        {
            writer.WriteLine("  - " + difference);
        }
    }
}
