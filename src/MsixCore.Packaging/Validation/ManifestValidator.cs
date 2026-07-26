using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using MsixCore.Packaging.Manifest;

namespace MsixCore.Packaging.Validation;

/// <summary>
/// Checks a parsed manifest against the rules Windows enforces at deployment time but which the
/// parser deliberately does not: identifier form, package-type consistency, and version ranges.
/// </summary>
/// <remarks>
/// <para>
/// Parsing and validating are separate on purpose. <see cref="AppxManifestParser"/> is tolerant so
/// that a package built against a newer SDK can still be inspected; this type is where a caller opts
/// in to strictness. Nothing here rejects a manifest merely for being unfamiliar — unknown
/// namespaces are a warning.
/// </para>
/// <para>
/// This is a semantic validator, not an XSD validator. It does not check element ordering,
/// cardinality, or attribute presence, all of which the schema covers and the parser already
/// enforces where it must. See <c>docs/manifest-validation.md</c> for the rule list and the known
/// divergences from Windows.
/// </para>
/// </remarks>
public static partial class ManifestValidator
{
    /// <summary>Schema maximum for <c>Identity/@Publisher</c> (<c>ST_Publisher</c>).</summary>
    private const int MaxPublisherLength = 8192;

    private const int MinPackageNameLength = 3;
    private const int MaxPackageNameLength = 50;
    private const int MaxResourceIdLength = 30;
    private const int MaxApplicationIdLength = 64;

