using System.Globalization;

namespace MsixCore.CorpusRoundtrip;

/// <summary>Coordinates normalization, packing, and comparisons.</summary>
public sealed class RoundtripHarness
{
    private readonly MakeAppxRunner _makeAppx;

    /// <summary>Creates a harness with default dependencies.</summary>
    public RoundtripHarness()
        : this(
            new MakeAppxRunner())
    {
    }

    /// <summary>Creates a harness with explicit dependencies.</summary>
    public RoundtripHarness(
        MakeAppxRunner makeAppx)
    {
        _makeAppx = makeAppx;
    }

    /// <summary>Runs the harness for all inputs and modes.</summary>
    public RoundtripReport Run(IReadOnlyList<string> inputPaths, string workDirectory, IReadOnlyList<RoundtripMode> modes)
    {
        ArgumentNullException.ThrowIfNull(inputPaths);
        ArgumentException.ThrowIfNullOrEmpty(workDirectory);
        ArgumentNullException.ThrowIfNull(modes);

        Directory.CreateDirectory(workDirectory);
        var packages = new List<PackageRoundtripReport>();
        for (int i = 0; i < inputPaths.Count; i++)
        {
            packages.Add(RunOne(inputPaths[i], workDirectory, i, modes));
        }

        return new RoundtripReport(_makeAppx.MakeAppxPath is not null, _makeAppx.MakeAppxPath, packages);
    }

    private PackageRoundtripReport RunOne(string inputPath, string workDirectory, int index, IReadOnlyList<RoundtripMode> modes)
    {
        string packageWork = Path.Combine(
            workDirectory,
            index.ToString("D3", CultureInfo.InvariantCulture) + "-" + Sanitize(Path.GetFileNameWithoutExtension(inputPath)));
        if (Directory.Exists(packageWork))
        {
            Directory.Delete(packageWork, recursive: true);
        }

        Directory.CreateDirectory(packageWork);
        string normalizedDirectory = Path.Combine(packageWork, "normalized");
        NormalizedSource normalized = SourceNormalizer.Normalize(inputPath, normalizedDirectory);

        var reports = new List<ModeRoundtripReport>();
        foreach (RoundtripMode mode in modes)
        {
            reports.Add(RunMode(normalized, packageWork, mode));
        }

        return new PackageRoundtripReport(inputPath, normalized.DirectoryPath, reports);
    }

    private ModeRoundtripReport RunMode(NormalizedSource source, string packageWork, RoundtripMode mode)
    {
        string modeName = mode.ToString().ToLower(CultureInfo.InvariantCulture);
        string modeWork = Path.Combine(packageWork, modeName);
        Directory.CreateDirectory(modeWork);
        string oursPath = Path.Combine(modeWork, "ours.msix");
        string oursRepeatPath = Path.Combine(modeWork, "ours-repeat.msix");
        string makeAppxPath = Path.Combine(modeWork, "makeappx.msix");

        ToolOutcome ours = OurPacker.Pack(source.DirectoryPath, oursPath, mode);
        ToolOutcome oursRepeat = OurPacker.Pack(source.DirectoryPath, oursRepeatPath, mode);
        bool oursDeterministic = RawByteDiffer.FindFirstDifference(oursPath, oursRepeatPath) is null;
        ToolOutcome makeAppx = _makeAppx.Pack(source.DirectoryPath, makeAppxPath, mode);

        StoredComparisonReport? stored = null;
        OptimalComparisonReport? optimal = null;
        if (makeAppx.Succeeded)
        {
            if (mode == RoundtripMode.Stored)
            {
                ZipStructuralDiffResult zip = ZipStructuralDiffer.Compare(oursPath, makeAppxPath);
                BlockMapSemanticDiffResult blockMap = BlockMapSemanticDiffer.ComparePackages(
                    oursPath,
                    makeAppxPath,
                    includeLfhSizeAndBlockSizes: true);
                stored = new StoredComparisonReport(
                    zip.FirstByteDifference is null,
                    zip.FirstByteDifference,
                    zip.Differences,
                    blockMap.Differences);
            }
            else
            {
                PayloadHashComparison payload = PayloadHashComparer.ComparePackages(oursPath, makeAppxPath);
                BlockMapSemanticDiffResult blockMap = BlockMapSemanticDiffer.ComparePackages(
                    oursPath,
                    makeAppxPath,
                    includeLfhSizeAndBlockSizes: false);
                long oursSize = new FileInfo(oursPath).Length;
                long makeAppxSize = new FileInfo(makeAppxPath).Length;
                optimal = new OptimalComparisonReport(
                    payload.IsEquivalent && blockMap.IsEquivalent,
                    oursSize,
                    makeAppxSize,
                    makeAppxSize - oursSize,
                    ours.Duration,
                    makeAppx.Duration,
                    payload.Differences,
                    blockMap.Differences);
            }
        }

        return new ModeRoundtripReport(mode, ours, oursRepeat, makeAppx, oursDeterministic, stored, optimal);
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "input";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        var characters = value.Select(character => invalid.Contains(character) ? '_' : character).ToArray();
        return new string(characters);
    }
}
