namespace MsixCore.Packaging.Manifest;

/// <summary>
/// An <c>&lt;Extension&gt;</c> declared by an application or by the package itself.
/// </summary>
/// <remarks>
/// <para>
/// Extensions are how a package declares its integration points with the OS: file type
/// associations, URI protocol handlers, console aliases, startup tasks, COM servers and so on.
/// Every extension carries a <see cref="Category"/> and, for the categories msixcore recognises, a
/// strongly-typed <see cref="Payload"/> holding the category's child element.
/// </para>
/// <para>
/// The schemas declare the category attribute as a closed enumeration per namespace, but msixcore
/// models it as a plain string. The enumeration grows with every schema revision (<c>uap</c> alone
/// spans base through <c>uap17</c>), so treating an unfamiliar category as a parse failure would
/// make this library reject packages that are valid against a newer schema than it was built
/// against. Unrecognised categories are reported with a <see langword="null"/> payload instead;
/// schema conformance is the job of manifest validation, not of the object model.
/// </para>
/// </remarks>
public sealed record AppExtension
{
    /// <summary>
    /// The extension category, e.g. <c>windows.fileTypeAssociation</c>. Always present — the
    /// attribute is required in every schema variant.
    /// </summary>
    public required string Category { get; init; }

    /// <summary>The package-relative executable that services the extension, if declared.</summary>
    public string? Executable { get; init; }

    /// <summary>The entry point (a runtime class name, or <c>Windows.FullTrustApplication</c>), if declared.</summary>
    public string? EntryPoint { get; init; }

    /// <summary>The start page servicing the extension, for web-hosted apps.</summary>
    public string? StartPage { get; init; }

    /// <summary>The resource group used to batch activations, if declared.</summary>
    public string? ResourceGroup { get; init; }

    /// <summary>The activation runtime type, if declared.</summary>
    public string? RuntimeType { get; init; }

    /// <summary>
    /// The parsed child element for a recognised category, or <see langword="null"/> when the
    /// category is unrecognised or declares no child element.
    /// </summary>
    public ExtensionPayload? Payload { get; init; }
}

/// <summary>
/// Base type for the strongly-typed child element of a recognised <see cref="AppExtension"/>.
/// </summary>
/// <remarks>
/// Modelled as a closed-ish hierarchy of records rather than one wide type so that a caller can
/// pattern-match on the category it cares about without every unrelated property being present and
/// null.
/// </remarks>
public abstract record ExtensionPayload;
