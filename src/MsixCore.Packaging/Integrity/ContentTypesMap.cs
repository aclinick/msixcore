namespace MsixCore.Packaging.Integrity;

/// <summary>The canonical declarations parsed from an OPC <c>[Content_Types].xml</c> part.</summary>
public sealed record ContentTypesMap
{
    /// <summary>Content types keyed by file extension without a leading dot.</summary>
    public required IReadOnlyDictionary<string, string> Defaults { get; init; }

    /// <summary>Content types keyed by canonical, package-root-relative part name.</summary>
    public required IReadOnlyDictionary<string, string> Overrides { get; init; }

    /// <summary>Returns whether a canonical part name is covered by an override or extension default.</summary>
    public bool Covers(string partName)
    {
        ArgumentException.ThrowIfNullOrEmpty(partName);
        if (Overrides.ContainsKey(partName))
        {
            return true;
        }

        string fileName = partName[(partName.LastIndexOf('/') + 1)..];
        int dot = fileName.LastIndexOf('.');
        return dot >= 0
            && dot < fileName.Length - 1
            && Defaults.ContainsKey(fileName[(dot + 1)..]);
    }
}
