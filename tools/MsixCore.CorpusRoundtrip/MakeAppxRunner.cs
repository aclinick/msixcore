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
        return PackAsync(sourceDirectory, outputPath, mode).GetAwaiter().GetResult();
    }

    /// <summary>Packs <paramref name="sourceDirectory"/> with makeappx.exe.</summary>
    public async Task<ToolOutcome> PackAsync(string sourceDirectory, string outputPath, RoundtripMode mode)
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
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        Task exitTask = process.WaitForExitAsync();
        await Task.WhenAll(stdoutTask, stderrTask, exitTask).ConfigureAwait(false);
        stopwatch.Stop();

        string stdout = await stdoutTask.ConfigureAwait(false);
        string stderr = await stderrTask.ConfigureAwait(false);
        string message = string.Join(Environment.NewLine, new[] { stdout.Trim(), stderr.Trim() }.Where(static text => text.Length > 0));
        bool succeeded = process.ExitCode == 0 && File.Exists(outputPath);
        if (process.ExitCode == 0 && !File.Exists(outputPath))
        {
            message = string.IsNullOrEmpty(message)
                ? "makeappx exited successfully but did not create the output package."
                : message + Environment.NewLine + "makeappx exited successfully but did not create the output package.";
        }

        return new ToolOutcome(
            "makeappx",
            outputPath,
            succeeded,
            Skipped: false,
            stopwatch.Elapsed,
            message);
    }
}
