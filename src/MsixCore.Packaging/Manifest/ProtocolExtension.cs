namespace MsixCore.Packaging.Manifest;

/// <summary>
/// The <c>uap:Protocol</c> child of a <c>windows.protocol</c> extension: a URI scheme the app
/// handles, e.g. <c>myscheme:</c>.
/// </summary>
public sealed record ProtocolExtension : ExtensionPayload
{
    /// <summary>
    /// The scheme name, without the colon. The schema restricts it to
    /// <c>[a-z][-a-z0-9.+]*</c> — lower-case only.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>The localizable display name shown for the protocol, if declared.</summary>
    public string? DisplayName { get; init; }

    /// <summary>The package-relative logo shown for the protocol, if declared.</summary>
    public string? Logo { get; init; }

    /// <summary>The requested window size on activation (<c>default</c>, <c>useLess</c>, ...), if declared.</summary>
    public string? DesiredView { get; init; }

    /// <summary>Whether activation returns results to the caller (<c>none</c>, <c>always</c>, <c>optional</c>).</summary>
    public string? ReturnResults { get; init; }

    /// <summary>
    /// The parameter template passed on activation. Added by <c>uap3:Protocol</c>; absent from the
    /// base <c>uap:Protocol</c> form.
    /// </summary>
    public string? Parameters { get; init; }
}
