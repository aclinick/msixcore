using System.Text.Json;

namespace MsixCore.Corpus.Tests;

/// <summary>
/// Loads <c>corpus.json</c> (copied next to the test assembly) and exposes the fixture matrix and
/// the xUnit <c>[Theory]</c> data sources over it.
/// </summary>
public static class CorpusRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>The directory that contains <c>corpus.json</c>, <c>fixtures/</c> and <c>packed/</c>.</summary>
    public static string CorpusRoot { get; } = Path.Combine(AppContext.BaseDirectory, "Corpus");

    /// <summary>The parsed corpus document.</summary>
    public static CorpusDocument Document { get; } = Load();

    private static readonly Dictionary<string, CorpusFixture> ById =
        Document.Fixtures.ToDictionary(f => f.Id, StringComparer.Ordinal);

    /// <summary>Looks up a fixture by id.</summary>
    public static CorpusFixture Get(string id) => ById[id];

    /// <summary>Resolves a corpus-relative path (forward slashes) to a full local path.</summary>
    public static string ResolvePath(string relative) =>
        Path.Combine(CorpusRoot, relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Every fixture that ships a loose (unpacked) layout.</summary>
    public static IEnumerable<object[]> LooseCases() =>
        Document.Fixtures.Where(f => f.LooseDir is not null).Select(f => new object[] { f.Id });

    /// <summary>Every non-bundle fixture that ships a packed <c>.msix</c>.</summary>
    public static IEnumerable<object[]> PackedPackageCases() =>
        Document.Fixtures
            .Where(f => f.PackedFile is not null && string.Equals(f.Kind, "package", StringComparison.Ordinal))
            .Select(f => new object[] { f.Id });

    /// <summary>Every bundle fixture (documented as not-yet-supported by the reader).</summary>
    public static IEnumerable<object[]> BundleCases() =>
        Document.Fixtures
            .Where(f => string.Equals(f.Kind, "bundle", StringComparison.Ordinal))
            .Select(f => new object[] { f.Id });

    private static CorpusDocument Load()
    {
        string path = Path.Combine(CorpusRoot, "corpus.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"corpus.json was not copied to the test output. Expected at '{path}'. " +
                "Run tests/Corpus/Build-Corpus.ps1 to (re)generate the corpus.",
                path);
        }

        using FileStream stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<CorpusDocument>(stream, JsonOptions)
            ?? throw new InvalidDataException("corpus.json deserialized to null.");
    }
}
