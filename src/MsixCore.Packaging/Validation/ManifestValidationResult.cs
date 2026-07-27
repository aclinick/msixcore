using System.Collections.ObjectModel;

namespace MsixCore.Packaging.Validation;

/// <summary>The outcome of validating a manifest.</summary>
public sealed class ManifestValidationResult
{
    /// <summary>Creates a result from a set of issues.</summary>
    /// <param name="issues">The issues found, in the order they were discovered.</param>
    public ManifestValidationResult(IEnumerable<ManifestValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        Issues = new ReadOnlyCollection<ManifestValidationIssue>([.. issues]);
    }

    /// <summary>Every issue found, in discovery order.</summary>
    public IReadOnlyList<ManifestValidationIssue> Issues { get; }

    /// <summary>
    /// Whether the manifest is valid. Warnings do not make a manifest invalid — only issues with
    /// <see cref="ManifestValidationSeverity.Error"/> do.
    /// </summary>
    public bool IsValid => !Issues.Any(i => i.Severity == ManifestValidationSeverity.Error);

    /// <summary>The subset of <see cref="Issues"/> that make the manifest invalid.</summary>
    public IEnumerable<ManifestValidationIssue> Errors =>
        Issues.Where(i => i.Severity == ManifestValidationSeverity.Error);

    /// <summary>The subset of <see cref="Issues"/> that are advisory only.</summary>
    public IEnumerable<ManifestValidationIssue> Warnings =>
        Issues.Where(i => i.Severity == ManifestValidationSeverity.Warning);
}
