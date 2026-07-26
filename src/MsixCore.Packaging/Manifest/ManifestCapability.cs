namespace MsixCore.Packaging.Manifest;

/// <summary>
/// The structural category of a declared capability, derived from the element name and XML
/// namespace it was declared with.
/// </summary>
/// <remarks>
/// MSIX does not flag a capability as restricted with an attribute: the distinction is carried
/// entirely by the namespace of the declaring element. <c>runFullTrust</c>, for example, is only
/// valid as <c>&lt;rescap:Capability Name="runFullTrust"/&gt;</c>.
/// </remarks>
public enum CapabilityKind
{
    /// <summary>
    /// The category could not be determined — an unrecognised element name or an element from a
    /// namespace this library does not know about.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// A general-use capability that any package may declare: the foundation <c>Capability</c>
    /// element and its <c>uap</c>, <c>mobile</c>, and <c>iot</c> equivalents.
    /// </summary>
    General,

    /// <summary>A <c>DeviceCapability</c>: access to a class of hardware device.</summary>
    Device,

    /// <summary>
    /// A restricted capability (<c>rescap:Capability</c>). Store submission requires explicit
    /// approval; this is where <c>runFullTrust</c> lives.
    /// </summary>
    Restricted,

    /// <summary>
    /// A Windows capability (<c>wincap:Capability</c>), reserved for Microsoft-authored packages.
    /// Reported separately from <see cref="Restricted"/> because it is a different namespace with a
    /// different approval path, even though both are gated.
    /// </summary>
    Windows,

    /// <summary>
    /// A custom capability (<c>uap4:CustomCapability</c>): a publisher-defined capability whose name
    /// is a Store-issued, publisher-suffixed identifier.
    /// </summary>
    Custom,
}

/// <summary>
/// A single capability declared under <c>&lt;Capabilities&gt;</c>, categorized by the element and
/// namespace that declared it.
/// </summary>
public sealed record ManifestCapability
{
    /// <summary>The capability name (the <c>Name</c> attribute).</summary>
    public required string Name { get; init; }

    /// <summary>The category this capability was declared under.</summary>
    public required CapabilityKind Kind { get; init; }

    /// <summary>
    /// The XML namespace URI of the declaring element, or an empty string when the element was
    /// unqualified. Preserved so that a caller can distinguish schema revisions (for example
    /// <c>uap</c> from <c>uap7</c>) that <see cref="Kind"/> deliberately collapses.
    /// </summary>
    public required string Namespace { get; init; }

    /// <summary>
    /// The devices constrained by a <c>DeviceCapability</c>. Empty for every other element, and also
    /// empty for an unconstrained <c>DeviceCapability</c> that declares no <c>Device</c> children.
    /// </summary>
    public IReadOnlyList<CapabilityDevice> Devices { get; init; } = [];
}

/// <summary>
/// A <c>Device</c> child of a <c>DeviceCapability</c>, narrowing the capability to a specific device
/// interface class.
/// </summary>
public sealed record CapabilityDevice
{
    /// <summary>The device identifier (typically <c>any</c> or a device interface class GUID).</summary>
    public required string Id { get; init; }

    /// <summary>
    /// The device functions requested, from the <c>Type</c> attribute of each <c>Function</c> child.
    /// </summary>
    public IReadOnlyList<string> Functions { get; init; } = [];
}
