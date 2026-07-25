using System.Diagnostics;

namespace MsixCore.CorpusRoundtrip;

/// <summary>Runs makeappx.exe when it is installed, otherwise reports a graceful skip.</summary>
public sealed class MakeAppxRunner
{
    private readonly string? _makeAppxPath;

    /// <summary>Creates a runner using the default makeappx discovery logic.</summary>
    public MakeAppxRunner()
        : this(MakeAppxLocator.Find())
    {
    }

    /// <summary>Creates a runner for a known makeappx.exe path, or <see langword="null"/> to skip.</summary>
    public MakeAppxRunner(string? makeAppxPath)
    {
        _makeAppxPath = makeAppxPath;
    }

    /// <summary>The discovered makeappx.exe path, or <see langword="null"/> when unavailable.</summary>
    public string? MakeAppxPath => _makeAppxPath;

    /// <summary>Packs <paramref name="sourceDirectory"/> with makeappx.exe.</summary>
    public ToolOutcome Pack(string sourceDirectory, string outputPath, RoundtripMode mode)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceDirectory);
        ArgumentException.ThrowIfNullOrEmpty(outputPath);

        if (_makeAppxPath is null)
        {
            return new ToolOutcome(
                "makeappx",
                outputPath,
                Succeeded: false,
                Skipped: true,
                TimeSpan.Zero,
                "makeappx not available");
        }

        var startInfo = new ProcessStartInfo(_makeAppxPath)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("pack");
        startInfo.ArgumentList.Add("/p");
        startInfo.ArgumentList.Add(outputPath);
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add(sourceDirectory);
        startInfo.ArgumentList.Add("/o");
        if (mode == RoundtripMode.Stored)
        {
            startInfo.ArgumentList.Add("/nc");
        }

        var stopwatch = Stopwatch.StartNew();
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start makeappx.exe.");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        stopwatch.Stop();

        string message = string.Join(Environment.NewLine, new[] { stdout.Trim(), stderr.Trim() }.Where(static text => text.Length > 0));
        return new ToolOutcome(
            "makeappx",
            outputPath,
            process.ExitCode == 0,
            Skipped: false,
            stopwatch.Elapsed,
            message);
    }
}
