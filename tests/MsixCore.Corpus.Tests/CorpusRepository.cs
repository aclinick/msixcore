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
        CorpusDocument document = JsonSerializer.Deserialize<CorpusDocument>(stream, JsonOptions)
            ?? throw new InvalidDataException("corpus.json deserialized to null.");

        Validate(document);
        return document;
    }

    /// <summary>
    /// Enforces the structural invariants the theories rely on, so that a field omitted from
    /// <c>corpus.json</c> fails loudly at load time instead of silently defaulting to a value that
    /// makes an assertion pass vacuously (e.g. a missing block-map expectation or missing
    /// <c>expected</c> block).
    /// </summary>
    private static void Validate(CorpusDocument document)
    {
        if (document.Fixtures.Count != document.Meta.FixtureCount)
        {
            throw new InvalidDataException(
                $"corpus.json meta.fixtureCount ({document.Meta.FixtureCount}) does not match the number " +
                $"of fixtures ({document.Fixtures.Count}).");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (CorpusFixture fx in document.Fixtures)
        {
            if (string.IsNullOrWhiteSpace(fx.Id))
            {
                throw new InvalidDataException("A corpus fixture has a missing or empty 'id'.");
            }

            if (!seen.Add(fx.Id))
            {
                throw new InvalidDataException($"Duplicate corpus fixture id '{fx.Id}'.");
            }

            if (fx.LooseDir is null && fx.PackedFile is null)
            {
                throw new InvalidDataException($"Fixture '{fx.Id}' declares neither a loose nor a packed layout.");
            }

            bool isBundle = string.Equals(fx.Kind, "bundle", StringComparison.Ordinal);

            if (isBundle)
            {
                if (fx.PackedFile is null)
                {
                    throw new InvalidDataException($"Bundle fixture '{fx.Id}' is missing its packed layout.");
                }
            }
            else
            {
                // Every non-bundle fixture ships both layouts so the loose and packed readers are
                // differentially tested against the same expected values; a missing path would
                // silently drop the fixture from one of the theories.
                if (fx.LooseDir is null || fx.PackedFile is null)
                {
                    throw new InvalidDataException(
                        $"Non-bundle fixture '{fx.Id}' must declare both a loose and a packed layout.");
                }

                if (fx.Expected is null)
                {
                    throw new InvalidDataException($"Non-bundle fixture '{fx.Id}' is missing its 'expected' values.");
                }
            }

            if (fx.LooseDir is not null && fx.BlockMapValidLoose is null)
            {
                throw new InvalidDataException(
                    $"Fixture '{fx.Id}' has a loose layout but no 'blockMapValidLoose' expectation.");
            }

            if (fx.PackedFile is not null && !isBundle && fx.BlockMapValidPacked is null)
            {
                throw new InvalidDataException(
                    $"Fixture '{fx.Id}' has a packed package layout but no 'blockMapValidPacked' expectation.");
            }
        }
    }
}
