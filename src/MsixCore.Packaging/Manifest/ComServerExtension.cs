namespace MsixCore.Packaging.Manifest;

/// <summary>
/// The <c>com:ComServer</c> child of a <c>windows.comServer</c> extension: the COM classes the
/// package registers.
/// </summary>
public sealed record ComServerExtension : ExtensionPayload
{
    /// <summary>Out-of-process (<c>.exe</c>) servers declared.</summary>
    public IReadOnlyList<ComExeServer> ExeServers { get; init; } = [];

    /// <summary>Surrogate-hosted (in-proc DLL run in a surrogate) servers declared.</summary>
    public IReadOnlyList<ComSurrogateServer> SurrogateServers { get; init; } = [];

    /// <summary>Programmatic identifiers mapped to CLSIDs.</summary>
    public IReadOnlyList<ComProgId> ProgIds { get; init; } = [];
}

/// <summary>A <c>com:ExeServer</c>: an out-of-process COM server executable.</summary>
public sealed record ComExeServer
{
    /// <summary>The package-relative path to the server executable.</summary>
    public required string Executable { get; init; }

    /// <summary>The command line passed when the server is activated, if declared.</summary>
    public string? Arguments { get; init; }

    /// <summary>The display name of the server, if declared.</summary>
    public string? DisplayName { get; init; }

    /// <summary>The classes the executable serves. The schema requires at least one.</summary>
    public IReadOnlyList<ComClass> Classes { get; init; } = [];
}

/// <summary>A <c>com:SurrogateServer</c>: an in-process server hosted in a surrogate process.</summary>
public sealed record ComSurrogateServer
{
    /// <summary>The display name of the surrogate, if declared.</summary>
    public string? DisplayName { get; init; }

    /// <summary>The AppID GUID of the surrogate, if declared.</summary>
    public string? AppId { get; init; }

    /// <summary>The classes the surrogate serves. The schema requires at least one.</summary>
    public IReadOnlyList<ComClass> Classes { get; init; } = [];
}

/// <summary>A <c>com:Class</c> registration.</summary>
public sealed record ComClass
{
    /// <summary>
    /// The CLSID. The schema requires the bare hyphenated GUID form with no surrounding braces; it
    /// is kept as written rather than parsed to <see cref="Guid"/> so that a malformed manifest can
    /// still be reported rather than rejected.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>The display name of the class, if declared.</summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// The in-process server path. Declared by <c>SurrogateServer/Class</c> only; always
    /// <see langword="null"/> for a class under an <see cref="ComExeServer"/>.
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// The apartment model (<c>STA</c>, <c>MTA</c>, <c>both</c>, <c>neutral</c>). Required on a
    /// <c>SurrogateServer/Class</c>, and not declared at all on an <c>ExeServer/Class</c>.
    /// </summary>
    public string? ThreadingModel { get; init; }

    /// <summary>The ProgID associated with the class, if declared.</summary>
    public string? ProgId { get; init; }
}

/// <summary>A <c>com:ProgId</c>: a programmatic identifier mapped to a CLSID.</summary>
public sealed record ComProgId
{
    /// <summary>The ProgID, e.g. <c>Contoso.Document.1</c>.</summary>
    public required string Id { get; init; }

    /// <summary>The CLSID the ProgID resolves to, if declared.</summary>
    public string? Clsid { get; init; }
}
