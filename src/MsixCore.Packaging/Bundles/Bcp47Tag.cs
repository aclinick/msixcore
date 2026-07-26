namespace MsixCore.Packaging.Bundles;

/// <summary>
/// How closely a resource package's language qualifier matches a requested language.
/// </summary>
/// <remarks>Ordered from weakest to strongest so callers can compare with <c>&gt;</c>.</remarks>
public enum LanguageMatch
{
    /// <summary>The tags do not match; the resource does not apply.</summary>
    None = 0,

    /// <summary>
    /// A sibling or child region of the requested language (requested <c>fr-FR</c>, resource
    /// <c>fr-CA</c>; or requested <c>fr</c>, resource <c>fr-FR</c>). Usable only as a fallback.
    /// </summary>
    Variant = 1,

    /// <summary>One side is the undetermined language <c>und</c>, which matches anything.</summary>
    Undetermined = 2,

    /// <summary>
    /// The resource is the region-neutral parent of the request (requested <c>fr-FR</c>, resource
    /// <c>fr</c>). Directly usable.
    /// </summary>
    Neutral = 3,

    /// <summary>Language, script, and region are all equal.</summary>
    Exact = 4,
}

/// <summary>
/// The subset of BCP-47 used by MSIX resource qualifiers: primary language, optional script, and
/// optional region.
/// </summary>
/// <remarks>
/// <para>
/// This deliberately mirrors the upstream MSIX SDK's <c>Bcp47Tag</c>, which supports only these
/// three subtags. Variants, extensions, and private-use subtags are parsed off and ignored rather
/// than rejected, so a tag such as <c>de-DE-1901</c> still matches as <c>de-DE</c>.
/// </para>
/// <para>
/// Full Windows Resource Management System matching (macro-regions, preferred regions, orthographic
/// affinity, suppress-script inference, and language-list position weighting) is <b>not</b>
/// implemented. See <c>docs/bundle-applicability.md</c> for the exact divergences.
/// </para>
/// </remarks>
public readonly record struct Bcp47Tag
{
    private const string UndeterminedLanguage = "und";

    private Bcp47Tag(string language, string script, string region)
    {
        Language = language;
        Script = script;
        Region = region;
    }

    /// <summary>The primary language subtag, lowercased (e.g. <c>fr</c>).</summary>
    public string Language { get; }

    /// <summary>The script subtag, lowercased (e.g. <c>hans</c>), or empty when absent.</summary>
    public string Script { get; }

    /// <summary>The region subtag, lowercased (e.g. <c>fr</c> from <c>fr-FR</c>), or empty when absent.</summary>
    public string Region { get; }

    /// <summary>Whether the primary language is the undetermined language <c>und</c>.</summary>
    public bool IsUndetermined => Language == UndeterminedLanguage;

    /// <summary>Whether the tag carries no region subtag, making it region-neutral.</summary>
    public bool IsRegionNeutral => Region.Length == 0;

    /// <summary>Parses a BCP-47 language tag.</summary>
    /// <param name="tag">The tag to parse (e.g. <c>zh-Hans-CN</c>). Case-insensitive.</param>
    /// <returns>
    /// The parsed tag, or <see langword="null"/> when <paramref name="tag"/> is null, empty, or
    /// whitespace. Malformed tags are not rejected; unrecognized subtags are ignored.
    /// </returns>
    public static Bcp47Tag? Parse(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        string[] parts = tag.Trim().Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        string language = parts[0].ToLowerInvariant();
        string script = string.Empty;
        string region = string.Empty;

        int index = 1;
        if (index < parts.Length && IsScript(parts[index]))
        {
            script = parts[index].ToLowerInvariant();
            index++;
        }

        if (index < parts.Length && IsRegion(parts[index]))
        {
            region = parts[index].ToLowerInvariant();
        }

        // Chinese tags are conventionally written without a script even though the script is what
        // actually distinguishes them. Upstream normalizes these; without it zh-CN would not match
        // a zh-Hans resource. Ref: upstream ApplicabilityCommon.cpp.
        if (language == "zh" && script.Length == 0)
        {
            script = region switch
            {
                "cn" or "sg" => "hans",
                "hk" or "mo" or "tw" => "hant",
                _ => script,
            };
        }

        return new Bcp47Tag(language, script, region);
    }

    /// <summary>Compares a requested language against a resource package's language qualifier.</summary>
    /// <param name="requested">The language the caller wants.</param>
    /// <param name="offered">The language a resource package provides.</param>
    /// <returns>How strongly the offered language satisfies the request.</returns>
    public static LanguageMatch Compare(Bcp47Tag requested, Bcp47Tag offered)
    {
        // 'und' is a wildcard on either side. Upstream still reports a match when the scripts
        // differ, so a script mismatch does not eliminate an undetermined package.
        if (requested.IsUndetermined || offered.IsUndetermined)
        {
            return LanguageMatch.Undetermined;
        }

        if (requested.Language != offered.Language)
        {
            return LanguageMatch.None;
        }

        // A differing explicit script is a genuine mismatch: zh-Hans resources are not usable by a
        // zh-Hant reader. An absent script on either side is treated as compatible.
        if (requested.Script.Length != 0 && offered.Script.Length != 0 && requested.Script != offered.Script)
        {
            return LanguageMatch.None;
        }

        if (requested.Region == offered.Region)
        {
            return LanguageMatch.Exact;
        }

        // The resource is the region-neutral parent of the request (fr-FR wanted, fr offered) and is
        // directly usable. The reverse (fr wanted, fr-FR offered) and sibling regions (fr-CA) are
        // fallbacks only.
        return offered.IsRegionNeutral ? LanguageMatch.Neutral : LanguageMatch.Variant;
    }

    /// <summary>Returns the normalized tag text (e.g. <c>zh-hans-cn</c>).</summary>
    /// <returns>The normalized tag.</returns>
    public override string ToString()
    {
        string result = Language;
        if (Script.Length != 0)
        {
            result += "-" + Script;
        }

        if (Region.Length != 0)
        {
            result += "-" + Region;
        }

        return result;
    }

    private static bool IsScript(string part) =>
        part.Length == 4 && part.All(char.IsAsciiLetter);

    private static bool IsRegion(string part) =>
        (part.Length == 2 && part.All(char.IsAsciiLetter))
        || (part.Length == 3 && part.All(char.IsAsciiDigit));
}