    /// <summary>DTD processing off and no resolver, to avoid XXE and entity-expansion attacks.</summary>
    private static readonly XmlReaderSettings SafeReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        CloseInput = false,
    };

    /// <summary>
    /// Names that cannot be used as identifiers because a package identifier becomes a directory
    /// name, and Windows still reserves the DOS device names at the filesystem level.
    /// </summary>
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".", "..", "con", "prn", "aux", "nul",
        "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
        "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
    };

    /// <summary>
    /// Prefixes that cannot start an identifier: a reserved device name followed by a period (the
    /// filesystem treats <c>con.txt</c> as the console), and <c>xn--</c>, which is reserved for
    /// punycode-encoded internationalized names.
    /// </summary>
    private static readonly string[] ReservedPrefixes =
        [.. ReservedNames.Where(n => n is not ("." or "..")).Select(n => n + "."), "xn--"];

    [GeneratedRegex(@"^[-.A-Za-z0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex(@"^([A-Za-z][A-Za-z0-9]*)(\.[A-Za-z][A-Za-z0-9]*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex ApplicationIdPattern();

    /// <summary>Validates the semantic rules that can be checked from the parsed manifest alone.</summary>
    /// <param name="manifest">The manifest to validate.</param>
    /// <returns>The issues found; see <see cref="ManifestValidationResult.IsValid"/>.</returns>
    public static ManifestValidationResult Validate(AppxManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        List<ManifestValidationIssue> issues = [];
        ValidateIdentity(manifest, issues);
        ValidateDependencies(manifest, issues);
        ValidatePackageType(manifest, issues);
        ValidateApplications(manifest, issues);
        ValidateCapabilities(manifest, issues);
        return new ManifestValidationResult(issues);
    }

    /// <summary>
    /// Validates the semantic rules and additionally checks every XML namespace the document uses
    /// against the known schema registry.
    /// </summary>
    /// <param name="manifest">The parsed manifest.</param>
    /// <param name="document">The XML the manifest was parsed from.</param>
    /// <returns>The issues found; see <see cref="ManifestValidationResult.IsValid"/>.</returns>
    public static ManifestValidationResult Validate(AppxManifest manifest, XDocument document)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(document);

        List<ManifestValidationIssue> issues = [.. Validate(manifest).Issues];
        ValidateNamespaces(document, issues);
        return new ManifestValidationResult(issues);
    }

    /// <summary>
    /// Parses a manifest from a stream and validates it, including the namespace check.
    /// </summary>
    /// <param name="manifestStream">A readable stream over an <c>AppxManifest.xml</c> document.</param>
    /// <returns>The issues found; see <see cref="ManifestValidationResult.IsValid"/>.</returns>
    /// <exception cref="InvalidDataException">The manifest is malformed or missing required data.</exception>
    public static ManifestValidationResult Validate(Stream manifestStream)
    {
        ArgumentNullException.ThrowIfNull(manifestStream);

        XDocument document;
        try
        {
            using XmlReader reader = XmlReader.Create(manifestStream, SafeReaderSettings);
            document = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw MsixError.Format(MsixErrorCode.Xml, "The manifest is not well-formed XML.", ex);
        }

        return Validate(AppxManifestParser.Parse(document), document);
    }

    private static void ValidateIdentity(AppxManifest manifest, List<ManifestValidationIssue> issues)
    {
        PackageIdentity identity = manifest.Identity;
        ValidateIdentifier(
            identity.Name,
            "Identity/@Name",
            MinPackageNameLength,
            MaxPackageNameLength,
            issues);

        if (identity.ResourceId.Length > 0)
        {
            ValidateIdentifier(identity.ResourceId, "Identity/@ResourceId", 1, MaxResourceIdLength, issues);
        }

        ValidatePublisher(identity.Publisher, issues);
    }

    private static void ValidateIdentifier(
        string value,
        string target,
        int minLength,
        int maxLength,
        List<ManifestValidationIssue> issues)
    {
        if (value.Length < minLength || value.Length > maxLength)
        {
            issues.Add(Error(
                ManifestValidationRule.IdentifierLength,
                target,
                $"'{value}' is {value.Length} characters; the schema allows {minLength} to {maxLength}."));
        }

        if (value.Length > 0 && !IdentifierPattern().IsMatch(value))
        {
            issues.Add(Error(
                ManifestValidationRule.IdentifierMalformed,
                target,
                $"'{value}' contains characters outside the allowed set (letters, digits, '.', '-')."));
            return;
        }

        if (ReservedName(value) is { } reason)
        {
            issues.Add(Error(ManifestValidationRule.IdentifierReserved, target, $"'{value}' {reason}"));
        }
    }

    private static string? ReservedName(string value)
    {
        if (value.Length == 0)
        {
            return null;
        }

        if (ReservedNames.Contains(value))
        {
            return "is a reserved name and cannot be used as an identifier.";
        }

        if (value.EndsWith('.'))
        {
            return "ends with a period, which is not allowed in an identifier.";
        }

        foreach (string prefix in ReservedPrefixes)
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return $"starts with the reserved prefix '{prefix}'.";
            }
        }

        return null;
    }

    private static void ValidatePublisher(string publisher, List<ManifestValidationIssue> issues)
    {
        const string Target = "Identity/@Publisher";

        // Length is checked first so a pathological value is rejected before any parsing.
        if (publisher.Length is 0 or > MaxPublisherLength)
        {
            issues.Add(Error(
                ManifestValidationRule.PublisherMalformed,
                Target,
                publisher.Length == 0
                    ? "The publisher is empty; a distinguished name is required."
                    : $"The publisher is {publisher.Length} characters; the schema allows at most {MaxPublisherLength}."));
            return;
        }

        try
        {
            var name = new X500DistinguishedName(publisher);

            // A syntactically parseable DN can still carry an empty value ("CN="), which Windows
            // rejects; the schema's DN pattern requires at least one character per attribute.
            foreach (X500RelativeDistinguishedName rdn in name.EnumerateRelativeDistinguishedNames())
            {
                if (rdn.GetSingleElementValue() is not { Length: > 0 })
                {
                    issues.Add(Error(
                        ManifestValidationRule.PublisherMalformed,
                        Target,
                        $"'{publisher}' has an attribute with no value."));
                    return;
                }
            }

            if (name.EnumerateRelativeDistinguishedNames().Any() is false)
            {
                issues.Add(Error(
                    ManifestValidationRule.PublisherMalformed,
                    Target,
                    $"'{publisher}' contains no distinguished-name attributes."));
            }
        }
        catch (CryptographicException)
        {
            issues.Add(Error(
                ManifestValidationRule.PublisherMalformed,
                Target,
                $"'{publisher}' is not a well-formed X.500 distinguished name."));
        }
    }

    private static void ValidateDependencies(AppxManifest manifest, List<ManifestValidationIssue> issues)
    {
        foreach (PackageDependency dependency in manifest.PackageDependencies)
        {
            string target = $"Dependencies/{DependencyElementName(dependency.Kind)}[{dependency.Name}]";
            ValidateIdentifier(dependency.Name, target + "/@Name", MinPackageNameLength, MaxPackageNameLength, issues);

            if (dependency.MinVersion is { } min &&
                dependency.MaxMajorVersionTested is { } maxMajor &&
                min.Major > maxMajor)
            {
                issues.Add(Error(
                    ManifestValidationRule.VersionRangeInverted,
                    target,
                    $"MinVersion '{min}' is above MaxMajorVersionTested '{maxMajor}'."));
            }
        }

        foreach (TargetDeviceFamily family in manifest.TargetDeviceFamilies)
        {
            if (family.MinVersion > family.MaxVersionTested)
            {
                issues.Add(Error(
                    ManifestValidationRule.VersionRangeInverted,
                    $"Dependencies/TargetDeviceFamily[{family.Name}]",
                    $"MinVersion '{family.MinVersion}' is above MaxVersionTested '{family.MaxVersionTested}'."));
            }
        }
    }

    private static string DependencyElementName(PackageDependencyKind kind) => kind switch
    {
        PackageDependencyKind.MainPackage => "MainPackageDependency",
        PackageDependencyKind.HostRuntime => "HostRuntimeDependency",
        _ => "PackageDependency",
    };

    private static void ValidatePackageType(AppxManifest manifest, List<ManifestValidationIssue> issues)
    {
        bool isOptional = manifest.PackageDependencies.Any(d => d.Kind == PackageDependencyKind.MainPackage);

        if (manifest.IsFramework && manifest.IsResourcePackage)
        {
            issues.Add(Error(
                ManifestValidationRule.ConflictingPackageType,
                "Properties",
                "A package cannot be both a framework and a resource package."));
        }

        if (isOptional && (manifest.IsFramework || manifest.IsResourcePackage))
        {
            issues.Add(Error(
                ManifestValidationRule.ConflictingPackageType,
                "Dependencies/MainPackageDependency",
                $"An optional package cannot also be a {(manifest.IsFramework ? "framework" : "resource")} package."));
        }

        if (manifest.IsFramework)
        {
            Forbid(manifest.Applications.Count > 0, ManifestValidationRule.FrameworkContent, "Applications", "framework", issues);
            Forbid(manifest.Capabilities.Count > 0, ManifestValidationRule.FrameworkContent, "Capabilities", "framework", issues);
        }

        if (manifest.IsResourcePackage)
        {
            Forbid(manifest.Applications.Count > 0, ManifestValidationRule.ResourcePackageContent, "Applications", "resource", issues);
            Forbid(manifest.Capabilities.Count > 0, ManifestValidationRule.ResourcePackageContent, "Capabilities", "resource", issues);
            Forbid(manifest.Extensions.Count > 0, ManifestValidationRule.ResourcePackageContent, "Extensions", "resource", issues);
            Forbid(
                manifest.PackageDependencies.Count > 0,
                ManifestValidationRule.ResourcePackageContent,
                "Dependencies/PackageDependency",
                "resource",
                issues);

            // The parser maps an absent ProcessorArchitecture to Neutral, so only a positively
            // declared non-neutral architecture is distinguishable — and only that is flagged.
            if (manifest.Identity.Architecture != ProcessorArchitecture.Neutral)
            {
                issues.Add(Error(
                    ManifestValidationRule.ResourcePackageContent,
                    "Identity/@ProcessorArchitecture",
                    "A resource package cannot declare a processor architecture."));
            }
        }

        if (isOptional && manifest.Capabilities.Count > 0)
        {
            issues.Add(Error(
                ManifestValidationRule.OptionalPackageContent,
                "Capabilities",
                "An optional package cannot declare capabilities."));
        }
    }

    private static void Forbid(
        bool present,
        ManifestValidationRule rule,
        string target,
        string packageKind,
        List<ManifestValidationIssue> issues)
    {
        if (present)
        {
            issues.Add(Error(rule, target, $"A {packageKind} package cannot declare {target}."));
        }
    }

    private static void ValidateApplications(AppxManifest manifest, List<ManifestValidationIssue> issues)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (ManifestApplication application in manifest.Applications)
        {
            string target = $"Applications/Application[{application.Id}]/@Id";

            if (application.Id.Length == 0 || application.Id.Length > MaxApplicationIdLength)
            {
                issues.Add(Error(
                    ManifestValidationRule.ApplicationIdMalformed,
                    target,
                    $"'{application.Id}' is {application.Id.Length} characters; the schema allows 1 to {MaxApplicationIdLength}."));
            }
            else if (!ApplicationIdPattern().IsMatch(application.Id))
            {
                issues.Add(Error(
                    ManifestValidationRule.ApplicationIdMalformed,
                    target,
                    $"'{application.Id}' is not a valid application identifier; each dot-separated segment must start with a letter and contain only letters and digits."));
            }

            if (!seen.Add(application.Id))
            {
                issues.Add(Error(
                    ManifestValidationRule.DuplicateApplicationId,
                    target,
                    $"Application id '{application.Id}' is declared more than once."));
            }
        }
    }

    private static void ValidateCapabilities(AppxManifest manifest, List<ManifestValidationIssue> issues)
    {
        // The schema's uniqueness constraints are per element type, and the element type is what the
        // namespace plus name identify — so the same name in two namespaces is not a duplicate.
        HashSet<(string Namespace, string Name)> seen = [];
        foreach (ManifestCapability capability in manifest.DeclaredCapabilities)
        {
            if (!seen.Add((capability.Namespace, capability.Name)))
            {
                issues.Add(Error(
                    ManifestValidationRule.DuplicateCapability,
                    $"Capabilities/{capability.Name}",
                    $"Capability '{capability.Name}' is declared more than once."));
            }
        }
    }

    private static void ValidateNamespaces(XDocument document, List<ManifestValidationIssue> issues)
    {
        HashSet<string> reported = new(StringComparer.Ordinal);
        foreach (XElement element in document.Descendants())
        {
            ReportIfUnknown(element.Name.NamespaceName, reported, issues);
            foreach (XAttribute attribute in element.Attributes())
            {
                if (!attribute.IsNamespaceDeclaration)
                {
                    ReportIfUnknown(attribute.Name.NamespaceName, reported, issues);
                }
            }
        }
    }

    private static void ReportIfUnknown(
        string namespaceUri,
        HashSet<string> reported,
        List<ManifestValidationIssue> issues)
    {
        // Unqualified names and the reserved xml/xsi namespaces are not schema namespaces.
        if (namespaceUri.Length == 0 ||
            namespaceUri == XNamespace.Xml.NamespaceName ||
            namespaceUri == XNamespace.Xmlns.NamespaceName ||
            ManifestNamespaces.IsKnownPackageNamespace(namespaceUri) ||
            !reported.Add(namespaceUri))
        {
            return;
        }

        issues.Add(new ManifestValidationIssue
        {
            Severity = ManifestValidationSeverity.Warning,
            Rule = ManifestValidationRule.UnknownNamespace,
            Target = namespaceUri,
            Message =
                $"'{namespaceUri}' is not a namespace this library knows. The package may target a " +
                "newer Windows SDK; its content in that namespace was not validated.",
        });
    }

    private static ManifestValidationIssue Error(ManifestValidationRule rule, string target, string message) =>
        new()
        {
            Severity = ManifestValidationSeverity.Error,
            Rule = rule,
            Target = target,
            Message = message,
        };
}
