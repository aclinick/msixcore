namespace MsixCore.CorpusRoundtrip;

/// <summary>Result from a package authoring tool invocation.</summary>
public sealed record ToolOutcome(
    string ToolName,
    string OutputPath,
    bool Succeeded,
    bool Skipped,
    TimeSpan Duration,
    string Message);
