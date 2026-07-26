# Manifest validation

Parsing and validating are separate operations in MSIX Core, on purpose.
`AppxManifestParser` is deliberately tolerant — it matches elements by local name
so a package built against a newer Windows SDK can still be inspected.
`ManifestValidator` is where a caller opts in to strictness.

```csharp
using MsixCore.Packaging;
using MsixCore.Packaging.Validation;

using MsixPackage package = MsixPackage.Open("App.msix");
ManifestValidationResult result = package.ValidateManifest();

if (!result.IsValid)
{
    foreach (ManifestValidationIssue issue in result.Errors)
    {
        Console.Error.WriteLine($"{issue.Rule} at {issue.Target}: {issue.Message}");
    }
}
```

The `msixkit validate` verb runs this automatically; a manifest error fails the
CI gate with exit code `1`.

## What this is not

This is a **semantic** validator, not an XSD validator. It does not check element
ordering, cardinality, or attribute presence — the schema covers those, and the
parser already enforces them where a missing value would make the result
meaningless. The Microsoft schema documents (~46 XSDs) are not vendored into this
repository, so no `XmlSchemaSet` validation is performed.

It also does not evaluate deployment policy. `runFullTrust`, for example, is a
restricted capability requiring Store approval, but it is a perfectly valid
declaration and is not flagged here.

## Severity

| Severity  | Meaning |
|-----------|---------|
| `Error`   | The manifest violates a rule Windows enforces. `IsValid` is `false`. |
| `Warning` | Advisory only. Never makes `IsValid` false. |

Only one rule produces warnings today: `UnknownNamespace`. A package targeting a
newer SDK than this library knows about is valid, and failing it would be wrong —
so the namespace is reported, and its content is simply not validated.

## Rules

| Rule | Severity | Checks |
|------|----------|--------|
| `IdentifierMalformed` | Error | `Identity/@Name`, `Identity/@ResourceId`, and every dependency `Name` contain only letters, digits, `.`, and `-`. |
| `IdentifierLength` | Error | `Identity/@Name` and dependency names are 3–50 characters; `Identity/@ResourceId` is 1–30. |
| `IdentifierReserved` | Error | An identifier is not `.`/`..`, a DOS device name (`con`, `prn`, `aux`, `nul`, `com1`–`com9`, `lpt1`–`lpt9`), does not start with one of those followed by a period or with `xn--`, and does not end with a period. |
| `PublisherMalformed` | Error | `Identity/@Publisher` is a well-formed X.500 distinguished name, at most 8192 characters, with no empty attribute values. |
| `VersionRangeInverted` | Error | A `PackageDependency`'s `MinVersion` major does not exceed its `MaxMajorVersionTested`, and a `TargetDeviceFamily`'s `MinVersion` does not exceed its `MaxVersionTested`. |
| `ConflictingPackageType` | Error | A package is not both a framework and a resource package, and an optional package is neither. |
| `FrameworkContent` | Error | A framework package declares no `Applications` and no `Capabilities`. |
| `ResourcePackageContent` | Error | A resource package declares no `Applications`, `Capabilities`, package `Extensions`, or dependencies, and no processor architecture. |
| `OptionalPackageContent` | Error | An optional package (one with a `MainPackageDependency`) declares no `Capabilities`. |
| `ApplicationIdMalformed` | Error | Each `Application/@Id` is 1–64 characters and each dot-separated segment starts with a letter and contains only letters and digits. |
| `DuplicateApplicationId` | Error | No two applications share an `Id`. |
| `DuplicateCapability` | Error | No capability element type declares the same `Name` twice. |
| `UnknownNamespace` | Warning | Every XML namespace actually used by an element or attribute is in the known schema registry. |

Identifier rules are stricter than a character-class check because a package
identifier becomes a directory name on disk, and Windows still reserves the DOS
device names at the filesystem level.

Capability uniqueness is scoped by namespace as well as name, because the
schema's `xs:unique` constraints are per element type. A foundation
`<Capability Name="documentsLibrary"/>` and a
`<rescap:Capability Name="documentsLibrary"/>` are two different declarations,
not a duplicate.

## Known divergences from Windows

These are the places where this validator knowingly differs from what Windows
enforces. Each is a consequence of what the parser models, not an oversight.

- **`Properties/SupportedUsers` is not modelled**, so the rule that an optional
  package may not declare it cannot be checked.
- **An absent `ProcessorArchitecture` is indistinguishable from `neutral`.** The
  parser maps both to `ProcessorArchitecture.Neutral`, so a resource package is
  only flagged when it positively declares a non-neutral architecture.
- **No XSD validation.** Structural violations that the parser tolerates —
  unexpected child elements, out-of-order content — are not reported.
- **Namespaces are checked, schemas are not.** A known namespace means "this
  library recognises the schema revision", not "the content conforms to it".

## Namespace registry

`ManifestNamespaces` is the table of namespaces the MSIX manifest schemas define,
mapped to the schema document that defines each. It mirrors
`cmake/msix_resources.cmake` in Microsoft's `msix-packaging` repository: 46
package-manifest namespaces and 6 bundle-manifest namespaces.

```csharp
bool known = ManifestNamespaces.IsKnownPackageNamespace(
    "http://schemas.microsoft.com/appx/manifest/uap/windows10/13");   // true
```

Two details of the upstream registry are worth knowing, because both look like
bugs and are not:

- There is **no `.../uap/windows10/9`**. The sequence jumps from `/8` to `/10`,
  with no comment upstream explaining why. Only its absence is a fact; any
  explanation would be a guess.
- The **2014 bundle schema document declares the 2013 namespace**
  (`http://schemas.microsoft.com/appx/2013/bundle`). The mismatch is upstream's.

There is also no Xbox namespace, despite one being widely assumed to exist.
