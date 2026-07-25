using System.Diagnostics;
using System.IO.Compression;
using MsixCore.Packaging.Authoring;

namespace MsixCore.CorpusRoundtrip;

/// <summary>Invokes the managed MSIX Core package builder.</summary>
public sealed class OurPacker
{
    /// <summary>Packs <paramref name="sourceDirectory"/> into <paramref name="outputPath"/>.</summary>
    public static ToolOutcome Pack(string sourceDirectory, string outputPath, RoundtripMode mode)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceDirectory);
        ArgumentException.ThrowIfNullOrEmpty(outputPath);

        var stopwatch = Stopwatch.StartNew();
        MsixPackageBuilder.Build(
            sourceDirectory,
            outputPath,
            new PackOptions
            {
                Overwrite = true,
                CompressionLevel = mode == RoundtripMode.Stored
                    ? CompressionLevel.NoCompression
                    : CompressionLevel.Optimal,
            });
        stopwatch.Stop();

        return new ToolOutcome("ours", outputPath, Succeeded: true, Skipped: false, stopwatch.Elapsed, string.Empty);
    }
}
