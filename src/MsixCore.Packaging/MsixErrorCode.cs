namespace MsixCore.Packaging;

/// <summary>
/// Stable, machine-readable categories for MSIX format and package-store failures.
/// Member names are the serialization contract; no explicit numeric values are assigned. This
/// deliberately avoids numeric aliases such as the upstream <c>FileRead</c>/<c>FileWrite</c> collision.
/// </summary>
public enum MsixErrorCode
{
    /// <summary>
    /// Reserved fallback for an exception that carries no category. Declared first so that
    /// <c>default(MsixErrorCode)</c> is <see cref="Unknown"/> rather than a specific, misleading
    /// category. Never assigned at a throw site.
    /// </summary>
    Unknown,

    /// <summary>Malformed ZIP structures, including invalid or inconsistent directory records.</summary>
    ZipStructure,

    /// <summary>An OPC part name is invalid, unsafe, non-canonical, or duplicated.</summary>
    PartName,

    /// <summary>A required package footprint part is absent.</summary>
    FootprintMissing,

    /// <summary>The content-types part is invalid or does not cover a package part.</summary>
    ContentTypes,

    /// <summary>The block map violates MSIX block-map semantics.</summary>
    BlockMapSemantics,

    /// <summary>The application manifest violates MSIX manifest semantics.</summary>
    ManifestSemantics,

    /// <summary>A bundle manifest or package/bundle kind relationship is invalid.</summary>
    BundleSemantics,

    /// <summary>The package signature or its APPX digest table is malformed.</summary>
    SignatureFormat,

    /// <summary>XML is not well formed or contains a prohibited DTD or DOCTYPE.</summary>
    Xml,

    /// <summary>Deployment or package-store state is invalid or corrupt.</summary>
    PackageStore,
}
