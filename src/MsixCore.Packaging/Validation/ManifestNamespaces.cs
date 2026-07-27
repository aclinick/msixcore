namespace MsixCore.Packaging.Validation;

/// <summary>
/// The registry of XML namespaces the MSIX manifest schemas define, mapped to the schema document
/// that defines each one.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately a data table rather than a set of parser branches. Every Windows release
/// adds namespace revisions, and the only maintenance a new revision should need is one row.
/// </para>
/// <para>
/// The schema paths are relative to <c>resources/AppxPackaging/</c> in Microsoft's
/// <c>msix-packaging</c> repository and are recorded here so that XSD-backed validation can be added
/// later without re-deriving the mapping. They are not resolved today.
/// </para>
/// <para>
/// The set mirrors <c>cmake/msix_resources.cmake</c> at msix-packaging commit
/// <c>efeb9dad695a2</c>. Two details are worth noting: there is no
/// <c>.../uap/windows10/9</c> — the sequence jumps from <c>/8</c> to <c>/10</c>, with no comment
/// upstream explaining why — and there is no Xbox namespace, despite one being widely assumed.
/// </para>
/// </remarks>
public static class ManifestNamespaces
{
    private const string Schema2015 = "Manifest/Schema/2015/";
    private const string Schema2016 = "Manifest/Schema/2016/";
    private const string Schema2017 = "Manifest/Schema/2017/";
    private const string Schema2018 = "Manifest/Schema/2018/";
    private const string Schema2019 = "Manifest/Schema/2019/";
    private const string Schema2020 = "Manifest/Schema/2020/";
    private const string Schema2021 = "Manifest/Schema/2021/";

    private static readonly Dictionary<string, string> PackageNamespaces = new(StringComparer.Ordinal)
    {
        ["http://schemas.microsoft.com/appx/manifest/types"] = Schema2015 + "AppxManifestTypes.xsd",
        ["http://schemas.microsoft.com/appx/manifest/foundation/windows10"] = Schema2015 + "FoundationManifestSchema.xsd",
        ["http://schemas.microsoft.com/appx/manifest/foundation/windows10/2"] = Schema2015 + "FoundationManifestSchema_v2.xsd",
        ["http://schemas.microsoft.com/appx/manifest/uap/windows10"] = Schema2015 + "UapManifestSchema.xsd",
        ["http://schemas.microsoft.com/appx/manifest/uap/windows10/2"] = Schema2015 + "UapManifestSchema_v2.xsd",
        ["http://schemas.microsoft.com/appx/manifest/uap/windows10/3"] = Schema2015 + "UapManifestSchema_v3.xsd",
        ["http://schemas.microsoft.com/appx/manifest/uap/windows10/4"] = Schema2016 + "UapManifestSchema_v4.xsd",
        ["http://schemas.microsoft.com/appx/manifest/uap/windows10/5"] = Schema2017 + "UapManifestSchema_v5.xsd",
        ["http://schemas.microsoft.com/appx/manifest/uap/windows10/6"] = Schema2017 + "UapManifestSchema_v6.xsd",
        ["http://schemas.microsoft.com/appx/manifest/uap/windows10/7"] = Schema2018 + "UapManifestSchema_v7.xsd",
        ["http://schemas.microsoft.com/appx/manifest/uap/windows10/8"] = Schema2018 + "UapManifestSchema_v8.xsd",
        ["http://schemas.microsoft.com/appx/manifest/uap/windows10/10"] = Schema2019 + "UapManifestSchema_v10.xsd",
        ["http://schemas.microsoft.com/appx/manifest/uap/windows10/11"] = Schema2019 + "UapManifestSchema_v11.xsd",
        ["http://schemas.microsoft.com/appx/manifest/uap/windows10/12"] = Schema2020 + "UapManifestSchema_v12.xsd",
        ["http://schemas.microsoft.com/appx/manifest/uap/windows10/13"] = Schema2021 + "UapManifestSchema_v13.xsd",
        ["http://schemas.microsoft.com/appx/2014/phone/manifest"] = Schema2015 + "AppxPhoneManifestSchema2014.xsd",
        ["http://schemas.microsoft.com/appx/manifest/foundation/windows10/windowscapabilities"] = Schema2015 + "WindowsCapabilitiesManifestSchema.xsd",
        ["http://schemas.microsoft.com/appx/manifest/foundation/windows10/windowscapabilities/2"] = Schema2015 + "WindowsCapabilitiesManifestSchema_v2.xsd",
        ["http://schemas.microsoft.com/appx/manifest/foundation/windows10/windowscapabilities/3"] = Schema2016 + "WindowsCapabilitiesManifestSchema_v3.xsd",
        ["http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"] = Schema2015 + "RestrictedCapabilitiesManifestSchema.xsd",
        ["http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities/2"] = Schema2015 + "RestrictedCapabilitiesManifestSchema_v2.xsd",
        ["http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities/3"] = Schema2016 + "RestrictedCapabilitiesManifestSchema_v3.xsd",
        ["http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities/4"] = Schema2017 + "RestrictedCapabilitiesManifestSchema_v4.xsd",
        ["http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities/5"] = Schema2018 + "RestrictedCapabilitiesManifestSchema_v5.xsd",
        ["http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities/6"] = Schema2018 + "RestrictedCapabilitiesManifestSchema_v6.xsd",
        ["http://schemas.microsoft.com/appx/manifest/mobile/windows10"] = Schema2015 + "MobileManifestSchema.xsd",
        ["http://schemas.microsoft.com/appx/manifest/iot/windows10"] = Schema2015 + "IotManifestSchema.xsd",
        ["http://schemas.microsoft.com/appx/manifest/iot/windows10/2"] = Schema2017 + "IotManifestSchema_v2.xsd",
        ["http://schemas.microsoft.com/appx/manifest/holographic/windows10"] = Schema2015 + "HolographicManifestSchema.xsd",
        ["http://schemas.microsoft.com/appx/manifest/serverpreview/windows10"] = Schema2015 + "ServerManifestSchema.xsd",
        ["http://schemas.microsoft.com/appx/manifest/desktop/windows10"] = Schema2015 + "DesktopManifestSchema.xsd",
        ["http://schemas.microsoft.com/appx/manifest/desktop/windows10/2"] = Schema2016 + "DesktopManifestSchema_v2.xsd",
        ["http://schemas.microsoft.com/appx/manifest/desktop/windows10/3"] = Schema2017 + "DesktopManifestSchema_v3.xsd",
        ["http://schemas.microsoft.com/appx/manifest/desktop/windows10/4"] = Schema2017 + "DesktopManifestSchema_v4.xsd",
        ["http://schemas.microsoft.com/appx/manifest/desktop/windows10/5"] = Schema2018 + "DesktopManifestSchema_v5.xsd",
        ["http://schemas.microsoft.com/appx/manifest/desktop/windows10/6"] = Schema2018 + "DesktopManifestSchema_v6.xsd",
        ["http://schemas.microsoft.com/appx/manifest/desktop/windows10/7"] = Schema2020 + "DesktopManifestSchema_v7.xsd",
        ["http://schemas.microsoft.com/appx/manifest/desktop/windows10/8"] = Schema2021 + "DesktopManifestSchema_v8.xsd",
        ["http://schemas.microsoft.com/appx/manifest/com/windows10"] = Schema2015 + "ComManifestSchema.xsd",
        ["http://schemas.microsoft.com/appx/manifest/com/windows10/2"] = Schema2017 + "ComManifestSchema_v2.xsd",
        ["http://schemas.microsoft.com/appx/manifest/com/windows10/3"] = Schema2019 + "ComManifestSchema_v3.xsd",
        ["http://schemas.microsoft.com/appx/manifest/com/windows10/4"] = Schema2020 + "ComManifestSchema_v4.xsd",
        ["http://schemas.microsoft.com/appx/manifest/cloudfiles/windows10"] = Schema2019 + "CloudFilesManifestSchema.xsd",
        ["http://schemas.microsoft.com/appx/manifest/preview/windows10/msixappcompatsupport"] = Schema2019 + "PreviewManifestSchema_MsixAppCompatSupport.xsd",
        ["http://schemas.microsoft.com/appx/manifest/preview/windows10/msixappcompatsupport/3"] = Schema2020 + "PreviewManifestSchema_MsixAppCompatSupport_v3.xsd",
        ["http://schemas.microsoft.com/appx/manifest/deployment/windows10"] = Schema2020 + "DeploymentManifestSchema.xsd",
        ["http://schemas.microsoft.com/appx/manifest/virtualization/windows10"] = Schema2020 + "VirtualizationManifestSchema.xsd",
    };

