using System.Text.Json.Serialization;

namespace MsixCore.Corpus.Tests;

/// <summary>The root of <c>corpus.json</c>: generator metadata plus the fixture matrix.</summary>
public sealed record CorpusDocument
{
    public CorpusMeta Meta { get; init; } = new();

    public IReadOnlyList<CorpusFixture> Fixtures { get; init; } = [];
}

/// <summary>Provenance for the corpus (generator, publisher, publisher hash, timestamp).</summary>
public sealed record CorpusMeta
{
    public string Generator { get; init; } = string.Empty;

    public string Publisher { get; init; } = string.Empty;

    public string PublisherHash { get; init; } = string.Empty;

    public int FixtureCount { get; init; }
}

/// <summary>One corpus entry: the feature(s) it exercises, its layout(s), the Windows-oracle
/// verdict, and the parsed values our library is expected to produce.</summary>
public sealed record CorpusFixture
{
    public required string Id { get; init; }

    public required IReadOnlyList<string> Features { get; init; }

    /// <summary><c>package</c> or <c>bundle</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>Corpus-relative path to the loose (unpacked) layout, or <see langword="null"/>.</summary>
    public string? LooseDir { get; init; }

    /// <summary>Corpus-relative path to the packed <c>.msix</c>/<c>.msixbundle</c>, or <see langword="null"/>.</summary>
    public string? PackedFile { get; init; }

    /// <summary>Whether our library currently fully supports this fixture (bundles are not yet).</summary>
    public required bool ExpectedSupported { get; init; }

    public CorpusOracle WindowsOracle { get; init; } = new();

    /// <summary>The expected parsed identity/metadata; <see langword="null"/> for bundles.</summary>
    public ExpectedValues? Expected { get; init; }

    public required bool IsSignedLoose { get; init; }

    public required bool IsSignedPacked { get; init; }

    public int? BlockMapFileCount { get; init; }

    public bool? BlockMapValidLoose { get; init; }

    public bool? BlockMapValidPacked { get; init; }

    /// <summary>A filed issue number when the packed layout reproduces a known library bug (e.g. <c>#7</c>).</summary>
    public string? PackedKnownBug { get; init; }

    public string Notes { get; init; } = string.Empty;
}

/// <summary>The real Windows deployment verdict recorded when the corpus was generated.</summary>
public sealed record CorpusOracle
{
    /// <summary><c>installed</c>, <c>expected-not-installable</c>, <c>failed</c>, or <c>not-attempted</c>.</summary>
    public string Verdict { get; init; } = "not-attempted";

    public string Reason { get; init; } = string.Empty;
}

/// <summary>The values our library's reader is expected to produce for a fixture.</summary>
public sealed record ExpectedValues
{
    public required string Name { get; init; }

    public required string Publisher { get; init; }

    public required string Version { get; init; }

    public required string Architecture { get; init; }

    public required string ResourceId { get; init; }

    public required string PackageFamilyName { get; init; }

    public required string PackageFullName { get; init; }

    public required string DisplayName { get; init; }

    public required string PublisherDisplayName { get; init; }

    public required IReadOnlyList<string> Capabilities { get; init; }

    public required bool IsFramework { get; init; }

    public required int ApplicationCount { get; init; }
}
