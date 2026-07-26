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

    /// <summary>
    /// Parses <c>&lt;Capabilities&gt;</c>, categorizing each declaration by its element name and
    /// namespace.
    /// </summary>
    /// <remarks>
    /// Unlike the rest of the parser, this cannot be namespace-blind: MSIX carries the
    /// general/restricted/Windows distinction purely in the namespace of the declaring element, so
    /// <c>rescap:Capability</c> and <c>uap:Capability</c> must be told apart. Element <em>names</em>
    /// are still matched by local name, so a capability from a schema revision this library has not
    /// seen is still captured — with <see cref="CapabilityKind.Unknown"/> and its namespace intact —
    /// rather than dropped.
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
            // Anything that is not a capability declaration is ignored: the container's content is
            // fixed by the schema, so an unrecognised element name can only come from a newer
            // revision, and rejecting it would fail packages that are perfectly valid.
            if (!IsCapabilityElement(child.Name.LocalName))
            {
                continue;
            }

            string name = RequiredAttribute(child, "Name");
            CapabilityKind kind = ClassifyCapability(child);
            result.Add(new ManifestCapability
            {
                Name = name,
                Kind = kind,
                Namespace = child.Name.NamespaceName,
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
        switch (element.Name.LocalName)
        {
            case "DeviceCapability":
                return CapabilityKind.Device;
            case "CustomCapability":
                return CapabilityKind.Custom;
        }

        string ns = element.Name.NamespaceName;
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
            devices.Add(new CapabilityDevice
            {
                Id = RequiredAttribute(device, "Id"),
                Functions = device.ElementsByLocalName("Function")
                    .Select(static function => RequiredAttribute(function, "Type"))
                    .ToList(),
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
