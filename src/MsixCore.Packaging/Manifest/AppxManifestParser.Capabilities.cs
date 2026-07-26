using System.Xml.Linq;

namespace MsixCore.Packaging.Manifest;

public static partial class AppxManifestParser
{
    private const string FoundationNamespace =
        "http://schemas.microsoft.com/appx/manifest/foundation/windows10";

    private const string RestrictedCapabilitiesNamespace =
        FoundationNamespace + "/restrictedcapabilities";

    private const string WindowsCapabilitiesNamespace =
        FoundationNamespace + "/windowscapabilities";

    private const string UapNamespace =
        "http://schemas.microsoft.com/appx/manifest/uap/windows10";

    private const string MobileNamespace =
        "http://schemas.microsoft.com/appx/manifest/mobile/windows10";

    private const string IotNamespace =
        "http://schemas.microsoft.com/appx/manifest/iot/windows10";

    private const string Uap4Namespace = UapNamespace + "/4";

    /// <summary>
    /// Parses <c>&lt;Capabilities&gt;</c>, categorizing each declaration by its element name and
    /// namespace.
    /// </summary>
    /// <remarks>
    /// Unlike the rest of the parser, this cannot be namespace-blind: MSIX carries the
    /// general/restricted/Windows distinction purely in the namespace of the declaring element, so
    /// <c>rescap:Capability</c> and <c>uap:Capability</c> must be told apart. Nothing is dropped for
    /// being unfamiliar, though: an element this library does not recognise is still reported, with
    /// <see cref="CapabilityKind.Unknown"/> and its namespace intact, as long as it carries a
    /// <c>Name</c>. Only the recognised element names are held to the schema's requirement that
    /// <c>Name</c> be present.
    /// </remarks>
    private static List<ManifestCapability> ParseCapabilities(XElement root)
    {
        XElement? capabilities = root.ElementByLocalName("Capabilities");
        if (capabilities is null)
        {
            return [];
        }

        var result = new List<ManifestCapability>();
        foreach (XElement child in capabilities.Elements())
        {
            bool recognised = IsCapabilityElement(child.Name.LocalName);

            // An unrecognised element can only come from a schema revision this library has not
            // seen. It is reported when it looks like a capability (it has a Name) and ignored
            // otherwise, exactly as the flat name list has always behaved -- rejecting it would fail
            // packages that are perfectly valid against a newer schema.
            string? name = recognised
                ? RequiredAttribute(child, "Name")
                : NullIfEmpty(child.AttributeValue("Name")?.Trim());
            if (name is null)
            {
                continue;
            }

            CapabilityKind kind = ClassifyCapability(child);
            result.Add(new ManifestCapability
            {
                Name = name,
                Kind = kind,
                Namespace = child.Name.NamespaceName,
                // Only a foundation DeviceCapability is parsed as one: a foreign element that merely
                // borrows the local name must not be held to foundation's Device/Function rules.
                Devices = kind == CapabilityKind.Device ? ParseCapabilityDevices(child) : [],
            });
        }

        return result;
    }

    private static bool IsCapabilityElement(string localName) => localName switch
    {
        "Capability" or "DeviceCapability" or "CustomCapability" => true,
        _ => false,
    };

    private static CapabilityKind ClassifyCapability(XElement element)
    {
        string ns = element.Name.NamespaceName;

        // The element name alone is not enough: an element that merely borrows a familiar local name
        // from a foreign namespace must not inherit its semantics.
        switch (element.Name.LocalName)
        {
            case "DeviceCapability":
                return ns == FoundationNamespace ? CapabilityKind.Device : CapabilityKind.Unknown;
            case "CustomCapability":
                // Pinned to uap4, the only revision that declares CustomCapability. A later revision
                // may well add its own, but its semantics would be unconfirmed, and Unknown is the
                // honest answer for a declaration this library has not verified.
                return ns == Uap4Namespace ? CapabilityKind.Custom : CapabilityKind.Unknown;
            case "Capability":
                break;
            default:
                return CapabilityKind.Unknown;
        }

        if (ns == RestrictedCapabilitiesNamespace)
        {
            return CapabilityKind.Restricted;
        }

        if (ns == WindowsCapabilitiesNamespace)
        {
            return CapabilityKind.Windows;
        }

        if (ns == FoundationNamespace
            || IsRevisionOf(ns, UapNamespace)
            || IsRevisionOf(ns, MobileNamespace)
            || IsRevisionOf(ns, IotNamespace))
        {
            return CapabilityKind.General;
        }

        return CapabilityKind.Unknown;
    }

    /// <summary>
    /// Determines whether <paramref name="ns"/> is <paramref name="baseNamespace"/> or one of its
    /// numbered revisions (for example <c>.../uap/windows10/7</c>), so that a capability from a
    /// schema revision released after this library still classifies correctly.
    /// </summary>
    private static bool IsRevisionOf(string ns, string baseNamespace)
    {
        if (ns == baseNamespace)
        {
            return true;
        }

        if (ns.Length <= baseNamespace.Length + 1
            || !ns.StartsWith(baseNamespace, StringComparison.Ordinal)
            || ns[baseNamespace.Length] != '/')
        {
            return false;
        }

        for (int i = baseNamespace.Length + 1; i < ns.Length; i++)
        {
            if (!char.IsAsciiDigit(ns[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static List<CapabilityDevice> ParseCapabilityDevices(XElement element)
    {
        var devices = new List<CapabilityDevice>();
        foreach (XElement device in element.ElementsByLocalName("Device"))
        {
            List<string> functions = device.ElementsByLocalName("Function")
                .Select(static function => RequiredAttribute(function, "Type"))
                .ToList();

            // The schema requires 1..100 Function children on every Device.
            if (functions.Count == 0)
            {
                throw MsixError.Format(MsixErrorCode.ManifestSemantics,
                    "A 'Device' element must declare at least one 'Function' child.");
            }

            devices.Add(new CapabilityDevice
            {
                Id = RequiredAttribute(device, "Id"),
                Functions = functions,
            });
        }

        return devices;
    }

    /// <summary>
    /// Projects the categorized capabilities onto the flat name list that has been part of the
    /// public surface since the first release, preserving document order and de-duplicating.
    /// </summary>
    private static List<string> ToCapabilityNames(List<ManifestCapability> capabilities)
    {
        var names = new List<string>(capabilities.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (ManifestCapability capability in capabilities)
        {
            if (seen.Add(capability.Name))
            {
                names.Add(capability.Name);
            }
        }

        return names;
    }
}
