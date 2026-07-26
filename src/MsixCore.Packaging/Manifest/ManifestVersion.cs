using System.Globalization;

namespace MsixCore.Packaging.Manifest;

/// <summary>
/// Parsing for the MSIX four-part version quad (<c>ST_VersionQuad</c>): exactly four dot-separated
/// components, each an integer in the range 0 to 65535. This is stricter than <see cref="Version"/>,
/// which accepts two- and three-part values and components up to <see cref="int.MaxValue"/>.
/// </summary>
internal static class ManifestVersion
{
    private const int MaxComponent = 65535;

    /// <summary>Attempts to parse a value as an MSIX four-part version quad.</summary>
    /// <param name="value">The version text (e.g. <c>1.2.3.4</c>).</param>
    /// <param name="version">The parsed version when successful.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> is a valid MSIX version.</returns>
    public static bool TryParse(string? value, out Version version)
    {
        version = new Version(0, 0, 0, 0);

        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        string[] parts = value.Split('.');
        if (parts.Length != 4)
        {
            return false;
        }

        Span<int> components = stackalloc int[4];
        for (int i = 0; i < 4; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out int component)
                || component > MaxComponent)
            {
                return false;
            }

            components[i] = component;
        }

        version = new Version(components[0], components[1], components[2], components[3]);
        return true;
    }

    /// <summary>Parses a value as an MSIX four-part version quad, throwing on failure.</summary>
    /// <param name="value">The version text.</param>
    /// <param name="context">A short description used in the error message (e.g. <c>Identity</c>).</param>
    /// <param name="errorCode">The category appropriate to the containing manifest kind.</param>
    /// <returns>The parsed version.</returns>
    /// <exception cref="InvalidDataException"><paramref name="value"/> is not a valid MSIX version.</exception>
    public static Version Parse(
        string? value,
        string context,
        MsixErrorCode errorCode)
    {
        if (!TryParse(value, out Version version))
        {
            throw MsixError.Format(errorCode,
                $"{context} has an invalid MSIX version '{value}'. Expected four components, each 0-65535.");
        }

        return version;
    }
}
