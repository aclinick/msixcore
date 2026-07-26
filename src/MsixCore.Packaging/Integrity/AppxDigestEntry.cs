namespace MsixCore.Packaging.Integrity;

/// <summary>A single tag → SHA-256 digest pair from the APPX digest table.</summary>
public sealed record AppxDigestEntry
{
    /// <summary>The 4-byte tag identifying which package component this digest covers.</summary>
    public required AppxDigestTag Tag { get; init; }

    /// <summary>The 32-byte SHA-256 digest value.</summary>
    public required ReadOnlyMemory<byte> Digest { get; init; }
}