    private static readonly Dictionary<string, string> BundleNamespaces = new(StringComparer.Ordinal)
    {
        ["http://schemas.microsoft.com/appx/manifest/types"] = Schema2015 + "AppxManifestTypes.xsd",
        // The 2014 schema document declares the 2013 namespace; the mismatch is upstream's, not a typo.
        ["http://schemas.microsoft.com/appx/2013/bundle"] = Schema2015 + "BundleManifestSchema2014.xsd",
        ["http://schemas.microsoft.com/appx/2016/bundle"] = Schema2016 + "BundleManifestSchema2016.xsd",
        ["http://schemas.microsoft.com/appx/2017/bundle"] = Schema2017 + "BundleManifestSchema2017.xsd",
        ["http://schemas.microsoft.com/appx/2018/bundle"] = Schema2018 + "BundleManifestSchema2018.xsd",
        ["http://schemas.microsoft.com/appx/2019/bundle"] = Schema2019 + "BundleManifestSchema2019.xsd",
    };

    /// <summary>The package-manifest namespaces, mapped to the schema document that defines each.</summary>
    public static IReadOnlyDictionary<string, string> Package => PackageNamespaces;

    /// <summary>The bundle-manifest namespaces, mapped to the schema document that defines each.</summary>
    public static IReadOnlyDictionary<string, string> Bundle => BundleNamespaces;

    /// <summary>Whether <paramref name="namespaceUri"/> is a known package-manifest namespace.</summary>
    public static bool IsKnownPackageNamespace(string namespaceUri) =>
        PackageNamespaces.ContainsKey(namespaceUri);

    /// <summary>Whether <paramref name="namespaceUri"/> is a known bundle-manifest namespace.</summary>
    public static bool IsKnownBundleNamespace(string namespaceUri) =>
        BundleNamespaces.ContainsKey(namespaceUri);
}
