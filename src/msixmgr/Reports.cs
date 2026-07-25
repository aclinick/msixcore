using System.Text.Json;
using System.Text.Json.Serialization;

namespace MsixMgr;

/// <summary>Machine-readable result of the <c>inspect</c> verb.</summary>
internal sealed record InspectionReport
{
    public required string Name { get; init; }

    public required string PackageFullName { get; init; }

    public required string PackageFamilyName { get; init; }

    public required string Version { get; init; }

    public required string Architecture { get; init; }

    public required string DisplayName { get; init; }

    public required string PublisherDisplayName { get; init; }

    public required IReadOnlyList<string> Capabilities { get; init; }

    public required bool IsSigned { get; init; }

    public int? BlockMapFileCount { get; init; }

    public string? BlockMapHashMethod { get; init; }
}

/// <summary>Machine-readable result of the <c>validate</c> verb.</summary>
internal sealed record ValidationReport
{
    public required string PackageFullName { get; init; }

    public required bool IsValid { get; init; }

    public required bool BlockMapValid { get; init; }

    public int VerifiedFileCount { get; init; }

    public required bool IsSigned { get; init; }

    public bool? CmsIntegrityValid { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = [];
}

/// <summary>Shared JSON options for CLI report serialization.</summary>
internal static class ReportJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
