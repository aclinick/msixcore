namespace MsixCore.CorpusRoundtrip;

/// <summary>Top-level harness report.</summary>
public sealed record RoundtripReport(
    bool MakeAppxAvailable,
    string? MakeAppxPath,
    IReadOnlyList<PackageRoundtripReport> Packages)
{
    /// <summary>True when every executed comparison met its mode-specific assertion.</summary>
    public bool Succeeded => Packages.All(static package => package.Succeeded);
}

/// <summary>Round-trip report for one input package or loose source directory.</summary>
public sealed record PackageRoundtripReport(
    string InputPath,
    string NormalizedSource,
    IReadOnlyList<ModeRoundtripReport> Modes)
{
    /// <summary>True when all requested modes passed for this input.</summary>
    public bool Succeeded => Modes.All(static mode => mode.Succeeded);
}

/// <summary>Round-trip report for one compression mode.</summary>
public sealed record ModeRoundtripReport(
    RoundtripMode Mode,
    ToolOutcome Ours,
    ToolOutcome OursRepeat,
    ToolOutcome MakeAppx,
    bool OursDeterministic,
    StoredComparisonReport? Stored,
    OptimalComparisonReport? Optimal)
{
    /// <summary>True when this mode's required assertions pass.</summary>
    public bool Succeeded =>
        Ours.Succeeded
        && OursRepeat.Succeeded
        && OursDeterministic
        && (MakeAppx.Skipped
            || (MakeAppx.Succeeded
                && ((Stored?.ByteIdentical ?? false) || (Optimal?.Equivalent ?? false))));
}

/// <summary>Stored-mode byte and semantic diff report.</summary>
public sealed record StoredComparisonReport(
    bool ByteIdentical,
    long? FirstByteDifference,
    IReadOnlyList<ZipStructuralDifference> ZipDifferences,
    IReadOnlyList<BlockMapSemanticDifference> BlockMapDifferences);

/// <summary>Optimal-mode semantic equivalence report.</summary>
public sealed record OptimalComparisonReport(
    bool Equivalent,
    long OursPackageSize,
    long MakeAppxPackageSize,
    long PackageSizeDelta,
    TimeSpan OursDuration,
    TimeSpan MakeAppxDuration,
    IReadOnlyList<string> PayloadHashDifferences,
    IReadOnlyList<BlockMapSemanticDifference> BlockMapDifferences);
